using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

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

public sealed class SettingRandomizerPack
{
    public bool Enabled { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<SettingRandomizerPackOption> Options { get; set; } = new();
}

public sealed class SettingRandomizerPackOption
{
    public string Name { get; set; } = string.Empty;
    public double ChancePercent { get; set; }
    public string StatusText { get; set; } = string.Empty;
    public List<SettingRandomizerPackValue> Values { get; set; } = new();
}

public sealed class SettingRandomizerPackValue
{
    public string Section { get; set; } = "Features";
    public string Key { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}
public sealed record SettingRandomizerCatalogEntry(
    string Section,
    string Key,
    string DisplayName,
    IReadOnlyList<SettingRandomizerOption> DefaultOptions)
{
    public string Id => Section + "/" + Key;
    public string Label => DisplayName + "  ·  " + Key;

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
            [Option("True", 50, "Die Tiere werden wild."), Option("False", 50, "Die Wildnis ist heute ungew\u00f6hnlich still.")]),
        new("World", "scum.EnableFog", "Nebel aktivieren",
            [Option("True", 35, "Dichter Nebel zieht \u00fcber die Insel."), Option("False", 65, "Die Sicht klart wieder auf.")]),
        new("World", "scum.RadiationEnabled", "Strahlung aktivieren",
            [Option("True", 25, "Die Messger\u00e4te schlagen aus \u2013 meidet die Strahlungszone."), Option("False", 75, "Die Strahlungswerte sind wieder unauff\u00e4llig.")]),
        new("World", "scum.EnableDropshipAbandonedBunkerEncounter", "Dropship am verlassenen Bunker",
            [Option("True", 20, "Unbekannte Flugbewegungen wurden an verlassenen Bunkern gemeldet."), Option("False", 80)]),
        new("World", "scum.PuppetHealthMultiplier", "Puppet-Lebenspunkte",
            [Option("0.750000", 25, "Die Puppets wirken heute etwas gebrechlich."), Option("1.000000", 50), Option("1.250000", 25, "Die Puppets sind heute besonders z\u00e4h.")]),
        new("World", "scum.AnimalGlobalDensityMultiplier", "Tierdichte",
            [Option("0.500000", 25), Option("1.000000", 50), Option("1.500000", 25, "Auf der Insel wurden ungew\u00f6hnlich viele Tiere gesichtet.")]),
        new("World", "scum.EncounterHordeActivationChanceMultiplier", "Horden-Aktivierung",
            [Option("0.500000", 35), Option("1.000000", 45), Option("1.500000", 20, "Die Horden sind heute besonders unruhig.")]),
        new("Damage", "scum.ZombieDamageMultiplier", "Puppet-Schaden",
            [Option("0.750000", 25), Option("1.000000", 50), Option("1.250000", 25, "Puppets richten heute mehr Schaden an.")]),
        new("Features", "scum.SpawnerProbabilityMultiplier", "Loot-Spawnchance",
            [Option("1.000000", 25), Option("1.500000", 50), Option("2.000000", 25, "Die Insel ist heute reichhaltiger best\u00fcckt.")]),
        new("World", "scum.EnableSentryRespawning", "Sentry-Respawn",
            [Option("True", 65, "Ausgefallene Mechs erhalten wieder Verstaerkung."), Option("False", 35, "Zerstoerte Mechs bleiben heute ausser Gefecht.")]),
        new("World", "scum.DisableSuicidePuppetSpawning", "Suicide-Puppets deaktivieren",
            [Option("True", 40, "Von den Suicide-Puppets fehlt heute jede Spur."), Option("False", 60, "Explosive Puppets wurden wieder gesichtet.")]),
        new("World", "scum.DisableLootPuppetSpawning", "Loot-Puppets deaktivieren",
            [Option("True", 25, "Die Loot-Puppets haben sich verkrochen."), Option("False", 75, "Loot-Puppets streifen wieder ueber die Insel.")]),
        new("World", "scum.EnableLootPuppetHorde", "Loot-Puppet-Horden",
            [Option("True", 30, "Loot-Puppets sammeln sich heute in Horden."), Option("False", 70)]),
        new("World", "scum.PuppetsCanOpenDoors", "Puppets oeffnen Tueren",
            [Option("True", 60, "Tueren halten die Puppets heute nicht auf."), Option("False", 40, "Geschlossene Tueren bieten wieder Schutz.")]),
        new("World", "scum.PuppetsCanVaultWindows", "Puppets springen durch Fenster",
            [Option("True", 60, "Auch Fenster sind heute kein sicherer Fluchtweg."), Option("False", 40)]),
        new("World", "scum.EnableDropshipBaseBuildingEncounter", "Dropship bei Basen",
            [Option("True", 20, "Dropships reagieren heute auf Aktivitaet an Spielerbasen."), Option("False", 80)]),
        new("World", "scum.PuppetRunningSpeedMultiplier", "Puppet-Laufgeschwindigkeit",
            [Option("0.750000", 25, "Die Puppets sind heute traeger."), Option("1.000000", 50), Option("1.250000", 25, "Die Puppets sind heute erschreckend schnell.")]),
        new("World", "scum.MaxAllowedPuppets", "Maximale Puppets",
            [Option("250", 25), Option("400", 50), Option("550", 25, "Besonders hohe Puppet-Aktivitaet wird erwartet.")]),
        new("World", "scum.EncounterBaseCharacterAmountMultiplier", "Encounter-Grundmenge",
            [Option("1.000000", 25), Option("2.000000", 50), Option("3.000000", 25, "Begegnungen fallen heute deutlich groesser aus.")]),
        new("World", "scum.EncounterExtraCharacterPerPlayerMultiplier", "Encounter-Zuwachs pro Spieler",
            [Option("1.000000", 30), Option("1.500000", 45), Option("2.000000", 25)]),
        new("World", "scum.EncounterHordeBaseCharacterAmountMultiplier", "Horden-Groesse",
            [Option("0.750000", 25), Option("1.000000", 50), Option("1.500000", 25, "Groessere Horden wurden gemeldet.")]),
        new("World", "scum.EncounterHordeShouldPlayActivationSound", "Horden-Warnsound",
            [Option("True", 70), Option("False", 30, "Die Horden greifen heute ohne Warnsignal an.")]),
        new("World", "scum.ArmedNPCDifficultyLevel", "Bewaffnete NPC-Schwierigkeit",
            [Option("1", 25), Option("3", 50), Option("5", 25, "Bewaffnete NPCs sind heute auf hoechster Alarmstufe.")]),
        new("World", "scum.ArmedNPCHealthMultiplier", "Bewaffnete NPC-Lebenspunkte",
            [Option("0.500000", 30), Option("1.000000", 50), Option("1.500000", 20)]),
        new("World", "scum.ArmedNPCDamageMultiplier", "Bewaffneter NPC-Schaden",
            [Option("0.200000", 30), Option("0.500000", 50), Option("1.000000", 20, "Bewaffnete NPCs sind heute besonders gefaehrlich.")]),
        new("World", "scum.TimeOfDaySpeed", "Tagesgeschwindigkeit",
            [Option("1.920000", 25, "Tage und Naechte verlaufen heute langsamer."), Option("3.840000", 50), Option("7.680000", 25, "Die Zeit rast heute ueber die Insel.")]),
        new("World", "scum.NighttimeDarkness", "Dunkelheit bei Nacht",
            [Option("0.000000", 40), Option("0.500000", 40), Option("1.000000", 20, "Eine besonders dunkle Nacht steht bevor.")]),
        new("World", "scum.EnableLockedLootContainers", "Verschlossene Loot-Container",
            [Option("True", 70), Option("False", 30, "Die Loot-Container sind heute unverschlossen.")]),
        new("Damage", "scum.SentryDamageMultiplier", "Sentry-Schaden",
            [Option("0.100000", 25), Option("0.200000", 50), Option("0.500000", 25, "Die Waffen der Mechs wurden hochgefahren.")]),
        new("Damage", "scum.HumanToHumanDamageMultiplier", "PvP-Schaden",
            [Option("0.750000", 25), Option("1.000000", 50), Option("1.250000", 25, "Gefechte zwischen Spielern sind heute besonders toedlich.")]),
        new("Features", "scum.ExamineSpawnerProbabilityMultiplier", "Untersuchungs-Lootchance",
            [Option("1.000000", 25), Option("1.500000", 50), Option("2.000000", 25)]),
        new("Features", "scum.StaminaDrainOnJumpMultiplier", "Ausdauer beim Springen",
            [Option("0.750000", 25), Option("1.000000", 50), Option("1.250000", 25)]),
        new("Features", "scum.StaminaDrainOnClimbMultiplier", "Ausdauer beim Klettern",
            [Option("0.750000", 25), Option("1.000000", 50), Option("1.250000", 25)]),
        new("Features", "scum.TurretsAttackPuppets", "Geschuetze greifen Puppets an",
            [Option("True", 65), Option("False", 35, "Automatische Geschuetze ignorieren heute Puppets.")]),
        new("Features", "scum.TurretsAttackVehicles", "Geschuetze greifen Fahrzeuge an",
            [Option("True", 25, "Fahrzeuge geraten ins Visier automatischer Geschuetze."), Option("False", 75)]),
        new("Features", "scum.TurretsAttackAnimals", "Geschuetze greifen Tiere an",
            [Option("True", 20, "Auch Wildtiere werden heute von Geschuetzen erfasst."), Option("False", 80)]),
        new("Respawn", "scum.AllowSectorRespawn", "Sektor-Respawn",
            [Option("True", 75), Option("False", 25, "Der Sektor-Respawn ist voruebergehend ausgefallen.")]),
        new("Respawn", "scum.AllowShelterRespawn", "Shelter-Respawn",
            [Option("True", 75), Option("False", 25, "Shelter koennen heute nicht als Respawn genutzt werden.")]),
        new("Respawn", "scum.AllowSquadmateRespawn", "Squadmate-Respawn",
            [Option("True", 75), Option("False", 25, "Der Respawn bei Squadmitgliedern ist heute deaktiviert.")]),
        new("Features", "scum.GasolinePricePerUnitMultiplier", "Benzinpreis",
            [Option("0.500000", 25, "Benzin ist heute besonders günstig."), Option("0.800000", 50), Option("1.500000", 25, "Benzin ist heute knapp und teuer.")]),
        new("Features", "scum.GasolinePeriodicInitialAmountMultiplier", "Benzin: Startmenge",
            [Option("0.500000", 25), Option("1.000000", 50), Option("2.000000", 25)]),
        new("Features", "scum.GasolinePeriodicMaxAmountMultiplier", "Benzin: maximale Menge",
            [Option("0.500000", 25), Option("1.000000", 50), Option("2.000000", 25)]),
        new("Features", "scum.GasolinePeriodicReplenishAmountMultiplier", "Benzin: Auffüllmenge",
            [Option("0.500000", 25), Option("1.000000", 50), Option("2.000000", 25)]),
        new("Features", "scum.GasolinePeriodicReplenishIntervalMultiplier", "Benzin: Auffüllintervall",
            [Option("0.500000", 25), Option("1.000000", 50), Option("2.000000", 25)]),
        new("Features", "scum.GasolineProximityReplenishAmountMultiplier", "Benzin: Nähe-Auffüllmenge",
            [Option("0.500000", 25), Option("1.000000", 50), Option("2.000000", 25)]),
        new("Features", "scum.GasolineProximityReplenishChanceMultiplier", "Benzin: Nähe-Auffüllchance",
            [Option("0.500000", 25), Option("1.000000", 50), Option("2.000000", 25)]),
        new("Features", "scum.GasolineProximityReplenishTimeoutMultiplier", "Benzin: Nähe-Timeout",
            [Option("0.500000", 25), Option("1.000000", 50), Option("2.000000", 25)]),        new("General", "scum.FameGainMultiplier", "Fame-Gewinn",
            [Option("0.500000", 25), Option("1.000000", 50), Option("2.000000", 25, "Heute gibt es doppelte Fame Points.")])
    ];

