namespace ScumRconTool.Services;

public sealed class KillFeedAutomationService
{
    private readonly SftpLogService _sftpLogService;
    private readonly Func<string, Task<string>> _sendRconAsync;
    private readonly Action<string> _log;
    private readonly KillLogSeenStore _seen = new();
    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    public KillFeedAutomationService(SftpLogService sftpLogService, Func<string, Task<string>> sendRconAsync, Action<string> log)
    {
        _sftpLogService = sftpLogService;
        _sendRconAsync = sendRconAsync;
        _log = log;
    }

    public bool IsRunning => _loopTask is not null && !_loopTask.IsCompleted;

    public void Start(BotSettings settings)
    {
        if (IsRunning) return;

        var pollSeconds = Math.Max(10, settings.KillPollSeconds <= 0 ? 30 : settings.KillPollSeconds);
        _cts = new CancellationTokenSource();
        _loopTask = Task.Run(() => LoopAsync(settings, TimeSpan.FromSeconds(pollSeconds), _cts.Token));
        _log($"Killfeed gestartet. Poll: {pollSeconds}s");
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts = null;
        _log("Killfeed gestoppt.");
    }

    public async Task<IReadOnlyList<KillLogEntry>> ScanOnceAsync(BotSettings settings, CancellationToken cancellationToken = default)
    {
        var pattern = string.IsNullOrWhiteSpace(settings.FtpKillLogPattern) ? "kill*.log" : settings.FtpKillLogPattern.Trim();
        var local = await _sftpLogService.DownloadLatestLogAsync(pattern, "Kill", cancellationToken);
        if (string.IsNullOrWhiteSpace(local) || !File.Exists(local))
        {
            _log($"Killfeed: keine Killlog-Datei gefunden. Pattern: {pattern}");
            return Array.Empty<KillLogEntry>();
        }

        _log("Killfeed: Killlog geladen: " + local);
        var entries = KillLogParser.ParseFile(local).OrderBy(x => x.Timestamp ?? DateTime.MinValue).ToList();
        if (entries.Count == 0)
        {
            _log("Killfeed: Datei gelesen, aber keine Kill-Zeilen erkannt.");
            return Array.Empty<KillLogEntry>();
        }

        if (_seen.IsEmpty)
        {
            foreach (var entry in entries)
            {
                _seen.Add(entry);
            }

            _seen.Save();
            _log($"Killfeed: Erstlauf, {entries.Count} vorhandene Kill-Zeile(n) gemerkt, ohne Broadcast.");
            return entries;
        }

        var newEntries = entries.Where(x => !_seen.Contains(x)).ToList();
        if (newEntries.Count == 0)
        {
            _log($"Killfeed: {entries.Count} Kill-Zeile(n) gelesen, keine neuen Kills.");
            return entries;
        }

        var announced = 0;
        foreach (var entry in newEntries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var text = SanitizeBroadcastText(entry.ToAnnounceText(settings.KillAnnounceTemplate));
            if (string.IsNullOrWhiteSpace(text))
            {
                _seen.Add(entry);
                continue;
            }

            var color = NormalizeBroadcastColor(settings.KillAnnounceColor);
            var command = $"#Broadcast {color} {text}";
            try
            {
                await _sendRconAsync(command);
                _seen.Add(entry);
                announced++;
                _log($"Killfeed Broadcast: {entry.Killer} -> {entry.Victim}");
            }
            catch (Exception ex)
            {
                _log("Killfeed: RCON Broadcast fehlgeschlagen, Kill bleibt fuer spaeter offen: " + ex.Message);
                AppLogService.WriteException("KillFeedBroadcast", ex);
                break;
            }
        }

        _seen.Save();
        _log($"Killfeed: {newEntries.Count} neue Kill(s), {announced} Broadcast(s) gesendet.");
        return entries;
    }

    private async Task LoopAsync(BotSettings settings, TimeSpan interval, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await ScanOnceAsync(settings, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _log("Killfeed Fehler: " + ex.Message);
                AppLogService.WriteException("KillFeed", ex);
            }

            try
            {
                await Task.Delay(interval, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }
    }


    private static string NormalizeBroadcastColor(string? value)
    {
        var color = (value ?? string.Empty).Trim();
        return color.Equals("White", StringComparison.OrdinalIgnoreCase) ? "White" :
            color.Equals("Cyan", StringComparison.OrdinalIgnoreCase) ? "Cyan" :
            color.Equals("Green", StringComparison.OrdinalIgnoreCase) ? "Green" :
            color.Equals("Red", StringComparison.OrdinalIgnoreCase) ? "Red" :
            color.Equals("Error", StringComparison.OrdinalIgnoreCase) ? "Error" :
            color.Equals("ServerMessage", StringComparison.OrdinalIgnoreCase) ? "ServerMessage" :
            "Yellow";
    }

    private static string SanitizeBroadcastText(string value)
    {
        value = (value ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
        value = string.Join(' ', value.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries));
        return value.Length <= 220 ? value : value[..220] + " ...";
    }
}
