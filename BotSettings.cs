using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using ScumRconTool.Services;
namespace ScumRconTool;

public sealed class BotSettings
{
    public string UiLanguage { get; set; } = "de";
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
    public ulong DiscordServerStatusChannelId { get; set; }
    public bool AutoStartDiscordServerStatusMessage { get; set; }
    public bool AutoStartDiscordBotStatus { get; set; } = true;
    public string DiscordBotStatusTemplate { get; set; } = "SCUM {players}/{max} Spieler online";
    public string DiscordServerStatusTitle { get; set; } = "SCUM Serverstatus";
    public string DiscordServerName { get; set; } = "LMNT-Gaming";
    public string DiscordServerAddress { get; set; } = string.Empty;
    public string GgconHttpBaseUrl { get; set; } = string.Empty;
    public int GgconHttpPort { get; set; } = 5376; // Direkter ggCON HTTP API Port. Bei GGHost oft RCON-Port minus 1.
    public string GgconHttpPassword { get; set; } = string.Empty;
    public bool AutoCheckForUpdates { get; set; } = true;
    public string UpdateLatestJsonUrl { get; set; } = "https://lmnt-gaming.net/rrrt/latest.json";
    public bool UsageDirectoryEnabled { get; set; }
    public string UsageDirectoryConsentVersion { get; set; } = string.Empty;
    public string UsageDirectoryConsentUtc { get; set; } = string.Empty;
    public string UsageDirectoryEndpointUrl { get; set; } = UsageDirectoryService.DefaultEndpointUrl;
    public string UsageDirectoryInstallId { get; set; } = string.Empty;
    public string UsageDirectoryInstallToken { get; set; } = string.Empty;
    public bool UsageDirectoryRemovalPending { get; set; }
    public bool DiscordChatLogEmbedsEnabled { get; set; } = true;
    public bool DiscordVehicleLogEmbedsEnabled { get; set; } = true;
    public bool DiscordGameBridgeEnabled { get; set; }
    public string DiscordGameBridgeMessageType { get; set; } = "Cyan";
    public int DiscordPollSeconds { get; set; } = 60;
    public int DiscordMaxPlayers { get; set; } = 20;
    public bool AutoStartDiscordChatLogs { get; set; }
    public bool AutoStartScripts { get; set; }
    public int ScriptPollSeconds { get; set; } = 10;
    public bool RandomQuestScheduledMode { get; set; } = true;
    public string RandomQuestRestartTimes { get; set; } = "04:00,10:00,16:00,22:00";
    public int RandomQuestIntervalMinutes { get; set; } = 60;
    public int RandomQuestStartDelayMinutes { get; set; } = 0;

    public string FtpChatLogPattern { get; set; } = "chat_*.log";
    public string FtpVehicleDestructionLogPattern { get; set; } = "vehicle_destruction_*.log";
    public string FtpLoginLogPattern { get; set; } = "login_*.log";
    public int AutomationPollSeconds { get; set; } = 30;
    public bool AutoStartAutomation { get; set; }
    public bool AutoStartChatCommands { get; set; }
    public bool GgconHttpLogsEnabled { get; set; } = true;
    public int GgconHttpLogPollSeconds { get; set; } = 3;
    public int GgconHttpLogInitialBackfillSeconds { get; set; } = 10;

    [JsonIgnore]
    public bool UseGgconLogsForChatCommands
    {
        get => GgconHttpLogsEnabled;
        set => GgconHttpLogsEnabled = value;
    }

    [JsonIgnore]
    public int GgconChatCommandPollSeconds
    {
        get => GgconHttpLogPollSeconds;
        set => GgconHttpLogPollSeconds = value;
    }

    [JsonIgnore]
    public int GgconChatCommandInitialBackfillSeconds
    {
        get => GgconHttpLogInitialBackfillSeconds;
        set => GgconHttpLogInitialBackfillSeconds = value;
    }

    public bool NewPlayerWelcomeEnabled { get; set; }
    public string NewPlayerWelcomeMessageType { get; set; } = "Cyan";
    public string NewPlayerWelcomeResponse { get; set; } = "[Server] Willkommen {name}! Viel Spass auf dem Server. Bei Fragen nutze den Chat oder komm auf unseren Discord.";
    public bool PaidVotesEnabled { get; set; } = true;
    public int VotePrice { get; set; } = 5000;
    public int VoteConfirmationTimeoutSeconds { get; set; } = 60;
    public string VotePurchasePromptResponse { get; set; } = "[Server] {name}, der Vote kostet {cost}$. Bestaetige mit /ja innerhalb von {timeoutSeconds}s.";
    public string VotePurchaseSuccessResponse { get; set; } = "[Server] {name} hat {cost}$ bezahlt und eine Abstimmung gestartet.";
    public string VoteInsufficientFundsResponse { get; set; } = "[Server] {name}, du hast nicht genug Geld. Vote kostet {cost}$, dein Kontostand: {balance}$.";
    public string VoteNoPendingResponse { get; set; } = "[Server] {name}, du hast keinen offenen Vote-Kauf.";
    public string VotePaymentFailedResponse { get; set; } = "[Server] {name}, der Vote-Kauf konnte nicht abgeschlossen werden. Bitte spaeter erneut versuchen.";
    public int VoteCooldownHours { get; set; } = 24;
    public string VoteCooldownBlockedResponse { get; set; } = "[Server] {name}, du kannst nur alle {cooldownHours}h eine Abstimmung starten. Verbleibend: {remaining}.";
    public bool AutoStartJoinCommands { get; set; }
    public string ChatAutomationRulesJson { get; set; } = "";
    public string RedeemCodeRulesJson { get; set; } = "";
    public string JoinAutomationRulesJson { get; set; } = "";

    public bool AutoStartWeeklyTasks { get; set; }
    public bool WeeklyTaskOnlyWhenPlayersOnline { get; set; } = true;
    public int WeeklyTaskPollMinutes { get; set; } = 30;
    public string WeeklyTaskDbRemoteFilePath { get; set; } = "/88.198.43.88_7182/SaveFiles/SCUM.db";
    public ulong WeeklyTaskDiscordChannelId { get; set; }
    public string WeeklyTaskJson { get; set; } = "";

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
            return WeeklyTaskDefinitionStore.Load(WeeklyTaskJson);
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
