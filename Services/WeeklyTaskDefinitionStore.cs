using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace ScumRconTool.Services;

public static class WeeklyTaskDefinitionStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static string FilePath => Path.Combine(EventDefinitionStore.DataDirectory, "weekly_tasks.json");

    public static List<WeeklyCommunityTaskDefinition> Load(string? legacyJson = null)
    {
        Directory.CreateDirectory(EventDefinitionStore.DataDirectory);

        if (File.Exists(FilePath))
        {
            return ReadDefinitions(File.ReadAllText(FilePath));
        }

        if (!string.IsNullOrWhiteSpace(legacyJson))
        {
            var migrated = ReadDefinitions(legacyJson);
            if (migrated.Count > 0)
            {
                Save(migrated);
            }

            return migrated;
        }

        return new List<WeeklyCommunityTaskDefinition>();
    }

    public static void Save(IEnumerable<WeeklyCommunityTaskDefinition> definitions)
    {
        Directory.CreateDirectory(EventDefinitionStore.DataDirectory);
        var clean = definitions.Where(definition => definition is not null).ToList();
        File.WriteAllText(FilePath, JsonSerializer.Serialize(clean, Options));
    }

    private static List<WeeklyCommunityTaskDefinition> ReadDefinitions(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<WeeklyCommunityTaskDefinition>();
        }

        try
        {
            var trimmed = json.Trim();
            if (trimmed.StartsWith("[", StringComparison.Ordinal))
            {
                return JsonSerializer.Deserialize<List<WeeklyCommunityTaskDefinition>>(trimmed, Options)?
                    .Where(definition => definition is not null)
                    .ToList() ?? new List<WeeklyCommunityTaskDefinition>();
            }

            var single = JsonSerializer.Deserialize<WeeklyCommunityTaskDefinition>(trimmed, Options);
            return single is null
                ? new List<WeeklyCommunityTaskDefinition>()
                : new List<WeeklyCommunityTaskDefinition> { single };
        }
        catch
        {
            return new List<WeeklyCommunityTaskDefinition>
            {
                new()
                {
                    Enabled = false,
                    Id = "json-error",
                    Title = "Weekly Task JSON fehlerhaft",
                    Description = "Das JSON konnte nicht gelesen werden. Bitte Data/weekly_tasks.json pruefen.",
                    StatColumn = "puppets_killed",
                    Target = 1
                }
            };
        }
    }
}
