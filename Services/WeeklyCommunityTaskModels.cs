namespace ScumRconTool.Services;

public sealed record WeeklyGoalScopeOption(string Value, string DisplayName);

public sealed record WeeklyGoalLogicOption(string Value, string DisplayName);

public sealed class WeeklyCommunityTaskDefinition
{
    public bool Enabled { get; set; } = true;
    public string Id { get; set; } = "weekly-puppets";
    public string Type { get; set; } = "Weekly"; // Weekly, Daily oder eigener Text
    public string Title { get; set; } = "Zombie Jagd";
    public string Description { get; set; } = "Killt gemeinsam 10.000 Zombies.";
    public string StatTable { get; set; } = "survival_stats"; // survival_stats oder fishing_stats
    public string StatColumn { get; set; } = "puppets_killed";
    public long Target { get; set; } = 10000;
    public List<WeeklyCommunityTaskGoalDefinition> Goals { get; set; } = new(); // empty keeps the legacy single goal; GoalLogic controls multiple entries
    public string GoalLogic { get; set; } = "Any"; // Any = eines reicht, All = alle Ziele erforderlich
    public string GoalScope { get; set; } = "Community"; // Community oder PerPlayer
    public string StartUtc { get; set; } = string.Empty; // optional, ISO-Format z.B. 2026-06-06T18:00:00Z; leer = sofort aktiv
    public int DurationHours { get; set; } // 0 = automatisch: Daily 24h, Weekly 168h
    public long MinimumParticipationValue { get; set; } // 0 erlaubt die einmalige Migration alter Prozentwerte
    public double MinimumParticipationPercent { get; set; } // Nur fuer Migration alter Konfigurationen
    public string EndUtc { get; set; } = string.Empty; // optional, ISO-Format z.B. 2026-06-06T22:00:00Z
    public string RewardText { get; set; } = string.Empty;
    public string RewardMode { get; set; } = "FreeText"; // FreeText oder Item
    public string RewardDistribution { get; set; } = "PerParticipant"; // PerParticipant oder PerSquad
    public string RewardItem { get; set; } = string.Empty;
    public int RewardItemQuantity { get; set; } = 1;
    public int RewardItemStackCount { get; set; }
    public List<WeeklyRewardItemDefinition> RewardItems { get; set; } = new();
    public string RewardLootPackName { get; set; } = string.Empty;
    public List<string> RewardLootPackNames { get; set; } = new();
    public int RewardMoney { get; set; }
    public int RewardFame { get; set; }
    public string CompletedText { get; set; } = "Community-Ziel erreicht! Reward ist fuer 1 Tag freigeschaltet.";
    public int QuizNumber { get; set; } = 1;
    public string QuizQuestion { get; set; } = string.Empty;
    public string QuizAcceptedAnswers { get; set; } = string.Empty;
    public string QuizImagePath { get; set; } = string.Empty;
}

public sealed class WeeklyCommunityTaskGoalDefinition
{
    public string StatTable { get; set; } = "survival_stats";
    public string StatColumn { get; set; } = "puppets_killed";
    public long Target { get; set; } = 1000;
}

public sealed class WeeklyRewardItemDefinition
{
    public string Item { get; set; } = string.Empty;
    public int Quantity { get; set; } = 1;
    public int StackCount { get; set; }
}

public static class WeeklyRewardItems
{
    public static IReadOnlyList<string> GetConfiguredLootPackNames(WeeklyCommunityTaskDefinition definition)
    {
        var names = (definition.RewardLootPackNames ?? new List<string>())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (names.Count == 0 && !string.IsNullOrWhiteSpace(definition.RewardLootPackName))
            names.Add(definition.RewardLootPackName.Trim());
        return names;
    }

    public static WeeklyResolvedLootPack? ResolveRandomLootPack(WeeklyCommunityTaskDefinition definition)
    {
        var configuredNames = GetConfiguredLootPackNames(definition);
        if (configuredNames.Count == 0) return null;
        var packsByName = LootPackStore.Load()
            .Where(pack => pack.Enabled && !string.IsNullOrWhiteSpace(pack.Name))
            .GroupBy(pack => pack.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var candidates = configuredNames
            .Where(packsByName.ContainsKey)
            .Select(name => packsByName[name])
            .Where(pack => pack.Items.Any(item => !string.IsNullOrWhiteSpace(item.Item)))
            .ToList();
        if (candidates.Count == 0) return null;
        var selected = candidates[Random.Shared.Next(candidates.Count)];
        return new WeeklyResolvedLootPack(selected.Name, ToRewardItems(selected.Items));
    }

    public static List<WeeklyRewardItemDefinition> GetRandomConfigured(WeeklyCommunityTaskDefinition definition)
    {
        var selected = ResolveRandomLootPack(definition);
        return selected?.Items.ToList() ?? GetConfiguredManualItems(definition);
    }

    public static List<WeeklyRewardItemDefinition> GetConfigured(WeeklyCommunityTaskDefinition definition)
    {
        var packName = GetConfiguredLootPackNames(definition).FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(packName))
        {
            var pack = LootPackStore.Load().FirstOrDefault(x =>
                string.Equals(x.Name, packName, StringComparison.OrdinalIgnoreCase));
            if (pack is not null)
            {
                return ToRewardItems(pack.Items);
            }
        }

        return GetConfiguredManualItems(definition);
    }

