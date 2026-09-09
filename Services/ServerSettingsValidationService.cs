using System.Globalization;
using System.Text.RegularExpressions;

namespace ScumRconTool.Services;

public sealed record ServerSettingIniEntry(string Section, string Key, string Value);

public sealed record ServerSettingsValidationIssue(
    string Severity,
    string Title,
    string Message,
    IReadOnlyList<string> Keys);

public static partial class ServerSettingsValidationService
{
    private static readonly (string Min, string Max, string Label)[] DistancePairs =
    [
        ("scum.EncounterCharacterSpawnDistanceMinOverrideLTZ", "scum.EncounterCharacterSpawnDistanceMaxOverrideLTZ", "LTZ spawn distance"),
        ("scum.EncounterCharacterRespawnDistanceMinOverrideLTZ", "scum.EncounterCharacterRespawnDistanceMaxOverrideLTZ", "LTZ respawn distance"),
        ("scum.EncounterCharacterSpawnDistanceMinOverrideLargePOI", "scum.EncounterCharacterSpawnDistanceMaxOverrideLargePOI", "Large POI spawn distance"),
        ("scum.EncounterCharacterRespawnDistanceMinOverrideLargePOI", "scum.EncounterCharacterRespawnDistanceMaxOverrideLargePOI", "Large POI respawn distance")
    ];

    private static readonly (string Max, string Decay, string Label)[] DecayPairs =
    [
        ("scum.WeaponRackMaxAmountPerFlagArea", "scum.WeaponRackStartDecayingIfFlagAreaHasMoreThan", "Weapon Rack"),
        ("scum.WallWeaponRackMaxAmountPerFlagArea", "scum.WallWeaponRackStartDecayingIfFlagAreaHasMoreThan", "Wall Weapon Rack"),
        ("scum.WellMaxAmountPerFlagArea", "scum.WellStartDecayingIfFlagAreaHasMoreThan", "Well"),
        ("scum.TurretMaxAmountPerFlagArea", "scum.TurretStartDecayingIfFlagAreaHasMoreThan", "Turret"),
        ("scum.OvenMaxAmountPerFlagArea", "scum.OvenStartDecayingIfFlagAreaHasMoreThan", "Oven"),
        ("scum.GardenMaxAmountPerFlagArea", "scum.GardenStartDecayingIfFlagAreaHasMoreThan", "Garden")
    ];

