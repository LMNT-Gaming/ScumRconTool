using System.Net;
using System.Net.Sockets;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ScumRconTool.Services;

public sealed class LocalChallengeAdminService : IAsyncDisposable
{
    private const int MaxRequestBytes = 2 * 1024 * 1024;
    private const int MaxHeaderBytes = 64 * 1024;
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };
    private readonly Action<string> _log;
    private readonly Action<IReadOnlyList<string>> _saved;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private string _token = string.Empty;
    private int _port;
    private HashSet<string> _allowedHosts = new(StringComparer.OrdinalIgnoreCase);
    private string _networkAddress = "127.0.0.1";

    public LocalChallengeAdminService(Action<string> log, Action<IReadOnlyList<string>> saved)
    {
        _log = log;
        _saved = saved;
    }

    public bool IsRunning => _listener is not null;
    public string Url => IsRunning ? $"http://127.0.0.1:{_port}/?token={_token}" : string.Empty;
    public string NetworkUrl => IsRunning ? $"http://{_networkAddress}:{_port}/?token={_token}" : string.Empty;

    public void Start(int port)
    {
        if (IsRunning) return;
        if (port is < 1024 or > 65535) throw new ArgumentOutOfRangeException(nameof(port), "Port must be between 1024 and 65535.");
        _token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        _port = port;
        _networkAddress = FindLanAddress();
        _allowedHosts = BuildAllowedHosts(port);
        _cts = new CancellationTokenSource();
        var listener = new TcpListener(IPAddress.Any, port);
        try { listener.Start(20); }
        catch
        {
            _cts.Dispose();
            _cts = null;
            _token = string.Empty;
            throw;
        }
        _listener = listener;
        _loop = Task.Run(() => AcceptLoopAsync(_cts.Token));
        _log($"Challenge-Webverwaltung gestartet: http://{_networkAddress}:{port}/ (lokales Netzwerk, geschuetzter Sitzungslink)." );
    }

    public async Task StopAsync()
    {
        var listener = _listener;
        var cts = _cts;
        var loop = _loop;
        _listener = null; _cts = null; _loop = null; _token = string.Empty;
        if (listener is null) return;
        cts?.Cancel();
        listener.Stop();
        if (loop is not null)
        {
            try { await loop; } catch (OperationCanceledException) { } catch (SocketException) when (cts?.IsCancellationRequested == true) { }
        }
        cts?.Dispose();
        _log("Lokale Challenge-Verwaltung gestoppt.");
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener!.AcceptTcpClientAsync(ct); }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            _ = Task.Run(() => HandleClientAsync(client, ct), CancellationToken.None);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken serverCt)
    {
        using (client)
        {
            client.ReceiveTimeout = 10_000;
            client.SendTimeout = 10_000;
            try
            {
                using var stream = client.GetStream();
                if (client.Client.RemoteEndPoint is not IPEndPoint remote || !IsPrivateOrLoopback(remote.Address))
                {
                    await WriteJsonAsync(stream, 403, new { error = "local_network_only" }, serverCt);
                    return;
                }
                var request = await ReadRequestAsync(stream, serverCt);
                if (request is null) return;
                if (!request.Headers.TryGetValue("Host", out var host) || !_allowedHosts.Contains(host))
                {
                    await WriteJsonAsync(stream, 421, new { error = "invalid_host" }, serverCt);
                    return;
                }
                await RouteAsync(stream, request, serverCt);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log("Lokale Challenge-Verwaltung: " + ex.Message);
            }
        }
    }

    private async Task RouteAsync(NetworkStream stream, LocalHttpRequest request, CancellationToken ct)
    {
        var uri = new Uri("http://127.0.0.1" + request.Target);
        if (request.Method == "GET" && uri.AbsolutePath == "/")
        {
            var queryToken = ParseQuery(uri.Query).GetValueOrDefault("token", string.Empty);
            if (!TokenEquals(queryToken)) { await WriteTextAsync(stream, 403, "text/plain; charset=utf-8", "Forbidden", ct); return; }
            await WriteResourceAsync(stream, "ScumRconTool.WebAdmin.challenges.html", "text/html; charset=utf-8", ct);
            return;
        }
        if (request.Method == "GET" && uri.AbsolutePath == "/app.css") { await WriteResourceAsync(stream, "ScumRconTool.WebAdmin.challenges.css", "text/css; charset=utf-8", ct); return; }
        if (request.Method == "GET" && uri.AbsolutePath == "/app.js") { await WriteResourceAsync(stream, "ScumRconTool.WebAdmin.challenges.js", "text/javascript; charset=utf-8", ct); return; }
        if (!uri.AbsolutePath.StartsWith("/api/", StringComparison.Ordinal) || !request.Headers.TryGetValue("X-RedRaven-Token", out var headerToken) || !TokenEquals(headerToken))
        {
            await WriteJsonAsync(stream, 403, new { error = "forbidden" }, ct); return;
        }
        if (request.Method == "GET" && uri.AbsolutePath == "/api/challenges")
        {
            var firstRevision = WeeklyTaskDefinitionStore.Revision;
            var definitions = WeeklyTaskDefinitionStore.Load();
            var revision = WeeklyTaskDefinitionStore.Revision;
            if (firstRevision != revision) { definitions = WeeklyTaskDefinitionStore.Load(); revision = WeeklyTaskDefinitionStore.Revision; }
            var targets = WeeklyCommunityTaskService.AvailableStatTargets.Select(x => new { table = x.TableName, column = x.ColumnName, name = x.DisplayName, category = x.Category, key = x.Key });
            var lootPacks = LootPackStore.Load().Where(x => x.Enabled && !string.IsNullOrWhiteSpace(x.Name)).Select(x => x.Name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
            await WriteJsonAsync(stream, 200, new { revision, challenges = definitions, targets, lootPacks }, ct); return;
        }
        if (request.Method == "POST" && uri.AbsolutePath == "/api/challenges")
        {
            var update = JsonSerializer.Deserialize<ChallengeAdminUpdate>(request.Body, Json);
            if (update?.Challenges is null || string.IsNullOrWhiteSpace(update.Revision)) { await WriteJsonAsync(stream, 400, new { error = "invalid_payload" }, ct); return; }
            var validation = Validate(update.Challenges);
            if (validation.Count > 0) { await WriteJsonAsync(stream, 400, new { error = "validation_failed", details = validation }, ct); return; }
            var currentRevision = WeeklyTaskDefinitionStore.Revision;
            if (!string.Equals(currentRevision, update.Revision, StringComparison.Ordinal))
            {
                await WriteJsonAsync(stream, 409, new { error = "configuration_changed", revision = currentRevision }, ct); return;
            }
            var currentDefinitions = WeeklyTaskDefinitionStore.Load();
            var resetCandidates = ChallengeReactivationService.Find(currentDefinitions, update.Challenges);
            if (resetCandidates.Count > 0 && !update.ResetReactivated.HasValue)
            {
                await WriteJsonAsync(stream, 428, new { error = "reset_confirmation_required", challenges = resetCandidates }, ct); return;
            }
            if (!WeeklyTaskDefinitionStore.TrySave(update.Challenges, update.Revision, out var revision))
            {
                await WriteJsonAsync(stream, 409, new { error = "configuration_changed", revision }, ct); return;
            }
            var resetIds = update.ResetReactivated == true ? resetCandidates.Select(x => x.Id).ToList() : new List<string>();
            try { _saved(resetIds); }
            catch (Exception ex) { _log("Lokale Challenge-Verwaltung: Desktop-Aktualisierung fehlgeschlagen: " + ex.Message); }
            _log($"Lokale Challenge-Verwaltung: {update.Challenges.Count} Herausforderungen gespeichert.");
            await WriteJsonAsync(stream, 200, new { ok = true, revision }, ct); return;
        }
        await WriteJsonAsync(stream, 404, new { error = "not_found" }, ct);
    }

    private static List<string> Validate(IReadOnlyList<WeeklyCommunityTaskDefinition> definitions)
    {
        var errors = new List<string>();
        if (definitions.Count > 500) errors.Add("Es koennen maximal 500 Herausforderungen gespeichert werden.");
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var quizNumbers = new HashSet<int>();
        var targets = WeeklyCommunityTaskService.AvailableStatTargets.Select(x => x.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var lootPacks = LootPackStore.Load().Where(x => x.Enabled).Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var item in definitions)
        {
            item.Id = (item.Id ?? string.Empty).Trim(); item.Title = (item.Title ?? string.Empty).Trim();
            var label = string.IsNullOrWhiteSpace(item.Title) ? item.Id : item.Title;
            if (!Regex.IsMatch(item.Id, "^[A-Za-z0-9_.-]{3,80}$")) errors.Add($"{label}: ungueltige ID.");
            else if (!ids.Add(item.Id)) errors.Add($"{label}: ID ist doppelt.");
            if (string.IsNullOrWhiteSpace(item.Title)) errors.Add($"{item.Id}: Titel fehlt.");
            if (!new[] { "Daily", "Weekly", "Event", "Quiz" }.Contains(item.Type ?? string.Empty, StringComparer.OrdinalIgnoreCase)) errors.Add($"{label}: Typ ist ungueltig.");
            if (!new[] { "Community", "PerPlayer" }.Contains(item.GoalScope ?? string.Empty, StringComparer.OrdinalIgnoreCase)) errors.Add($"{label}: Zielmodus ist ungueltig.");
            if (!new[] { "Any", "All" }.Contains(item.GoalLogic ?? string.Empty, StringComparer.OrdinalIgnoreCase)) errors.Add($"{label}: Ziellogik ist ungueltig.");
            if (!new[] { "PerParticipant", "PerSquad" }.Contains(item.RewardDistribution ?? string.Empty, StringComparer.OrdinalIgnoreCase)) errors.Add($"{label}: Auszahlungsmodus ist ungueltig.");
            if (item.DurationHours is < 0 or > 8760) errors.Add($"{label}: Laufzeit muss zwischen 0 und 8760 Stunden liegen.");
            if (item.RewardMoney < 0 || item.RewardFame < 0) errors.Add($"{label}: Rewards duerfen nicht negativ sein.");
            if (!string.IsNullOrWhiteSpace(item.StartUtc) && !DateTimeOffset.TryParse(item.StartUtc, out _)) errors.Add($"{label}: Startdatum ist ungueltig.");
            if (!string.IsNullOrWhiteSpace(item.EndUtc) && !DateTimeOffset.TryParse(item.EndUtc, out _)) errors.Add($"{label}: Enddatum ist ungueltig.");
            foreach (var pack in WeeklyRewardItems.GetConfiguredLootPackNames(item)) if (!lootPacks.Contains(pack)) errors.Add($"{label}: Lootpack '{pack}' existiert nicht oder ist deaktiviert.");
            if (string.Equals(item.Type, "Quiz", StringComparison.OrdinalIgnoreCase))
            {
                if (item.QuizNumber < 1 || (item.Enabled && !quizNumbers.Add(item.QuizNumber))) errors.Add($"{label}: Quiznummer ist ungueltig oder doppelt.");
                if (string.IsNullOrWhiteSpace(item.QuizQuestion) || string.IsNullOrWhiteSpace(item.QuizAcceptedAnswers)) errors.Add($"{label}: Frage und akzeptierte Antworten sind Pflicht.");
                item.GoalScope = "PerPlayer"; item.RewardDistribution = "PerParticipant";
            }
            else
            {
                var goals = item.Goals?.Count > 0 ? item.Goals : [new WeeklyCommunityTaskGoalDefinition { StatTable = item.StatTable, StatColumn = item.StatColumn, Target = item.Target }];
                foreach (var goal in goals.Where(x => x is not null))
                {
                    if (!targets.Contains(goal.StatTable + "." + goal.StatColumn)) errors.Add($"{label}: Statistikziel '{goal.StatTable}.{goal.StatColumn}' ist unbekannt.");
                    if (goal.Target < 1) errors.Add($"{label}: Zielwert muss mindestens 1 sein.");
                }
            }
        }
        return errors;
    }

    private bool TokenEquals(string supplied)
    {
        var left = Encoding.ASCII.GetBytes(_token); var right = Encoding.ASCII.GetBytes(supplied ?? string.Empty);
        return left.Length == right.Length && CryptographicOperations.FixedTimeEquals(left, right);
    }

    private static async Task<LocalHttpRequest?> ReadRequestAsync(NetworkStream stream, CancellationToken ct)
    {
        using var received = new MemoryStream();
        var buffer = new byte[4096];
        var headerEnd = -1;
        while (headerEnd < 0)
        {
            var read = await stream.ReadAsync(buffer, ct);
            if (read == 0) return received.Length == 0 ? null : throw new InvalidDataException("Incomplete HTTP headers.");
            received.Write(buffer, 0, read);
            if (received.Length > MaxHeaderBytes) throw new InvalidDataException("HTTP headers too large.");
            headerEnd = FindHeaderEnd(received.GetBuffer(), (int)received.Length);
        }

        var raw = received.GetBuffer();
        var headerText = Encoding.ASCII.GetString(raw, 0, headerEnd);
        var lines = headerText.Split("\r\n", StringSplitOptions.None);
        if (lines.Length == 0 || string.IsNullOrWhiteSpace(lines[0])) return null;
        var first = lines[0].Split(' '); if (first.Length != 3) throw new InvalidDataException("Invalid HTTP request line.");
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Skip(1))
        {
            var separator = line.IndexOf(':'); if (separator > 0) headers[line[..separator].Trim()] = line[(separator + 1)..].Trim();
        }
        var length = headers.TryGetValue("Content-Length", out var rawLength) && int.TryParse(rawLength, out var parsed) ? parsed : 0;
        if (length is < 0 or > MaxRequestBytes) throw new InvalidDataException("HTTP request body too large.");
        var bodyStart = headerEnd + 4;
        while (received.Length - bodyStart < length)
        {
            var remaining = length - (int)(received.Length - bodyStart);
            var read = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, remaining)), ct);
            if (read == 0) throw new InvalidDataException("Incomplete HTTP request body.");
            received.Write(buffer, 0, read);
        }
        var body = length == 0 ? string.Empty : Encoding.UTF8.GetString(received.GetBuffer(), bodyStart, length);
        return new LocalHttpRequest(first[0].ToUpperInvariant(), first[1], headers, body);
    }

    private static int FindHeaderEnd(byte[] buffer, int length)
    {
        for (var i = 0; i <= length - 4; i++)
            if (buffer[i] == 13 && buffer[i + 1] == 10 && buffer[i + 2] == 13 && buffer[i + 3] == 10) return i;
        return -1;
    }

    private static Dictionary<string, string> ParseQuery(string query) => query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
        .Select(x => x.Split('=', 2)).ToDictionary(x => Uri.UnescapeDataString(x[0]), x => x.Length > 1 ? Uri.UnescapeDataString(x[1].Replace('+', ' ')) : string.Empty, StringComparer.OrdinalIgnoreCase);

    private static async Task WriteResourceAsync(NetworkStream stream, string name, string contentType, CancellationToken ct)
    {
        await using var resource = typeof(LocalChallengeAdminService).Assembly.GetManifestResourceStream(name) ?? throw new FileNotFoundException(name);
        using var memory = new MemoryStream(); await resource.CopyToAsync(memory, ct);
        await WriteAsync(stream, 200, contentType, memory.ToArray(), ct);
    }
    private static Task WriteJsonAsync(NetworkStream stream, int status, object value, CancellationToken ct) => WriteAsync(stream, status, "application/json; charset=utf-8", JsonSerializer.SerializeToUtf8Bytes(value, Json), ct);
    private static Task WriteTextAsync(NetworkStream stream, int status, string contentType, string value, CancellationToken ct) => WriteAsync(stream, status, contentType, Encoding.UTF8.GetBytes(value), ct);
    private static async Task WriteAsync(NetworkStream stream, int status, string contentType, byte[] body, CancellationToken ct)
    {
        var reason = status switch { 200 => "OK", 400 => "Bad Request", 403 => "Forbidden", 404 => "Not Found", 409 => "Conflict", 421 => "Misdirected Request", 428 => "Precondition Required", _ => "Error" };
        var headers = Encoding.ASCII.GetBytes($"HTTP/1.1 {status} {reason}\r\nContent-Type: {contentType}\r\nContent-Length: {body.Length}\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\nContent-Security-Policy: default-src 'self'; img-src 'self' data:; style-src 'self'; script-src 'self'; connect-src 'self'; frame-ancestors 'none'\r\nReferrer-Policy: no-referrer\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(headers, ct); await stream.WriteAsync(body, ct); await stream.FlushAsync(ct);
    }

    public async ValueTask DisposeAsync() => await StopAsync();
    private static HashSet<string> BuildAllowedHosts(int port)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { $"127.0.0.1:{port}", $"localhost:{port}", $"{Dns.GetHostName()}:{port}" };
        foreach (var address in Dns.GetHostAddresses(Dns.GetHostName()).Where(x => x.AddressFamily == AddressFamily.InterNetwork)) result.Add($"{address}:{port}");
        return result;
    }

    private static string FindLanAddress()
    {
        try
        {
            var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                .Where(x => x.OperationalStatus == OperationalStatus.Up && x.NetworkInterfaceType is not NetworkInterfaceType.Loopback and not NetworkInterfaceType.Tunnel)
                .OrderByDescending(x => x.GetIPProperties().GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any)));
            var address = interfaces
                .SelectMany(x => x.GetIPProperties().UnicastAddresses)
                .Select(x => x.Address)
                .FirstOrDefault(x => x.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(x));
            return address?.ToString() ?? "127.0.0.1";
        }
        catch { return "127.0.0.1"; }
    }

    private static bool IsPrivateOrLoopback(IPAddress address)
    {
        if (IPAddress.IsLoopback(address)) return true;
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (address.AddressFamily != AddressFamily.InterNetwork) return false;
        var bytes = address.GetAddressBytes();
        return bytes[0] == 10 ||
               (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) ||
               (bytes[0] == 192 && bytes[1] == 168) ||
               (bytes[0] == 169 && bytes[1] == 254) ||
               (bytes[0] == 100 && bytes[1] is >= 64 and <= 127);
    }

    private sealed record LocalHttpRequest(string Method, string Target, Dictionary<string, string> Headers, string Body);
    private sealed class ChallengeAdminUpdate { public string Revision { get; set; } = string.Empty; public List<WeeklyCommunityTaskDefinition> Challenges { get; set; } = []; public bool? ResetReactivated { get; set; } }
}
