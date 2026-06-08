namespace ScumRconTool.Services;

public sealed class ChatLogDiscordForwarder
{
    private readonly SftpLogService _sftpLogService;
    private readonly DiscordBridgeService _discord;
    private readonly Action<string> _log;
    private readonly FileReadPositionStore _positions = new("chatlog-discord-positions.json");
    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    public ChatLogDiscordForwarder(SftpLogService sftpLogService, DiscordBridgeService discord, Action<string> log)
    {
        _sftpLogService = sftpLogService;
        _discord = discord;
        _log = log;
    }

    public bool IsRunning => _loopTask is not null && !_loopTask.IsCompleted;

    public void Start(BotSettings settings, ulong discordChannelId)
    {
        if (IsRunning) return;
        if (discordChannelId == 0) throw new InvalidOperationException("Discord Chatlog Channel-ID fehlt.");

        var pollSeconds = Math.Max(5, settings.AutomationPollSeconds <= 0 ? 30 : settings.AutomationPollSeconds);
        _cts = new CancellationTokenSource();
        _loopTask = Task.Run(() => LoopAsync(settings, discordChannelId, TimeSpan.FromSeconds(pollSeconds), _cts.Token));
        _log($"Discord Chatlog Forwarder gestartet. Poll: {pollSeconds}s");
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts = null;
        _log("Discord Chatlog Forwarder gestoppt.");
    }

    public async Task ScanOnceAsync(BotSettings settings, ulong discordChannelId, CancellationToken cancellationToken = default)
    {
        using var fileLock = await ChatLogFileCoordinator.EnterAsync(cancellationToken);

        var local = await _sftpLogService.DownloadLatestLogAsync(settings.FtpChatLogPattern, "Chat", cancellationToken);
        if (string.IsNullOrWhiteSpace(local) || !File.Exists(local))
        {
            _log($"Chatlog Discord: keine Datei gefunden. Pattern: {settings.FtpChatLogPattern}");
            return;
        }

        _log("Chatlog Discord: Chatlog geladen: " + local);
        var lines = ScumLogFileReader.ReadNewLines(local, _positions, Path.GetFileName(local));
        if (lines.Count == 0)
        {
            _log("Chatlog Discord: keine neuen Zeilen.");
            return;
        }

        var parsed = 0;
        var skippedNonGlobal = 0;
        var sent = 0;
        foreach (var line in lines)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var message = AutomationLogParser.ParseChatLine(line);
            if (message is null) continue;
            parsed++;
            if (!message.IsGlobal)
            {
                skippedNonGlobal++;
                continue;
            }

            if (message.Message.Contains("[DC]", StringComparison.OrdinalIgnoreCase)) continue;

            await _discord.SendChatEmbedAsync(discordChannelId, message);
            sent++;
        }

        _log($"Chatlog Discord: {lines.Count} neue Zeilen, {parsed} Chat-Zeilen, {skippedNonGlobal} nicht-globale Zeilen uebersprungen, {sent} Embeds gesendet.");
    }

    private async Task LoopAsync(BotSettings settings, ulong discordChannelId, TimeSpan interval, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await ScanOnceAsync(settings, discordChannelId, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _log("Discord Chatlog Forwarder Fehler: " + ex.Message);
                AppLogService.WriteException("ChatLogDiscordForwarder", ex);
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
}
