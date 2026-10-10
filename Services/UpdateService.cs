using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Net;
using System.IO;
using ScumRconTool.Models;

namespace ScumRconTool.Services;

public sealed class UpdateService
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(8)
    };

    public const string GitHubLatestUrl = "https://api.github.com/repos/LMNT-Gaming/ScumRconTool/releases/latest";

    static UpdateService()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("RedRaven-Updater/1.3");
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<UpdateInfo?> GetLatestAsync(string latestUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(latestUrl))
        {
            return null;
        }

        // Migrate the old official website feed without changing private/custom feeds.
        var source = latestUrl.Trim();
        if (source.TrimEnd('/').Equals("https://lmnt-gaming.net/rrrt/latest.json", StringComparison.OrdinalIgnoreCase))
            source = GitHubLatestUrl;
        using var response = await Http.GetAsync(source, cancellationToken);
        if (source == GitHubLatestUrl && response.StatusCode == HttpStatusCode.NotFound)
            return null; // No published stable release yet.
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (source == GitHubLatestUrl) return ParseGitHubRelease(json);
        return JsonSerializer.Deserialize<UpdateInfo>(json, JsonOptions);
    }

    public static UpdateInfo ParseGitHubRelease(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean())
            throw new InvalidDataException("A stable release is required.");
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var version))
            throw new InvalidDataException("Invalid release version.");
        var expected = $"RedRavenRconTool-{version}-win-x64.zip";
        var asset = root.GetProperty("assets").EnumerateArray()
            .FirstOrDefault(a => a.GetProperty("name").GetString() == expected);
        if (asset.ValueKind == JsonValueKind.Undefined)
            throw new InvalidDataException($"Release package missing: {expected}");
        var url = asset.GetProperty("browser_download_url").GetString() ?? "";
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            uri.Host != "github.com" || !uri.AbsolutePath.StartsWith("/LMNT-Gaming/ScumRconTool/releases/download/", StringComparison.Ordinal))
            throw new InvalidDataException("Unexpected GitHub download source.");
        var digest = asset.TryGetProperty("digest", out var hash) ? hash.GetString() ?? "" : "";
        if (!digest.StartsWith("sha256:", StringComparison.Ordinal) || digest.Length != 71 || !digest[7..].All(Uri.IsHexDigit))
            throw new InvalidDataException("The release asset requires a SHA-256 digest.");
        var size = asset.GetProperty("size").GetInt64();
        if (size <= 0 || size > 512L * 1024 * 1024) throw new InvalidDataException("Invalid package size.");
        return new UpdateInfo { version = version.ToString(), downloadUrl = url, sha256 = digest[7..], size = size,
            patchNotesUrl = root.GetProperty("html_url").GetString() };
    }

    public static Version GetCurrentVersion()
    {
        var asm = Assembly.GetExecutingAssembly();
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info))
        {
            var clean = info.Split('+')[0].Trim();
            if (Version.TryParse(clean, out var parsed))
            {
                return parsed;
            }
        }

        return asm.GetName().Version ?? new Version(0, 0, 0, 0);
    }

    public static string GetCurrentVersionText()
    {
        var asm = Assembly.GetExecutingAssembly();
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info))
        {
            return info.Split('+')[0].Trim();
        }

        return asm.GetName().Version?.ToString() ?? "0.0.0";
    }

    public static bool IsNewer(string? latestVersion)
    {
        if (string.IsNullOrWhiteSpace(latestVersion))
        {
            return false;
        }

        if (!Version.TryParse(latestVersion.Trim(), out var latest))
        {
            return false;
        }

        return latest > GetCurrentVersion();
    }
}
