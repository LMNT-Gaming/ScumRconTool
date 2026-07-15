using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace ScumRconTool.Services;

public sealed class ChatAutomationRule
{
    public bool Enabled { get; set; } = true;
    public string Trigger { get; set; } = string.Empty;
    public string MatchMode { get; set; } = "equals";
    public string Command { get; set; } = string.Empty;
    public string Response { get; set; } = string.Empty;
    public int DelaySeconds { get; set; }

    // Wenn aktiv, wird der Command automatisch als #ExecAs fuer den Spieler ausgefuehrt, der den Chat-Trigger geschrieben hat.
    public bool ExecuteAsChatPlayer { get; set; }

    // Per default: ein Spieler kann dieselbe Regel nur alle 5 Minuten ausloesen.
    public int CooldownSeconds { get; set; } = 300;

    // player = je SteamID/Name limitiert, global = einmal fuer alle Spieler zusammen.
    public string CooldownScope { get; set; } = "player";

    // Zusaetzlicher globaler Schutz gegen gleichzeitiges Spammen durch mehrere Spieler.
    public int GlobalCooldownSeconds { get; set; } = 10;

    // Bei Chat-Regeln wird aus "#execas vote ..." automatisch "#execas {steamId} #vote ...".
    public bool AutoInsertSteamIdForExecas { get; set; } = true;

    // Wenn #execas genutzt wird und keine SteamID aus der Chat-Zeile gelesen werden kann, wird nicht ausgefuehrt.
    public bool RequireSteamIdForExecas { get; set; } = true;
}

public sealed class RedeemCodeRule
{
    public bool Enabled { get; set; } = true;
    public string Code { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public string Response { get; set; } = string.Empty;
    public bool ExecuteAsChatPlayer { get; set; } = true;
    public int DelaySeconds { get; set; }
    public int MaxUses { get; set; } = 1;
    public int Uses { get; set; }
}

public sealed class JoinAutomationRule
{
    public bool Enabled { get; set; } = true;
    public int DelaySeconds { get; set; } = 300;
    public string Command { get; set; } = string.Empty;
    public string TargetSteamId { get; set; } = string.Empty;
    public bool OnlyOncePerSession { get; set; } = true;
    public int CooldownSeconds { get; set; } = 300;

    // Wenn aktiv, wird der Command automatisch als #ExecAs fuer den joinenden Spieler ausgefuehrt.
    // Die SteamID wird vom Backend eingesetzt. Der sichtbare Editor muss keine rohe JSON-Regel anzeigen.
    public bool ExecuteAsJoinedPlayer { get; set; }

    // Bei Join-Regeln wird aus "#execas #shownameplates true" automatisch
    // "#execas {steamId} #shownameplates true".
    public bool AutoInsertSteamIdForExecas { get; set; } = true;

    // Wenn #execas genutzt wird und keine SteamID aus der Login-Zeile gelesen werden kann,
    // wird nicht ausgefuehrt.
    public bool RequireSteamIdForExecas { get; set; } = true;
}


public sealed class AutomationLimiter
{
    private readonly Dictionary<string, DateTime> _lastRunUtc = new(StringComparer.OrdinalIgnoreCase);

    public bool TryAcquire(string key, TimeSpan cooldown, out TimeSpan remaining)
    {
        remaining = TimeSpan.Zero;
        if (string.IsNullOrWhiteSpace(key) || cooldown <= TimeSpan.Zero) return true;

        var now = DateTime.UtcNow;
        if (_lastRunUtc.TryGetValue(key, out var lastRun))
        {
            var elapsed = now - lastRun;
            if (elapsed < cooldown)
            {
                remaining = cooldown - elapsed;
                return false;
            }
        }

        _lastRunUtc[key] = now;
        return true;
    }

