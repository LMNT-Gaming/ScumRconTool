namespace ScumRconTool.Services;

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
    public string StartUtc { get; set; } = string.Empty; // optional, ISO-Format z.B. 2026-06-06T18:00:00Z; leer = sofort aktiv
    public int DurationHours { get; set; } // 0 = automatisch: Daily 24h, Weekly 168h
    public double MinimumParticipationPercent { get; set; } = 2.0; // Mindestbeitrag fuer erfolgreiche Squad-Teilnahme, 2% = Standard
    public string EndUtc { get; set; } = string.Empty; // optional, ISO-Format z.B. 2026-06-06T22:00:00Z
    public string RewardText { get; set; } = "Reward wird manuell fuer 1 Tag freigeschaltet.";
    public string CompletedText { get; set; } = "Community-Ziel erreicht! Reward ist fuer 1 Tag freigeschaltet.";
}

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
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public long BaselineValue { get; set; }
    public Dictionary<string, long> SquadBaselineValues { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTime? CompletedUtc { get; set; }
    public DateTime? LastAnnouncementUtc { get; set; }
    public DateTime? LastDiscordUpdateUtc { get; set; }
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
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}
