using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Net.Http;
using ScumRconTool.Models;

namespace ScumRconTool.Services;

public static class UpdateInstaller
{
    // Only shipped program files are replaced. Never replace user scripts/configuration/state.
    public static bool IsProgramFile(string relative)
    {
        var name = relative.Replace('\\', '/');
        if (name.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            return Path.GetExtension(name).Equals(".png", StringComparison.OrdinalIgnoreCase);
        if (name.Equals("scum_kartographer/assets/scum_map.jpg", StringComparison.OrdinalIgnoreCase)) return true;
        if (name.StartsWith("runtimes/", StringComparison.OrdinalIgnoreCase))
            return Path.GetExtension(name).Equals(".dll", StringComparison.OrdinalIgnoreCase);
        if (System.Text.RegularExpressions.Regex.IsMatch(name, @"^[a-z]{2,3}(?:-[a-z0-9]{2,8})?/[a-z0-9_.-]+\.resources\.dll$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase)) return true;
        if (name.Contains('/')) return false;
        return name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
            name is "RedRavenRconTool.deps.json" or "RedRavenRconTool.runtimeconfig.json" or "dbs14756250.sql";
    }

    public static string SafeArchivePath(string root, string entry)
    {
        if (entry.Contains(':') || entry.Split('/', '\\').Any(p => p is "." or ".."))
            throw new InvalidDataException("Unsafe archive path.");
        var full = Path.GetFullPath(Path.Combine(root, entry));
        if (!full.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Archive path leaves staging directory.");
        return full;
    }

    public static async Task<string> PrepareAsync(UpdateInfo release, IProgress<double> progress, CancellationToken cancellationToken = default)
    {
        if (release.sha256 is not { Length: 64 } || !release.sha256.All(Uri.IsHexDigit))
            throw new InvalidDataException("Update requires a valid SHA-256 checksum.");
        if (!Uri.TryCreate(release.downloadUrl, UriKind.Absolute, out var uri) || uri.Scheme != "https")
            throw new InvalidDataException("Updates require HTTPS.");
        var work = Path.Combine(Path.GetTempPath(), "RedRaven-Update-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        // Fail before closing the application if it is installed in a read-only directory.
        using (new FileStream(Path.Combine(AppContext.BaseDirectory, ".rr-update-probe-" + Guid.NewGuid().ToString("N")),
            FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
        var zipPath = Path.Combine(work, "package.zip");
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("RedRaven-Updater/1.3");
        using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var expected = release.size > 0 ? release.size : response.Content.Headers.ContentLength;
        await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var output = File.Create(zipPath))
        {
            var buffer = new byte[81920]; long total = 0; int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                total += read;
                if (total > 512L * 1024 * 1024 || (expected.HasValue && total > expected.Value))
                    throw new InvalidDataException("Update exceeds declared size.");
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                progress.Report(expected is > 0 ? total * 100d / expected.Value : 0);
            }
            if (expected.HasValue && total != expected.Value) throw new InvalidDataException("Incomplete update.");
        }
        await using (var file = File.OpenRead(zipPath))
            if (!Convert.ToHexString(await SHA256.HashDataAsync(file, cancellationToken)).Equals(release.sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Update checksum mismatch.");
        var stage = Path.Combine(work, "app");
        Directory.CreateDirectory(stage);
        using (var archive = ZipFile.OpenRead(zipPath))
        {
            long expanded = 0;
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in archive.Entries)
            {
                var path = SafeArchivePath(stage, entry.FullName);
                if (entry.Name.Length == 0) continue;
                if (!paths.Add(path)) throw new InvalidDataException("Duplicate archive file.");
                expanded += entry.Length;
                if (expanded > 2L * 1024 * 1024 * 1024) throw new InvalidDataException("Expanded package too large.");
                if (!IsProgramFile(entry.FullName)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                entry.ExtractToFile(path);
            }
        }
        var dll = Path.Combine(stage, "RedRavenRconTool.dll");
        if (!File.Exists(Path.Combine(stage, "RedRavenRconTool.exe")) || !File.Exists(dll) ||
            !Version.TryParse(release.version, out var version) ||
            AssemblyName.GetAssemblyName(dll).Version != new Version(version.Major, version.Minor, Math.Max(0, version.Build), Math.Max(0, version.Revision)))
            throw new InvalidDataException("Package application/version does not match the release.");
        var manifest = new { processId = Environment.ProcessId, target = AppContext.BaseDirectory, stage,
            files = Directory.GetFiles(stage, "*", SearchOption.AllDirectories).Select(p => Path.GetRelativePath(stage, p)).ToArray() };
        await File.WriteAllTextAsync(Path.Combine(work, "install.json"), JsonSerializer.Serialize(manifest), cancellationToken);
        using var script = typeof(UpdateInstaller).Assembly.GetManifestResourceStream("ScumRconTool.InstallUpdate.ps1")!;
        using var reader = new StreamReader(script);
        await File.WriteAllTextAsync(Path.Combine(work, "install.ps1"), await reader.ReadToEndAsync(cancellationToken), cancellationToken);
        return work;
    }

    public static void Start(string work)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"))
        { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = work };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", Path.Combine(work, "install.ps1") })
            start.ArgumentList.Add(argument);
        if (Process.Start(start) == null) throw new IOException("Cannot start update installer.");
    }
}
