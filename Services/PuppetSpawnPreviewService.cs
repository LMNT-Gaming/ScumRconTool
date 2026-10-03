using System.Globalization;

namespace ScumRconTool.Services;

public static class PuppetSpawnPreviewService
{
    private static readonly string[] RelevantKeys =
    [
        "scum.MaxAllowedPuppets",
        "scum.EncounterBaseCharacterAmountMultiplier",
        "scum.EncounterExtraCharacterPerPlayerMultiplier",
        "scum.EncounterExtraCharacterPlayerCapMultiplier",
        "scum.EncounterHordeGroupBaseCharacterAmountMultiplier",
        "scum.EncounterHordeGroupExtraCharacterPerPlayerMultiplier",
        "scum.EncounterHordeGroupExtraCharacterPlayerCapMultiplier",
        "scum.EncounterHordeBaseCharacterAmountMultiplier",
        "scum.EncounterHordeExtraCharacterPerPlayerMultiplier",
        "scum.EncounterHordeExtraCharacterPlayerCapMultiplier",
        "scum.PuppetWorldEncounterSpawnWeightMultiplier",
        "scum.DropshipWorldEncounterSpawnWeightMultiplier"
    ];

    public static bool IsRelevant(string? key) => RelevantKeys.Contains(key ?? string.Empty, StringComparer.OrdinalIgnoreCase);

    public static string Build(IEnumerable<SettingRandomizerPackValue> variantValues, bool isGerman)
    {
        var overrides = variantValues.Where(x => IsRelevant(x.Key)).ToList();
        if (overrides.Count == 0) return string.Empty;

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (File.Exists(SettingRandomizerService.LastUploadedCachePath))
                values = ServerSettingsValidationService.ParseValues(File.ReadAllText(SettingRandomizerService.LastUploadedCachePath));
        }
        catch
        {
            // The preview still works with neutral defaults when no local INI cache is available.
        }
        foreach (var value in variantValues.Where(x => !string.IsNullOrWhiteSpace(x.Key))) values[value.Key.Trim()] = value.Value?.Trim() ?? string.Empty;

        var regular = new Multipliers(
            Number(values, "scum.EncounterBaseCharacterAmountMultiplier", 1),
            Number(values, "scum.EncounterExtraCharacterPerPlayerMultiplier", 1),
            Number(values, "scum.EncounterExtraCharacterPlayerCapMultiplier", 1));
        var horde = new Multipliers(
            Number(values, "scum.EncounterHordeGroupBaseCharacterAmountMultiplier", Number(values, "scum.EncounterHordeBaseCharacterAmountMultiplier", 1)),
            Number(values, "scum.EncounterHordeGroupExtraCharacterPerPlayerMultiplier", Number(values, "scum.EncounterHordeExtraCharacterPerPlayerMultiplier", 1)),
            Number(values, "scum.EncounterHordeGroupExtraCharacterPlayerCapMultiplier", Number(values, "scum.EncounterHordeExtraCharacterPlayerCapMultiplier", 1)));

        string Row(int players)
        {
            var poi = Estimate(3, 4, 2, 1, players, regular);
            var dynamicEncounter = Estimate(1, 3, 4, 1, players, regular);
            var lowHorde = Estimate(2, 3, 4, 1, players, horde);
            var heavyHorde = Estimate(8, 8, 0, 0, players, horde);
            return isGerman
                ? $"{players} Spieler: POI {Range(poi)}, dynamisch {Range(dynamicEncounter)}, Horde {Range(lowHorde)}, schwer {Range(heavyHorde)}"
                : $"{players} players: POI {Range(poi)}, dynamic {Range(dynamicEncounter)}, horde {Range(lowHorde)}, heavy {Range(heavyHorde)}";
        }

        var lines = new List<string> { Row(1), Row(5), Row(10) };
        if (Number(values, "scum.MaxAllowedPuppets", double.NaN) is var maxPuppets && !double.IsNaN(maxPuppets))
            lines.Add(isGerman ? $"Globales Puppet-Limit: {maxPuppets:0}" : $"Global puppet limit: {maxPuppets:0}");

        var puppetWeight = Math.Max(0, Number(values, "scum.PuppetWorldEncounterSpawnWeightMultiplier", 0));
        var dropshipWeight = Math.Max(0, Number(values, "scum.DropshipWorldEncounterSpawnWeightMultiplier", 0));
        var totalWeight = puppetWeight + dropshipWeight;
        if (totalWeight > 0)
        {
            var percent = puppetWeight / totalWeight * 100d;
            lines.Add(isGerman ? $"World-Encounter-Anteil Puppets: {percent:0.0}%" : $"Puppet share of world encounters: {percent:0.0}%");
        }
        lines.Add(isGerman
            ? "Schätzung: Spawnpunkte, Cooldowns, Zonen und globale Limits können den echten Spawn reduzieren."
            : "Estimate: spawn points, cooldowns, zones, and global limits can reduce actual spawns.");
        return string.Join(Environment.NewLine, lines);
    }

    private static (int Min, int Max) Estimate(double baseMin, double baseMax, double playerCap, double extraPerPlayer, int players, Multipliers multipliers)
    {
        var effectiveCap = Math.Max(0, playerCap * multipliers.PlayerCap);
        var cappedPlayers = Math.Min(players, effectiveCap);
        var extraPlayers = Math.Max(0, cappedPlayers - 1);
        var extra = extraPlayers * extraPerPlayer * multipliers.ExtraPerPlayer;
        return ((int)Math.Ceiling(baseMin * multipliers.Base + extra), (int)Math.Ceiling(baseMax * multipliers.Base + extra));
    }

    private static string Range((int Min, int Max) value) => value.Min == value.Max ? value.Min.ToString(CultureInfo.InvariantCulture) : $"{value.Min}-{value.Max}";
    private static double Number(Dictionary<string, string> values, string key, double fallback) =>
        values.TryGetValue(key, out var raw) && double.TryParse(raw.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : fallback;

    private readonly record struct Multipliers(double Base, double ExtraPerPlayer, double PlayerCap);
}