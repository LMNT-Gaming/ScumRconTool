using System.Globalization;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ScumRconTool.Models;

namespace ScumRconTool.Services;

public sealed partial class GgconHttpApiService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly BotSettings _settings;

    public GgconHttpApiService(BotSettings settings)
    {
        _settings = settings;
    }

    public async Task<GgconWeatherResponse?> GetWeatherAsync(CancellationToken cancellationToken = default)
    {
        var baseUrl = RequireBaseUrl();
        using var client = CreateClient();
        var url = Combine(baseUrl, "/weather.json");
        using var response = await client.GetAsync(url, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var shortBody = string.IsNullOrWhiteSpace(json) ? string.Empty : " Antwort: " + Truncate(json, 300);
            throw new InvalidOperationException($"ggCON HTTP Weather Fehler {(int)response.StatusCode} {response.ReasonPhrase}.{shortBody}");
        }

        var weather = JsonSerializer.Deserialize<GgconWeatherResponse>(json, JsonOptions);
        if (weather is null || !weather.Ok)
        {
            throw new InvalidOperationException("ggCON HTTP Weather Antwort war leer oder ok=false.");
        }

        return weather;
    }

    public async Task SetServerTimeAsync(double hour, CancellationToken cancellationToken = default) =>
        _ = await PostServerControlAsync("/server/time", new { hour = Math.Clamp(hour, 0d, 23.999d) }, "Serverzeit", cancellationToken);

    public async Task SetServerWeatherAsync(double value, CancellationToken cancellationToken = default) =>
        _ = await PostServerControlAsync("/server/weather", new { value = Math.Clamp(value, 0d, 1d) }, "Wetter", cancellationToken);

    public Task<string> SpawnRazorAtAsync(double x, double y, double z, CancellationToken cancellationToken = default) =>
        PostServerControlAsync("/spawn-at", new { type = "razor", x, y, z }, "Razor-Spawn", cancellationToken);

    public async Task<string> ExecuteCommandAsync(string command, CancellationToken cancellationToken = default)
    {
        command = command?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(command))
        {
            throw new ArgumentException("ggCON HTTP Command fehlt.", nameof(command));
        }

        var baseUrl = RequireBaseUrl();
        using var client = CreateClient();
        var url = Combine(baseUrl, "/command");

        try
        {
            using var response = await client.PostAsJsonAsync(
                url,
                new GgconCommandRequest(command),
                JsonOptions,
                cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var shortBody = string.IsNullOrWhiteSpace(json) ? string.Empty : " Antwort: " + Truncate(json, 300);
                throw new InvalidOperationException($"ggCON HTTP Command Fehler {(int)response.StatusCode} {response.ReasonPhrase}.{shortBody}");
            }

            var result = JsonSerializer.Deserialize<GgconCommandResponse>(json, JsonOptions);
            if (result is null)
            {
                throw new InvalidOperationException("ggCON HTTP Command Antwort war leer.");
            }

            // Location-based cleanup commands can return only { "ok": true,
            // "message": "Command dispatched ..." }. Missing status flags are
            // therefore unknown; an explicitly false flag is still an error.
            if (!result.Ok || result.Accepted == false || result.Dispatched == false)
            {
                var reason = !string.IsNullOrWhiteSpace(result.Message)
                    ? result.Message
                    : !string.IsNullOrWhiteSpace(result.Error)
                        ? result.Error
                        : "Command wurde nicht ausgefuehrt.";
                throw new InvalidOperationException("ggCON HTTP Command abgelehnt: " + reason);
            }

            return result.Lines is { Count: > 0 }
                ? string.Join(Environment.NewLine, result.Lines)
                : result.Message ?? string.Empty;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("ggCON HTTP Command: Der Server hat innerhalb von 12 Sekunden nicht geantwortet.");
        }
    }

    public Task SendMessageAsync(
        string text,
        string type = "ServerMessage",
        string? steamId = null,
        CancellationToken cancellationToken = default)
    {
        text = (text ?? string.Empty)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("ggCON HTTP Nachricht fehlt.", nameof(text));
        }

        var payload = new Dictionary<string, object>
        {
            ["text"] = text,
            ["type"] = string.IsNullOrWhiteSpace(type) ? "ServerMessage" : type.Trim()
        };

        if (!string.IsNullOrWhiteSpace(steamId))
        {
            payload["steamId"] = steamId.Trim();
        }

        return AwaitServerControlAsync("/message", payload, "Nachricht", cancellationToken);
    }

    public Task SendWarningAsync(
        string text,
        string color = "#d3152a",
        int durationSeconds = 8,
        string? steamId = null,
        CancellationToken cancellationToken = default)
    {
        text = (text ?? string.Empty)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("ggCON HTTP Announcement fehlt.", nameof(text));
        }

        var payload = new Dictionary<string, object>
        {
            ["method"] = "warning",
            ["text"] = text,
            ["color"] = string.IsNullOrWhiteSpace(color) ? "#d3152a" : color.Trim(),
            ["duration"] = Math.Clamp(durationSeconds, 1, 30)
        };

        if (!string.IsNullOrWhiteSpace(steamId))
        {
            payload["steamId"] = steamId.Trim();
        }

        return AwaitServerControlAsync("/message", payload, "Announcement", cancellationToken);
    }

    public async Task<List<ScumPlayer>> GetOnlinePlayersAsync(CancellationToken cancellationToken = default)
    {
        var baseUrl = RequireBaseUrl();
        using var client = CreateClient();
        var url = Combine(baseUrl, "/players.json");

        try
        {
            using var response = await client.GetAsync(url, cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var shortBody = string.IsNullOrWhiteSpace(json) ? string.Empty : " Antwort: " + Truncate(json, 300);
                throw new InvalidOperationException($"ggCON HTTP Spieler Fehler {(int)response.StatusCode} {response.ReasonPhrase}.{shortBody}");
            }

            var result = JsonSerializer.Deserialize<PlayerListResponse>(json, JsonOptions);
            if (result is null || !result.Ok)
            {
                throw new InvalidOperationException("ggCON HTTP Spieler-Antwort war leer oder ok=false.");
            }

            return result.Players
                .Where(player => !string.IsNullOrWhiteSpace(player.DisplayName))
                .ToList();
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("ggCON HTTP Spieler: Der Server hat innerhalb von 12 Sekunden nicht geantwortet.");
        }
    }
    private async Task AwaitServerControlAsync(string path, object payload, string label, CancellationToken cancellationToken)
    {
        _ = await PostServerControlAsync(path, payload, label, cancellationToken);
    }

    private async Task<string> PostServerControlAsync(string path, object payload, string label, CancellationToken cancellationToken)
    {
        var baseUrl = RequireBaseUrl();
        using var client = CreateClient();
        var url = Combine(baseUrl, path);

        try
        {
            using var response = await client.PostAsJsonAsync(url, payload, JsonOptions, cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var shortBody = string.IsNullOrWhiteSpace(json) ? string.Empty : " Antwort: " + Truncate(json, 300);
                throw new InvalidOperationException($"ggCON HTTP {label} Fehler {(int)response.StatusCode} {response.ReasonPhrase}.{shortBody}");
            }

            if (string.IsNullOrWhiteSpace(json))
            {
                return "HTTP " + (int)response.StatusCode;
            }

            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("ok", out var ok) &&
                ok.ValueKind == JsonValueKind.False)
            {
                throw new InvalidOperationException($"ggCON HTTP {label} meldet ok=false. Antwort: {Truncate(json, 300)}");
            }

            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("message", out var message) &&
                message.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(message.GetString()))
            {
                return message.GetString()!;
            }

            return Truncate(json, 300);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"ggCON HTTP {label}: Der Server hat innerhalb von 12 Sekunden nicht geantwortet.");
        }
    }
    public async Task<GgconPlayerAccountResponse> GetPlayerAccountAsync(string steamId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(steamId))
        {
            throw new ArgumentException("SteamID fehlt.", nameof(steamId));
        }

        var baseUrl = RequireBaseUrl();
        using var client = CreateClient();
        var url = Combine(baseUrl, "/players/" + Uri.EscapeDataString(steamId.Trim()) + ".json");
        using var response = await client.GetAsync(url, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var shortBody = string.IsNullOrWhiteSpace(json) ? string.Empty : " Antwort: " + Truncate(json, 300);
            throw new InvalidOperationException($"ggCON HTTP Player Fehler {(int)response.StatusCode} {response.ReasonPhrase}.{shortBody}");
        }

        var player = DeserializePlayerAccount(json);
        if (player is null || !player.Ok)
        {
            throw new InvalidOperationException("ggCON HTTP Player Antwort war leer oder ok=false.");
        }

        if (!player.AccountBalance.HasValue)
        {
            var onlinePlayer = await GetOnlinePlayerAccountAsync(steamId, cancellationToken);
            if (onlinePlayer?.AccountBalance.HasValue == true)
            {
                return onlinePlayer;
            }
        }

        return player;
    }

    private async Task<GgconPlayerAccountResponse?> GetOnlinePlayerAccountAsync(string steamId, CancellationToken cancellationToken)
    {
        var baseUrl = RequireBaseUrl();
        using var client = CreateClient();
        var url = Combine(baseUrl, "/players.json");
        using var response = await client.GetAsync(url, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var players = JsonSerializer.Deserialize<GgconPlayersResponse>(json, JsonOptions);
        if (players is null || !players.Ok || players.Players.Count == 0)
        {
            return null;
        }

        var match = players.Players.FirstOrDefault(player =>
            !string.IsNullOrWhiteSpace(player.UserId) &&
            player.UserId.Equals(steamId.Trim(), StringComparison.OrdinalIgnoreCase));

        if (match is not null)
        {
            match.Ok = true;
        }

        return match;
    }

    public async Task<GgconSquadsResponse> GetSquadsAsync(CancellationToken cancellationToken = default)
    {
        var baseUrl = RequireBaseUrl();
        using var client = CreateClient();
        var url = Combine(baseUrl, "/squads.json");
        using var response = await client.GetAsync(url, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var shortBody = string.IsNullOrWhiteSpace(json) ? string.Empty : " Antwort: " + Truncate(json, 300);
            throw new InvalidOperationException($"ggCON HTTP Squads Fehler {(int)response.StatusCode} {response.ReasonPhrase}.{shortBody}");
        }

        var squads = JsonSerializer.Deserialize<GgconSquadsResponse>(json, JsonOptions);
        if (squads is null || !squads.Ok)
        {
            throw new InvalidOperationException("ggCON HTTP Squads Antwort war leer oder ok=false.");
        }

        return squads;
    }

    public async Task RemovePlayerCurrencyAsync(string steamId, int amount, CancellationToken cancellationToken = default)
    {
        try
        {
            await ChangePlayerCurrencyAsync(steamId, "change", -Math.Abs(amount), cancellationToken);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Invalid action", StringComparison.OrdinalIgnoreCase))
        {
            await ChangePlayerCurrencyAsync(steamId, "remove", Math.Abs(amount), cancellationToken);
        }
    }

    public async Task AddPlayerCurrencyAsync(string steamId, int amount, CancellationToken cancellationToken = default)
    {
        try
        {
            await ChangePlayerCurrencyAsync(steamId, "change", Math.Abs(amount), cancellationToken);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Invalid action", StringComparison.OrdinalIgnoreCase))
        {
            await ChangePlayerCurrencyAsync(steamId, "add", Math.Abs(amount), cancellationToken);
        }
    }

    public async Task AddPlayerFameAsync(string steamId, int amount, CancellationToken cancellationToken = default)
    {
        try
        {
            await ChangePlayerFameAsync(steamId, "change", Math.Abs(amount), cancellationToken);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Invalid action", StringComparison.OrdinalIgnoreCase))
        {
            // Older ggCON versions used add/remove; newer builds use a signed change amount.
            await ChangePlayerFameAsync(steamId, "add", Math.Abs(amount), cancellationToken);
        }
    }

    private async Task ChangePlayerFameAsync(string steamId, string action, int amount, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(steamId))
        {
            throw new ArgumentException("SteamID fehlt.", nameof(steamId));
        }

        if (amount == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Fame-Betrag darf nicht 0 sein.");
        }

        var baseUrl = RequireBaseUrl();
        using var client = CreateClient();
        var url = Combine(baseUrl, "/players/" + Uri.EscapeDataString(steamId.Trim()) + "/fame");
        var payload = JsonSerializer.Serialize(new GgconCurrencyChangeRequest(action, amount), JsonOptions);
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var response = await client.PostAsync(url, content, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var shortBody = string.IsNullOrWhiteSpace(json) ? string.Empty : " Antwort: " + Truncate(json, 300);
            throw new InvalidOperationException($"ggCON HTTP Fame Fehler {(int)response.StatusCode} {response.ReasonPhrase}.{shortBody}");
        }

        if (!string.IsNullOrWhiteSpace(json))
        {
            var result = JsonSerializer.Deserialize<GgconOkResponse>(json, JsonOptions);
            if (result is not null && !result.Ok)
            {
                var reason = !string.IsNullOrWhiteSpace(result.Reason) ? result.Reason : result.Error;
                throw new InvalidOperationException("ggCON HTTP Fame Antwort ok=false" + (string.IsNullOrWhiteSpace(reason) ? "." : ": " + reason));
            }
        }
    }
    private async Task ChangePlayerCurrencyAsync(string steamId, string action, int amount, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(steamId))
        {
            throw new ArgumentException("SteamID fehlt.", nameof(steamId));
        }

        if (amount == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Betrag darf nicht 0 sein.");
        }

        var baseUrl = RequireBaseUrl();
        using var client = CreateClient();
        var url = Combine(baseUrl, "/players/" + Uri.EscapeDataString(steamId.Trim()) + "/currency");
        var payload = JsonSerializer.Serialize(new GgconCurrencyChangeRequest(action, amount), JsonOptions);
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var response = await client.PostAsync(url, content, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var shortBody = string.IsNullOrWhiteSpace(json) ? string.Empty : " Antwort: " + Truncate(json, 300);
            throw new InvalidOperationException($"ggCON HTTP Currency Fehler {(int)response.StatusCode} {response.ReasonPhrase}.{shortBody}");
        }

        if (!string.IsNullOrWhiteSpace(json))
        {
            var result = JsonSerializer.Deserialize<GgconOkResponse>(json, JsonOptions);
            if (result is not null && !result.Ok)
            {
                var reason = !string.IsNullOrWhiteSpace(result.Reason) ? result.Reason : result.Error;
                throw new InvalidOperationException("ggCON HTTP Currency Antwort ok=false" + (string.IsNullOrWhiteSpace(reason) ? "." : ": " + reason));
            }
        }
    }

    public async Task<GgconLogsResponse> GetLogsAsync(long? sinceUnixMs, string sources, CancellationToken cancellationToken = default)
    {
        var baseUrl = RequireBaseUrl();
        using var client = CreateClient();

        var query = new List<string>();
        if (sinceUnixMs.HasValue && sinceUnixMs.Value > 0)
        {
            query.Add("since=" + sinceUnixMs.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (!string.IsNullOrWhiteSpace(sources))
        {
            query.Add("sources=" + Uri.EscapeDataString(sources.Trim()));
        }

        var url = Combine(baseUrl, "/logs");
        if (query.Count > 0)
        {
            url += "?" + string.Join("&", query);
        }

        using var response = await client.GetAsync(url, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var shortBody = string.IsNullOrWhiteSpace(json) ? string.Empty : " Antwort: " + Truncate(json, 300);
            throw new InvalidOperationException($"ggCON HTTP Logs Fehler {(int)response.StatusCode} {response.ReasonPhrase}.{shortBody}");
        }

        var result = JsonSerializer.Deserialize<GgconLogsResponse>(json, JsonOptions);
        if (result is null || !result.Ok)
        {
            throw new InvalidOperationException("ggCON HTTP Logs Antwort war leer oder ok=false.");
        }

        return result;
    }

    private string RequireBaseUrl()
    {
        var baseUrl = BuildBaseUrl();
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException("ggCON HTTP Base URL fehlt.");
        }

        return baseUrl;
    }

    private HttpClient CreateClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(12)
        };

        var password = string.IsNullOrWhiteSpace(_settings.GgconHttpPassword)
            ? _settings.Password
            : _settings.GgconHttpPassword;

        if (!string.IsNullOrWhiteSpace(password))
        {
            client.DefaultRequestHeaders.Remove("X-Password");
            client.DefaultRequestHeaders.Add("X-Password", password.Trim());
        }

        return client;
    }

    private string BuildBaseUrl()
    {
        var configuredBaseUrl = NormalizeConfiguredBaseUrl(_settings.GgconHttpBaseUrl);
        if (!string.IsNullOrWhiteSpace(configuredBaseUrl))
        {
            return configuredBaseUrl;
        }

        // Direct ggCON HTTP API fallback.
        // On GGHost the HTTP API can be exposed next to RCON, e.g.:
        // RCON 5377 -> HTTP API 5376.
        // Use GgconHttpBaseUrl only for a full custom/proxy URL.
        var host = string.IsNullOrWhiteSpace(_settings.Host) ? "127.0.0.1" : _settings.Host.Trim();
        var port = ResolveDirectHttpPort();
        return $"http://{host}:{port}";
    }

    private int ResolveDirectHttpPort()
    {
        if (_settings.GgconHttpPort > 0)
        {
            return _settings.GgconHttpPort;
        }

        if (_settings.Port > 1)
        {
            return _settings.Port - 1;
        }

        return 5376;
    }

    private static string NormalizeConfiguredBaseUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var url = value.Trim().Trim('\"', '\'').Trim();
        if (string.IsNullOrWhiteSpace(url))
        {
            return string.Empty;
        }

        // Accept pasted values like ggcon.gghost.games/s/6139982 without scheme.
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            url = "https://" + url;
        }

        // Accept pasted full endpoint URLs and reduce them to the base path.
        foreach (var endpoint in new[] { "/weather.json", "/server.json", "/health" })
        {
            if (url.EndsWith(endpoint, StringComparison.OrdinalIgnoreCase))
            {
                url = url[..^endpoint.Length];
                break;
            }
        }

        return url.TrimEnd('/');
    }

    private static string Combine(string baseUrl, string path)
    {
        return baseUrl.TrimEnd('/') + "/" + path.TrimStart('/');
    }

    private static GgconPlayerAccountResponse? DeserializePlayerAccount(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("player", out var playerElement) &&
            playerElement.ValueKind == JsonValueKind.Object)
        {
            var player = JsonSerializer.Deserialize<GgconPlayerAccountResponse>(playerElement.GetRawText(), JsonOptions);
            if (player is not null)
            {
                player.Ok = !root.TryGetProperty("ok", out var okElement) ||
                            okElement.ValueKind != JsonValueKind.False;
            }

            return player;
        }

        return JsonSerializer.Deserialize<GgconPlayerAccountResponse>(json, JsonOptions);
    }

    private static string Truncate(string value, int max)
    {
        value = value.Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal).Trim();
        return value.Length <= max ? value : value[..max] + "...";
    }
}

