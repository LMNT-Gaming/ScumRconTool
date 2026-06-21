using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using ScumRconTool.Models;

namespace ScumRconTool.Services;

public sealed class UpdateService
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(8)
    };

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

        var json = await Http.GetStringAsync(latestUrl.Trim(), cancellationToken);
        return JsonSerializer.Deserialize<UpdateInfo>(json, JsonOptions);
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