    public static IReadOnlyList<ServerSettingsValidationIssue> Validate(string iniText, bool isGerman)
    {
        var values = ParseValues(iniText);
        var issues = new List<ServerSettingsValidationIssue>();
        void Add(string severity, string deTitle, string enTitle, string deMessage, string enMessage, params string[] keys) =>
            issues.Add(new ServerSettingsValidationIssue(severity, isGerman ? deTitle : enTitle, isGerman ? deMessage : enMessage, keys));

        foreach (var key in new[]
                 {
                     "scum.AllowFirstPerson", "scum.AllowThirdPerson", "scum.AllowVoting", "scum.MaxPingCheckEnabled",
                     "scum.DisableSentrySpawning", "scum.EnableSentryRespawning", "scum.AreAnimalsAllowedInWorld",
                     "scum.EncounterNeverRespawnCharacters"
                 })
        {
            if (values.TryGetValue(key, out var value) && !IsBoolean(value))
                Add("Danger", "Bool ungültig", "Invalid Boolean", $"{key} sollte True oder False sein.", $"{key} should be True or False.", key);
        }

        foreach (var key in new[] { "scum.SunriseTime", "scum.SunsetTime", "scum.StartTimeOfDay" })
        {
            if (values.TryGetValue(key, out var value) && ParseTime(value) is null)
                Add("Danger", "Zeitformat ungültig", "Invalid time format", $"{key} benötigt HH:MM:SS.", $"{key} requires HH:MM:SS.", key);
        }

        var minTick = Number(values, "scum.MinServerTickRate");
        var maxTick = Number(values, "scum.MaxServerTickRate");
        if (minTick is not null && maxTick is not null && minTick > maxTick)
            Add("Danger", "Tickrate vertauscht", "Tick rates reversed", "MinServerTickRate ist größer als MaxServerTickRate.", "MinServerTickRate is greater than MaxServerTickRate.", "scum.MinServerTickRate", "scum.MaxServerTickRate");

        if (Boolean(values, "scum.MaxPingCheckEnabled") == true && Number(values, "scum.MaxPing") is > 300)
            Add("Warning", "Sehr hohes Pinglimit", "Very high ping limit", "Die Pingprüfung ist aktiv, aber MaxPing liegt über 300 ms.", "Ping checking is enabled, but MaxPing is above 300 ms.", "scum.MaxPingCheckEnabled", "scum.MaxPing");

        if (Boolean(values, "scum.AllowFirstPerson") == false && Boolean(values, "scum.AllowThirdPerson") == false)
            Add("Danger", "Keine Kamera erlaubt", "No camera mode allowed", "First Person und Third Person sind beide deaktiviert.", "First person and third person are both disabled.", "scum.AllowFirstPerson", "scum.AllowThirdPerson");

        if (Boolean(values, "scum.AllowVoting") == false && new[] { "scum.VotingDuration", "scum.PlayerMinimalVotingInterest", "scum.PlayerPositiveVotePercentage" }.Any(values.ContainsKey))
            Add("Info", "Votingwerte ohne Wirkung", "Voting values have no effect", "Voting ist deaktiviert; die Voting-Schwellen sind wirkungslos.", "Voting is disabled; its thresholds have no effect.", "scum.AllowVoting");

        var sunrise = Time(values, "scum.SunriseTime");
        var sunset = Time(values, "scum.SunsetTime");
        if (sunrise is not null && sunset is not null)
        {
            if (sunset <= sunrise)
                Add("Warning", "Sonnenzeiten über Mitternacht", "Sun times cross midnight", "SunsetTime liegt vor oder gleich SunriseTime.", "SunsetTime is before or equal to SunriseTime.", "scum.SunriseTime", "scum.SunsetTime");
            else if ((sunset.Value - sunrise.Value).TotalHours > 16)
                Add("Info", "Sehr langer Tag", "Very long day", "Zwischen Sonnenaufgang und Sonnenuntergang liegen mehr als 16 Stunden.", "There are more than 16 hours between sunrise and sunset.", "scum.SunriseTime", "scum.SunsetTime");
            else if ((sunset.Value - sunrise.Value).TotalHours < 8)
                Add("Info", "Sehr kurzer Tag", "Very short day", "Zwischen Sonnenaufgang und Sonnenuntergang liegen weniger als 8 Stunden.", "There are fewer than 8 hours between sunrise and sunset.", "scum.SunriseTime", "scum.SunsetTime");
        }

        if (Number(values, "scum.TimeOfDaySpeed") is > 4)
            Add("Warning", "Schnelle Tageszeit", "Fast time of day", "Tag und Nacht wechseln deutlich schneller.", "Day and night cycle significantly faster.", "scum.TimeOfDaySpeed");

        foreach (var pair in DistancePairs)
        {
            var min = Number(values, pair.Min);
            var max = Number(values, pair.Max);
            if (min is not null && max is not null && min >= 0 && max >= 0 && min > max)
                Add("Danger", $"{pair.Label}: Min/Max fehlerhaft", $"{pair.Label}: invalid min/max", "Minimum ist größer als Maximum.", "Minimum is greater than maximum.", pair.Min, pair.Max);
            if ((min is >= 0) != (max is >= 0))
                Add("Warning", $"{pair.Label} nur halb aktiv", $"{pair.Label} only partially active", "Min und Max sollten gemeinsam gesetzt werden oder beide -1 sein.", "Set min and max together or leave both at -1.", pair.Min, pair.Max);
        }

        var puppetWeight = Number(values, "scum.PuppetWorldEncounterSpawnWeightMultiplier");
        var dropshipWeight = Number(values, "scum.DropshipWorldEncounterSpawnWeightMultiplier");
        if (puppetWeight is not null && dropshipWeight is not null && puppetWeight + dropshipWeight <= 0)
            Add("Danger", "Alle Encounter-Spawngewichte 0", "All encounter spawn weights are 0", "Puppets und Dropships besitzen zusammen kein Spawngewicht.", "Puppets and dropships have no combined spawn weight.", "scum.PuppetWorldEncounterSpawnWeightMultiplier", "scum.DropshipWorldEncounterSpawnWeightMultiplier");

        foreach (var pair in DecayPairs)
        {
            var max = Number(values, pair.Max);
            var decay = Number(values, pair.Decay);
            if (max is not null && decay is not null && !IsGameDefault(max.Value) && !IsGameDefault(decay.Value) && decay > max)
                Add("Warning", $"{pair.Label}: Decay greift nie", $"{pair.Label}: decay never applies", "Die Decay-Schwelle ist größer als die erlaubte Maximalmenge.", "The decay threshold is greater than the allowed maximum amount.", pair.Max, pair.Decay);
        }

        foreach (var (key, raw) in values)
        {
            var match = VehicleMaximumRegex().Match(key);
            if (!match.Success) continue;
            var prefix = match.Groups[1].Value;
            var max = ParseNumber(raw);
            var functionalKey = $"scum.{prefix}MaxFunctionalAmount";
            var purchasedKey = $"scum.{prefix}MinPurchasedAmount";
            var functional = Number(values, functionalKey);
            var purchased = Number(values, purchasedKey);
            if (max is not null && functional is not null && !IsGameDefault(max.Value) && !IsGameDefault(functional.Value) && functional > max)
                Add("Warning", $"{prefix}: Fahrzeugpool widersprüchlich", $"{prefix}: inconsistent vehicle pool", "MaxFunctionalAmount ist größer als MaxAmount.", "MaxFunctionalAmount is greater than MaxAmount.", key, functionalKey);
            if (max is not null && purchased is not null && !IsGameDefault(max.Value) && !IsGameDefault(purchased.Value) && purchased > max)
                Add("Warning", $"{prefix}: Kaufreserve widersprüchlich", $"{prefix}: inconsistent purchase reserve", "MinPurchasedAmount ist größer als MaxAmount.", "MinPurchasedAmount is greater than MaxAmount.", key, purchasedKey);
        }

        return issues;
    }

