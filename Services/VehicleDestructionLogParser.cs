using System.Globalization;
using System.Text.RegularExpressions;

namespace ScumRconTool.Services;

public static partial class VehicleDestructionLogParser
{
    public static VehicleDestructionLogEntry? ParseLine(string line, string sourceFile)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        if (line.Contains("Game version:", StringComparison.OrdinalIgnoreCase)) return null;

        var match = VehicleLineRegex().Match(line.Trim());
        if (!match.Success) return null;

        DateTime? timestamp = null;
        if (DateTime.TryParseExact(
                match.Groups["ts"].Value,
                "yyyy.MM.dd-HH.mm.ss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal,
                out var parsedTimestamp))
        {
            timestamp = parsedTimestamp;
        }

        var ownerRaw = match.Groups["owner"].Value.Trim();
        var ownerSteamId = string.Empty;
        var ownerName = string.Empty;
        int? ownerProfileId = null;

        var ownerMatch = OwnerRegex().Match(ownerRaw);
        if (ownerMatch.Success)
        {
            ownerSteamId = ownerMatch.Groups["steam"].Value.Trim();
            if (int.TryParse(ownerMatch.Groups["profile"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var profileId))
            {
                ownerProfileId = profileId;
            }
            ownerName = ownerMatch.Groups["name"].Value.Trim();
        }

        return new VehicleDestructionLogEntry
        {
            Timestamp = timestamp,
            Action = match.Groups["action"].Value.Trim(),
            VehicleClass = match.Groups["vehicle"].Value.Trim(),
            VehicleId = match.Groups["id"].Value.Trim(),
            OwnerRaw = ownerRaw,
            OwnerSteamId = ownerSteamId,
            OwnerProfileId = ownerProfileId,
            OwnerName = ownerName,
            X = ParseDouble(match.Groups["x"].Value),
            Y = ParseDouble(match.Groups["y"].Value),
            Z = ParseDouble(match.Groups["z"].Value),
            SourceFile = sourceFile,
            RawLine = line.Trim()
        };
    }

    public static string BuildVehicleIconName(string vehicleClass)
    {
        var value = (vehicleClass ?? string.Empty).Trim();
        if (value.EndsWith("_ES", StringComparison.OrdinalIgnoreCase)) value = value[..^3];
        return value.Trim('_');
    }

    private static double? ParseDouble(string value)
    {
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)) return parsed;
        return null;
    }

    [GeneratedRegex(@"^(?<ts>\d{4}\.\d{2}\.\d{2}-\d{2}\.\d{2}\.\d{2}):\s*\[(?<action>[^\]]+)\]\s+(?<vehicle>\S+)\.\s+VehicleId:\s*(?<id>\d+)\.\s+Owner:\s*(?<owner>.*?)\.\s+Location:\s*X=(?<x>-?\d+(?:\.\d+)?)\s+Y=(?<y>-?\d+(?:\.\d+)?)\s+Z=(?<z>-?\d+(?:\.\d+)?)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex VehicleLineRegex();

    [GeneratedRegex(@"^(?<steam>\d{17})\s*\((?<profile>\d+),\s*(?<name>.*)\)$", RegexOptions.CultureInvariant)]
    private static partial Regex OwnerRegex();
}
