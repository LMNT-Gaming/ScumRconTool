using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ScumRconTool.Services;

public sealed class WeeklyChallengeWebApiService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task PublishAsync(BotSettings settings, IReadOnlyList<WeeklyCommunityTaskProgress> progresses, CancellationToken cancellationToken = default)
    {
        if (!settings.WeeklyTaskWebApiEnabled) return;
        var endpoint = (settings.WeeklyTaskWebApiEndpointUrl ?? string.Empty).Trim();
        var token = (settings.WeeklyTaskWebApiToken ?? string.Empty).Trim();
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var endpointUri) || endpointUri.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException("Challenge Web-API Endpoint ist ungueltig.");
        }
        if (token.Length < 16) throw new InvalidOperationException("Challenge Web-API Token fehlt oder ist zu kurz.");

        var payload = new WeeklyChallengeWebSnapshot
        {
            GeneratedAtUtc = DateTime.UtcNow,
            Challenges = progresses.Select(BuildChallenge).ToList()
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, endpointUri);
        request.Headers.Add("X-Challenge-Token", token);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");
        using var response = await Http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Challenge Web-API meldet HTTP {(int)response.StatusCode}: {Trim(body, 300)}");
        }
    }

    private static WeeklyChallengeWebChallenge BuildChallenge(WeeklyCommunityTaskProgress progress)
    {
        var definition = progress.Definition;
        var startUtc = WeeklyCommunityTaskService.GetTaskStartUtc(definition) ?? progress.Baseline.CreatedUtc;
        var endUtc = WeeklyCommunityTaskService.GetTaskEndUtc(definition, startUtc);
        var goals = progress.GoalProgresses.Count > 0
            ? progress.GoalProgresses
            : new List<WeeklyCommunityTaskGoalProgress>
            {
                new()
                {
                    StatTable = definition.StatTable,
                    StatColumn = definition.StatColumn,
                    DisplayName = definition.StatColumn,
                    Target = Math.Max(1, definition.Target),
                    Progress = progress.Progress,
                    Percent = progress.Percent,
                    IsCompleted = progress.IsCompleted,
                    PlayerProgress = progress.PlayerProgress
                }
            };

        return new WeeklyChallengeWebChallenge
        {
            Id = definition.Id,
            Type = WeeklyCommunityTaskService.GetTaskKind(definition),
            Title = definition.Title,
            Description = definition.Description,
            GoalScope = WeeklyCommunityTaskService.IsPersonalGoal(definition) ? "PerPlayer" : "Community",
            GoalLogic = WeeklyCommunityTaskService.RequiresAllGoals(definition) ? "All" : "Any",
            StartUtc = startUtc,
            EndUtc = endUtc,
            IsCompleted = progress.IsCompleted,
            Percent = progress.Percent,
            Reward = BuildReward(definition),
            Goals = goals.Select(goal => new WeeklyChallengeWebGoal
            {
                Key = goal.StatTable + "." + goal.StatColumn,
                Name = goal.DisplayName,
                Target = Math.Max(1, goal.Target),
                Progress = goal.Progress,
                Percent = goal.Percent,
                IsCompleted = goal.IsCompleted,
                Players = goal.PlayerProgress.Select(player => new WeeklyChallengeWebPlayer
                {
                    SteamId = player.SteamId,
                    PlayerName = player.PlayerName,
                    SquadName = player.SquadName,
                    Progress = player.Progress,
                    Target = player.Target > 0 ? player.Target : Math.Max(1, goal.Target),
                    Percent = player.Percent,
                    IsCompleted = player.IsCompleted
                }).ToList()
            }).ToList()
        };
    }

    private static string BuildReward(WeeklyCommunityTaskDefinition definition)
    {
        var packNames = WeeklyRewardItems.GetConfiguredLootPackNames(definition);
        var parts = packNames.Count > 0
            ? new List<string> { "Zufälliges Lootpack: " + string.Join(" / ", packNames) }
            : WeeklyRewardItems.GetConfigured(definition).Select(WeeklyRewardItems.Format).ToList();
        if (definition.RewardMoney > 0) parts.Add($"{definition.RewardMoney:N0} Geld");
        if (definition.RewardFame > 0) parts.Add($"{definition.RewardFame:N0} Fame");
        return string.Join(" · ", parts);
    }

    private static string Trim(string value, int length) => string.IsNullOrWhiteSpace(value)
        ? "leere Antwort"
        : value.Trim().Length <= length ? value.Trim() : value.Trim()[..length] + " …";
}

public sealed class WeeklyChallengeWebSnapshot
{
    public DateTime GeneratedAtUtc { get; set; }
    public List<WeeklyChallengeWebChallenge> Challenges { get; set; } = new();
}

public sealed class WeeklyChallengeWebChallenge
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string GoalScope { get; set; } = "Community";
    public string GoalLogic { get; set; } = "Any";
    public DateTime StartUtc { get; set; }
    public DateTime? EndUtc { get; set; }
    public bool IsCompleted { get; set; }
    public double Percent { get; set; }
    public string Reward { get; set; } = string.Empty;
    public List<WeeklyChallengeWebGoal> Goals { get; set; } = new();
}

public sealed class WeeklyChallengeWebGoal
{
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public long Target { get; set; }
    public long Progress { get; set; }
    public double Percent { get; set; }
    public bool IsCompleted { get; set; }
    public List<WeeklyChallengeWebPlayer> Players { get; set; } = new();
}

public sealed class WeeklyChallengeWebPlayer
{
    public string SteamId { get; set; } = string.Empty;
    public string PlayerName { get; set; } = string.Empty;
    public string SquadName { get; set; } = string.Empty;
    public long Progress { get; set; }
    public long Target { get; set; }
    public double Percent { get; set; }
    public bool IsCompleted { get; set; }
}
