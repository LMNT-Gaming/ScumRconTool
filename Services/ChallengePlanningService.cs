namespace ScumRconTool.Services;

public static class ChallengePlanningService
{
    public static bool Maintain(BotSettings settings, List<WeeklyCommunityTaskDefinition> definitions,
        DateTime nowUtc, Func<WeeklyCommunityTaskDefinition, bool> expired)
    {
        var changed = false;
        foreach (var task in definitions.Where(x => x.Enabled && !IsQuiz(x)))
        {
            var completed = !WeeklyCommunityTaskService.IsPersonalGoal(task) && task.CompletedRunUtc.HasValue &&
                task.CompletedRunUtc.Value.AddMinutes(Math.Max(0, settings.ChallengeCompletedVisibleMinutes)) <= nowUtc;
            if (!expired(task) && !completed) continue;
            task.Enabled = false;
            task.LastRunEndedUtc = nowUtc;
            changed = true;
        }
        if (!settings.ChallengeRotationEnabled) return changed;

        // Manual reservations also occupy slots so rotation cannot overbook them.
        var available = Math.Max(0, Math.Clamp(settings.ChallengeRotationMaximum, 1, 50) - definitions.Count(x => x.Enabled));
        var candidates = definitions.Where(x => x.AutoRotate && !x.Enabled && !IsQuiz(x))
            .Where(x => !x.LastRunEndedUtc.HasValue || x.LastRunEndedUtc.Value.AddHours(Math.Max(1, settings.ChallengeRotationPauseHours)) <= nowUtc)
            .OrderBy(x => x.LastScheduledUtc ?? DateTime.MinValue)
            .ThenBy(_ => Random.Shared.Next())
            .Take(available).ToList();
        foreach (var task in candidates)
        {
            Schedule(task, nowUtc);
            changed = true;
        }
        return changed;
    }

    public static void Schedule(WeeklyCommunityTaskDefinition task, DateTime startUtc)
    {
        WeeklyCommunityTaskService.DeleteSavedBaseline(task);
        task.StartUtc = startUtc.ToUniversalTime().ToString("O");
        task.EndUtc = string.Empty;
        task.CompletedRunUtc = null;
        task.LastScheduledUtc = startUtc.ToUniversalTime();
        task.Enabled = true;
    }

    public static void RecordCompletions(BotSettings settings, IReadOnlyList<WeeklyCommunityTaskProgress> progresses)
    {
        var definitions = settings.GetWeeklyTaskDefinitions();
        var changed = false;
        foreach (var progress in progresses.Where(x => x.IsCompleted && !WeeklyCommunityTaskService.IsPersonalGoal(x.Definition)))
        {
            var task = definitions.FirstOrDefault(x => x.Id == progress.Definition.Id && x.StartUtc == progress.Definition.StartUtc);
            if (task is null || task.CompletedRunUtc.HasValue) continue;
            task.CompletedRunUtc = DateTime.UtcNow;
            changed = true;
        }
        if (changed) WeeklyTaskDefinitionStore.Save(definitions);
    }

    private static bool IsQuiz(WeeklyCommunityTaskDefinition task) =>
        string.Equals(task.Type, "Quiz", StringComparison.OrdinalIgnoreCase);
}
