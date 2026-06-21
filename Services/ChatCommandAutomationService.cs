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
    private readonly VoteCooldownStore _voteCooldowns = new();
    private readonly Dictionary<string, PendingPaidVote> _pendingPaidVotes = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _cts;
    private Task? _loopTask;
    private long? _ggconChatLogCursorMs;

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
        var pollSeconds = ResolvePollSeconds(settings);
        _cts = new CancellationTokenSource();
        _loopTask = Task.Run(() => LoopAsync(settings, TimeSpan.FromSeconds(pollSeconds), _cts.Token));
        var source = settings.UseGgconLogsForChatCommands ? "ggCON HTTP Logs" : "SFTP Chatlog";
        _log($"Chat Commands gestartet. Quelle: {source}, Poll: {pollSeconds}s");
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts = null;
        _log("Chat Commands gestoppt.");
    }

    private static int ResolvePollSeconds(BotSettings settings)
    {
        if (settings.UseGgconLogsForChatCommands)
        {
            return Math.Max(1, settings.GgconChatCommandPollSeconds <= 0 ? 3 : settings.GgconChatCommandPollSeconds);
        }

        return Math.Max(5, settings.AutomationPollSeconds <= 0 ? 30 : settings.AutomationPollSeconds);
    }

    public async Task ScanOnceAsync(BotSettings settings, CancellationToken cancellationToken = default)
    {
        var rules = LoadRules(settings.ChatAutomationRulesJson);
        if (rules.Count == 0)
        {
            _log("Chat Commands: keine Regeln konfiguriert.");
            return;
        }

        var lines = settings.UseGgconLogsForChatCommands
            ? await ReadChatLinesFromGgconLogsAsync(settings, cancellationToken)
            : await ReadChatLinesFromSftpAsync(settings, cancellationToken);

        if (lines.Count == 0)
        {
            _log("Chat Commands: keine neuen Zeilen.");
            return;
        }

        var parsed = 0;
        var executed = 0;
        foreach (var line in lines)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var message = AutomationLogParser.ParseChatLine(line);
            if (message is null) continue;
            parsed++;

            if (settings.PaidVotesEnabled && IsVoteConfirmation(message))
            {
                if (await HandlePaidVoteConfirmationAsync(settings, message, cancellationToken))
                {
                    executed++;
                }

                continue;
            }

            foreach (var rule in rules.Where(x => x.Enabled))
            {
                if (!AutomationLogParser.IsMatch(rule, message.Message)) continue;

                var isVoteRule = IsVoteRule(rule);
                var voteKey = isVoteRule ? BuildVoteCooldownKey(message) : string.Empty;
                if (isVoteRule && !settings.PaidVotesEnabled)
                {
                    var voteCooldown = TimeSpan.FromHours(Math.Max(1, settings.VoteCooldownHours <= 0 ? 24 : settings.VoteCooldownHours));
                    if (!_voteCooldowns.CanVote(voteKey, voteCooldown, out var voteRemaining, out _))
                    {
                        _log($"Chat Commands: Vote fuer {message.PlayerName} blockiert. Restzeit: {FormatDuration(voteRemaining)}.");
                        await SendVoteBlockedResponseAsync(settings, message, voteRemaining);
                        continue;
                    }

                    _voteCooldowns.Prune(TimeSpan.FromDays(7));
                }

                if (isVoteRule && settings.PaidVotesEnabled)
                {
                    if (!AutomationLogParser.TryBuildChatCommand(rule, message, out var paidVoteCommand, out var paidVoteError))
                    {
                        _log("Chat Commands: " + paidVoteError);
                        continue;
                    }

                    StorePendingPaidVote(settings, message, rule, paidVoteCommand);
                    await SendPaidVotePromptAsync(settings, message);
                    executed++;
                    continue;
                }

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

                    if (isVoteRule)
                    {
                        _voteCooldowns.RecordVote(voteKey);
                        _log($"Chat Commands: Vote-Cooldown fuer {message.PlayerName} gespeichert: {Math.Max(1, settings.VoteCooldownHours <= 0 ? 24 : settings.VoteCooldownHours)}h.");
                    }
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

        PrunePendingPaidVotes(settings);
        _log($"Chat Commands: {lines.Count} neue Zeilen, {parsed} Chat-Zeilen aus allen Kanaelen verarbeitet, {executed} Aktionen.");
    }

    private async Task<IReadOnlyList<string>> ReadChatLinesFromSftpAsync(BotSettings settings, CancellationToken cancellationToken)
    {
        using var fileLock = await ChatLogFileCoordinator.EnterAsync(cancellationToken);

        var local = await _sftpLogService.DownloadLatestLogAsync(settings.FtpChatLogPattern, "Chat", cancellationToken);
        if (string.IsNullOrWhiteSpace(local) || !File.Exists(local))
        {
            _log($"Chat Commands: keine Chatlog-Datei gefunden. Pattern: {settings.FtpChatLogPattern}");
            return new List<string>();
        }

        _log("Chat Commands: Chatlog geladen: " + local);
        return ScumLogFileReader.ReadNewLines(local, _positions, Path.GetFileName(local));
    }

    private async Task<IReadOnlyList<string>> ReadChatLinesFromGgconLogsAsync(BotSettings settings, CancellationToken cancellationToken)
    {
        var api = new GgconHttpApiService(settings);
        _ggconChatLogCursorMs ??= DateTimeOffset.UtcNow
            .AddSeconds(-Math.Max(0, settings.GgconChatCommandInitialBackfillSeconds))
            .ToUnixTimeMilliseconds();

        var result = await api.GetLogsAsync(_ggconChatLogCursorMs, "chat", cancellationToken);
        if (result.Next.HasValue && result.Next.Value > 0)
        {
            _ggconChatLogCursorMs = result.Next.Value;
        }
        else if (result.Lines.Count > 0)
        {
            _ggconChatLogCursorMs = result.Lines.Max(x => x.T) + 1;
        }

        var lines = result.Lines
            .Where(x => !string.IsNullOrWhiteSpace(x.Line))
            .Where(x => string.IsNullOrWhiteSpace(x.Source) || x.Source.Equals("chat", StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.T)
            .Select(x => x.Line)
            .ToList();

        _log($"Chat Commands: ggCON Logs gelesen. Chat-Zeilen: {lines.Count}, Cursor: {_ggconChatLogCursorMs}.");
        return lines;
    }

    private async Task<bool> HandlePaidVoteConfirmationAsync(BotSettings settings, ChatLogMessage message, CancellationToken cancellationToken)
    {
        var key = BuildVoteCooldownKey(message);
        var confirmationUtc = message.LoggedAtUtc ?? DateTime.UtcNow;
        if (!_pendingPaidVotes.TryGetValue(key, out var pending) || pending.ExpiresUtc <= confirmationUtc)
        {
            _pendingPaidVotes.Remove(key);
            await SendVoteTemplateAsync(settings.VoteNoPendingResponse, settings, message, null, 0, cancellationToken);
            _log($"Chat Commands: /ja von {message.PlayerName}, aber kein offener Vote-Kauf gefunden.");
            return true;
        }

        if (string.IsNullOrWhiteSpace(message.SteamId))
        {
            await SendVoteTemplateAsync(settings.VotePaymentFailedResponse, settings, message, pending, 0, cancellationToken);
            _log($"Chat Commands: Vote-Kauf fuer {message.PlayerName} abgebrochen: keine SteamID in der Chat-Zeile.");
            return true;
        }

        if (string.IsNullOrWhiteSpace(pending.Command))
        {
            _pendingPaidVotes.Remove(key);
            await SendVoteTemplateAsync(settings.VotePaymentFailedResponse, settings, message, pending, 0, cancellationToken);
            _log($"Chat Commands: Vote-Kauf fuer {message.PlayerName} abgebrochen: Regel erzeugte keinen Vote-Command.");
            return true;
        }

        var api = new GgconHttpApiService(settings);
        var balance = 0d;
        var charged = false;

        try
        {
            var player = await api.GetPlayerAccountAsync(message.SteamId, cancellationToken);
            if (!player.AccountBalance.HasValue)
            {
                await SendVoteTemplateAsync(settings.VotePaymentFailedResponse, settings, message, pending, balance, cancellationToken);
                _log($"Chat Commands: Vote-Kauf fuer {message.PlayerName} abgebrochen: ggCON lieferte kein accountBalance.");
                return true;
            }

            balance = player.AccountBalance.Value;
            _log($"Chat Commands: Kontostand fuer {message.PlayerName}: {FormatMoney(balance)}$, Vote kostet {pending.Cost}$.");
            if (balance < pending.Cost)
            {
                await SendVoteTemplateAsync(settings.VoteInsufficientFundsResponse, settings, message, pending, balance, cancellationToken);
                _log($"Chat Commands: Vote-Kauf fuer {message.PlayerName} blockiert. Guthaben {FormatMoney(balance)}$, benoetigt {pending.Cost}$.");
                return true;
            }

            await api.RemovePlayerCurrencyAsync(message.SteamId, pending.Cost, cancellationToken);
            charged = true;

            if (pending.Rule.DelaySeconds > 0)
            {
                _log($"Chat Commands: bezahlter Vote fuer {message.PlayerName}/{pending.Rule.Trigger} in {pending.Rule.DelaySeconds}s geplant.");
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(0, pending.Rule.DelaySeconds)), cancellationToken);
            }

            await _sendRconAsync(pending.Command);
            _pendingPaidVotes.Remove(key);
            _log($"Chat Commands: bezahlter Vote ausgefuehrt fuer {message.PlayerName}: {pending.Command} ({pending.Cost}$).");

            var successTemplate = string.IsNullOrWhiteSpace(pending.Rule.Response)
                ? settings.VotePurchaseSuccessResponse
                : pending.Rule.Response;
            await SendVoteTemplateAsync(successTemplate, settings, message, pending, balance - pending.Cost, cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            if (charged)
            {
                try
                {
                    await api.AddPlayerCurrencyAsync(message.SteamId, pending.Cost, cancellationToken);
                    _log($"Chat Commands: Vote-Kauf fuer {message.PlayerName} fehlgeschlagen, {pending.Cost}$ wurden erstattet.");
                }
                catch (Exception refundEx)
                {
                    AppLogService.WriteException("PaidVoteRefund", refundEx);
                    _log($"Chat Commands: Vote-Kauf Erstattung fuer {message.PlayerName} fehlgeschlagen: {refundEx.Message}");
                }
            }

            AppLogService.WriteException("PaidVote", ex);
            await SendVoteTemplateAsync(settings.VotePaymentFailedResponse, settings, message, pending, balance, cancellationToken);
            _log($"Chat Commands: Vote-Kauf fuer {message.PlayerName} fehlgeschlagen: {ex.Message}");
            return true;
        }
    }

    private void StorePendingPaidVote(BotSettings settings, ChatLogMessage message, ChatAutomationRule rule, string command)
    {
        var key = BuildVoteCooldownKey(message);
        var cost = Math.Max(1, settings.VotePrice <= 0 ? 5000 : settings.VotePrice);
        var timeoutSeconds = Math.Max(10, settings.VoteConfirmationTimeoutSeconds <= 0 ? 60 : settings.VoteConfirmationTimeoutSeconds);
        _pendingPaidVotes[key] = new PendingPaidVote(
            CloneMessage(message),
            rule,
            command,
            cost,
            DateTime.UtcNow.AddSeconds(timeoutSeconds));

        _log($"Chat Commands: Vote-Kauf fuer {message.PlayerName}/{rule.Trigger} vorgemerkt. Preis: {cost}$, Timeout: {timeoutSeconds}s.");
    }

    private async Task SendPaidVotePromptAsync(BotSettings settings, ChatLogMessage message)
    {
        var key = BuildVoteCooldownKey(message);
        _pendingPaidVotes.TryGetValue(key, out var pending);
        await SendVoteTemplateAsync(settings.VotePurchasePromptResponse, settings, message, pending, 0, CancellationToken.None);
    }

    private async Task SendVoteTemplateAsync(
        string template,
        BotSettings settings,
        ChatLogMessage message,
        PendingPaidVote? pending,
        double balance,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var response = ApplyVotePlaceholders(template, settings, message, pending, balance);
        if (string.IsNullOrWhiteSpace(response))
        {
            return;
        }

        await SendPlayerNoticeAsync(message, response);
    }

    private async Task SendPlayerNoticeAsync(ChatLogMessage message, string response)
    {
        response = response.Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal).Trim();
        if (string.IsNullOrWhiteSpace(response))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(message.SteamId))
        {
            await _sendRconAsync(CommandRegistry.MessagePlayer(message.SteamId, "Cyan", response));
            return;
        }

        await _sendRconAsync(CommandRegistry.Broadcast("Cyan", response));
    }

    private static string ApplyVotePlaceholders(
        string template,
        BotSettings settings,
        ChatLogMessage message,
        PendingPaidVote? pending,
        double balance)
    {
        var cost = pending?.Cost ?? Math.Max(1, settings.VotePrice <= 0 ? 5000 : settings.VotePrice);
        var timeoutSeconds = Math.Max(10, settings.VoteConfirmationTimeoutSeconds <= 0 ? 60 : settings.VoteConfirmationTimeoutSeconds);
        var remaining = pending is null
            ? TimeSpan.Zero
            : pending.ExpiresUtc - DateTime.UtcNow;
        if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;

        return AutomationLogParser.ApplyPlaceholders(template, message)
            .Replace("{cost}", cost.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("{price}", cost.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("{balance}", FormatMoney(balance), StringComparison.OrdinalIgnoreCase)
            .Replace("{timeoutSeconds}", timeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("{confirmCommand}", "/ja", StringComparison.OrdinalIgnoreCase)
            .Replace("{voteCommand}", pending?.Command ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("{remaining}", FormatDuration(remaining), StringComparison.OrdinalIgnoreCase)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();
    }

    private void PrunePendingPaidVotes(BotSettings settings)
    {
        if (_pendingPaidVotes.Count == 0)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var processingGrace = TimeSpan.FromMinutes(5);
        var expired = _pendingPaidVotes
            .Where(x => x.Value.ExpiresUtc.Add(processingGrace) <= now)
            .Select(x => x.Key)
            .ToList();

        foreach (var key in expired)
        {
            _pendingPaidVotes.Remove(key);
        }

        if (expired.Count > 0)
        {
            _log($"Chat Commands: {expired.Count} offene Vote-Kaeufe abgelaufen.");
        }
    }

    private async Task SendVoteBlockedResponseAsync(BotSettings settings, ChatLogMessage message, TimeSpan remaining)
    {
        var template = settings.VoteCooldownBlockedResponse;
        if (string.IsNullOrWhiteSpace(template))
        {
            return;
        }

        var response = AutomationLogParser.ApplyPlaceholders(template, message)
            .Replace("{cooldownHours}", Math.Max(1, settings.VoteCooldownHours <= 0 ? 24 : settings.VoteCooldownHours).ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("{remaining}", FormatDuration(remaining), StringComparison.OrdinalIgnoreCase)
            .Replace("{remainingHours}", Math.Ceiling(Math.Max(0, remaining.TotalHours)).ToString("0", System.Globalization.CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("{remainingMinutes}", Math.Ceiling(Math.Max(0, remaining.TotalMinutes)).ToString("0", System.Globalization.CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();

        if (string.IsNullOrWhiteSpace(response))
        {
            return;
        }

        await _sendRconAsync(CommandRegistry.Broadcast("Cyan", response));
    }

    private static bool IsVoteConfirmation(ChatLogMessage message)
    {
        var text = (message.Message ?? string.Empty).Trim();
        return text.Equals("/ja", StringComparison.OrdinalIgnoreCase)
            || text.Equals("ja", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsVoteRule(ChatAutomationRule rule)
    {
        var trigger = (rule.Trigger ?? string.Empty).Trim();
        var command = (rule.Command ?? string.Empty).Trim();

        if (trigger.StartsWith("/vote", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (command.StartsWith("#vote", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return command.Contains(" #vote", StringComparison.OrdinalIgnoreCase)
            || command.Contains("#ExecAs", StringComparison.OrdinalIgnoreCase) && command.Contains("#vote", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildVoteCooldownKey(ChatLogMessage message)
    {
        if (!string.IsNullOrWhiteSpace(message.SteamId))
        {
            return "steam:" + message.SteamId.Trim();
        }

        if (!string.IsNullOrWhiteSpace(message.PlayerName))
        {
            return "name:" + message.PlayerName.Trim();
        }

        return "raw:" + message.RawLine.Trim();
    }

    private static ChatLogMessage CloneMessage(ChatLogMessage message) => new()
    {
        PlayerName = message.PlayerName,
        SteamId = message.SteamId,
        Channel = message.Channel,
        Message = message.Message,
        RawLine = message.RawLine,
        LoggedAtUtc = message.LoggedAtUtc
    };

    private static string FormatDuration(TimeSpan value)
    {
        if (value < TimeSpan.Zero) value = TimeSpan.Zero;

        if (value.TotalHours >= 1)
        {
            return $"{(int)value.TotalHours:0}h {value.Minutes:00}m";
        }

        if (value.TotalMinutes >= 1)
        {
            return $"{(int)value.TotalMinutes:0}m {value.Seconds:00}s";
        }

        return $"{Math.Max(0, (int)Math.Ceiling(value.TotalSeconds)):0}s";
    }

    private static string FormatMoney(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return "0";
        }

        return Math.Round(value, 0, MidpointRounding.AwayFromZero).ToString("0", System.Globalization.CultureInfo.InvariantCulture);
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
            },
            new ChatAutomationRule
            {
                Trigger = "/vote weather",
                MatchMode = "equals",
                Command = "#vote weather",
                ExecuteAsChatPlayer = true,
                DelaySeconds = 0,
                Response = "[Server] {name} hat eine Wetter-Abstimmung gestartet.",
                CooldownSeconds = 300,
                GlobalCooldownSeconds = 30
            }
        };

        return JsonSerializer.Serialize(rules, new JsonSerializerOptions { WriteIndented = true });
    }

    private sealed record PendingPaidVote(
        ChatLogMessage Message,
        ChatAutomationRule Rule,
        string Command,
        int Cost,
        DateTime ExpiresUtc);
}
