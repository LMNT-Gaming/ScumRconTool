using System.Text.Json;

namespace ScumRconTool.Services;

public sealed class AutoMessageStep
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = "Text"; // Text oder Challenges
    public string Text { get; set; } = string.Empty;
    public string MessageType { get; set; } = string.Empty;
    public string Mode { get; set; } = "Queue"; // Queue oder Standalone
    public int IntervalMinutes { get; set; } = 15;
    public bool Enabled { get; set; } = true;
}

public static class AutoMessageFlow
{
    public static IReadOnlyList<AutoMessageStep> Parse(string json, string fallbackMessageType, bool includeDisabled = false)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(json)) return BuildDefaultSteps();
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            };

            var steps = JsonSerializer.Deserialize<List<AutoMessageStep>>(json, options) ?? new List<AutoMessageStep>();
            return steps
                .Where(x => x is not null)
                .Select(x => new AutoMessageStep
                {
                    Name = x.Name?.Trim() ?? string.Empty,
                    Type = string.IsNullOrWhiteSpace(x.Type) ? "Text" : x.Type.Trim(),
                    Text = x.Text?.Trim() ?? string.Empty,
                    MessageType = string.IsNullOrWhiteSpace(x.MessageType) ? fallbackMessageType : x.MessageType.Trim(),
                    Mode = string.IsNullOrWhiteSpace(x.Mode) ? "Queue" : x.Mode.Trim(),
                    IntervalMinutes = x.IntervalMinutes <= 0 ? 15 : x.IntervalMinutes,
                    Enabled = x.Enabled
                })
                .Where(x => includeDisabled || x.Enabled)
                .Where(x => includeDisabled || IsChallengeStep(x.Type) || !string.IsNullOrWhiteSpace(x.Text))
                .ToList();
        }
        catch
        {
            return new List<AutoMessageStep>
            {
                new() { Name = "JSON Fehler", Type = "Text", Text = "Auto Messages JSON fehlerhaft. Bitte Flow pruefen.", MessageType = "Error", Mode = "Standalone", IntervalMinutes = 15 }
            };
        }
    }

    public static bool IsChallengeStep(string? type)
    {
        if (string.IsNullOrWhiteSpace(type)) return false;
        var value = type.Trim();
        return value.Equals("Challenge", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("Challenges", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("Challange", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("Challanges", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("CommunityChallenge", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("CommunityChallenges", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsStandalone(string? mode) =>
        string.Equals(mode?.Trim(), "Standalone", StringComparison.OrdinalIgnoreCase);

    public static string BuildDefaultJson()
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        return JsonSerializer.Serialize(BuildDefaultSteps(), options);
    }

    private static List<AutoMessageStep> BuildDefaultSteps() => new()
    {
        new() { Name = "Challenge Status", Type = "Challenges", MessageType = "Cyan", Mode = "Queue", IntervalMinutes = 15 },
        new() { Name = "Hilfe Hinweis", Type = "Text", Text = "Gebt /help ein fuer Hilfe.", MessageType = "Yellow", Mode = "Queue", IntervalMinutes = 15 },
        new() { Name = "Discord Hinweis", Type = "Text", Text = "Joint Discord fuer alle Updates und Funktionen.", MessageType = "Green", Mode = "Standalone", IntervalMinutes = 60 }
    };
}