    public void Clear() => _lastRunUtc.Clear();
}

public sealed class ChatLogMessage
{
    public string PlayerName { get; init; } = string.Empty;
    public string SteamId { get; init; } = string.Empty;
    public string Channel { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string RawLine { get; init; } = string.Empty;
    public DateTime? LoggedAtUtc { get; init; }

    public bool IsGlobal => string.Equals(Channel?.Trim(), "Global", StringComparison.OrdinalIgnoreCase);
}

public sealed class PlayerJoinEvent
{
    public string PlayerName { get; init; } = string.Empty;
    public string SteamId { get; init; } = string.Empty;
    public string RawLine { get; init; } = string.Empty;
    public DateTime? LoggedAtUtc { get; init; }

    public string SessionKey => !string.IsNullOrWhiteSpace(SteamId) ? SteamId : (!string.IsNullOrWhiteSpace(PlayerName) ? PlayerName : RawLine);
}

public static partial class AutomationLogParser
{
    public static ChatLogMessage? ParseChatLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;

        var match = ScumChatRegex().Match(line);
        if (!match.Success) match = ChatRegex1().Match(line);
        if (!match.Success) match = ChatRegex2().Match(line);
        if (!match.Success) match = ChatRegex3().Match(line);
        if (!match.Success) return null;

        var message = match.Groups["message"].Value.Trim();
        if (string.IsNullOrWhiteSpace(message)) return null;

        return new ChatLogMessage
        {
            PlayerName = match.Groups["name"].Value.Trim().Trim('"', '\'', '[', ']'),
            SteamId = match.Groups["steamid"].Success ? match.Groups["steamid"].Value.Trim() : string.Empty,
            Channel = match.Groups["channel"].Success ? match.Groups["channel"].Value.Trim() : string.Empty,
            Message = message,
            LoggedAtUtc = TryParseScumTimestampUtc(match.Groups["ts"].Success ? match.Groups["ts"].Value : string.Empty),
            RawLine = line
        };
    }

