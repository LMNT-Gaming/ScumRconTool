using System.Text.Json;

namespace ScumRconTool.Services;

public sealed class SettingRandomizerRule
{
    public bool Enabled { get; set; } = true;
    public string Section { get; set; } = "World";
    public string Key { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public List<SettingRandomizerOption> Options { get; set; } = new();
}

public sealed class SettingRandomizerOption
{
    public string Value { get; set; } = string.Empty;
    public double ChancePercent { get; set; } = 50;
    public string DiscordAnnouncement { get; set; } = string.Empty;
}

public sealed record SettingRandomizerCatalogEntry(
    string Section,
    string Key,
    string DisplayName,
    IReadOnlyList<SettingRandomizerOption> DefaultOptions)
{
    public string Id => Section + "/" + Key;
    public string Label => DisplayName + "  [" + Section + "]";

    public SettingRandomizerRule CreateRule(bool enabled = false) => new()
    {
        Enabled = enabled,
        Section = Section,
        Key = Key,
        DisplayName = DisplayName,
        Options = DefaultOptions.Select(x => new SettingRandomizerOption
        {
            Value = x.Value,
            ChancePercent = x.ChancePercent,
            DiscordAnnouncement = x.DiscordAnnouncement
        }).ToList()
    };
}

public static class SettingRandomizerConfiguration
{
    private static SettingRandomizerOption Option(string value, double chance, string announcement = "") => new()
    {
        Value = value,
        ChancePercent = chance,
        DiscordAnnouncement = announcement
    };

    public static IReadOnlyList<SettingRandomizerCatalogEntry> Catalog { get; } =
    [
        new("World", "scum.DisableSentrySpawning", "Sentries deaktivieren",
            [Option("True", 50, "Den Mechs geht der Saft aus."), Option("False", 50, "Die Mechs sind wieder einsatzbereit.")]),
        new("World", "scum.AreAnimalsAllowedInWorld", "Tiere in der Welt",
            [Option("True", 50, "Die Tiere werden wild."), Option("False", 50, "Die Wildnis ist heute ungew?hnlich still.")]),
        new("World", "scum.EnableFog", "Nebel aktivieren",
            [Option("True", 35, "Dichter Nebel zieht ?ber die Insel."), Option("False", 65, "Die Sicht klart wieder auf.")]),
        new("World", "scum.RadiationEnabled", "Strahlung aktivieren",
            [Option("True", 25, "Die Messger?te schlagen aus ? meidet die Strahlungszone."), Option("False", 75, "Die Strahlungswerte sind wieder unauff?llig.")]),
        new("World", "scum.EnableDropshipAbandonedBunkerEncounter", "Dropship am verlassenen Bunker",
            [Option("True", 20, "Unbekannte Flugbewegungen wurden an verlassenen Bunkern gemeldet."), Option("False", 80)]),
        new("World", "scum.PuppetHealthMultiplier", "Puppet-Lebenspunkte",
            [Option("0.750000", 25, "Die Puppets wirken heute etwas gebrechlich."), Option("1.000000", 50), Option("1.250000", 25, "Die Puppets sind heute besonders z?h.")]),
        new("World", "scum.AnimalGlobalDensityMultiplier", "Tierdichte",
            [Option("0.500000", 25), Option("1.000000", 50), Option("1.500000", 25, "Auf der Insel wurden ungew?hnlich viele Tiere gesichtet.")]),
        new("World", "scum.EncounterHordeActivationChanceMultiplier", "Horden-Aktivierung",
            [Option("0.500000", 35), Option("1.000000", 45), Option("1.500000", 20, "Die Horden sind heute besonders unruhig.")]),
        new("Damage", "scum.ZombieDamageMultiplier", "Puppet-Schaden",
            [Option("0.750000", 25), Option("1.000000", 50), Option("1.250000", 25, "Puppets richten heute mehr Schaden an.")]),
        new("Features", "scum.SpawnerProbabilityMultiplier", "Loot-Spawnchance",
            [Option("1.000000", 25), Option("1.500000", 50), Option("2.000000", 25, "Die Insel ist heute reichhaltiger best?ckt.")])
    ];

    public static string BuildDefaultJson()
    {
        var defaults = new[]
        {
            Catalog[0].CreateRule(enabled: true),
            Catalog[1].CreateRule(),
            Catalog[2].CreateRule()
        };
        return JsonSerializer.Serialize(defaults, JsonOptions);
    }

    public static List<SettingRandomizerRule> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return JsonSerializer.Deserialize<List<SettingRandomizerRule>>(BuildDefaultJson(), JsonOptions) ?? new();
        }

        return JsonSerializer.Deserialize<List<SettingRandomizerRule>>(json, JsonOptions) ?? new();
    }

    public static string Serialize(IEnumerable<SettingRandomizerRule> rules) =>
        JsonSerializer.Serialize(rules, JsonOptions);

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };
}

public sealed record SettingRandomizerSelection(
    string Section,
    string Key,
    string DisplayName,
    string OldValue,
    string NewValue,
    string DiscordAnnouncement);

public sealed record SettingRandomizerResult(
    string RemoteFilePath,
    IReadOnlyList<SettingRandomizerSelection> Selections,
    bool Uploaded)
{
    public string Summary => Selections.Count == 0
        ? "Keine Einstellungen ausgew?hlt."
        : string.Join(" | ", Selections.Select(x => $"{x.DisplayName}: {x.OldValue} ? {x.NewValue}"));

    public string DiscordText => string.Join(
        Environment.NewLine,
        Selections.Select(x => x.DiscordAnnouncement.Trim()).Where(x => x.Length > 0).Distinct(StringComparer.Ordinal));
}
