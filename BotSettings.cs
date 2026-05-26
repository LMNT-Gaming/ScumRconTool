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
    public bool AutoStartKillFeed { get; set; }

    public string DiscordBotToken { get; set; } = string.Empty;
    public int DiscordPollSeconds { get; set; } = 60;
    public int DiscordMaxPlayers { get; set; } = 20;
    public string DiscordStatusTemplate { get; set; } = "SCUM {players}/{max} Spieler online";
    public bool AutoStartDiscordStatus { get; set; }
    public bool AutoStartScripts { get; set; }
    public int ScriptPollSeconds { get; set; } = 10;

    public string FtpChatLogPattern { get; set; } = "chat_*.log";
    public string FtpLoginLogPattern { get; set; } = "login_*.log";
    public int AutomationPollSeconds { get; set; } = 30;
    public bool AutoStartAutomation { get; set; }
    public string ChatAutomationRulesJson { get; set; } = "";
    public string JoinAutomationRulesJson { get; set; } = "";
}

