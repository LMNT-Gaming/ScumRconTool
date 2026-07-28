using System.Globalization;
using System.Text;
using Renci.SshNet;

namespace ScumRconTool.Services;

public sealed class SettingRandomizerService
{
    private static readonly SemaphoreSlim ApplyLock = new(1, 1);
    private readonly BotSettings _settings;
    private readonly Action<string> _log;

    public SettingRandomizerService(BotSettings settings, Action<string> log)
    {
        _settings = settings;
        _log = log;
    }

    public async Task<SettingRandomizerResult> ApplyAsync(
        IReadOnlyCollection<SettingRandomizerRule> rules,
        bool upload,
        CancellationToken cancellationToken = default)
    {
        ValidateRules(rules);
        await ApplyLock.WaitAsync(cancellationToken);
        try
        {
            return await Task.Run(() => ApplyCore(rules, upload, cancellationToken), cancellationToken);
        }
        finally
        {
            ApplyLock.Release();
        }
    }

    public static void ValidateRules(IEnumerable<SettingRandomizerRule> rules)
    {
        var enabled = rules.Where(x => x.Enabled).ToList();
        if (enabled.Count == 0) throw new InvalidOperationException("Aktiviere mindestens eine Servereinstellung.");

        foreach (var rule in enabled)
        {
            if (string.IsNullOrWhiteSpace(rule.Section) || string.IsNullOrWhiteSpace(rule.Key))
            {
                throw new InvalidOperationException("Bei einer Einstellung fehlen Sektion oder INI-Schl?ssel.");
            }

            var usable = rule.Options.Where(x => !string.IsNullOrWhiteSpace(x.Value) && x.ChancePercent > 0).ToList();
            if (usable.Count == 0)
            {
                throw new InvalidOperationException($"'{rule.DisplayName}': Mindestens ein Wert ben?tigt eine Chance gr??er 0.");
            }

            var total = usable.Sum(x => x.ChancePercent);
            if (Math.Abs(total - 100d) > 0.01d)
            {
                throw new InvalidOperationException(
                    $"'{rule.DisplayName}': Die Chancen ergeben {total.ToString("0.##", CultureInfo.CurrentCulture)} % statt 100 %.");
            }
        }
    }

    public static IReadOnlyList<TimeSpan> ParseSchedule(string? value)
    {
        var times = new List<TimeSpan>();
        foreach (var part in (value ?? string.Empty).Split([',', ';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!TimeSpan.TryParseExact(part, [@"h\:mm", @"hh\:mm"], CultureInfo.InvariantCulture, out var time) ||
                time < TimeSpan.Zero ||
                time >= TimeSpan.FromDays(1))
            {
                throw new InvalidOperationException($"Ung?ltige Uhrzeit '{part}'. Verwende z. B. 04:00, 10:00, 16:00, 22:00.");
            }

            if (!times.Contains(time)) times.Add(time);
        }

        if (times.Count == 0) throw new InvalidOperationException("Trage mindestens eine Uhrzeit ein.");
        times.Sort();
        return times;
    }

    public static string? GetDueScheduleKey(DateTime localNow, string? schedule, string? lastScheduleKey)
    {
        foreach (var time in ParseSchedule(schedule))
        {
            var scheduled = localNow.Date.Add(time);
            var age = localNow - scheduled;
            if (age < TimeSpan.Zero || age >= TimeSpan.FromMinutes(5)) continue;
            var key = scheduled.ToString("yyyy-MM-dd|HH:mm", CultureInfo.InvariantCulture);
            if (!string.Equals(key, lastScheduleKey, StringComparison.Ordinal)) return key;
        }

        return null;
    }

    private SettingRandomizerResult ApplyCore(
        IReadOnlyCollection<SettingRandomizerRule> rules,
        bool upload,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var remoteFile = NormalizeRemoteFilePath(_settings.SettingRandomizerRemoteFilePath);
        var (host, port) = ParseHostAndPort(_settings.FtpHost, _settings.FtpPort);
        if (string.IsNullOrWhiteSpace(host)) throw new InvalidOperationException("SFTP-Host fehlt.");
        if (string.IsNullOrWhiteSpace(_settings.FtpUser)) throw new InvalidOperationException("SFTP-Benutzer fehlt.");

        using var client = new SftpClient(host, port, _settings.FtpUser, _settings.FtpPassword ?? string.Empty);
        client.ConnectionInfo.Timeout = TimeSpan.FromSeconds(30);
        client.OperationTimeout = TimeSpan.FromSeconds(30);
        client.Connect();

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!client.Exists(remoteFile))
            {
                throw new FileNotFoundException("ServerSettings.ini wurde auf dem SFTP-Server nicht gefunden.", remoteFile);
            }

            byte[] originalBytes;
            using (var input = new MemoryStream())
            {
                client.DownloadFile(remoteFile, input);
                originalBytes = input.ToArray();
            }

            var (originalText, encoding) = Decode(originalBytes);
            var newline = originalText.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            var lines = originalText.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToList();
            var selections = new List<SettingRandomizerSelection>();

            foreach (var rule in rules.Where(x => x.Enabled))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var option = Choose(rule.Options);
                var oldValue = ReplaceIniValue(lines, rule.Section.Trim(), rule.Key.Trim(), option.Value.Trim());
                selections.Add(new SettingRandomizerSelection(
                    rule.Section.Trim(),
                    rule.Key.Trim(),
                    string.IsNullOrWhiteSpace(rule.DisplayName) ? rule.Key.Trim() : rule.DisplayName.Trim(),
                    oldValue,
                    option.Value.Trim(),
                    option.DiscordAnnouncement ?? string.Empty));
            }

            var updatedText = string.Join(newline, lines);
            if (upload)
            {
                var updatedBytes = Encode(updatedText, encoding);
                UploadAtomically(client, remoteFile, originalBytes, updatedBytes, cancellationToken);
                _log($"SettingRandomizer: ServerSettings.ini aktualisiert ({selections.Count} Werte).");
            }

            return new SettingRandomizerResult(remoteFile, selections, upload);
        }
        finally
        {
            if (client.IsConnected) client.Disconnect();
        }
    }

    private static SettingRandomizerOption Choose(IEnumerable<SettingRandomizerOption> options)
    {
        var usable = options.Where(x => !string.IsNullOrWhiteSpace(x.Value) && x.ChancePercent > 0).ToList();
        var roll = Random.Shared.NextDouble() * usable.Sum(x => x.ChancePercent);
        foreach (var option in usable)
        {
            roll -= option.ChancePercent;
            if (roll <= 0) return option;
        }
        return usable[^1];
    }

    private static string ReplaceIniValue(List<string> lines, string section, string key, string newValue)
    {
        var currentSection = string.Empty;
        var sectionFound = false;
        var insertAt = lines.Count;

        for (var i = 0; i < lines.Count; i++)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.StartsWith("[", StringComparison.Ordinal) && trimmed.EndsWith("]", StringComparison.Ordinal))
            {
                if (sectionFound)
                {
                    insertAt = i;
                    break;
                }
                currentSection = trimmed[1..^1].Trim();
                sectionFound = currentSection.Equals(section, StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!sectionFound || !currentSection.Equals(section, StringComparison.OrdinalIgnoreCase)) continue;
            var equals = lines[i].IndexOf('=');
            if (equals < 0) continue;
            var candidate = lines[i][..equals].Trim();
            if (!candidate.Equals(key, StringComparison.OrdinalIgnoreCase)) continue;

            var oldValue = lines[i][(equals + 1)..].Trim();
            var prefix = lines[i][..(equals + 1)];
            lines[i] = prefix + newValue;
            return oldValue;
        }

        if (!sectionFound)
        {
            if (lines.Count > 0 && lines[^1].Length > 0) lines.Add(string.Empty);
            lines.Add($"[{section}]");
            lines.Add($"{key}={newValue}");
        }
        else
        {
            lines.Insert(insertAt, $"{key}={newValue}");
        }
        return "(nicht gesetzt)";
    }

    private static void UploadAtomically(
        SftpClient client,
        string remoteFile,
        byte[] originalBytes,
        byte[] updatedBytes,
        CancellationToken cancellationToken)
    {
        var tempFile = remoteFile + ".rrrt-" + Guid.NewGuid().ToString("N") + ".tmp";
        var backupFile = remoteFile + ".rrrt-backup";
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using (var backup = new MemoryStream(originalBytes, writable: false))
            {
                client.UploadFile(backup, backupFile, true);
            }

            using (var updated = new MemoryStream(updatedBytes, writable: false))
            {
                client.UploadFile(updated, tempFile, true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                client.RenameFile(tempFile, remoteFile, true);
            }
            catch
            {
                using var fallback = new MemoryStream(updatedBytes, writable: false);
                client.UploadFile(fallback, remoteFile, true);
            }
        }
        finally
        {
            try
            {
                if (client.IsConnected && client.Exists(tempFile)) client.DeleteFile(tempFile);
            }
            catch
            {
                // Eine verwaiste Temp-Datei darf den erfolgreichen Upload nicht zur?cksetzen.
            }
        }
    }

    private static (string Text, Encoding Encoding) Decode(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return (Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3), new UTF8Encoding(true));
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return (Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2), Encoding.Unicode);
        return (Encoding.UTF8.GetString(bytes), new UTF8Encoding(false));
    }

    private static byte[] Encode(string text, Encoding encoding)
    {
        var body = encoding.GetBytes(text);
        var preamble = encoding.GetPreamble();
        if (preamble.Length == 0) return body;
        var result = new byte[preamble.Length + body.Length];
        Buffer.BlockCopy(preamble, 0, result, 0, preamble.Length);
        Buffer.BlockCopy(body, 0, result, preamble.Length, body.Length);
        return result;
    }

    private static string NormalizeRemoteFilePath(string? path)
    {
        var value = (path ?? string.Empty).Trim().Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException("Remote-Pfad zur ServerSettings.ini fehlt.");
        if (!value.StartsWith('/')) value = "/" + value;
        return value;
    }

    private static (string Host, int Port) ParseHostAndPort(string? value, int configuredPort)
    {
        var host = (value ?? string.Empty).Trim();
        if (host.StartsWith("sftp://", StringComparison.OrdinalIgnoreCase)) host = host[7..];
        var slash = host.IndexOf('/');
        if (slash >= 0) host = host[..slash];
        var port = configuredPort > 0 ? configuredPort : 22;
        var colon = host.LastIndexOf(':');
        if (colon > 0 && int.TryParse(host[(colon + 1)..], out var parsedPort))
        {
            port = parsedPort;
            host = host[..colon];
        }
        return (host, port);
    }
}
