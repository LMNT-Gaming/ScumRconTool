using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace ScumRconTool.Services;

public static class WeeklyTaskDefinitionStore
{
    private static readonly object Sync = new();
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
        lock (Sync)
        {
            Directory.CreateDirectory(EventDefinitionStore.DataDirectory);

            if (File.Exists(FilePath))
            {
                return ReadDefinitions(File.ReadAllText(FilePath));
            }

            if (!string.IsNullOrWhiteSpace(legacyJson))
            {
                var migrated = ReadDefinitions(legacyJson);
                if (migrated.Count > 0) SaveUnsafe(migrated);
                return migrated;
            }

            return new List<WeeklyCommunityTaskDefinition>();
        }
    }

    public static void Save(IEnumerable<WeeklyCommunityTaskDefinition> definitions)
    {
        lock (Sync) SaveUnsafe(definitions);
    }

    public static string Revision
    {
        get { lock (Sync) return GetRevisionUnsafe(); }
    }

    public static bool TrySave(IEnumerable<WeeklyCommunityTaskDefinition> definitions, string expectedRevision, out string currentRevision)
    {
        lock (Sync)
        {
            currentRevision = GetRevisionUnsafe();
            if (!string.Equals(currentRevision, expectedRevision, StringComparison.Ordinal)) return false;
            SaveUnsafe(definitions);
            currentRevision = GetRevisionUnsafe();
            return true;
        }
    }

    private static void SaveUnsafe(IEnumerable<WeeklyCommunityTaskDefinition> definitions)
    {
        Directory.CreateDirectory(EventDefinitionStore.DataDirectory);
        var clean = definitions.Where(definition => definition is not null).ToList();
        var temporary = FilePath + ".tmp";
        var json = JsonSerializer.Serialize(clean, Options);
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var writer = new StreamWriter(stream))
        {
            writer.Write(json);
            writer.Flush();
            stream.Flush(true);
        }
        File.Move(temporary, FilePath, true);
    }

    private static string GetRevisionUnsafe()
    {
        if (!File.Exists(FilePath)) return "missing";
        var info = new FileInfo(FilePath);
        return info.LastWriteTimeUtc.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture) + "-" + info.Length.ToString(System.Globalization.CultureInfo.InvariantCulture);
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
