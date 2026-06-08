using System.Text.Json;

namespace ScumRconTool.Services;

public sealed class ChatCommandAutomationService
{
    private readonly SftpLogService _sftpLogService;
    private readonly Func<string, Task<string>> _sendRconAsync;
    private readonly Action<string> _log;
    private readonly FileReadPositionStore _positions = new("chat-command-positions.json");
    private readonly AutomationLimiter _limiter = new();
    private readonly AutomationLimiter _globalLimiter = new();
    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    public ChatCommandAutomationService(SftpLogService sftpLogService, Func<string, Task<string>> sendRconAsync, Action<string> log)
    {
        _sftpLogService = sftpLogService;
        _sendRconAsync = sendRconAsync;
        _log = log;
    }

    public bool IsRunning => _loopTask is not null && !_loopTask.IsCompleted;

    public void Start(BotSettings settings)
    {
        if (IsRunning) return;
        var pollSeconds = Math.Max(5, settings.AutomationPollSeconds <= 0 ? 30 : settings.AutomationPollSeconds);
        _cts = new CancellationTokenSource();
        _loopTask = Task.Run(() => LoopAsync(settings, TimeSpan.FromSeconds(pollSeconds), _cts.Token));
        _log($"Chat Commands gestartet. Poll: {pollSeconds}s");
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts = null;
        _log("Chat Commands gestoppt.");
    }

    public async Task ScanOnceAsync(BotSettings settings, CancellationToken cancellationToken = default)
    {
        var rules = LoadRules(settings.ChatAutomationRulesJson);
        if (rules.Count == 0)
        {
            _log("Chat Commands: keine Regeln konfiguriert.");
            return;
        }

        using var fileLock = await ChatLogFileCoordinator.EnterAsync(cancellationToken);

        var local = await _sftpLogService.DownloadLatestLogAsync(settings.FtpChatLogPattern, "Chat", cancellationToken);
        if (string.IsNullOrWhiteSpace(local) || !File.Exists(local))
        {
            _log($"Chat Commands: keine Chatlog-Datei gefunden. Pattern: {settings.FtpChatLogPattern}");
            return;
        }

        _log("Chat Commands: Chatlog geladen: " + local);
        var lines = ScumLogFileReader.ReadNewLines(local, _positions, Path.GetFileName(local));
        if (lines.Count == 0)
        {
            _log("Chat Commands: keine neuen Zeilen.");
            return;
        }

        var parsed = 0;
        var skippedNonGlobal = 0;
        var executed = 0;
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

            foreach (var rule in rules.Where(x => x.Enabled))
            {
                if (!AutomationLogParser.IsMatch(rule, message.Message)) continue;

                var globalKey = AutomationLogParser.BuildGlobalChatCooldownKey(rule);
                if (!_globalLimiter.TryAcquire(globalKey, TimeSpan.FromSeconds(Math.Max(0, rule.GlobalCooldownSeconds)), out var globalRemaining))
                {
                    _log($"Chat Commands: globale Sperre fuer '{rule.Trigger}' noch {globalRemaining.TotalSeconds:0}s.");
                    continue;
                }

                var playerKey = AutomationLogParser.BuildChatCooldownKey(rule, message);
                if (!_limiter.TryAcquire(playerKey, TimeSpan.FromSeconds(Math.Max(0, rule.CooldownSeconds)), out var remaining))
                {
                    _log($"Chat Commands: Cooldown fuer {message.PlayerName}/{rule.Trigger} noch {remaining.TotalSeconds:0}s.");
                    continue;
                }

                if (!AutomationLogParser.TryBuildChatCommand(rule, message, out var command, out var error))
                {
                    _log("Chat Commands: " + error);
                    continue;
                }

                if ((rule.DelaySeconds > 0) && (!string.IsNullOrWhiteSpace(command) || !string.IsNullOrWhiteSpace(rule.Response)))
                {
                    _log($"Chat Commands: Aktion fuer {message.PlayerName}/{rule.Trigger} in {rule.DelaySeconds}s geplant.");
                    await Task.Delay(TimeSpan.FromSeconds(Math.Max(0, rule.DelaySeconds)), cancellationToken);
                }

                if (!string.IsNullOrWhiteSpace(command))
                {
                    await _sendRconAsync(command);
                    executed++;
                    _log($"Chat Commands: ausgefuehrt fuer {message.PlayerName}: {command}");
                }

                if (!string.IsNullOrWhiteSpace(rule.Response))
                {
                    var response = AutomationLogParser.ApplyPlaceholders(rule.Response, message);
                    response = response.Replace("\r", " ").Replace("\n", " ").Trim();
                    if (!string.IsNullOrWhiteSpace(response))
                    {
                        await _sendRconAsync(CommandRegistry.Broadcast("Cyan", response));
                        executed++;
                        _log($"Chat Commands: Antwort gesendet fuer {message.PlayerName}: {response}");
                    }
                }
            }
        }

        _log($"Chat Commands: {lines.Count} neue Zeilen, {parsed} Chat-Zeilen, {skippedNonGlobal} nicht-globale Zeilen uebersprungen, {executed} Aktionen.");
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
                _log("Chat Commands Fehler: " + ex.Message);
                AppLogService.WriteException("ChatCommands", ex);
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

    private static List<ChatAutomationRule> LoadRules(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<ChatAutomationRule>();
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
        return JsonSerializer.Deserialize<List<ChatAutomationRule>>(json, options) ?? new List<ChatAutomationRule>();
    }

    public static string BuildDefaultRulesJson()
    {
        var rules = new[]
        {
            new ChatAutomationRule
            {
                Trigger = "/help",
                MatchMode = "equals",
                Response = "[Server] Befehle: /help, /wc, /discord",
                DelaySeconds = 0,
                CooldownSeconds = 30,
                GlobalCooldownSeconds = 3
            },
            new ChatAutomationRule
            {
                Trigger = "/discord",
                MatchMode = "equals",
                Response = "[Server] Discord-Link bitte hier eintragen.",
                DelaySeconds = 0,
                CooldownSeconds = 60,
                GlobalCooldownSeconds = 5
            },
            new ChatAutomationRule
            {
                Trigger = "/vote day",
                MatchMode = "equals",
                Command = "#vote day",
                ExecuteAsChatPlayer = true,
                DelaySeconds = 0,
                Response = "[Server] {name} hat eine Tag-Abstimmung gestartet.",
                CooldownSeconds = 300,
                GlobalCooldownSeconds = 30
            }
        };

        return JsonSerializer.Serialize(rules, new JsonSerializerOptions { WriteIndented = true });
    }
}