public sealed class GgconPlayerAccountResponse
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("characterName")]
    public string? CharacterName { get; set; }

    [JsonPropertyName("steamName")]
    public string? SteamName { get; set; }

    [JsonPropertyName("userId")]
    public string? UserId { get; set; }

    [JsonPropertyName("fame")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public double? Fame { get; set; }

    [JsonPropertyName("accountBalance")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public double? AccountBalance { get; set; }

    [JsonPropertyName("goldBalance")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public double? GoldBalance { get; set; }

    public string DisplayName => !string.IsNullOrWhiteSpace(CharacterName) ? CharacterName! : SteamName ?? UserId ?? "Unbekannt";
}

public sealed class GgconPlayersResponse
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("players")]
    public List<GgconPlayerAccountResponse> Players { get; set; } = new();
}

public sealed class GgconSquadsResponse
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("squads")]
    public List<GgconSquadResponse> Squads { get; set; } = new();
}

public sealed class GgconSquadResponse
{
    [JsonPropertyName("id")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("score")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public double Score { get; set; }

    [JsonPropertyName("members")]
    public List<GgconSquadMemberResponse> Members { get; set; } = new();
}

public sealed class GgconSquadMemberResponse
{
    [JsonPropertyName("characterName")]
    public string CharacterName { get; set; } = string.Empty;

