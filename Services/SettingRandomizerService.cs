using System.Globalization;
using System.Text;
using Renci.SshNet;

namespace ScumRconTool.Services;

public sealed class SettingRandomizerService
{
    private static readonly SemaphoreSlim ApplyLock = new(1, 1);
    private readonly BotSettings _settings;
    private readonly Action<string> _log;
    private readonly bool _isGerman;

    public SettingRandomizerService(BotSettings settings, Action<string> log, bool isGerman = true)
    {
        _settings = settings;
        _log = log;
        _isGerman = isGerman;
    }

    public async Task<SettingRandomizerResult> ApplyAsync(
        IReadOnlyCollection<SettingRandomizerRule> rules,
        IReadOnlyCollection<SettingRandomizerPack> packs,
        bool upload,
        CancellationToken cancellationToken = default)
    {
        ValidateRules(rules, packs, _isGerman);
        await ApplyLock.WaitAsync(cancellationToken);
        try
        {
            return await Task.Run(() => ApplyCore(rules, packs, upload, cancellationToken), cancellationToken);
        }
        finally
        {
            ApplyLock.Release();
        }
    }

    public async Task<string> DownloadCurrentAsync(CancellationToken cancellationToken = default)
    {
        await ApplyLock.WaitAsync(cancellationToken);
        try
        {
            return await Task.Run(() => DownloadCurrent(cancellationToken), cancellationToken);
        }
        finally
        {
            ApplyLock.Release();
        }
    }