    public static IReadOnlyList<SettingRandomizerCatalogEntry> BuildCatalogFromIni(string iniText, bool isGerman)
    {
        var localizedKnown = GetCatalog(isGerman).ToDictionary(x => x.Key, StringComparer.OrdinalIgnoreCase);
        return ServerSettingsValidationService.ParseEntries(iniText)
            .Where(x => !string.IsNullOrWhiteSpace(x.Key))
            .Select(entry =>
            {
                localizedKnown.TryGetValue(entry.Key, out var known);
                var displayName = known?.DisplayName ?? HumanizeSettingKey(entry.Key);
                var options = BuildAutomaticOptions(entry.Key, entry.Value, known?.DefaultOptions);
                return new SettingRandomizerCatalogEntry(entry.Section, entry.Key, displayName, options);
            })
            .OrderBy(x => x.Section, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(x => x.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<SettingRandomizerOption> BuildAutomaticOptions(
        string key,
        string currentValue,
        IReadOnlyList<SettingRandomizerOption>? knownOptions)
    {
        var options = new List<SettingRandomizerOption>
        {
            Option(currentValue, 1000)
        };

        if (knownOptions is not null)
        {
            options.AddRange(knownOptions.Select(x => Option(x.Value, x.ChancePercent, x.DiscordAnnouncement)));
        }
        else if (bool.TryParse(currentValue, out var currentBool))
        {
            options.Add(Option((!currentBool).ToString(), 50));
        }
        else if (double.TryParse(currentValue.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && Math.Abs(number + 1d) > 0.000001d)
        {
            var decimals = DecimalPlaces(currentValue);
            var integer = decimals == 0 && !key.Contains("Multiplier", StringComparison.OrdinalIgnoreCase);
            options.Add(Option(FormatSuggestedNumber(number * 0.75d, decimals, integer), 25));
            options.Add(Option(FormatSuggestedNumber(number * 1.25d, decimals, integer), 25));
        }

        return options
            .Where(x => !string.IsNullOrWhiteSpace(x.Value))
            .GroupBy(x => x.Value, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .ToList();
    }

    private static int DecimalPlaces(string value)
    {
        var normalized = value.Replace(',', '.');
        var separator = normalized.IndexOf('.');
        return separator < 0 ? 0 : Math.Min(6, normalized.Length - separator - 1);
    }

    private static string FormatSuggestedNumber(double value, int decimals, bool integer) => integer
        ? Math.Round(value, MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture)
        : value.ToString("F" + Math.Max(1, decimals), CultureInfo.InvariantCulture);

    private static string HumanizeSettingKey(string key)
    {
        var name = key.StartsWith("scum.", StringComparison.OrdinalIgnoreCase) ? key[5..] : key;
        name = name.Replace('_', ' ').Replace('.', ' ');
        return Regex.Replace(name, "(?<=[a-z0-9])(?=[A-Z])", " ").Trim();
    }
    public static IReadOnlyList<SettingRandomizerCatalogEntry> GetCatalog(bool isGerman)
    {
        if (isGerman) return Catalog;
        return Catalog.Select(entry => new SettingRandomizerCatalogEntry(
            entry.Section,
            entry.Key,
            GetEnglishDisplayName(entry.Key, entry.DisplayName),
            entry.DefaultOptions.Select(option => new SettingRandomizerOption
            {
                Value = option.Value,
                ChancePercent = option.ChancePercent,
                DiscordAnnouncement = string.IsNullOrWhiteSpace(option.DiscordAnnouncement)
                    ? string.Empty
                    : BuildEnglishAnnouncement(entry.Key, entry.DisplayName, option.Value)
            }).ToList())).ToList();
    }

    public static string GetLocalizedDisplayName(string key, string current, bool isGerman)
    {
        var source = Catalog.FirstOrDefault(x => x.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (source is null) return current;
        var english = GetEnglishDisplayName(key, source.DisplayName);
        return current.Equals(source.DisplayName, StringComparison.OrdinalIgnoreCase) || current.Equals(english, StringComparison.OrdinalIgnoreCase)
            ? (isGerman ? source.DisplayName : english)
            : current;
    }

    public static string GetLocalizedAnnouncement(string key, string value, string current, bool isGerman)
    {
        if (string.IsNullOrWhiteSpace(current)) return string.Empty;
        var source = Catalog.FirstOrDefault(x => x.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        var sourceOption = source?.DefaultOptions.FirstOrDefault(x => x.Value.Equals(value, StringComparison.OrdinalIgnoreCase));
        if (source is null || sourceOption is null || string.IsNullOrWhiteSpace(sourceOption.DiscordAnnouncement)) return current;
        var english = BuildEnglishAnnouncement(key, source.DisplayName, value);
        return current.Equals(sourceOption.DiscordAnnouncement, StringComparison.Ordinal) || current.Equals(english, StringComparison.Ordinal)
            ? (isGerman ? sourceOption.DiscordAnnouncement : english)
            : current;
    }

    private static string BuildEnglishAnnouncement(string key, string germanName, string value) =>
        $"{GetEnglishDisplayName(key, germanName)} is now set to {value}.";

    private static string GetEnglishDisplayName(string key, string fallback) => key switch
    {
        "scum.DisableSentrySpawning" => "Disable sentry spawning",
        "scum.AreAnimalsAllowedInWorld" => "Animals in the world",
        "scum.EnableFog" => "Enable fog",
        "scum.RadiationEnabled" => "Enable radiation",
        "scum.EnableDropshipAbandonedBunkerEncounter" => "Dropship at abandoned bunkers",
        "scum.PuppetHealthMultiplier" => "Puppet health",
        "scum.AnimalGlobalDensityMultiplier" => "Animal density",
        "scum.EncounterHordeActivationChanceMultiplier" => "Horde activation chance",
        "scum.ZombieDamageMultiplier" => "Puppet damage",
        "scum.SpawnerProbabilityMultiplier" => "Loot spawn chance",
        "scum.EnableSentryRespawning" => "Sentry respawn",
        "scum.DisableSuicidePuppetSpawning" => "Disable suicide puppets",
        "scum.DisableLootPuppetSpawning" => "Disable loot puppets",
        "scum.EnableLootPuppetHorde" => "Loot puppet hordes",
        "scum.PuppetsCanOpenDoors" => "Puppets can open doors",
        "scum.PuppetsCanVaultWindows" => "Puppets can vault windows",
        "scum.EnableDropshipBaseBuildingEncounter" => "Dropships at player bases",
        "scum.PuppetRunningSpeedMultiplier" => "Puppet running speed",
        "scum.MaxAllowedPuppets" => "Maximum puppets",
        "scum.EncounterBaseCharacterAmountMultiplier" => "Base encounter size",
        "scum.EncounterExtraCharacterPerPlayerMultiplier" => "Extra encounter characters per player",
        "scum.EncounterHordeBaseCharacterAmountMultiplier" => "Base horde size",
        "scum.EncounterHordeShouldPlayActivationSound" => "Horde warning sound",
        "scum.ArmedNPCDifficultyLevel" => "Armed NPC difficulty",
        "scum.ArmedNPCHealthMultiplier" => "Armed NPC health",
        "scum.ArmedNPCDamageMultiplier" => "Armed NPC damage",
        "scum.TimeOfDaySpeed" => "Time-of-day speed",
        "scum.NighttimeDarkness" => "Nighttime darkness",
        "scum.EnableLockedLootContainers" => "Locked loot containers",
        "scum.SentryDamageMultiplier" => "Sentry damage",
        "scum.HumanToHumanDamageMultiplier" => "PvP damage",
        "scum.ExamineSpawnerProbabilityMultiplier" => "Examine loot chance",
        "scum.StaminaDrainOnJumpMultiplier" => "Jump stamina drain",
        "scum.StaminaDrainOnClimbMultiplier" => "Climb stamina drain",
        "scum.TurretsAttackPuppets" => "Turrets attack puppets",
        "scum.TurretsAttackVehicles" => "Turrets attack vehicles",
        "scum.TurretsAttackAnimals" => "Turrets attack animals",
        "scum.AllowSectorRespawn" => "Sector respawn",
        "scum.AllowShelterRespawn" => "Shelter respawn",
        "scum.AllowSquadmateRespawn" => "Squadmate respawn",
        "scum.GasolinePricePerUnitMultiplier" => "Gasoline price",
        "scum.GasolinePeriodicInitialAmountMultiplier" => "Gasoline initial amount",
        "scum.GasolinePeriodicMaxAmountMultiplier" => "Gasoline maximum amount",
        "scum.GasolinePeriodicReplenishAmountMultiplier" => "Gasoline periodic refill amount",
        "scum.GasolinePeriodicReplenishIntervalMultiplier" => "Gasoline periodic refill interval",
        "scum.GasolineProximityReplenishAmountMultiplier" => "Gasoline proximity refill amount",
        "scum.GasolineProximityReplenishChanceMultiplier" => "Gasoline proximity refill chance",
        "scum.GasolineProximityReplenishTimeoutMultiplier" => "Gasoline proximity refill timeout",
        "scum.FameGainMultiplier" => "Fame gain",
        _ => fallback
    };
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

    public static string BuildDefaultPacksJson() => JsonSerializer.Serialize(new[]
    {
        ConvertRuleToPack(Catalog[0].CreateRule(enabled: true)),
        ConvertRuleToPack(Catalog[1].CreateRule()),
        ConvertRuleToPack(Catalog[2].CreateRule()),
        BuildGasolinePack()
    }, JsonOptions);

    public static SettingRandomizerPack ConvertRuleToPack(SettingRandomizerRule rule) => new()
    {
        Enabled = rule.Enabled,
        Name = rule.DisplayName,
        Options = rule.Options.Select((option, index) => new SettingRandomizerPackOption
        {
            Name = string.IsNullOrWhiteSpace(option.Value) ? $"Variante {index + 1}" : option.Value,
            ChancePercent = option.ChancePercent,
            StatusText = option.DiscordAnnouncement,
            Values =
            [
                new SettingRandomizerPackValue
                {
                    Section = rule.Section,
                    Key = rule.Key,
                    DisplayName = rule.DisplayName,
                    Value = option.Value
                }
            ]
        }).ToList()
    };

    public static SettingRandomizerPack BuildGasolinePack()
    {
        static SettingRandomizerPackValue V(string key, string name, string value) => new()
        {
            Section = "Features",
            Key = key,
            DisplayName = name,
            Value = value
        };
        static List<SettingRandomizerPackValue> Scenario(string price, string initial, string max, string refill, string interval, string proximityAmount, string proximityChance, string proximityTimeout) =>
        [
            V("scum.GasolinePricePerUnitMultiplier", "Benzinpreis", price),
            V("scum.GasolinePeriodicInitialAmountMultiplier", "Benzin: Startmenge", initial),
            V("scum.GasolinePeriodicMaxAmountMultiplier", "Benzin: maximale Menge", max),
            V("scum.GasolinePeriodicReplenishAmountMultiplier", "Benzin: Auffüllmenge", refill),
            V("scum.GasolinePeriodicReplenishIntervalMultiplier", "Benzin: Auffüllintervall", interval),
            V("scum.GasolineProximityReplenishAmountMultiplier", "Benzin: Nähe-Auffüllmenge", proximityAmount),
            V("scum.GasolineProximityReplenishChanceMultiplier", "Benzin: Nähe-Auffüllchance", proximityChance),
            V("scum.GasolineProximityReplenishTimeoutMultiplier", "Benzin: Nähe-Timeout", proximityTimeout)
        ];

        return new SettingRandomizerPack
        {
            Enabled = false,
            Name = "Benzinversorgung",
            Options =
            [
                new SettingRandomizerPackOption { Name = "Knapp", ChancePercent = 25, StatusText = "Benzin ist knapp und teuer.", Values = Scenario("1.500000", "0.500000", "0.500000", "0.500000", "2.000000", "0.500000", "0.500000", "2.000000") },
                new SettingRandomizerPackOption { Name = "Normal", ChancePercent = 50, StatusText = "Die Benzinversorgung ist stabil.", Values = Scenario("0.800000", "1.000000", "1.000000", "1.000000", "1.000000", "1.000000", "1.000000", "1.000000") },
                new SettingRandomizerPackOption { Name = "Überfluss", ChancePercent = 25, StatusText = "Die Tankstellen sind besonders gut versorgt.", Values = Scenario("0.500000", "2.000000", "2.000000", "2.000000", "0.500000", "2.000000", "2.000000", "0.500000") }
            ]
        };
    }

    public static List<SettingRandomizerPack> ParsePacks(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return JsonSerializer.Deserialize<List<SettingRandomizerPack>>(BuildDefaultPacksJson(), JsonOptions) ?? new();
        return JsonSerializer.Deserialize<List<SettingRandomizerPack>>(json, JsonOptions) ?? new();
    }

    public static string SerializePacks(IEnumerable<SettingRandomizerPack> packs) => JsonSerializer.Serialize(packs, JsonOptions);
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
    bool Uploaded,
    string UpdatedText)
{
    public string Summary => GetSummary(true);

    public string GetSummary(bool isGerman) => Selections.Count == 0
        ? (isGerman ? "Keine Einstellungen ausgewählt." : "No settings selected.")
        : string.Join(" | ", Selections.Select(x => $"{x.DisplayName}: {x.OldValue} → {x.NewValue}"));
    public string DiscordText => string.Join(
        Environment.NewLine,
        Selections.Select(x => x.DiscordAnnouncement.Trim()).Where(x => x.Length > 0).Distinct(StringComparer.Ordinal));
}