    [JsonPropertyName("steamId")]
    public string SteamId { get; set; } = string.Empty;

    [JsonPropertyName("rank")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int Rank { get; set; }

    [JsonPropertyName("rankName")]
    public string RankName { get; set; } = string.Empty;

    [JsonPropertyName("online")]
    public bool Online { get; set; }
}

public sealed class GgconLogsResponse
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("lines")]
    public List<GgconLogLine> Lines { get; set; } = new();

    [JsonPropertyName("next")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public long? Next { get; set; }
}

public sealed class GgconLogLine
{
    [JsonPropertyName("t")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public long T { get; set; }

    [JsonPropertyName("src")]
    public string Source { get; set; } = string.Empty;

    [JsonPropertyName("line")]
    public string Line { get; set; } = string.Empty;
}

public sealed record GgconCurrencyChangeRequest(
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("amount")] int Amount);

public sealed record GgconCommandRequest(
    [property: JsonPropertyName("command")] string Command);

public sealed class GgconCommandResponse
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("accepted")]
    public bool? Accepted { get; set; }

    [JsonPropertyName("dispatched")]
    public bool? Dispatched { get; set; }

    [JsonPropertyName("command")]
    public string Command { get; set; } = string.Empty;

    [JsonPropertyName("lines")]
    public List<string> Lines { get; set; } = new();

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

