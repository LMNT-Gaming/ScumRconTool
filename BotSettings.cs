using System.IO;
using System.Text.Json;
using ScumRconTool.Services;
namespace ScumRconTool;

public sealed class BotSettings
{
    public string Host { get; set; } = "88.198.43.88";
    public int Port { get; set; } = 5377;
    public string Password { get; set; } = string.Empty;

    public string FtpHost { get; set; } = string.Empty;
    public int FtpPort { get; set; } = 22;
    public string FtpUser { get; set; } = string.Empty;
    public string FtpPassword { get; set; } = string.Empty;
    public bool FtpUseSsl { get; set; } // legacy setting, ignored for SFTP
    public string FtpRemoteDirectory { get; set; } = "/";
    public string FtpKillLogPattern { get; set; } = "kill*.log";
    public string FtpLocalDirectory { get; set; } = string.Empty;
    public int KillPollSeconds { get; set; } = 30;
    public string KillAnnounceTemplate { get; set; } = "{killer} killed {victim} {weapon} {distance}";
    public string KillAnnounceColor { get; set; } = "Red";
    public bool AutoStartKillFeed { get; set; }

    public string DiscordBotToken { get; set; } = string.Empty;
    public ulong DiscordChatLogChannelId { get; set; }
    public ulong DiscordGameBridgeChannelId { get; set; }
    public ulong DiscordPlayerListChannelId { get; set; }
    public bool DiscordChatLogEmbedsEnabled { get; set; } = true;
    public bool DiscordGameBridgeEnabled { get; set; }
    public string DiscordGameBridgeMessageType { get; set; } = "Cyan";
    public int DiscordPollSeconds { get; set; } = 60;
    public int DiscordMaxPlayers { get; set; } = 20;
    public string DiscordStatusTemplate { get; set; } = "SCUM {players}/{max} Spieler online";
    public bool AutoStartDiscordStatus { get; set; }
    public bool AutoStartDiscordPlayerList { get; set; }
    public bool AutoStartDiscordRandomEvents { get; set; } = true;
    public bool AutoStartDiscordChatLogs { get; set; }
    public bool AutoStartScripts { get; set; }
    public int ScriptPollSeconds { get; set; } = 10;
    public bool RandomQuestScheduledMode { get; set; } = true;
    public string RandomQuestRestartTimes { get; set; } = "04:00,10:00,16:00,22:00";
    public int RandomQuestIntervalMinutes { get; set; } = 60;
    public int RandomQuestStartDelayMinutes { get; set; } = 0;

    public string FtpChatLogPattern { get; set; } = "chat_*.log";
    public string FtpLoginLogPattern { get; set; } = "login_*.log";
    public int AutomationPollSeconds { get; set; } = 30;
    public bool AutoStartAutomation { get; set; }
    public bool AutoStartChatCommands { get; set; }
    public bool AutoStartJoinCommands { get; set; }
    public string ChatAutomationRulesJson { get; set; } = "";
    public string JoinAutomationRulesJson { get; set; } = "";

    public bool AutoStartWeeklyTasks { get; set; }
    public bool WeeklyTaskOnlyWhenPlayersOnline { get; set; } = true;
    public int WeeklyTaskPollMinutes { get; set; } = 30;
    public string WeeklyTaskDbRemoteFilePath { get; set; } = "/88.198.43.88_7182/SaveFiles/SCUM.db";
    public ulong WeeklyTaskDiscordChannelId { get; set; }
    public string WeeklyTaskJson { get; set; } = WeeklyCommunityTaskService.BuildDefaultTaskJson();

    public bool AutoStartAutoMessages { get; set; }
    public bool AutoMessagesOnlyWhenPlayersOnline { get; set; } = true;
    public int AutoMessagesIntervalMinutes { get; set; } = 15;
    public string AutoMessagesBroadcastType { get; set; } = "Yellow";
    public int AutoMessagesMaxLength { get; set; } = 180;
    public string AutoMessagesNoChallengeText { get; set; } = "Aktuell sind keine aktiven Community Challenges konfiguriert.";
    public string AutoMessagesFlowJson { get; set; } = AutoMessageFlow.BuildDefaultJson();

    public List<WeeklyCommunityTaskDefinition> GetWeeklyTaskDefinitions()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(WeeklyTaskJson)) return new List<WeeklyCommunityTaskDefinition> { new() };

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            };

            var trimmed = WeeklyTaskJson.Trim();
            if (trimmed.StartsWith("[", StringComparison.Ordinal))
            {
                return JsonSerializer.Deserialize<List<WeeklyCommunityTaskDefinition>>(trimmed, options)?
                    .Where(x => x is not null)
                    .ToList() ?? new List<WeeklyCommunityTaskDefinition>();
            }

            var single = JsonSerializer.Deserialize<WeeklyCommunityTaskDefinition>(trimmed, options);
            return single is null ? new List<WeeklyCommunityTaskDefinition>() : new List<WeeklyCommunityTaskDefinition> { single };
        }
        catch
        {
            return new List<WeeklyCommunityTaskDefinition>
            {
                new WeeklyCommunityTaskDefinition
                {
                    Enabled = false,
                    Id = "json-error",
                    Title = "Weekly Task JSON fehlerhaft",
                    Description = "Das JSON konnte nicht gelesen werden. Bitte Array mit [ ... ] oder ein einzelnes Objekt verwenden.",
                    StatColumn = "puppets_killed",
                    Target = 1
                }
            };
        }
    }

    public WeeklyCommunityTaskDefinition GetWeeklyTaskDefinition()
    {
        return GetWeeklyTaskDefinitions().FirstOrDefault() ?? new WeeklyCommunityTaskDefinition();
    }
}


