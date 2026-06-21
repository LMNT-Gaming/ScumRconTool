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
            var settings = JsonSerializer.Deserialize<BotSettings>(json) ?? new BotSettings();
            ApplyLegacyDiscordStatusMigration(settings, json);
            return settings;
        }
        catch
        {
            return new BotSettings();
        }
    }

    private static void ApplyLegacyDiscordStatusMigration(BotSettings settings, string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (settings.DiscordServerStatusChannelId == 0 &&
                root.TryGetProperty("DiscordPlayerListChannelId", out var oldPlayerChannel) &&
                oldPlayerChannel.TryGetUInt64(out var migratedChannelId))
            {
                settings.DiscordServerStatusChannelId = migratedChannelId;
            }

            if (!settings.AutoStartDiscordServerStatusMessage &&
                (ReadBool(root, "AutoStartDiscordStatus") ||
                 ReadBool(root, "AutoStartDiscordPlayerList") ||
                 ReadBool(root, "AutoStartDiscordWeatherChannel") ||
                 ReadBool(root, "AutoStartDiscordRandomEvents") ||
                 ReadBool(root, "DiscordRenamePlayerListChannelEnabled")))
            {
                settings.AutoStartDiscordServerStatusMessage = true;
            }
        }
        catch
        {
            // Alte Settings-Migration darf Laden niemals verhindern.
        }
    }

    private static bool ReadBool(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value) &&
               value.ValueKind is JsonValueKind.True or JsonValueKind.False &&
               value.GetBoolean();
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

    public static void SaveUiLanguage(string language)
    {
        var settings = Load();
        settings.UiLanguage = language?.StartsWith("en", StringComparison.OrdinalIgnoreCase) == true ? "en" : "de";
        Save(settings);
    }
}
