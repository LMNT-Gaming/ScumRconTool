using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Text;
using System.Text.Json;
using ScumRconTool.Models;

namespace ScumRconTool.Services;

public sealed class JoinCommandAutomationService
{
    private readonly SftpLogService _sftpLogService;
    private readonly Func<string, Task<string>> _sendRconAsync;
    private readonly Action<string> _log;
    private readonly FileReadPositionStore _positions = new("join-command-positions.json");
    private readonly AutomationLimiter _limiter = new();
    private readonly JoinCommandStateStore _state = new();
    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    public JoinCommandAutomationService(SftpLogService sftpLogService, Func<string, Task<string>> sendRconAsync, Action<string> log)
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
        _log($"Join Commands gestartet. Poll: {pollSeconds}s, Login-Delay wird pro Join-Zeile geplant.");
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts = null;
        _log("Join Commands gestoppt.");
    }

    public async Task ScanOnceAsync(BotSettings settings, CancellationToken cancellationToken = default)
    {
        var rules = LoadRules(settings.JoinAutomationRulesJson);
        if (rules.Count == 0)
        {
            _log("Join Commands: keine Regeln konfiguriert.");
            return;
        }

        await ExecuteDueAsync(cancellationToken);

        var local = await _sftpLogService.DownloadLatestLogAsync(settings.FtpLoginLogPattern, "Login", cancellationToken);
        if (string.IsNullOrWhiteSpace(local) || !File.Exists(local))
        {
            _log($"Join Commands: keine Loginlog-Datei gefunden. Pattern: {settings.FtpLoginLogPattern}");
            return;
        }

        _log("Join Commands: Loginlog geladen: " + local);
        var lines = ScumLogFileReader.ReadNewLines(local, _positions, Path.GetFileName(local));
        if (lines.Count == 0)
        {
            _log("Join Commands: keine neuen Login-Zeilen.");
            return;
        }

        var parsed = 0;
        var scheduled = 0;
        foreach (var line in lines)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var join = AutomationLogParser.ParseJoinLine(line);
            if (join is null || string.IsNullOrWhiteSpace(join.SteamId)) continue;
            parsed++;

            foreach (var rule in rules.Where(x => x.Enabled))
            {
                if (!AutomationLogParser.TryBuildJoinCommand(rule, join, out var command, out var error))
                {
                    _log("Join Commands: " + error);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(command)) continue;

                var cooldownKey = "join|" + rule.Command + "|" + join.SessionKey;
                if (!_limiter.TryAcquire(cooldownKey, TimeSpan.FromSeconds(Math.Max(0, rule.CooldownSeconds)), out var remaining))
                {
                    _log($"Join Commands: Cooldown fuer {join.PlayerName}/{join.SteamId} noch {remaining.TotalSeconds:0}s.");
                    continue;
                }

                var jobKey = BuildJobKey(rule, join);
                if (_state.HasExecuted(jobKey) || _state.HasPending(jobKey))
                {
                    continue;
                }

                var dueUtc = ComputeDueUtc(rule, join);
                _state.AddPending(new JoinCommandJob
                {
                    Key = jobKey,
                    PlayerName = join.PlayerName,
                    SteamId = join.SteamId,
                    Command = NormalizeImportantCommandCase(command),
                    DelaySeconds = Math.Max(0, rule.DelaySeconds),
                    DueUtc = dueUtc,
                    RawLine = join.RawLine
                });
                scheduled++;
                _log($"Join Commands: geplant fuer {join.PlayerName}/{join.SteamId} in {Math.Max(0, rule.DelaySeconds)}s: {command}");
            }
        }

        _state.Save();
        _log($"Join Commands: {lines.Count} neue Zeilen, {parsed} Join-Zeilen, {scheduled} Befehle geplant, {_state.PendingCount} offen.");
        await ExecuteDueAsync(cancellationToken);
    }

    private async Task ExecuteDueAsync(CancellationToken cancellationToken)
    {
        _state.RefreshOverdueBacklogJobs(DateTime.UtcNow);
        var due = _state.GetDue(DateTime.UtcNow).ToList();
        if (due.Count == 0) return;

        foreach (var job in due)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var command = NormalizeImportantCommandCase(job.Command);
                await _sendRconAsync(command);
                job.Command = command;
                _state.MarkExecuted(job.Key);
                _log($"Join Commands: ausgefuehrt fuer {job.PlayerName}/{job.SteamId}: {job.Command}");
            }
            catch (Exception ex)
            {
                // Job bleibt pending und wird beim naechsten Poll erneut versucht.
                _log($"Join Commands: RCON nicht erreichbar, Befehl bleibt offen fuer {job.PlayerName}/{job.SteamId}: {ex.Message}");
                AppLogService.WriteException("JoinCommandsExecute", ex);
            }
        }

        _state.Save();
    }


    public async Task ExecuteForOnlinePlayersAsync(BotSettings settings, CancellationToken cancellationToken = default)
    {
        var rules = LoadRules(settings.JoinAutomationRulesJson).Where(x => x.Enabled).ToList();
        if (rules.Count == 0)
        {
            _log("Join Commands: keine Regeln konfiguriert.");
            return;
        }

        var listPlayersResponse = await _sendRconAsync("#ListPlayersJson");
        var players = PlayerParser.ParseListPlayersJson(listPlayersResponse)
            .Where(x => !string.IsNullOrWhiteSpace(x.UserId))
            .ToList();

        if (players.Count == 0)
        {
            _log("Join Commands: keine verbundenen Spieler ueber #ListPlayersJson gefunden.");
            return;
        }

        var executed = 0;
        foreach (var player in players)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var join = new PlayerJoinEvent
            {
                PlayerName = player.DisplayName,
                SteamId = player.UserId ?? string.Empty,
                RawLine = "manual-online-playerlist"
            };

            foreach (var rule in rules)
            {
                if (!AutomationLogParser.TryBuildJoinCommand(rule, join, out var command, out var error))
                {
                    _log("Join Commands: " + error);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(command)) continue;

                command = NormalizeImportantCommandCase(command);
                await _sendRconAsync(command);
                _state.RemovePendingForSteamId(join.SteamId);
                executed++;
                _log($"Join Commands: fuer verbundenen Spieler ausgefuehrt: {join.PlayerName}/{join.SteamId}: {command}");
            }
        }

        _state.Save();
        _log($"Join Commands: Fuer alle verbundenen Spieler ausgefuehrt: {executed} Befehle bei {players.Count} Spielern.");
    }

    private static DateTime ComputeDueUtc(JoinAutomationRule rule, PlayerJoinEvent join)
    {
        var delaySeconds = Math.Max(0, rule.DelaySeconds);
        var nowUtc = DateTime.UtcNow;
        if (join.LoggedAtUtc is DateTime loggedAtUtc)
        {
            var dueFromLogin = loggedAtUtc.AddSeconds(delaySeconds);
            return dueFromLogin <= nowUtc ? nowUtc : dueFromLogin;
        }

        return nowUtc.AddSeconds(delaySeconds);
    }

    private static string NormalizeImportantCommandCase(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return string.Empty;
        command = Regex.Replace(command.Trim(), @"^#execas\b", "#ExecAs", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        command = Regex.Replace(command, @"#shownameplates\b", "#ShowNameplates", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return command;
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
                _log("Join Commands Fehler: " + ex.Message);
                AppLogService.WriteException("JoinCommands", ex);
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

    private static List<JoinAutomationRule> LoadRules(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<JoinAutomationRule>();
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
        return JsonSerializer.Deserialize<List<JoinAutomationRule>>(json, options) ?? new List<JoinAutomationRule>();
    }

    private static string BuildJobKey(JoinAutomationRule rule, PlayerJoinEvent join)
    {
        var source = $"{rule.Command}|{join.SteamId}|{join.RawLine}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(source));
        return Convert.ToHexString(bytes);
    }

    public static string BuildDefaultRulesJson()
    {
        var rules = new[]
        {
            new JoinAutomationRule
            {
                Enabled = true,
                DelaySeconds = 300,
                Command = "#ShowNameplates true",
                OnlyOncePerSession = true,
                CooldownSeconds = 300,
                ExecuteAsJoinedPlayer = true,
                AutoInsertSteamIdForExecas = true,
                RequireSteamIdForExecas = true
            }
        };

        return JsonSerializer.Serialize(rules, new JsonSerializerOptions { WriteIndented = true });
    }
}

public sealed class JoinCommandJob
{
    public string Key { get; set; } = string.Empty;
    public string PlayerName { get; set; } = string.Empty;
    public string SteamId { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public DateTime DueUtc { get; set; }
    public int DelaySeconds { get; set; } = 300;
    public string RawLine { get; set; } = string.Empty;
}

public sealed class JoinCommandState
{
    public List<JoinCommandJob> Pending { get; set; } = new();
    public Dictionary<string, DateTime> ExecutedUtc { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class JoinCommandStateStore
{
    private readonly string _path;
    private readonly JoinCommandState _state;

    public JoinCommandStateStore()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScumRconTool", "State");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "join-command-state.json");
        _state = Load();
    }

    public int PendingCount => _state.Pending.Count;

    public bool HasPending(string key) => _state.Pending.Any(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));

    public bool HasExecuted(string key) => _state.ExecutedUtc.ContainsKey(key);

    public void AddPending(JoinCommandJob job)
    {
        if (!HasPending(job.Key) && !HasExecuted(job.Key)) _state.Pending.Add(job);
    }

    public IEnumerable<JoinCommandJob> GetDue(DateTime nowUtc) => _state.Pending.Where(x => x.DueUtc <= nowUtc).ToList();

    public void MarkExecuted(string key)
    {
        _state.Pending.RemoveAll(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));
        _state.ExecutedUtc[key] = DateTime.UtcNow;

        var cutoff = DateTime.UtcNow.AddDays(-7);
        foreach (var old in _state.ExecutedUtc.Where(x => x.Value < cutoff).Select(x => x.Key).ToList())
        {
            _state.ExecutedUtc.Remove(old);
        }
    }

    public void RemovePendingForSteamId(string steamId)
    {
        if (string.IsNullOrWhiteSpace(steamId)) return;
        _state.Pending.RemoveAll(x => string.Equals(x.SteamId, steamId, StringComparison.OrdinalIgnoreCase));
    }

    public void RefreshOverdueBacklogJobs(DateTime nowUtc)
    {
        var changed = false;
        foreach (var job in _state.Pending)
        {
            if (job.DueUtc <= nowUtc || string.IsNullOrWhiteSpace(job.RawLine)) continue;
            var parsed = AutomationLogParser.ParseJoinLine(job.RawLine);
            if (parsed?.LoggedAtUtc is not DateTime loggedAtUtc) continue;

            var delay = job.DelaySeconds <= 0 ? 300 : job.DelaySeconds;
            if (loggedAtUtc.AddSeconds(delay) <= nowUtc)
            {
                job.DueUtc = nowUtc;
                changed = true;
            }
        }

        if (changed) Save();
    }

    public void Save()
    {
        var json = JsonSerializer.Serialize(_state, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_path, json);
    }

    private JoinCommandState Load()
    {
        try
        {
            if (!File.Exists(_path)) return new JoinCommandState();
            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<JoinCommandState>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new JoinCommandState();
        }
        catch
        {
            return new JoinCommandState();
        }
    }
}
