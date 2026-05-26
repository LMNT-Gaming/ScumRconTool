using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ScumRconTool.Services;

public static partial class KillLogParser
{
    public static IReadOnlyList<KillLogEntry> ParseFile(string path)
    {
        var result = new List<KillLogEntry>();
        if (!File.Exists(path)) return result;

        foreach (var line in ReadScumLogLines(path))
        {
            var entry = ParseLine(line, Path.GetFileName(path));
            if (entry is not null)
            {
                result.Add(entry);
            }
        }

        return result;
    }

    private static IEnumerable<string> ReadScumLogLines(string path)
    {
        // SCUM server logs are UTF-16 little-endian. Detect a BOM when present,
        // but default to UTF-16 LE so logs without BOM are still read correctly.
        using var stream = File.OpenRead(path);
        var encoding = DetectEncoding(stream) ?? Encoding.Unicode;
        stream.Position = 0;
        using var reader = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: true);
        while (!reader.EndOfStream)
        {
            var line = reader.ReadLine();
            if (line is not null) yield return line;
        }
    }

    private static Encoding? DetectEncoding(Stream stream)
    {
        Span<byte> bom = stackalloc byte[4];
        var read = stream.Read(bom);
        if (read >= 2 && bom[0] == 0xFF && bom[1] == 0xFE) return Encoding.Unicode;
        if (read >= 2 && bom[0] == 0xFE && bom[1] == 0xFF) return Encoding.BigEndianUnicode;
        if (read >= 3 && bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF) return Encoding.UTF8;
        return null;
    }

    public static KillLogEntry? ParseLine(string line, string sourceFile)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        var raw = line.Trim();

        var killer = FindNamedValue(raw, "killer") ?? FindNamedValue(raw, "attacker") ?? FindNamedValue(raw, "instigator");
        var victim = FindNamedValue(raw, "victim") ?? FindNamedValue(raw, "killed") ?? FindNamedValue(raw, "target");
        var weapon = FindNamedValue(raw, "weapon") ?? FindNamedValue(raw, "weaponName") ?? FindNamedValue(raw, "item");
        var distance = FindNamedValue(raw, "distance") ?? FindNamedValue(raw, "distanceMeters");

        if (string.IsNullOrWhiteSpace(killer) || string.IsNullOrWhiteSpace(victim))
        {
            var m = KilledRegex().Match(raw);
            if (m.Success)
            {
                killer = CleanValue(m.Groups["killer"].Value);
                victim = CleanValue(m.Groups["victim"].Value);
                if (string.IsNullOrWhiteSpace(weapon) && m.Groups["weapon"].Success) weapon = CleanValue(m.Groups["weapon"].Value);
                if (string.IsNullOrWhiteSpace(distance) && m.Groups["distance"].Success) distance = CleanValue(m.Groups["distance"].Value);
            }
        }

        if (string.IsNullOrWhiteSpace(killer) || string.IsNullOrWhiteSpace(victim))
        {
            return null;
        }

        return new KillLogEntry
        {
            Timestamp = TryParseTimestamp(raw),
            Killer = killer,
            Victim = victim,
            Weapon = weapon ?? "",
            Distance = distance ?? "",
            RawLine = raw,
            SourceFile = sourceFile
        };
    }

    private static string? FindNamedValue(string line, string name)
    {
        var pattern = $"(?i)(?:^|[\\s,;|]){Regex.Escape(name)}\\s*[:=]\\s*(?:\\\"(?<value>[^\\\"]+)\\\"|'(?<value>[^']+)'|(?<value>[^,;|]+))";
        var match = Regex.Match(line, pattern);
        return match.Success ? CleanValue(match.Groups["value"].Value) : null;
    }

    private static string CleanValue(string value)
    {
        value = value.Trim().Trim('"', '\'', '[', ']');
        value = Regex.Replace(value, "\\s+", " ");
        return value;
    }

    private static DateTime? TryParseTimestamp(string line)
    {
        var match = Regex.Match(line, @"(?<time>\d{4}[.\-/]\d{2}[.\-/]\d{2}[ T-]\d{2}[:.]\d{2}[:.]\d{2})");
        if (!match.Success)
        {
            match = Regex.Match(line, @"(?<time>\d{2}[.\-/]\d{2}[.\-/]\d{4}[ T-]\d{2}[:.]\d{2}[:.]\d{2})");
        }

        if (!match.Success) return null;
        var value = match.Groups["time"].Value;
        var formats = new[]
        {
            "yyyy.MM.dd-HH.mm.ss", "yyyy.MM.dd HH:mm:ss", "yyyy-MM-dd HH:mm:ss", "yyyy/MM/dd HH:mm:ss", "yyyy-MM-ddTHH:mm:ss",
            "dd.MM.yyyy HH:mm:ss", "dd-MM-yyyy HH:mm:ss", "dd/MM/yyyy HH:mm:ss"
        };

        return DateTime.TryParseExact(value, formats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dt)
            ? dt
            : DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out dt) ? dt : null;
    }

    [GeneratedRegex("(?i)(?<killer>.+?)\\s+(?:killed|eliminated|murdered)\\s+(?<victim>.+?)(?:\\s+(?:with|using)\\s+(?<weapon>.+?))?(?:\\s+(?:from|distance)\\s+(?<distance>[0-9.,]+\\s*m?))?$")]
    private static partial Regex KilledRegex();
}
