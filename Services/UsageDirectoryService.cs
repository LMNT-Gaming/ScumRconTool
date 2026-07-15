using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace ScumRconTool.Services;

public sealed class UsageDirectoryService : IDisposable
{
    public const string DefaultEndpointUrl = "https://lmnt-gaming.net/redravenserver/api/heartbeat.php";
    public const string BrowserUrl = "https://lmnt-gaming.net/redravenserver/";
    public const string PrivacyUrl = "https://lmnt-gaming.net/redravenserver/privacy.php";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private CancellationTokenSource? _cts;
    private Action<string>? _log;

    public void Start(BotSettings settings, string version, Action<string> log)
    {
        Stop();
        if (!settings.UsageDirectoryEnabled) return;
        _log = log;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested && settings.UsageDirectoryEnabled)
            {
                try { await SendHeartbeatAsync(settings, version, token); }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
                catch (Exception ex) { log("LMNT Serverliste: Meldung fehlgeschlagen: " + ex.Message); }
                try { await Task.Delay(TimeSpan.FromMinutes(15), token); }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            }
        }, token);
    }

    public void Stop()
    {
        try { _cts?.Cancel(); } catch { }
        _cts?.Dispose();
        _cts = null;
    }

    public async Task SendHeartbeatAsync(BotSettings settings, string version, CancellationToken cancellationToken = default)
    {
        if (!settings.UsageDirectoryEnabled) return;
        EnsureIdentity(settings);
        var serverIp = await ResolveServerIpAsync(settings.Host, cancellationToken);
        if (string.IsNullOrWhiteSpace(serverIp)) throw new InvalidOperationException("SCUM-Server-IP konnte nicht ermittelt werden.");
        var payload = new UsageDirectoryRequest
        {
            Action = "heartbeat",
            InstanceId = settings.UsageDirectoryInstallId,
            InstanceToken = settings.UsageDirectoryInstallToken,
            ServerName = string.IsNullOrWhiteSpace(settings.DiscordServerName) ? "SCUM Server" : settings.DiscordServerName.Trim(),
            ServerIp = serverIp,
            ToolVersion = version
        };
        await PostAsync(settings.UsageDirectoryEndpointUrl, payload, cancellationToken);
        _log?.Invoke($"LMNT Serverliste aktualisiert: {payload.ServerName} ({payload.ServerIp}).");
    }

    public async Task<bool> RemoveAsync(BotSettings settings, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(settings.UsageDirectoryInstallId) || string.IsNullOrWhiteSpace(settings.UsageDirectoryInstallToken)) return true;
        try
        {
            await PostAsync(settings.UsageDirectoryEndpointUrl, new UsageDirectoryRequest
            {
                Action = "remove",
                InstanceId = settings.UsageDirectoryInstallId,
                InstanceToken = settings.UsageDirectoryInstallToken
            }, cancellationToken);
            _log?.Invoke("LMNT Serverliste: Eintrag entfernt.");
            return true;
        }
        catch (Exception ex)
        {
            _log?.Invoke("LMNT Serverliste: Entfernen fehlgeschlagen, wird beim naechsten Start erneut versucht: " + ex.Message);
            return false;
        }
    }

    public static void EnsureIdentity(BotSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.UsageDirectoryInstallId)) settings.UsageDirectoryInstallId = Guid.NewGuid().ToString("N");
        if (string.IsNullOrWhiteSpace(settings.UsageDirectoryInstallToken)) settings.UsageDirectoryInstallToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
    }

    private static async Task PostAsync(string endpoint, UsageDirectoryRequest payload, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("LMNT Serverlisten-URL muss eine gueltige HTTPS-Adresse sein.");
        using var request = new HttpRequestMessage(HttpMethod.Post, uri) { Content = JsonContent.Create(payload) };
        request.Headers.TryAddWithoutValidation("User-Agent", "RedRavenRconTool/" + (payload.ToolVersion ?? "unknown"));
        using var response = await Http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"HTTP {(int)response.StatusCode}: {body}");
        }
    }

    private static async Task<string> ResolveServerIpAsync(string host, CancellationToken cancellationToken)
    {
        host = (host ?? string.Empty).Trim().Trim('[', ']');
        if (IPAddress.TryParse(host, out var parsed)) return parsed.ToString();
        if (string.IsNullOrWhiteSpace(host)) return string.Empty;
        var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken);
        return addresses.FirstOrDefault(x => x.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)?.ToString()
               ?? addresses.FirstOrDefault()?.ToString()
               ?? string.Empty;
    }

    public void Dispose() => Stop();
}

public sealed class UsageDirectoryRequest
{
    [JsonPropertyName("action")] public string Action { get; set; } = "heartbeat";
    [JsonPropertyName("instance_id")] public string InstanceId { get; set; } = string.Empty;
    [JsonPropertyName("instance_token")] public string InstanceToken { get; set; } = string.Empty;
    [JsonPropertyName("server_name")] public string ServerName { get; set; } = string.Empty;
    [JsonPropertyName("server_ip")] public string ServerIp { get; set; } = string.Empty;
    [JsonPropertyName("tool_version")] public string ToolVersion { get; set; } = string.Empty;
}
