namespace ScumRconTool.Services;

public sealed record ChallengeReactivationCandidate(string Id, string Title, bool IsQuiz);

public static class ChallengeReactivationService
{
    public static IReadOnlyList<ChallengeReactivationCandidate> Find(
        IReadOnlyCollection<WeeklyCommunityTaskDefinition> previous,
        IReadOnlyCollection<WeeklyCommunityTaskDefinition> current)
    {
        var oldById = previous
            .Where(x => !string.IsNullOrWhiteSpace(x.Id))
            .GroupBy(x => x.Id.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
        var baselines = WeeklyCommunityTaskService.LoadSavedBaselines()
            .Where(x => !string.IsNullOrWhiteSpace(x.TaskId))
            .GroupBy(x => x.TaskId.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.OrderByDescending(y => y.CreatedUtc).First(), StringComparer.OrdinalIgnoreCase);
        var quizzes = new QuizChallengeService().GetSnapshots()
            .Where(x => !string.IsNullOrWhiteSpace(x.DefinitionId))
            .GroupBy(x => x.DefinitionId.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.OrderByDescending(y => y.CreatedUtc).First(), StringComparer.OrdinalIgnoreCase);
        var nowUtc = DateTime.UtcNow;
        var result = new List<ChallengeReactivationCandidate>();

        foreach (var next in current.Where(x => x.Enabled && !string.IsNullOrWhiteSpace(x.Id)))
        {
            if (!oldById.TryGetValue(next.Id.Trim(), out var old)) continue;
            var isQuiz = string.Equals(next.Type, "Quiz", StringComparison.OrdinalIgnoreCase);
            baselines.TryGetValue(next.Id.Trim(), out var baseline);
            quizzes.TryGetValue(next.Id.Trim(), out var quiz);
            var hasHistory = isQuiz ? quiz is not null : baseline is not null;
            if (!hasHistory) continue;

            var scheduleChanged = old.DurationHours != next.DurationHours ||
                                  !Same(old.StartUtc, next.StartUtc) ||
                                  !Same(old.EndUtc, next.EndUtc);
            var oldStart = WeeklyCommunityTaskService.GetTaskStartUtc(old) ?? baseline?.CreatedUtc ?? quiz?.CreatedUtc;
            var oldEnd = oldStart.HasValue ? WeeklyCommunityTaskService.GetTaskEndUtc(old, oldStart.Value) : null;
            var closed = !old.Enabled || baseline?.CompletedUtc is not null || quiz is { IsActive: false } || (oldEnd.HasValue && oldEnd.Value <= nowUtc);
            if ((!old.Enabled && next.Enabled) || (closed && scheduleChanged))
                result.Add(new ChallengeReactivationCandidate(next.Id.Trim(), string.IsNullOrWhiteSpace(next.Title) ? next.Id.Trim() : next.Title.Trim(), isQuiz));
        }

        return result;
    }

    private static bool Same(string? left, string? right) =>
        string.Equals((left ?? string.Empty).Trim(), (right ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);
}