    public static PlayerJoinEvent? ParseJoinLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        if (!line.Contains("join", StringComparison.OrdinalIgnoreCase) &&
            !line.Contains("login", StringComparison.OrdinalIgnoreCase) &&
            !line.Contains("logged in", StringComparison.OrdinalIgnoreCase) &&
            !line.Contains("connected", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var match = ScumLoginRegex().Match(line);
        if (!match.Success) match = JoinRegex1().Match(line);
        if (!match.Success) match = JoinRegex2().Match(line);
        if (!match.Success) return null;

        return new PlayerJoinEvent
        {
            PlayerName = match.Groups["name"].Success ? match.Groups["name"].Value.Trim().Trim('"', '\'', '[', ']') : string.Empty,
            SteamId = match.Groups["steamid"].Success ? match.Groups["steamid"].Value.Trim() : string.Empty,
            LoggedAtUtc = TryParseScumTimestampUtc(match.Groups["ts"].Success ? match.Groups["ts"].Value : string.Empty),
            RawLine = line
        };
    }

    private static DateTime? TryParseScumTimestampUtc(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (!DateTime.TryParseExact(value, "yyyy.MM.dd-HH.mm.ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var localTime))
        {
            return null;
        }

        return DateTime.SpecifyKind(localTime, DateTimeKind.Local).ToUniversalTime();
    }

    public static bool IsMatch(ChatAutomationRule rule, string message)
    {
        var trigger = rule.Trigger?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trigger)) return false;
        var mode = (rule.MatchMode ?? "equals").Trim().ToLowerInvariant();

        return mode switch
        {
            "startswith" => message.StartsWith(trigger, StringComparison.OrdinalIgnoreCase),
            "contains" => message.Contains(trigger, StringComparison.OrdinalIgnoreCase),
            "regex" => Regex.IsMatch(message, trigger, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            _ => string.Equals(message.Trim(), trigger, StringComparison.OrdinalIgnoreCase)
        };
    }

    public static string ApplyPlaceholders(string template, ChatLogMessage message)
    {
        return ApplyCommonPlaceholders(template, message.PlayerName, message.SteamId, message.RawLine)
            .Replace("{message}", message.Message, StringComparison.OrdinalIgnoreCase);
    }

    public static string BuildChatCooldownKey(ChatAutomationRule rule, ChatLogMessage message)
    {
        var ruleId = $"{rule.Trigger}|{rule.MatchMode}|{rule.Command}|{rule.Response}";
        var scope = (rule.CooldownScope ?? "player").Trim().ToLowerInvariant();
        if (scope == "global") return "global|" + ruleId;

        var playerKey = !string.IsNullOrWhiteSpace(message.SteamId)
            ? message.SteamId
            : (!string.IsNullOrWhiteSpace(message.PlayerName) ? message.PlayerName : message.RawLine);
        return "player|" + playerKey + "|" + ruleId;
    }

    public static string BuildGlobalChatCooldownKey(ChatAutomationRule rule)
    {
        return $"global-safety|{rule.Trigger}|{rule.MatchMode}|{rule.Command}|{rule.Response}";
    }

    public static bool IsJoinTargetMatch(JoinAutomationRule rule, PlayerJoinEvent join)
    {
        var target = (rule.TargetSteamId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(target)) return true;
        if (string.IsNullOrWhiteSpace(join.SteamId)) return false;

        return Regex.Split(target, @"[\s,;]+")
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Any(x => string.Equals(x.Trim(), join.SteamId.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public static bool TryBuildChatCommand(ChatAutomationRule rule, ChatLogMessage message, out string command, out string error)
    {
        var template = rule.Command ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(template) && rule.ExecuteAsChatPlayer && !StartsWithExecas(template))
        {
            template = "#ExecAs " + template.Trim();
        }

        command = ApplyPlaceholders(template, message);
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(command)) return true;

        if (!StartsWithExecas(command)) return true;

        if (string.IsNullOrWhiteSpace(message.SteamId))
        {
            if (rule.RequireSteamIdForExecas)
            {
                error = "#execas uebersprungen: keine SteamID in der Chat-Zeile gefunden.";
                command = string.Empty;
                return false;
            }

            return true;
        }

        if (rule.AutoInsertSteamIdForExecas)
        {
            command = EnsureExecasSteamId(command, message.SteamId);
        }

        return true;
    }

    private static bool StartsWithExecas(string command)
    {
        return command.TrimStart().StartsWith("#execas ", StringComparison.OrdinalIgnoreCase);
    }

    private static string EnsureExecasSteamId(string command, string steamId)
    {
        var trimmed = command.Trim();
        const string prefix = "#execas";
        var rest = trimmed.Length > prefix.Length ? trimmed[prefix.Length..].TrimStart() : string.Empty;
        if (string.IsNullOrWhiteSpace(rest)) return trimmed;

        var firstSpace = rest.IndexOf(' ');
        var firstArg = firstSpace >= 0 ? rest[..firstSpace] : rest;
        if (string.Equals(firstArg, steamId, StringComparison.OrdinalIgnoreCase)) return NormalizeExecasInnerHash(trimmed);
        if (Regex.IsMatch(firstArg, "^\\d{10,}$", RegexOptions.CultureInvariant)) return NormalizeExecasInnerHash(trimmed);

        return NormalizeExecasInnerHash($"#execas {steamId} {rest}");
    }

    private static string NormalizeExecasInnerHash(string command)
    {
        var trimmed = command.Trim();
        const string prefix = "#execas";
        if (!trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return trimmed;

        var rest = trimmed.Length > prefix.Length ? trimmed[prefix.Length..].TrimStart() : string.Empty;
        if (string.IsNullOrWhiteSpace(rest)) return trimmed;

        var parts = rest.Split(new[] { ' ' }, 3, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return trimmed;

        // ggCON erwartet: #ExecAs <steamId> <command>
        // Der innere SCUM/Admin-Command muss mit # beginnen, z.B. #shownameplates true.
        if (Regex.IsMatch(parts[0], "^\\d{10,}$", RegexOptions.CultureInvariant))
        {
            if (parts.Length == 1) return $"#execas {parts[0]}";

            var innerCommand = parts[1];
            if (!innerCommand.StartsWith("#", StringComparison.Ordinal))
            {
                innerCommand = "#" + innerCommand;
            }

            var tail = parts.Length == 3 ? " " + parts[2] : string.Empty;
            return $"#execas {parts[0]} {innerCommand}{tail}";
        }

        // Noch keine SteamID vorhanden. Nicht den Hash entfernen; hoechstens den inneren Command sicherstellen.
        if (!parts[0].StartsWith("#", StringComparison.Ordinal))
        {
            var tail = parts.Length >= 2 ? " " + string.Join(" ", parts.Skip(1)) : string.Empty;
            return $"#execas #{parts[0]}{tail}";
        }

        return trimmed;
    }

    public static bool TryBuildJoinCommand(JoinAutomationRule rule, PlayerJoinEvent join, out string command, out string error)
    {
        var template = rule.Command ?? string.Empty;
        if (rule.ExecuteAsJoinedPlayer && !StartsWithExecas(template))
        {
            template = "#ExecAs " + template.Trim();
        }

        command = ApplyPlaceholders(template, join);
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(command)) return true;

        if (!StartsWithExecas(command)) return true;

        if (string.IsNullOrWhiteSpace(join.SteamId))
        {
            if (rule.RequireSteamIdForExecas)
            {
                error = "#execas uebersprungen: keine SteamID in der Login-Zeile gefunden.";
                command = string.Empty;
                return false;
            }

            command = NormalizeExecasInnerHash(command);
            return true;
        }

        if (rule.AutoInsertSteamIdForExecas)
        {
            command = EnsureExecasSteamId(command, join.SteamId);
        }

        command = NormalizeExecasInnerHash(command);
        return true;
    }

    public static string ApplyPlaceholders(string template, PlayerJoinEvent join)
    {
        return ApplyCommonPlaceholders(template, join.PlayerName, join.SteamId, join.RawLine);
    }

    private static string ApplyCommonPlaceholders(string template, string playerName, string steamId, string rawLine)
    {
        return (template ?? string.Empty)
            .Replace("{playerName}", playerName ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("{name}", playerName ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("{steamId}", steamId ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("{rawLine}", rawLine ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim();
    }

    // SCUM chat log format, UTF-16 LE, for example:
    // 2026.05.13-18.42.58: '76561198045804979:TOMahawk918(10)' 'Global: /vote day'
    [GeneratedRegex(@"^(?<ts>\d{4}\.\d{2}\.\d{2}-\d{2}\.\d{2}\.\d{2}):\s*'(?<steamid>\d{10,}):(?<name>.+?)\(\d+\)'\s*'(?<channel>[^:']+):\s*(?<message>.*?)'\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ScumChatRegex();

    [GeneratedRegex(@"^(?:\[[^\]]+\]\s*)?(?<name>[^:]+?)\s*(?:\((?<steamid>\d{10,})\))?\s*:\s*(?<message>.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ChatRegex1();

    [GeneratedRegex(@"(?<name>[^\[\]\(\):]+)\s*\[(?<steamid>\d{10,})\].*?:\s*(?<message>.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ChatRegex2();

    [GeneratedRegex(@"Chat.*?(?<name>[^:]+?)\s*:\s*(?<message>.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ChatRegex3();

    [GeneratedRegex(@"^(?<ts>\d{4}\.\d{2}\.\d{2}-\d{2}\.\d{2}\.\d{2}):\s*'[^']*?\s+(?<steamid>\d{10,}):(?<name>.+?)\(\d+\)'\s+logged in at:", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ScumLoginRegex();

    [GeneratedRegex(@"(?<name>[A-Za-z0-9_\- .]+).*?(?<steamid>\d{10,}).*?(join|login|logged in|connected)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex JoinRegex1();

    [GeneratedRegex(@"(?<steamid>\d{10,}).*?(?<name>[A-Za-z0-9_\- .]+).*?(join|login|logged in|connected)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex JoinRegex2();
}