public sealed class GgconOkResponse
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }
}

public sealed class GgconWeatherResponse
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("timeOfDay")]
    public double? TimeOfDay { get; set; }

    [JsonPropertyName("airTemperature")]
    public double? AirTemperature { get; set; }

    [JsonPropertyName("waterTemperature")]
    public double? WaterTemperature { get; set; }

    [JsonPropertyName("rainIntensity")]
    public double? RainIntensity { get; set; }

    [JsonPropertyName("snowIntensity")]
    public double? SnowIntensity { get; set; }

    [JsonPropertyName("fogDensity")]
    public double? FogDensity { get; set; }

    [JsonPropertyName("windSpeedKph")]
    public double? WindSpeedKph { get; set; }

    [JsonPropertyName("lightningRate")]
    public double? LightningRate { get; set; }

    [JsonPropertyName("cirrostratusCoverage")]
    public double? CirrostratusCoverage { get; set; }

    [JsonPropertyName("cumulonimbusCoverage")]
    public double? CumulonimbusCoverage { get; set; }

    [JsonPropertyName("nimbostratusCoverage")]
    public double? NimbostratusCoverage { get; set; }

    public string FormatIngameTime()
    {
        if (!TimeOfDay.HasValue) return "--:--";

        var totalMinutes = (int)Math.Round(TimeOfDay.Value * 60.0, MidpointRounding.AwayFromZero);
        totalMinutes %= 24 * 60;
        if (totalMinutes < 0) totalMinutes += 24 * 60;
        var hour = totalMinutes / 60;
        var minute = totalMinutes % 60;
        return $"{hour:00}:{minute:00}";
    }

    public double GetWeatherScore()
    {
        // SCUM/GGHost weather intensity maps directly to nimbostratusCoverage.
        // Other fields like fogDensity and cirrostratusCoverage can stay high even when the weather slider is 0%,
        // so they must not be mixed into the displayed weather value.
        return Clamp01(NimbostratusCoverage);
    }

    public string GetWeatherIcon()
    {
        var score = GetWeatherScore();

        if (score >= 0.90d) return "⛈️";
        if (score >= 0.70d) return "🌧️";
        if (score >= 0.50d) return "☁️";
        if (score >= 0.30d) return "⛅";
        if (score >= 0.10d) return "🌤️";
        return "☀️";
    }


    private static double Clamp01(double? value)
    {
        if (!value.HasValue)
        {
            return 0d;
        }

        return Math.Max(0d, Math.Min(1d, value.Value));
    }

    public static string FormatTemperatureValue(double? value)
    {
        return value.HasValue
            ? Math.Round(value.Value, 0, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture)
            : "--";
    }
}