    public static IReadOnlyList<ServerSettingIniEntry> ParseEntries(string iniText)
    {
        var entries = new List<ServerSettingIniEntry>();
        var section = "General";
        foreach (var rawLine in (iniText ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#')) continue;
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line[1..^1].Trim();
                continue;
            }
            var equals = line.IndexOf('=');
            if (equals <= 0) continue;
            entries.Add(new ServerSettingIniEntry(section, line[..equals].Trim(), line[(equals + 1)..].Trim()));
        }
        return entries
            .GroupBy(x => x.Section + "\u001f" + x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Last())
            .ToList();
    }
    public static Dictionary<string, string> ParseValues(string iniText)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in (iniText ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#') || line.StartsWith('[')) continue;
            var equals = line.IndexOf('=');
            if (equals <= 0) continue;
            values[line[..equals].Trim()] = line[(equals + 1)..].Trim();
        }
        return values;
    }

    private static bool IsBoolean(string value) => value.Equals("True", StringComparison.OrdinalIgnoreCase) || value.Equals("False", StringComparison.OrdinalIgnoreCase);
    private static bool? Boolean(Dictionary<string, string> values, string key) => values.TryGetValue(key, out var value) && IsBoolean(value) ? bool.Parse(value) : null;
    private static double? Number(Dictionary<string, string> values, string key) => values.TryGetValue(key, out var value) ? ParseNumber(value) : null;
    private static double? ParseNumber(string value) => double.TryParse(value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var result) ? result : null;
    private static bool IsGameDefault(double value) => Math.Abs(value + 1d) < 0.000001d;
    private static TimeSpan? Time(Dictionary<string, string> values, string key) => values.TryGetValue(key, out var value) ? ParseTime(value) : null;
    private static TimeSpan? ParseTime(string value) => TimeSpan.TryParseExact(value, @"hh\:mm\:ss", CultureInfo.InvariantCulture, out var result) ? result : null;

    [GeneratedRegex(@"^scum\.(.+)MaxAmount$", RegexOptions.IgnoreCase)]
    private static partial Regex VehicleMaximumRegex();
}