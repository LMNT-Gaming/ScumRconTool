using System.Diagnostics;
using System.Text.Json;
using ScumRconTool.Services;

int checks = 0;
void Require(bool value, string label) { if (!value) throw new Exception(label); checks++; }
void Reject(Action action, string label)
{
    try { action(); } catch (InvalidDataException) { checks++; return; }
    throw new Exception("Expected rejection: " + label);
}
string Release(string url = "https://github.com/LMNT-Gaming/ScumRconTool/releases/download/v1.3.2/RedRavenRconTool-1.3.2-win-x64.zip", string? digest = null, bool draft = false) =>
    JsonSerializer.Serialize(new { draft, prerelease = false, tag_name = "v1.3.2", html_url = "https://github.com/LMNT-Gaming/ScumRconTool/releases/tag/v1.3.2",
        assets = new[] { new { name = "RedRavenRconTool-1.3.2-win-x64.zip", browser_download_url = url, size = 100,
            digest = digest ?? "sha256:" + new string('a', 64) } } });
Require(UpdateService.ParseGitHubRelease(Release()).version == "1.3.2", "Release parsing");
Reject(() => UpdateService.ParseGitHubRelease(Release(draft: true)), "Draft");
Reject(() => UpdateService.ParseGitHubRelease(Release(digest: "")), "Missing digest");
Reject(() => UpdateService.ParseGitHubRelease(Release(url: "https://example.org/package.zip")), "Untrusted source");
Reject(() => UpdateService.ParseGitHubRelease(Release().Replace("win-x64.zip", "win-arm64.zip")), "Architecture");
foreach (var path in new[] { "settings.json", "lootpacks.json", "Data/Scripts/mine.json", "Data/weapon_compatibility.json", "logs/test.log", "Scum.db", ".env" })
    Require(!UpdateInstaller.IsProgramFile(path), "Protected: " + path);
foreach (var path in new[] { "RedRavenRconTool.dll", "RedRavenRconTool.exe", "RedRavenRconTool.deps.json", "Assets/icon.png", "runtimes/win-x64/native/test.dll", "de/System.resources.dll" })
    Require(UpdateInstaller.IsProgramFile(path), "Program: " + path);
var work = Path.Combine(Path.GetTempPath(), "RedRaven-Updater-Test-" + Guid.NewGuid().ToString("N"));
foreach (var path in new[] { "../evil.dll", "..\\evil.dll", "C:\\evil.dll", "safe/../../evil.dll", "safe.dll:stream" })
    Reject(() => UpdateInstaller.SafeArchivePath(work, path), "Traversal: " + path);
Require(UpdateInstaller.SafeArchivePath(work, "Assets/icon.png").StartsWith(work), "Valid archive path");
var target = Path.Combine(work, "target");
var stage = Path.Combine(work, "app");
Directory.CreateDirectory(target); Directory.CreateDirectory(stage);
File.WriteAllText(Path.Combine(target, "RedRavenRconTool.exe"), "old-exe");
File.WriteAllText(Path.Combine(target, "RedRavenRconTool.dll"), "old-dll");
File.WriteAllText(Path.Combine(target, "lootpacks.json"), "my-packs");
Directory.CreateDirectory(Path.Combine(target, "Data", "Scripts"));
File.WriteAllText(Path.Combine(target, "Data", "Scripts", "mine.json"), "my-script");
File.WriteAllText(Path.Combine(stage, "RedRavenRconTool.exe"), "new-exe");
File.WriteAllText(Path.Combine(stage, "RedRavenRconTool.dll"), "new-dll");
File.WriteAllText(Path.Combine(work, "install.json"), JsonSerializer.Serialize(new { processId = 99999999, target, stage,
    files = new[] { "RedRavenRconTool.exe", "RedRavenRconTool.dll" } }));
File.Copy(Path.Combine(AppContext.BaseDirectory, "install.ps1"), Path.Combine(work, "install.ps1"));
var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", Path.Combine(work, "install.ps1"), "-SkipRestart" }) start.ArgumentList.Add(argument);
using (var process = Process.Start(start)!)
{
    var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
    if (!process.WaitForExit(30000)) throw new Exception("Helper timed out; see " + work);
    Require(process.ExitCode == 0, "Helper: " + await stdout + await stderr);
}
Require(File.ReadAllText(Path.Combine(target, "RedRavenRconTool.dll")) == "new-dll", "Replaced binary");
Require(File.ReadAllText(Path.Combine(work, "backup", "RedRavenRconTool.dll")) == "old-dll", "Backup");
Require(File.ReadAllText(Path.Combine(target, "lootpacks.json")) == "my-packs", "Preserved packs");
Require(File.ReadAllText(Path.Combine(target, "Data", "Scripts", "mine.json")) == "my-script", "Preserved scripts");
// A tampered/out-of-bounds plan must fail before touching installed files.
File.WriteAllText(Path.Combine(work, "install.json"), JsonSerializer.Serialize(new { processId = 99999999, target, stage,
    files = new[] { "../outside.dll" } }));
using (var process = Process.Start(start)!)
{
    var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
    if (!process.WaitForExit(30000)) throw new Exception("Negative helper test timed out.");
    Require(process.ExitCode == 1, "Helper rejected out-of-bounds plan: " + await stdout + await stderr);
}
Require(File.ReadAllText(Path.Combine(target, "RedRavenRconTool.dll")) == "new-dll", "No changes on invalid plan");
// Copy failure after the first replacement must restore both original binaries.
File.WriteAllText(Path.Combine(work, "install.json"), JsonSerializer.Serialize(new { processId = 99999999, target, stage,
    files = new[] { "RedRavenRconTool.exe", "RedRavenRconTool.dll" } }));
File.WriteAllText(Path.Combine(stage, "RedRavenRconTool.exe"), "replacement-that-must-roll-back");
using (var locked = new FileStream(Path.Combine(stage, "RedRavenRconTool.dll"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
using (var process = Process.Start(start)!)
{
    var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
    if (!process.WaitForExit(30000)) throw new Exception("Rollback test timed out.");
    Require(process.ExitCode == 1, "Helper failed on locked source: " + await stdout + await stderr);
}
Require(File.ReadAllText(Path.Combine(target, "RedRavenRconTool.exe")) == "new-exe", "First replacement rolled back");
Require(File.ReadAllText(Path.Combine(target, "RedRavenRconTool.dll")) == "new-dll", "Second file restored");
Require(File.ReadAllText(Path.Combine(target, "lootpacks.json")) == "my-packs", "Data intact after rollback");
Console.WriteLine($"PASS: {checks} offline update checks. Test files: {work}");