    private static List<WeeklyRewardItemDefinition> GetConfiguredManualItems(WeeklyCommunityTaskDefinition definition)
    {
        var items = (definition.RewardItems ?? new List<WeeklyRewardItemDefinition>())
            .Where(x => x is not null && !string.IsNullOrWhiteSpace(x.Item))
            .Select(x => new WeeklyRewardItemDefinition
            {
                Item = x.Item.Trim(),
                Quantity = Math.Max(1, x.Quantity),
                StackCount = Math.Max(0, x.StackCount)
            })
            .ToList();

        if (items.Count == 0 && !string.IsNullOrWhiteSpace(definition.RewardItem))
        {
            items.Add(new WeeklyRewardItemDefinition
            {
                Item = definition.RewardItem.Trim(),
                Quantity = Math.Max(1, definition.RewardItemQuantity),
                StackCount = Math.Max(0, definition.RewardItemStackCount)
            });
        }

        return items;
    }

    private static List<WeeklyRewardItemDefinition> ToRewardItems(IEnumerable<ScumRconTool.Models.LootItem>? items) =>
        (items ?? Array.Empty<ScumRconTool.Models.LootItem>())
            .Where(item => !string.IsNullOrWhiteSpace(item.Item))
            .Select(item => new WeeklyRewardItemDefinition
            {
                Item = item.Item.Trim(),
                Quantity = Math.Max(1, item.Quantity),
                StackCount = 0
            }).ToList();

    public static string GetIconUrl(string itemName)
    {
        return "https://www.lmnt-gaming.net/images/items/" + Uri.EscapeDataString(itemName.Trim()) + ".png";
    }

    public static string Format(WeeklyRewardItemDefinition item)
    {
        return $"{Math.Max(1, item.Quantity)}x {item.Item}" + (item.StackCount > 0 ? $" (Stack {item.StackCount})" : string.Empty);
    }
}

public sealed record WeeklyResolvedLootPack(string Name, IReadOnlyList<WeeklyRewardItemDefinition> Items);

public sealed class WeeklyCommunityTaskStatTarget
{
    public WeeklyCommunityTaskStatTarget(string tableName, string columnName, string displayName, string category)
    {
        TableName = tableName;
        ColumnName = columnName;
        DisplayName = displayName;
        Category = category;
    }

    public string TableName { get; }
    public string ColumnName { get; }
    public string DisplayName { get; }
    public string Category { get; }
    public string Key => TableName + "." + ColumnName;
    public string FullDisplay => $"{Category} | {DisplayName} ({Key})";
    public string JsonSnippet => $"\"StatTable\": \"{TableName}\", \"StatColumn\": \"{ColumnName}\"";
}

public sealed class WeeklyCommunityTaskBaseline
{
    public string TaskId { get; set; } = string.Empty;
    public string StatTable { get; set; } = "survival_stats";
    public string StatColumn { get; set; } = string.Empty;
    public string GoalScope { get; set; } = "Community";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public long BaselineValue { get; set; }
    public Dictionary<string, long> SquadBaselineValues { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, long> PlayerBaselineValues { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, long> CompletedSquadProgressValues { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTime? CompletedUtc { get; set; }
    public DateTime? LastAnnouncementUtc { get; set; }
    public DateTime? LastDiscordUpdateUtc { get; set; }
}

public sealed class WeeklyCommunityTaskPlayerProgress
{
    public string SteamId { get; set; } = string.Empty;
    public string PlayerName { get; set; } = string.Empty;
    public string SquadId { get; set; } = string.Empty;
    public string SquadName { get; set; } = string.Empty;
    public bool HasSquad => !string.IsNullOrWhiteSpace(SquadId);
    public long CurrentTotal { get; set; }
    public long BaselineValue { get; set; }
    public long Progress { get; set; }
    public long Target { get; set; }
    public long Remaining { get; set; }
    public double Percent { get; set; }
    public bool IsCompleted { get; set; }
}

public sealed class WeeklyCommunityTaskSquadProgress
{
    public string SquadId { get; set; } = string.Empty;
    public string SquadName { get; set; } = string.Empty;
    public long CurrentTotal { get; set; }
    public long BaselineValue { get; set; }
    public long Progress { get; set; }
    public double Percent { get; set; }
    public bool IsSuccessfulParticipant { get; set; }
}

public sealed class WeeklyCommunityTaskGoalProgress
{
    public string StatTable { get; set; } = string.Empty;
    public string StatColumn { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public long Target { get; set; }
    public long Progress { get; set; }
    public double Percent { get; set; }
    public bool IsCompleted { get; set; }
    public List<WeeklyCommunityTaskPlayerProgress> PlayerProgress { get; set; } = new();
}

public sealed class WeeklyCommunityTaskProgress
{
    public WeeklyCommunityTaskDefinition Definition { get; set; } = new();
    public WeeklyCommunityTaskBaseline Baseline { get; set; } = new();
    public long CurrentTotal { get; set; }
    public long Progress { get; set; }
    public long Remaining { get; set; }
    public double Percent { get; set; }
    public bool IsCompleted { get; set; }
    public long MinimumParticipationValue { get; set; }
    public double MinimumParticipationPercent { get; set; }
    public List<WeeklyCommunityTaskSquadProgress> SquadProgress { get; set; } = new();
    public List<WeeklyCommunityTaskPlayerProgress> PlayerProgress { get; set; } = new();
    public List<WeeklyCommunityTaskGoalProgress> GoalProgresses { get; set; } = new();
    public int CompletedPlayerCount => PlayerProgress.Count(x => x.IsCompleted);
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}
