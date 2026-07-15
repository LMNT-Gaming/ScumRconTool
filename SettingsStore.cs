using System;
using System.IO;
using System.Text.Json;
using ScumRconTool.Services;

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
            ApplyGgconHttpLogSettingsMigration(settings, json);
            ApplyUsageDirectoryEndpointMigration(settings);
            return settings;
        }
        catch
        {
            return new BotSettings();
        }
    }

    private static void ApplyUsageDirectoryEndpointMigration(BotSettings settings)
    {
        var current = (settings.UsageDirectoryEndpointUrl ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(current) ||
            current.Equals("https://www.lmnt-gaming.net/rrrt/server-browser/api/heartbeat.php", StringComparison.OrdinalIgnoreCase) ||
            current.Equals("https://lmnt-gaming.net/rrrt/server-browser/api/heartbeat.php", StringComparison.OrdinalIgnoreCase))
        {
            settings.UsageDirectoryEndpointUrl = UsageDirectoryService.DefaultEndpointUrl;
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

    private static void ApplyGgconHttpLogSettingsMigration(BotSettings settings, string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty(nameof(BotSettings.GgconHttpLogsEnabled), out _) &&
                TryReadBool(root, "UseGgconLogsForChatCommands", out var oldEnabled))
            {
                settings.GgconHttpLogsEnabled = oldEnabled;
            }

            if (!root.TryGetProperty(nameof(BotSettings.GgconHttpLogPollSeconds), out _) &&
                TryReadInt(root, "GgconChatCommandPollSeconds", out var oldPollSeconds))
            {
                settings.GgconHttpLogPollSeconds = oldPollSeconds;
            }

            if (!root.TryGetProperty(nameof(BotSettings.GgconHttpLogInitialBackfillSeconds), out _) &&
                TryReadInt(root, "GgconChatCommandInitialBackfillSeconds", out var oldBackfillSeconds))
            {
                settings.GgconHttpLogInitialBackfillSeconds = oldBackfillSeconds;
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

    private static bool TryReadBool(JsonElement root, string propertyName, out bool result)
    {
        result = false;
        if (!root.TryGetProperty(propertyName, out var value) ||
            value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return false;
        }

        result = value.GetBoolean();
        return true;
    }

    private static bool TryReadInt(JsonElement root, string propertyName, out int result)
    {
        result = 0;
        if (!root.TryGetProperty(propertyName, out var value))
        {
            return false;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out result))
        {
            return true;
        }

        return value.ValueKind == JsonValueKind.String &&
               int.TryParse(value.GetString(), out result);
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
