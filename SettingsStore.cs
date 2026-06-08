using System;
using System.IO;
using System.Text.Json;

namespace ScumRconTool;

public static class SettingsStore
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "RedRavenRconTool",
        "settings.json");

    public static BotSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return new BotSettings();
            }

            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<BotSettings>(json) ?? new BotSettings();
        }
        catch
        {
            return new BotSettings();
        }
    }

    public static void Save(BotSettings settings)
    {
        var directory = Path.GetDirectoryName(SettingsPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        File.WriteAllText(SettingsPath, json);
    }
}