    private string DownloadCurrent(CancellationToken cancellationToken)
    {
        var remoteFile = NormalizeRemoteFilePath(_settings.SettingRandomizerRemoteFilePath);
        var (host, port) = ParseHostAndPort(_settings.FtpHost, _settings.FtpPort);
        if (string.IsNullOrWhiteSpace(host)) throw new InvalidOperationException(L("SFTP-Host fehlt.", "SFTP host is missing."));
        if (string.IsNullOrWhiteSpace(_settings.FtpUser)) throw new InvalidOperationException(L("SFTP-Benutzer fehlt.", "SFTP user is missing."));

        using var client = new SftpClient(host, port, _settings.FtpUser, _settings.FtpPassword ?? string.Empty);
        client.ConnectionInfo.Timeout = TimeSpan.FromSeconds(30);
        client.OperationTimeout = TimeSpan.FromSeconds(30);
        client.Connect();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!client.Exists(remoteFile)) throw new FileNotFoundException(L("ServerSettings.ini wurde auf dem SFTP-Server nicht gefunden.", "ServerSettings.ini was not found on the SFTP server."), remoteFile);
            using var input = new MemoryStream();
            client.DownloadFile(remoteFile, input);
            var (text, _) = Decode(input.ToArray());
            Directory.CreateDirectory(Path.GetDirectoryName(LastUploadedCachePath)!);
            File.WriteAllText(LastUploadedCachePath, text, new UTF8Encoding(false));
            return text;
        }
        finally
        {
            if (client.IsConnected) client.Disconnect();
        }
    }
    public static string LastUploadedCachePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ScumRconTool",
        "State",
        "server-settings-last-uploaded.ini");

    public static string? ReadLastUploadedValue(string section, string key)
    {
        if (string.IsNullOrWhiteSpace(section) || string.IsNullOrWhiteSpace(key) || !File.Exists(LastUploadedCachePath)) return null;
        return FindIniValue(File.ReadAllText(LastUploadedCachePath), section.Trim(), key.Trim());
    }
    public static void ValidateRules(
        IEnumerable<SettingRandomizerRule> rules,
        IEnumerable<SettingRandomizerPack> packs,
        bool isGerman = true)
    {
        var allRules = rules.ToList();
        var allPacks = packs.ToList();
        var enabledRules = allRules.Where(x => x.Enabled).ToList();
        var enabledPacks = allPacks.Where(x => x.Enabled).ToList();
        if (enabledRules.Count == 0 && enabledPacks.Count == 0)
            throw new InvalidOperationException(isGerman ? "Aktiviere mindestens eine Servereinstellung oder ein Würfelset." : "Enable at least one server setting or dice pack.");

        foreach (var rule in enabledRules)
        {
            if (string.IsNullOrWhiteSpace(rule.Section) || string.IsNullOrWhiteSpace(rule.Key))
                throw new InvalidOperationException(isGerman ? "Bei einer Einstellung fehlen Sektion oder INI-Schlüssel." : "A setting is missing its section or INI key.");
            ValidateChanceTotal(rule.DisplayName, rule.Options.Where(x => !string.IsNullOrWhiteSpace(x.Value)).Select(x => x.ChancePercent), isGerman);
        }

        foreach (var pack in enabledPacks)
        {
            if (string.IsNullOrWhiteSpace(pack.Name))
                throw new InvalidOperationException(isGerman ? "Ein Würfelset hat keinen Namen." : "A dice pack has no name.");
            var usableOptions = pack.Options.Where(x => x.Values.Count > 0).ToList();
            ValidateChanceTotal(pack.Name, usableOptions.Select(x => x.ChancePercent), isGerman);
            foreach (var option in usableOptions)
            {
                if (option.Values.Any(x => string.IsNullOrWhiteSpace(x.Section) || string.IsNullOrWhiteSpace(x.Key) || string.IsNullOrWhiteSpace(x.Value)))
                    throw new InvalidOperationException(isGerman ? $"Würfelset '{pack.Name}', Variante '{option.Name}': Sektion, Schlüssel und Wert müssen ausgefüllt sein." : $"Dice pack '{pack.Name}', variant '{option.Name}': section, key, and value are required.");
                var duplicate = option.Values.GroupBy(x => x.Section.Trim() + "/" + x.Key.Trim(), StringComparer.OrdinalIgnoreCase).FirstOrDefault(x => x.Count() > 1);
                if (duplicate is not null)
                    throw new InvalidOperationException(isGerman ? $"Würfelset '{pack.Name}', Variante '{option.Name}': '{duplicate.Key}' ist doppelt enthalten." : $"Dice pack '{pack.Name}', variant '{option.Name}': '{duplicate.Key}' is duplicated.");
            }
        }

        // A key may belong to only one configured set, even while that set is disabled.
        var owners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in allRules.Where(x => !string.IsNullOrWhiteSpace(x.Section) && !string.IsNullOrWhiteSpace(x.Key)))
            RegisterTarget(rule.Section, rule.Key, string.IsNullOrWhiteSpace(rule.DisplayName) ? rule.Key : rule.DisplayName, owners, isGerman);
        foreach (var pack in allPacks)
            foreach (var value in pack.Options.SelectMany(x => x.Values)
                         .Where(x => !string.IsNullOrWhiteSpace(x.Section) && !string.IsNullOrWhiteSpace(x.Key))
                         .GroupBy(x => x.Section.Trim() + "/" + x.Key.Trim(), StringComparer.OrdinalIgnoreCase)
                         .Select(x => x.First()))
                RegisterTarget(value.Section, value.Key, string.IsNullOrWhiteSpace(pack.Name) ? "(unbenanntes Set)" : pack.Name, owners, isGerman);
    }

    private static void ValidateChanceTotal(string name, IEnumerable<double> chances, bool isGerman)
    {
        var usable = chances.Where(x => x > 0).ToList();
        if (usable.Count == 0)
            throw new InvalidOperationException(isGerman ? $"'{name}': Mindestens eine Variante benötigt eine Chance größer 0." : $"'{name}': At least one variant needs a chance greater than 0.");
        var total = usable.Sum();
        if (Math.Abs(total - 100d) > 0.01d)
            throw new InvalidOperationException(isGerman ? $"'{name}': Die Chancen ergeben {total.ToString("0.##", CultureInfo.CurrentCulture)} % statt 100 %." : $"'{name}': The chances add up to {total.ToString("0.##", CultureInfo.CurrentCulture)}% instead of 100%.");
    }

    private static void RegisterTarget(string section, string key, string owner, IDictionary<string, string> owners, bool isGerman)
    {
        var id = section.Trim() + "/" + key.Trim();
        if (owners.TryGetValue(id, out var existing))
            throw new InvalidOperationException(isGerman ? $"'{id}' wird gleichzeitig von '{existing}' und '{owner}' geändert." : $"'{id}' is changed by both '{existing}' and '{owner}'.");
        owners[id] = owner;
    }
    public static IReadOnlyList<TimeSpan> ParseSchedule(string? value, bool isGerman = true)
    {
        var times = new List<TimeSpan>();
        foreach (var part in (value ?? string.Empty).Split([',', ';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!TimeSpan.TryParseExact(part, [@"h\:mm", @"hh\:mm"], CultureInfo.InvariantCulture, out var time) ||
                time < TimeSpan.Zero ||
                time >= TimeSpan.FromDays(1))
            {
                throw new InvalidOperationException(isGerman ? $"Ungültige Uhrzeit '{part}'. Verwende z. B. 04:00, 10:00, 16:00, 22:00." : $"Invalid time '{part}'. Use e.g. 04:00, 10:00, 16:00, 22:00.");
            }

            if (!times.Contains(time)) times.Add(time);
        }

        if (times.Count == 0) throw new InvalidOperationException(isGerman ? "Trage mindestens eine Uhrzeit ein." : "Enter at least one time.");
        times.Sort();
        return times;
    }

    public static string? GetDueScheduleKey(DateTime localNow, string? schedule, string? lastScheduleKey, bool isGerman = true)
    {
        foreach (var time in ParseSchedule(schedule, isGerman))
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
        IReadOnlyCollection<SettingRandomizerPack> packs,
        bool upload,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var remoteFile = NormalizeRemoteFilePath(_settings.SettingRandomizerRemoteFilePath);
        var (host, port) = ParseHostAndPort(_settings.FtpHost, _settings.FtpPort);
        if (string.IsNullOrWhiteSpace(host)) throw new InvalidOperationException(L("SFTP-Host fehlt.", "SFTP host is missing."));
        if (string.IsNullOrWhiteSpace(_settings.FtpUser)) throw new InvalidOperationException(L("SFTP-Benutzer fehlt.", "SFTP user is missing."));

        using var client = new SftpClient(host, port, _settings.FtpUser, _settings.FtpPassword ?? string.Empty);
        client.ConnectionInfo.Timeout = TimeSpan.FromSeconds(30);
        client.OperationTimeout = TimeSpan.FromSeconds(30);
        client.Connect();

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!client.Exists(remoteFile))
            {
                throw new FileNotFoundException(L("ServerSettings.ini wurde auf dem SFTP-Server nicht gefunden.", "ServerSettings.ini was not found on the SFTP server."), remoteFile);
            }

            byte[] originalBytes;
            using (var input = new MemoryStream())
            {
                client.DownloadFile(remoteFile, input);
                originalBytes = input.ToArray();
            }

            var (originalText, encoding) = Decode(originalBytes);
            ValidateConfiguredTargetsExist(originalText, rules, packs);
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

            foreach (var pack in packs.Where(x => x.Enabled))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var option = ChoosePackOption(pack.Options);
                foreach (var value in option.Values)
                {
                    var oldValue = ReplaceIniValue(lines, value.Section.Trim(), value.Key.Trim(), value.Value.Trim());
                    selections.Add(new SettingRandomizerSelection(
                        value.Section.Trim(),
                        value.Key.Trim(),
                        string.IsNullOrWhiteSpace(value.DisplayName) ? value.Key.Trim() : value.DisplayName.Trim(),
                        oldValue,
                        value.Value.Trim(),
                        option.StatusText ?? string.Empty));
                }
            }

            var updatedText = string.Join(newline, lines);
            if (upload)
            {
                var updatedBytes = Encode(updatedText, encoding);
                UploadAtomically(client, remoteFile, originalBytes, updatedBytes, cancellationToken);
                Directory.CreateDirectory(Path.GetDirectoryName(LastUploadedCachePath)!);
                File.WriteAllText(LastUploadedCachePath, updatedText, new UTF8Encoding(false));
                _log($"SettingRandomizer: ServerSettings.ini aktualisiert und lokal zwischengespeichert ({selections.Count} Werte).");
            }

            return new SettingRandomizerResult(remoteFile, selections, upload, updatedText);
        }
        finally
        {
            if (client.IsConnected) client.Disconnect();
        }
    }

    private void ValidateConfiguredTargetsExist(
        string iniText,
        IReadOnlyCollection<SettingRandomizerRule> rules,
        IReadOnlyCollection<SettingRandomizerPack> packs)
    {
        var available = ServerSettingsValidationService.ParseEntries(iniText)
            .Select(x => x.Section.Trim() + "/" + x.Key.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var configured = rules.Where(x => x.Enabled).Select(x => (x.Section, x.Key, Owner: x.DisplayName))
            .Concat(packs.Where(x => x.Enabled).SelectMany(pack => pack.Options.SelectMany(option => option.Values)
                .Select(value => (value.Section, value.Key, Owner: pack.Name))))
            .Where(x => !string.IsNullOrWhiteSpace(x.Section) && !string.IsNullOrWhiteSpace(x.Key))
            .DistinctBy(x => x.Section.Trim() + "/" + x.Key.Trim(), StringComparer.OrdinalIgnoreCase);

        foreach (var target in configured)
        {
            var id = target.Section.Trim() + "/" + target.Key.Trim();
            if (available.Contains(id)) continue;
            throw new InvalidOperationException(L(
                $"'{target.Key}' aus Set '{target.Owner}' existiert nicht mehr in der aktuellen ServerSettings.ini. Entferne oder ersetze diesen Wert.",
                $"'{target.Key}' from set '{target.Owner}' no longer exists in the current ServerSettings.ini. Remove or replace this value."));
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

    private static SettingRandomizerPackOption ChoosePackOption(IEnumerable<SettingRandomizerPackOption> options)
    {
        var usable = options.Where(x => x.Values.Count > 0 && x.ChancePercent > 0).ToList();
        var roll = Random.Shared.NextDouble() * usable.Sum(x => x.ChancePercent);
        foreach (var option in usable)
        {
            roll -= option.ChancePercent;
            if (roll <= 0) return option;
        }
        return usable[^1];
    }
    private string ReplaceIniValue(List<string> lines, string section, string key, string newValue)
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
        return L("(nicht gesetzt)", "(not set)");
    }

    private static string? FindIniValue(string text, string section, string key)
    {
        var currentSection = string.Empty;
        foreach (var line in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("[", StringComparison.Ordinal) && trimmed.EndsWith("]", StringComparison.Ordinal))
            {
                currentSection = trimmed[1..^1].Trim();
                continue;
            }
            if (!currentSection.Equals(section, StringComparison.OrdinalIgnoreCase)) continue;
            var equals = line.IndexOf('=');
            if (equals < 0 || !line[..equals].Trim().Equals(key, StringComparison.OrdinalIgnoreCase)) continue;
            return line[(equals + 1)..].Trim();
        }
        return null;
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

    private string NormalizeRemoteFilePath(string? path)
    {
        var value = (path ?? string.Empty).Trim().Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException(L("Remote-Pfad zur ServerSettings.ini fehlt.", "Remote path to ServerSettings.ini is missing."));
        if (!value.StartsWith('/')) value = "/" + value;
        return value;
    }

    private string L(string de, string en) => _isGerman ? de : en;

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
