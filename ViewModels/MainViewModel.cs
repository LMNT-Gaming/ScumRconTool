using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows.Input;
using ScumRconTool.Services;
using ScumRconTool.Models;
using System.IO;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Runtime.CompilerServices;
using ScumRconTool.Views;

namespace ScumRconTool.ViewModels;

public sealed class MainViewModel : ObservableObject, IAsyncDisposable
{
    private SourceRconClient? _rcon;
    private DiscordBridgeService? _discord;
    private ChatLogDiscordForwarder? _chatForwarder;
    private ChatCommandAutomationService? _chatCommands;
    private JoinCommandAutomationService? _joinCommands;
    private KillFeedAutomationService? _killFeed;
    private WeeklyCommunityTaskService? _weeklyTasks;
    private AutoMessageService? _autoMessages;
    private readonly UsageDirectoryService _usageDirectory = new();
    private bool _usageDirectoryEnabledAtLastSave;
    private EventEngine? _eventEngine;
    private CancellationTokenSource? _discordServerStatusMessageCts;
    private CancellationTokenSource? _playerStatusCts;
    private readonly SemaphoreSlim _discordStartLock = new(1, 1);
    private bool _discordServerStatusMessageLoopStarted;
    private bool _chatForwarderRequested;
    private readonly SemaphoreSlim _playerScanLock = new(1, 1);
    private readonly SemaphoreSlim _weeklyRewardClaimLock = new(1, 1);
    private readonly SemaphoreSlim _weeklyRewardNotificationLock = new(1, 1);
    private readonly WeeklyRewardStore _weeklyRewardStore = new();
    private List<ScumPlayer> _cachedPlayers = new();
    private DateTime _cachedPlayersUtc = DateTime.MinValue;
    private readonly TimeSpan _playerCacheDuration = TimeSpan.FromSeconds(25);

    public BotSettings Settings { get; } = SettingsStore.Load();
    public UiTextProvider Texts { get; }
    public ObservableCollection<string> LogLines { get; } = new();
    public ObservableCollection<ScriptFileViewModel> Scripts { get; } = new();
    public ObservableCollection<ScriptRuntimeStatusViewModel> ScriptRuntimeStatuses { get; } = new();
    public ObservableCollection<ScriptZoneMapItemViewModel> ScriptZoneMapItems { get; } = new();
    public ObservableCollection<ChatCommandRuleEditorViewModel> ChatCommandRules { get; } = new();
    public ObservableCollection<RedeemCodeEditorViewModel> RedeemCodeRules { get; } = new();
    public ObservableCollection<JoinCommandRuleEditorViewModel> JoinCommandRules { get; } = new();
    public ObservableCollection<AutoMessageEditorViewModel> AutoMessageEditors { get; } = new();
    public ObservableCollection<LootPackEditorViewModel> GlobalLootPacks { get; } = new();
    public IReadOnlyList<string> ChatMatchModes { get; } = new[] { "equals", "startswith", "contains", "regex" };
    public IReadOnlyList<string> ChatCooldownScopes { get; } = new[] { "player", "global" };
    public IReadOnlyList<string> ScumCommandSuggestions { get; } = ScumCommandCatalog.Commands;
    public IReadOnlyList<string> LootItemSuggestions { get; } = new[]
    {
        "Weapon_SKS",
        "Magazine_Clip_SKS",
        "Cal_7_62x39mm_Ammobox",
        "Copper_Coins",
        "MRE_Stew",
        "Bandage",
        "Emergency_Bandage",
        "Painkillers",
        "Screwdriver",
        "Lockpick",
        "Advanced_Lockpick",
        "Fireplace",
        "Tent",
        "Improved_Wooden_Chest"
    };
    public IReadOnlyList<string> BroadcastMessageTypes { get; } = new[] { "Yellow", "White", "Cyan", "Green", "Red", "ServerMessage", "Error" };
    public IReadOnlyList<string> AutoMessageModes { get; } = new[] { "Queue", "Standalone" };
    public IReadOnlyList<string> AutoMessageTypes { get; } = new[] { "Text", "Challenges" };
    public IReadOnlyList<string> ScriptModes { get; } = new[] { "Random", "SilentZone", "Buyzone", "RandomActivated", "DirectLive", "RandomAnnouncedZone" };
    public IReadOnlyList<string> SpawnBlockTypes { get; } = new[] { "Zombie", "Random Zombie", "Lootpuppet", "Item", "Custom", "CargoDrop", "ArmedNPC", "Vehicle" };
    public IReadOnlyList<LootSpawnModeOption> LootSpawnModeOptions { get; } = new[]
    {
        new LootSpawnModeOption("OneTotal", "Ein Lootpack insgesamt"),
        new LootSpawnModeOption("OnePerLocation", "Ein Lootpack je Lootpunkt")
    };
    public IReadOnlyList<WeeklyCommunityTaskStatTarget> WeeklyTaskStatTargets { get; } = WeeklyCommunityTaskService.AvailableStatTargets;

    public ObservableCollection<WeeklyTaskEditorViewModel> WeeklyTaskEditors { get; } = new();
    public ObservableCollection<WeeklySquadOverviewViewModel> WeeklySquadOverview { get; } = new();
    public ObservableCollection<WeeklyRewardClaimViewModel> WeeklyRewardClaims { get; } = new();
    public IReadOnlyList<string> WeeklyRewardModes { get; } = new[] { "FreeText", "Item" };
    public IReadOnlyList<string> WeeklyRewardDistributions { get; } = new[] { "PerParticipant", "PerSquad" };
    public IReadOnlyList<string> WeeklyGoalScopes { get; } = new[] { "Community", "PerPlayer" };

    private string _weeklyRewardStatus = "Squads und Rewards wurden noch nicht geladen.";
    public string WeeklyRewardStatus
    {
        get => _weeklyRewardStatus;
        set => SetProperty(ref _weeklyRewardStatus, value);
    }

    private string _usageDirectoryStatus = "LMNT Serverliste: noch keine Entscheidung fuer diese Version.";
    public string UsageDirectoryStatus
    {
        get => _usageDirectoryStatus;
        set => SetProperty(ref _usageDirectoryStatus, value);
    }
    private WeeklyTaskEditorViewModel? _selectedWeeklyTaskEditor;
    public WeeklyTaskEditorViewModel? SelectedWeeklyTaskEditor
    {
        get => _selectedWeeklyTaskEditor;
        set => SetProperty(ref _selectedWeeklyTaskEditor, value);
    }

    public IReadOnlyList<string> WeeklyTaskTypes { get; } = new[] { "Daily", "Weekly", "Event" };

    private WeeklyCommunityTaskStatTarget? _selectedWeeklyTaskStatTarget = WeeklyCommunityTaskService.AvailableStatTargets.FirstOrDefault(x => x.ColumnName == "puppets_killed");
    public WeeklyCommunityTaskStatTarget? SelectedWeeklyTaskStatTarget
    {
        get => _selectedWeeklyTaskStatTarget;
        set
        {
            if (SetProperty(ref _selectedWeeklyTaskStatTarget, value))
            {
                OnPropertyChanged(nameof(SelectedWeeklyTaskStatTargetSnippet));
                OnPropertyChanged(nameof(SelectedWeeklyTaskTargetSnippet));
                OnPropertyChanged(nameof(SelectedWeeklyTaskTarget));
            }
        }
    }

    public string SelectedWeeklyTaskStatTargetSnippet => SelectedWeeklyTaskStatTarget is null
        ? string.Empty
        : $"\"StatTable\": \"{SelectedWeeklyTaskStatTarget.TableName}\",\n\"StatColumn\": \"{SelectedWeeklyTaskStatTarget.ColumnName}\"";

    // Backward-compatible aliases for older XAML builds / cached bindings.
    public WeeklyCommunityTaskStatTarget? SelectedWeeklyTaskTarget
    {
        get => SelectedWeeklyTaskStatTarget;
        set => SelectedWeeklyTaskStatTarget = value;
    }

    public string SelectedWeeklyTaskTargetSnippet
    {
        get => SelectedWeeklyTaskStatTargetSnippet;
        set { }
    }

    private string _scriptRuntimeSummary = string.Empty;
    public string ScriptRuntimeSummary
    {
        get => _scriptRuntimeSummary;
        set => SetProperty(ref _scriptRuntimeSummary, value);
    }

    public string LogFilePath => AppLogService.CurrentLogFilePath;
    public string LogDirectory => AppLogService.LogDirectory;

    private string _discordStatus = "Offline";
    public string DiscordStatus
    {
        get => _discordStatus;
        set => SetProperty(ref _discordStatus, value);
    }


    private ScriptFileViewModel? _selectedScript;
    public ScriptFileViewModel? SelectedScript
    {
        get => _selectedScript;
        set
        {
            if (ReferenceEquals(_selectedScript, value)) return;
            if (_selectedScript is not null && !ConfirmScriptChangeAllowed())
            {
                OnPropertyChanged(nameof(SelectedScript));
                return;
            }

            if (SetProperty(ref _selectedScript, value)) LoadSelectedScript();
        }
    }

    private string _scriptJson = string.Empty;
    public string ScriptJson
    {
        get => _scriptJson;
        set => SetProperty(ref _scriptJson, value);
    }

    private ScriptStructuredEditorViewModel? _scriptEditorModel;
    public ScriptStructuredEditorViewModel? ScriptEditorModel
    {
        get => _scriptEditorModel;
        set
        {
            if (SetProperty(ref _scriptEditorModel, value))
            {
                _scriptEditorModel?.RebuildFlow();
            }
        }
    }

    private string _scriptValidation = string.Empty;
    public string ScriptValidation
    {
        get => _scriptValidation;
        set => SetProperty(ref _scriptValidation, value);
    }

    private bool _scriptHasUnsavedChanges;
    public bool ScriptHasUnsavedChanges
    {
        get => _scriptHasUnsavedChanges;
        set
        {
            if (SetProperty(ref _scriptHasUnsavedChanges, value))
            {
                OnPropertyChanged(nameof(ScriptDirtyStatus));
            }
        }
    }

    public string ScriptDirtyStatus => ScriptHasUnsavedChanges
        ? T("ScriptUnsavedChanges")
        : T("ScriptViewSaved");

    private string _rconCommand = "#ListPlayersJson";
    public string RconCommand
    {
        get => _rconCommand;
        set => SetProperty(ref _rconCommand, value);
    }

    private string _lastRconResponse = string.Empty;
    public string LastRconResponse
    {
        get => _lastRconResponse;
        set => SetProperty(ref _lastRconResponse, value);
    }

    private bool _rconConnected;
    public bool RconConnected
    {
        get => _rconConnected;
        set => SetProperty(ref _rconConnected, value);
    }

    private string _currentPlayersStatus = "-/-";
    public string CurrentPlayersStatus
    {
        get => _currentPlayersStatus;
        set => SetProperty(ref _currentPlayersStatus, value);
    }

    private bool _discordConnected;
    public bool DiscordConnected
    {
        get => _discordConnected;
        set => SetProperty(ref _discordConnected, value);
    }

    private bool _chatCommandsRunning;
    public bool ChatCommandsRunning
    {
        get => _chatCommandsRunning;
        set => SetProperty(ref _chatCommandsRunning, value);
    }

    private bool _joinCommandsRunning;
    public bool JoinCommandsRunning
    {
        get => _joinCommandsRunning;
        set => SetProperty(ref _joinCommandsRunning, value);
    }

    private bool _chatLogForwarderRunning;
    public bool ChatLogForwarderRunning
    {
        get => _chatLogForwarderRunning;
        set => SetProperty(ref _chatLogForwarderRunning, value);
    }

    private bool _killFeedRunning;
    public bool KillFeedRunning
    {
        get => _killFeedRunning;
        set => SetProperty(ref _killFeedRunning, value);
    }

    private bool _weeklyTasksRunning;
    public bool WeeklyTasksRunning
    {
        get => _weeklyTasksRunning;
        set => SetProperty(ref _weeklyTasksRunning, value);
    }

    private string _weeklyTaskStatus = string.Empty;
    public string WeeklyTaskStatus
    {
        get => _weeklyTaskStatus;
        set => SetProperty(ref _weeklyTaskStatus, value);
    }

    private bool _weeklyTaskScanInProgress;
    public bool WeeklyTaskScanInProgress
    {
        get => _weeklyTaskScanInProgress;
        private set
        {
            if (SetProperty(ref _weeklyTaskScanInProgress, value)) OnPropertyChanged(nameof(WeeklyTaskScanButtonEnabled));
        }
    }
    public bool WeeklyTaskScanButtonEnabled => !WeeklyTaskScanInProgress;

    private string _weeklyTaskNextScanText = "Naechster Scan: nicht geplant";
    public string WeeklyTaskNextScanText
    {
        get => _weeklyTaskNextScanText;
        private set => SetProperty(ref _weeklyTaskNextScanText, value);
    }

    private WeeklyCommunityTaskProgress? _weeklyTaskProgress;
    public WeeklyCommunityTaskProgress? WeeklyTaskProgress
    {
        get => _weeklyTaskProgress;
        set => SetProperty(ref _weeklyTaskProgress, value);
    }

    private List<WeeklyCommunityTaskProgress> _lastWeeklyTaskProgresses = new();

    private bool _autoMessagesRunning;
    public bool AutoMessagesRunning
    {
        get => _autoMessagesRunning;
        set => SetProperty(ref _autoMessagesRunning, value);
    }

    private string _autoMessageStatus = string.Empty;
    public string AutoMessageStatus
    {
        get => _autoMessageStatus;
        set => SetProperty(ref _autoMessageStatus, value);
    }

    private bool _scriptEngineRunning;
    public bool ScriptEngineRunning
    {
        get => _scriptEngineRunning;
        set => SetProperty(ref _scriptEngineRunning, value);
    }

    private string _versionText = "Version " + UpdateService.GetCurrentVersionText();
    public string VersionText
    {
        get => _versionText;
        set => SetProperty(ref _versionText, value);
    }

    private string _updateStatusText = string.Empty;
    public string UpdateStatusText
    {
        get => _updateStatusText;
        set => SetProperty(ref _updateStatusText, value);
    }

    private string _updateButtonText = string.Empty;
    public string UpdateButtonText
    {
        get => _updateButtonText;
        set => SetProperty(ref _updateButtonText, value);
    }

    private bool _updateAvailable;
    public bool UpdateAvailable
    {
        get => _updateAvailable;
        set => SetProperty(ref _updateAvailable, value);
    }

    private string? _updateDownloadUrl;
    private string? _updatePatchNotesUrl;

    public ICommand SaveSettingsCommand { get; }
    public ICommand ConnectRconCommand { get; }
    public ICommand SendRconCommand { get; }
    public ICommand StartDiscordCommand { get; }
    public ICommand UpdateServerStatusCommand { get; }
    public ICommand ScanChatLogCommand { get; }
    public ICommand StartChatLogForwarderCommand { get; }
    public ICommand StopChatLogForwarderCommand { get; }
    public ICommand StartChatCommandsCommand { get; }
    public ICommand StopChatCommandsCommand { get; }
    public ICommand ScanChatCommandsCommand { get; }
    public ICommand InsertDefaultChatCommandsCommand { get; }
    public ICommand AddChatCommandRuleCommand { get; }
    public ICommand RemoveChatCommandRuleCommand { get; }
    public ICommand InsertDefaultRedeemCodesCommand { get; }
    public ICommand AddRedeemCodeCommand { get; }
    public ICommand RemoveRedeemCodeCommand { get; }
    public ICommand StartJoinCommandsCommand { get; }
    public ICommand StopJoinCommandsCommand { get; }
    public ICommand ScanJoinCommandsCommand { get; }
    public ICommand ExecuteJoinCommandsForOnlinePlayersCommand { get; }
    public ICommand InsertDefaultJoinCommandsCommand { get; }
    public ICommand AddJoinCommandRuleCommand { get; }
    public ICommand RemoveJoinCommandRuleCommand { get; }
    public ICommand StartKillFeedCommand { get; }
    public ICommand StopKillFeedCommand { get; }
    public ICommand ScanKillFeedCommand { get; }
    public ICommand StartWeeklyTasksCommand { get; }
    public ICommand StopWeeklyTasksCommand { get; }
    public ICommand ScanWeeklyTasksCommand { get; }
    public ICommand ResetWeeklyTaskBaselineCommand { get; }
    public ICommand InsertDefaultWeeklyTaskCommand { get; }
    public ICommand InsertSelectedWeeklyTaskCommand { get; }
    public ICommand AddWeeklyTaskEditorCommand { get; }
    public ICommand DuplicateWeeklyTaskEditorCommand { get; }
    public ICommand DeleteWeeklyTaskEditorCommand { get; }
    public ICommand ApplyWeeklyTaskEditorCommand { get; }
    public ICommand ReloadWeeklyTaskEditorCommand { get; }
    public ICommand AddWeeklyRewardItemCommand { get; }
    public ICommand RemoveWeeklyRewardItemCommand { get; }
    public ICommand AcknowledgeWeeklyRewardCommand { get; }
    public ICommand StartAutoMessagesCommand { get; }
    public ICommand StopAutoMessagesCommand { get; }
    public ICommand SendAutoMessageNowCommand { get; }
    public ICommand InsertDefaultAutoMessagesCommand { get; }
    public ICommand ResetAutoMessageFlowCommand { get; }
    public ICommand AddAutoMessageCommand { get; }
    public ICommand RemoveAutoMessageCommand { get; }
    public ICommand ReloadAutoMessagesCommand { get; }
    public ICommand StartScriptsCommand { get; }
    public ICommand StopScriptsCommand { get; }
    public ICommand ScanScriptsCommand { get; }
    public ICommand RefreshScriptsCommand { get; }
    public ICommand ValidateScriptCommand { get; }
    public ICommand FormatScriptCommand { get; }
    public ICommand SaveScriptCommand { get; }
    public ICommand CreateScriptCommand { get; }
    public ICommand DuplicateScriptCommand { get; }
    public ICommand AddScriptCommandCommand { get; }
    public ICommand RemoveScriptCommandCommand { get; }
    public ICommand AddSpawnBlockCommand { get; }
    public ICommand RemoveSpawnBlockCommand { get; }
    public ICommand AddLootLocationVariableCommand { get; }
    public ICommand AddNpcLocationVariableCommand { get; }
    public ICommand PasteLocationVariableFromClipboardCommand { get; }
    public ICommand RemoveLocationVariableCommand { get; }
    public ICommand AddLootPackCommand { get; }
    public ICommand RemoveLootPackCommand { get; }
    public ICommand AddGlobalLootPackCommand { get; }
    public ICommand RemoveGlobalLootPackCommand { get; }
    public ICommand SaveGlobalLootPacksCommand { get; }
    public ICommand AddLootItemCommand { get; }
    public ICommand RemoveLootItemCommand { get; }
    public ICommand AddGlobalLootItemCommand { get; }
    public ICommand RemoveGlobalLootItemCommand { get; }
    public ICommand AddLootCommandPackCommand { get; }
    public ICommand RemoveLootCommandPackCommand { get; }
    public ICommand AddLootCleanupCommandsCommand { get; }
    public ICommand ClearLogCommand { get; }
    public ICommand OpenLogFolderCommand { get; }
    public ICommand CheckForUpdatesCommand { get; }
    public ICommand OpenUpdateDownloadCommand { get; }
    public ICommand OpenGgconDocsCommand { get; }
    public ICommand OpenUsageDirectorySourceCommand { get; }
    public ICommand SwitchLanguageCommand { get; }

    public MainViewModel()
    {
        Texts = new UiTextProvider(Settings.UiLanguage);
        CurrentPlayersStatus = $"-/{GetConfiguredMaxPlayers()}";
        ApplyLocalizedInitialStatusTexts();
        UpdateButtonText = Texts["CheckUpdate"];
        SaveSettingsCommand = new RelayCommand(async _ => await SaveSettingsAsync());
        ConnectRconCommand = new RelayCommand(async _ => await ConnectRconAsync());
        SendRconCommand = new RelayCommand(async _ => await SendRconAsync());
        StartDiscordCommand = new RelayCommand(async _ => await StartDiscordAsync());
        UpdateServerStatusCommand = new RelayCommand(async _ => await UpdateDiscordServerStatusMessageManualAsync());
        ScanChatLogCommand = new RelayCommand(async _ => await ScanChatLogAsync());
        StartChatLogForwarderCommand = new RelayCommand(async _ => await StartChatLogForwarderAsync());
        StopChatLogForwarderCommand = new RelayCommand(_ => StopChatLogForwarder());
        StartChatCommandsCommand = new RelayCommand(async _ => await StartChatCommandsAsync());
        StopChatCommandsCommand = new RelayCommand(_ => StopChatCommands());
        ScanChatCommandsCommand = new RelayCommand(async _ => await ScanChatCommandsOnceAsync());
        InsertDefaultChatCommandsCommand = new RelayCommand(_ => InsertDefaultChatCommands());
        AddChatCommandRuleCommand = new RelayCommand(_ => AddChatCommandRule());
        RemoveChatCommandRuleCommand = new RelayCommand(rule => RemoveChatCommandRule(rule as ChatCommandRuleEditorViewModel));
        InsertDefaultRedeemCodesCommand = new RelayCommand(_ => InsertDefaultRedeemCodes());
        AddRedeemCodeCommand = new RelayCommand(_ => AddRedeemCode());
        RemoveRedeemCodeCommand = new RelayCommand(rule => RemoveRedeemCode(rule as RedeemCodeEditorViewModel));
        StartJoinCommandsCommand = new RelayCommand(async _ => await StartJoinCommandsAsync());
        StopJoinCommandsCommand = new RelayCommand(_ => StopJoinCommands());
        ScanJoinCommandsCommand = new RelayCommand(async _ => await ScanJoinCommandsOnceAsync());
        ExecuteJoinCommandsForOnlinePlayersCommand = new RelayCommand(async _ => await ExecuteJoinCommandsForOnlinePlayersAsync());
        InsertDefaultJoinCommandsCommand = new RelayCommand(_ => InsertDefaultJoinCommands());
        AddJoinCommandRuleCommand = new RelayCommand(_ => AddJoinCommandRule());
        RemoveJoinCommandRuleCommand = new RelayCommand(rule => RemoveJoinCommandRule(rule as JoinCommandRuleEditorViewModel));
        StartKillFeedCommand = new RelayCommand(async _ => await StartKillFeedAsync());
        StopKillFeedCommand = new RelayCommand(_ => StopKillFeed());
        ScanKillFeedCommand = new RelayCommand(async _ => await ScanKillFeedOnceAsync());
        StartWeeklyTasksCommand = new RelayCommand(async _ => await StartWeeklyTasksAsync());
        StopWeeklyTasksCommand = new RelayCommand(_ => StopWeeklyTasks());
        ScanWeeklyTasksCommand = new RelayCommand(async _ => await ScanWeeklyTasksOnceAsync());
        ResetWeeklyTaskBaselineCommand = new RelayCommand(async _ => await ResetWeeklyTaskBaselineAsync());
        InsertDefaultWeeklyTaskCommand = new RelayCommand(_ => InsertDefaultWeeklyTask());
        InsertSelectedWeeklyTaskCommand = new RelayCommand(_ => InsertSelectedWeeklyTask());
        AddWeeklyTaskEditorCommand = new RelayCommand(_ => AddWeeklyTaskEditor());
        DuplicateWeeklyTaskEditorCommand = new RelayCommand(parameter => DuplicateWeeklyTaskEditor(parameter as WeeklyTaskEditorViewModel));
        DeleteWeeklyTaskEditorCommand = new RelayCommand(parameter => DeleteWeeklyTaskEditor(parameter as WeeklyTaskEditorViewModel));
        ApplyWeeklyTaskEditorCommand = new RelayCommand(_ => ApplyWeeklyTaskEditorToJson());
        ReloadWeeklyTaskEditorCommand = new RelayCommand(_ => LoadWeeklyTaskEditorsFromSettings());
        AddWeeklyRewardItemCommand = new RelayCommand(task => AddWeeklyRewardItem(task as WeeklyTaskEditorViewModel));
        RemoveWeeklyRewardItemCommand = new RelayCommand(item => RemoveWeeklyRewardItem(item as WeeklyRewardItemEditorViewModel));
        AcknowledgeWeeklyRewardCommand = new RelayCommand(claim => AcknowledgeWeeklyReward(claim as WeeklyRewardClaimViewModel));
        StartAutoMessagesCommand = new RelayCommand(async _ => await StartAutoMessagesAsync());
        StopAutoMessagesCommand = new RelayCommand(_ => StopAutoMessages());
        SendAutoMessageNowCommand = new RelayCommand(async _ => await SendAutoMessageNowAsync());
        InsertDefaultAutoMessagesCommand = new RelayCommand(_ => InsertDefaultAutoMessages());
        ResetAutoMessageFlowCommand = new RelayCommand(_ => ResetAutoMessageFlow());
        AddAutoMessageCommand = new RelayCommand(_ => AddAutoMessage());
        RemoveAutoMessageCommand = new RelayCommand(message => RemoveAutoMessage(message as AutoMessageEditorViewModel));
        ReloadAutoMessagesCommand = new RelayCommand(_ => LoadAutoMessageEditorsFromSettings());
        StartScriptsCommand = new RelayCommand(async _ => await StartScriptsAsync());
        StopScriptsCommand = new RelayCommand(_ => StopScripts());
        ScanScriptsCommand = new RelayCommand(async _ => await ScanScriptsOnceAsync());
        RefreshScriptsCommand = new RelayCommand(_ => RefreshScripts());
        ValidateScriptCommand = new RelayCommand(_ => ValidateScript());
        FormatScriptCommand = new RelayCommand(_ => FormatScript());
        SaveScriptCommand = new RelayCommand(_ => SaveScript());
        CreateScriptCommand = new RelayCommand(_ => CreateScript());
        DuplicateScriptCommand = new RelayCommand(_ => DuplicateScript());
        AddScriptCommandCommand = new RelayCommand(block => AddScriptCommand(block as ScriptBlockEditorViewModel));
        RemoveScriptCommandCommand = new RelayCommand(command => RemoveScriptCommand(command as ScriptCommandEditorViewModel));
        AddSpawnBlockCommand = new RelayCommand(_ => AddSpawnBlock());
        RemoveSpawnBlockCommand = new RelayCommand(block => RemoveSpawnBlock(block as SpawnBlockEditorViewModel));
        AddLootLocationVariableCommand = new RelayCommand(_ => AddLootLocationVariable());
        AddNpcLocationVariableCommand = new RelayCommand(_ => AddNpcLocationVariable());
        PasteLocationVariableFromClipboardCommand = new RelayCommand(location => PasteLocationVariableFromClipboard(location as ScriptLocationVariableEditorViewModel));
        RemoveLocationVariableCommand = new RelayCommand(location => RemoveLocationVariable(location as ScriptLocationVariableEditorViewModel));
        AddLootPackCommand = new RelayCommand(_ => AddLootPackReference());
        RemoveLootPackCommand = new RelayCommand(pack => RemoveLootPackReference(pack as string));
        AddGlobalLootPackCommand = new RelayCommand(_ => AddGlobalLootPack());
        RemoveGlobalLootPackCommand = new RelayCommand(pack => RemoveGlobalLootPack(pack as LootPackEditorViewModel));
        SaveGlobalLootPacksCommand = new RelayCommand(_ => SaveGlobalLootPacks());
        AddLootItemCommand = new RelayCommand(pack => AddLootItem(pack as LootPackEditorViewModel));
        RemoveLootItemCommand = new RelayCommand(item => RemoveLootItem(item as LootItemEditorViewModel));
        AddGlobalLootItemCommand = new RelayCommand(pack => AddGlobalLootItem(pack as LootPackEditorViewModel));
        RemoveGlobalLootItemCommand = new RelayCommand(item => RemoveGlobalLootItem(item as LootItemEditorViewModel));
        AddLootCommandPackCommand = new RelayCommand(_ => AddLootCommandPack());
        RemoveLootCommandPackCommand = new RelayCommand(pack => RemoveLootCommandPack(pack as LootCommandPackEditorViewModel));
        AddLootCleanupCommandsCommand = new RelayCommand(_ => AddLootCleanupCommands());
        ClearLogCommand = new RelayCommand(_ => ClearLog());
        OpenLogFolderCommand = new RelayCommand(_ => OpenLogFolder());
        CheckForUpdatesCommand = new RelayCommand(async _ => await CheckForUpdatesAsync(showMessage: true));
        OpenUpdateDownloadCommand = new RelayCommand(_ => OpenUpdateDownload());
        OpenGgconDocsCommand = new RelayCommand(_ => OpenGgconDocs());
        OpenUsageDirectorySourceCommand = new RelayCommand(_ => OpenUsageDirectorySource());
        SwitchLanguageCommand = new RelayCommand(_ => SwitchLanguage());

        EnsureLogDirectory();
        EnsureLocalLogDirectories();
        LoadGlobalLootPacks();
        RefreshScripts();
        LoadChatCommandRulesFromSettings();
        LoadRedeemCodesFromSettings();
        LoadJoinCommandRulesFromSettings();
        LoadWeeklyTaskEditorsFromSettings();
        ClearInactiveWeeklyRewardCodes();
        RefreshWeeklyRewardClaims();
        LoadAutoMessageEditorsFromSettings();
        _usageDirectoryEnabledAtLastSave = Settings.UsageDirectoryEnabled;
        Log("Red Raven Rcon Tool geladen. Logdatei: " + LogFilePath);
    }

    private void ApplyLocalizedInitialStatusTexts()
    {
        ScriptRuntimeSummary = T("ScriptEngineNotStarted");
        ScriptValidation = T("ScriptNotValidated");
        WeeklyTaskStatus = T("WeeklyTasksNotStarted");
        AutoMessageStatus = T("AutoMessagesNotStarted");
        UpdateStatusText = T("UpdateNotChecked");
        UsageDirectoryStatus = Settings.UsageDirectoryEnabled ? T("UsageDirectoryStatusEnabled") : T("UsageDirectoryStatusDisabled");
        OnPropertyChanged(nameof(ScriptDirtyStatus));
    }

    private string T(string key) => Texts[key];

    private string Tf(string key, params object[] args) =>
        string.Format(CultureInfo.CurrentCulture, Texts[key], args);


    public async Task CheckForUpdatesAsync(bool showMessage = false, bool silentIfCurrent = false, CancellationToken cancellationToken = default)
    {
        try
        {
            VersionText = "Version " + UpdateService.GetCurrentVersionText();

            if (string.IsNullOrWhiteSpace(Settings.UpdateLatestJsonUrl))
            {
                UpdateAvailable = false;
                UpdateButtonText = Texts["CheckUpdate"];
                UpdateStatusText = T("UpdateNoUrl");
                if (showMessage && !silentIfCurrent)
                {
                    MessageBox.Show("Keine Update-URL konfiguriert.", "Red Raven Rcon Tool Update", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                return;
            }

            UpdateStatusText = T("UpdateChecking");
            var updater = new UpdateService();
            var latest = await updater.GetLatestAsync(Settings.UpdateLatestJsonUrl, cancellationToken);

            if (latest is null || string.IsNullOrWhiteSpace(latest.version))
            {
                UpdateAvailable = false;
                UpdateButtonText = Texts["CheckUpdate"];
                UpdateStatusText = T("UpdateInvalidResponse");
                if (showMessage && !silentIfCurrent)
                {
                    MessageBox.Show("Update-Check lieferte keine gueltige Antwort.", "Red Raven Rcon Tool Update", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                return;
            }

            _updateDownloadUrl = latest.downloadUrl;
            _updatePatchNotesUrl = latest.patchNotesUrl;

            if (!UpdateService.IsNewer(latest.version))
            {
                UpdateAvailable = false;
                UpdateButtonText = Texts["CheckUpdate"];
                UpdateStatusText = Tf("UpdateCurrentFormat", UpdateService.GetCurrentVersionText());
                if (showMessage && !silentIfCurrent)
                {
                    MessageBox.Show($"Du nutzt bereits die aktuelle Version {UpdateService.GetCurrentVersionText()}.", "Red Raven Rcon Tool Update", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                return;
            }

            UpdateAvailable = true;
            UpdateButtonText = Texts["DownloadUpdate"];
            UpdateStatusText = latest.mandatory
                ? Tf("UpdateMandatoryAvailableFormat", latest.version)
                : Tf("UpdateAvailableFormat", latest.version);
            Log(UpdateStatusText);

            if (showMessage)
            {
                var result = MessageBox.Show(
                    $"Neue Version gefunden: v{latest.version}\nAktuell installiert: v{UpdateService.GetCurrentVersionText()}\n\nDownload jetzt oeffnen?",
                    "Red Raven Rcon Tool Update",
                    MessageBoxButton.YesNo,
                    latest.mandatory ? MessageBoxImage.Warning : MessageBoxImage.Information);

                if (result == MessageBoxResult.Yes)
                {
                    OpenUpdateDownload();
                }
            }
        }
        catch (Exception ex)
        {
            UpdateAvailable = false;
            UpdateButtonText = Texts["CheckUpdate"];
            UpdateStatusText = T("UpdateCheckFailed");
            Log("Update-Check Fehler: " + ex.Message);

            if (showMessage && !silentIfCurrent)
            {
                MessageBox.Show("Update-Check fehlgeschlagen: " + ex.Message, "Red Raven Rcon Tool Update", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    private void OpenUpdateDownload()
    {
        var url = !string.IsNullOrWhiteSpace(_updateDownloadUrl)
            ? _updateDownloadUrl
            : _updatePatchNotesUrl;

        if (string.IsNullOrWhiteSpace(url))
        {
            MessageBox.Show("Es ist kein Download-Link fuer das Update vorhanden.", "Red Raven Rcon Tool Update", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Process.Start(new ProcessStartInfo(url)
        {
            UseShellExecute = true
        });
    }

    private static void OpenGgconDocs()
    {
        Process.Start(new ProcessStartInfo("https://ggcon.gghost.games/docs/")
        {
            UseShellExecute = true
        });
    }

    private static void OpenUsageDirectorySource()
    {
        Process.Start(new ProcessStartInfo("https://github.com/LMNT-Gaming/ScumRconTool")
        {
            UseShellExecute = true
        });
    }
    private void SwitchLanguage()
    {
        Texts.Toggle();
        Settings.UiLanguage = Texts.Language;
        SettingsStore.SaveUiLanguage(Settings.UiLanguage);
        UpdateButtonText = UpdateAvailable ? Texts["DownloadUpdate"] : Texts["CheckUpdate"];
        if (!UpdateAvailable && UpdateStatusText is not null && UpdateStatusText.StartsWith("Update:", StringComparison.OrdinalIgnoreCase))
        {
            UpdateStatusText = T("UpdateNotChecked");
        }

        if (!ScriptEngineRunning)
        {
            ScriptRuntimeSummary = T("ScriptEngineNotStarted");
        }
        else
        {
            RefreshScriptRuntimeStatuses();
        }

        if (!WeeklyTasksRunning && WeeklyTaskProgress is null)
        {
            WeeklyTaskStatus = T("WeeklyTasksNotStarted");
        }

        if (!AutoMessagesRunning)
        {
            AutoMessageStatus = T("AutoMessagesNotStarted");
        }

        if (!ScriptHasUnsavedChanges && (ScriptValidation.Contains("validiert", StringComparison.OrdinalIgnoreCase) || ScriptValidation.Contains("validated", StringComparison.OrdinalIgnoreCase)))
        {
            ScriptValidation = T("ScriptNotValidated");
        }

        OnPropertyChanged(nameof(ScriptDirtyStatus));
        UsageDirectoryStatus = Settings.UsageDirectoryEnabled ? T("UsageDirectoryStatusEnabled") : T("UsageDirectoryStatusDisabled");
        Log(Texts.IsGerman ? "Sprache auf Deutsch umgestellt." : "Language switched to English.");
    }

    public async Task InitializeUsageDirectoryAsync(CancellationToken cancellationToken = default)
    {
        var currentVersion = UpdateService.GetCurrentVersionText();
        var needsConsent = !string.Equals(Settings.UsageDirectoryConsentVersion, currentVersion, StringComparison.OrdinalIgnoreCase);
        if (needsConsent)
        {
            var wasEnabled = Settings.UsageDirectoryEnabled;
            var accepted = UsageDirectoryConsentDialog.ShowConsent(Texts.IsGerman);

            Settings.UsageDirectoryConsentVersion = currentVersion;
            Settings.UsageDirectoryEnabled = accepted;
            if (Settings.UsageDirectoryEnabled)
            {
                UsageDirectoryService.EnsureIdentity(Settings);
                Settings.UsageDirectoryConsentUtc = DateTime.UtcNow.ToString("O");
                Settings.UsageDirectoryRemovalPending = false;
            }
            else if (wasEnabled)
            {
                Settings.UsageDirectoryRemovalPending = true;
            }
            SettingsStore.Save(Settings);
        }

        if (Settings.UsageDirectoryEnabled)
        {
            UsageDirectoryService.EnsureIdentity(Settings);
            Settings.UsageDirectoryRemovalPending = false;
            SettingsStore.Save(Settings);
            _usageDirectory.Start(Settings, currentVersion, Log);
            UsageDirectoryStatus = T("UsageDirectoryStatusEnabled");
        }
        else
        {
            _usageDirectory.Stop();
            if (Settings.UsageDirectoryRemovalPending)
            {
                Settings.UsageDirectoryRemovalPending = !await _usageDirectory.RemoveAsync(Settings, cancellationToken);
                SettingsStore.Save(Settings);
            }
            UsageDirectoryStatus = T("UsageDirectoryStatusDisabled");
        }

        _usageDirectoryEnabledAtLastSave = Settings.UsageDirectoryEnabled;
    }

    private async Task SaveSettingsAsync()
    {
        var wasEnabled = _usageDirectoryEnabledAtLastSave;
        SyncChatCommandRulesToSettings();
        SyncRedeemCodesToSettings();
        SyncJoinCommandRulesToSettings();
        SyncWeeklyTaskEditorsToSettings();
        SyncAutoMessageEditorsToSettings();
        SettingsStore.Save(Settings);

        if (Settings.UsageDirectoryEnabled)
        {
            if (!wasEnabled)
            {
                Settings.UsageDirectoryConsentVersion = UpdateService.GetCurrentVersionText();
                Settings.UsageDirectoryConsentUtc = DateTime.UtcNow.ToString("O");
            }
            UsageDirectoryService.EnsureIdentity(Settings);
            Settings.UsageDirectoryRemovalPending = false;
            SettingsStore.Save(Settings);
            _usageDirectory.Start(Settings, UpdateService.GetCurrentVersionText(), Log);
            UsageDirectoryStatus = T("UsageDirectoryStatusEnabled");
        }
        else
        {
            _usageDirectory.Stop();
            if (wasEnabled) Settings.UsageDirectoryRemovalPending = true;
            if (Settings.UsageDirectoryRemovalPending)
            {
                Settings.UsageDirectoryRemovalPending = !await _usageDirectory.RemoveAsync(Settings);
                SettingsStore.Save(Settings);
            }
            UsageDirectoryStatus = T("UsageDirectoryStatusDisabled");
        }

        _usageDirectoryEnabledAtLastSave = Settings.UsageDirectoryEnabled;
        Log("Einstellungen gespeichert.");
    }
    private async Task ConnectRconAsync()
    {
        if (_rcon is not null && !_rcon.Matches(Settings.Host, Settings.Port, Settings.Password))
        {
            // Bei geaenderten RCON-Zugangsdaten alle RCON-Nutzer zuerst stoppen,
            // damit keine alte Client-Instanz weiter pollt und ggCON mit Auth-Versuchen flutet.
            StopScripts();
            StopAutoMessages(persistAutoStart: false);
            StopWeeklyTasks(persistAutoStart: false);
            await _rcon.DisposeAsync();
            _rcon = null;
            RconConnected = false;
            Log("RCON Zugangsdaten geaendert: alte Verbindung und RCON-Services wurden sauber beendet.");
        }

        _rcon ??= new SourceRconClient(Settings.Host, Settings.Port, Settings.Password);
        await _rcon.ReconnectAsync();
        RconConnected = true;
        ClearPlayerCache();
        StartPlayerStatusLoop();
        Log("RCON verbunden.");
    }

    private async Task SendRconAsync()
    {
        if (_rcon is null || !_rcon.IsConnected) await ConnectRconAsync();
        if (_rcon is null) return;
        var command = RconCommand?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(command)) return;
        LastRconResponse = await _rcon.SendCommandAsync(command);
        Log("RCON gesendet: " + command);
        if (!string.IsNullOrWhiteSpace(LastRconResponse)) Log("RCON Antwort: " + TrimForLog(LastRconResponse));
    }

    private async Task StartDiscordAsync()
    {
        await _discordStartLock.WaitAsync();
        try
        {
            if (_discord is not null && _discord.IsStarted)
            {
                DiscordConnected = _discord.IsReady;
                DiscordStatus = _discord.IsReady ? "Online" : T("DiscordStartedNotReady");
                StartDiscordDependentLoops();
                return;
            }

            if (_discord is not null) await _discord.DisposeAsync();
            _discord = new DiscordBridgeService(Log, isReady =>
            {
                App.Current?.Dispatcher.Invoke(() =>
                {
                    DiscordConnected = isReady;
                    DiscordStatus = isReady ? "Online" : T("DiscordNotReady");
                });

                if (isReady)
                {
                    StartDiscordDependentLoops();
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            if (Settings.AutoStartDiscordServerStatusMessage || Settings.AutoStartDiscordBotStatus)
                            {
                                await UpdateDiscordServerStatusMessageAsync();
                            }
                        }
                        catch (Exception ex)
                        {
                            Log("Discord Ready Initial-Update Fehler: " + ex.Message);
                            AppLogService.WriteException("DiscordReadyInitialUpdate", ex);
                        }
                    });
                }
                else
                {
                    Log("Discord: Verbindung aktuell nicht bereit. Loops bleiben aktiv und versuchen beim naechsten Poll weiter.");
                }
            });

            await _discord.StartAsync(
                Settings.DiscordBotToken,
                Settings.DiscordGameBridgeEnabled ? Settings.DiscordGameBridgeChannelId : 0,
                async command =>
                {
                    if (_rcon is null || !_rcon.IsConnected) await ConnectRconAsync();
                    if (_rcon is not null) await _rcon.SendCommandAsync(command);
                },
                Settings.DiscordGameBridgeMessageType);

            DiscordConnected = _discord.IsReady;
            DiscordStatus = _discord.IsReady ? "Online" : T("DiscordStartedNotReady");
            StartDiscordDependentLoops();

            // Nicht awaiten: Discord-Ready und Message-Updates duerfen den App-Autostart niemals blockieren.
            _ = Task.Run(async () =>
            {
                try
                {
                    if (Settings.AutoStartDiscordServerStatusMessage || Settings.AutoStartDiscordBotStatus)
                    {
                        await UpdateDiscordServerStatusMessageAsync();
                    }
                }
                catch (Exception ex)
                {
                    Log("Discord Start Initial-Update Fehler: " + ex.Message);
                    AppLogService.WriteException("DiscordStartInitialUpdate", ex);
                }
            });

            Log(_discord.IsReady ? "Discord Bot verbunden." : "Discord Bot gestartet, wartet aber noch auf Ready. Details stehen im Debug Log.");
        }
        finally
        {
            _discordStartLock.Release();
        }
    }

    private void StartDiscordDependentLoops()
    {

        if (Settings.AutoStartDiscordServerStatusMessage || Settings.AutoStartDiscordBotStatus)
        {
            StartDiscordServerStatusMessageLoop();
        }

        if ((Settings.AutoStartDiscordChatLogs || _chatForwarderRequested) &&
            (Settings.DiscordChatLogEmbedsEnabled || Settings.DiscordVehicleLogEmbedsEnabled))
        {
            try
            {
                StartChatForwarder();
            }
            catch (Exception ex)
            {
                Log("Discord Chatlog Forwarder Start Fehler: " + ex.Message);
                AppLogService.WriteException("DiscordChatForwarderStart", ex);
            }
        }
    }

    private async Task ScanChatLogAsync()
    {
        EnsureLocalLogDirectories();
        if (_discord is null || !_discord.IsReady) await StartDiscordAsync();
        if (_discord is null) return;
        var forwarder = GetChatForwarder();
        await forwarder.ScanOnceAsync(Settings, Settings.DiscordChatLogChannelId);
    }

    public async Task AutoStartConfiguredAsync()
    {
        EnsureLocalLogDirectories();
        LogAutoStartFlags();

        // RCON-/SFTP-basierte Dienste zuerst starten. Discord darf diese Dienste nicht blockieren,
        // weil Discord-Ready oder Message-Updates serverseitig warten koennen.
        if (Settings.AutoStartScripts)
        {
            try
            {
                await StartScriptsAsync();
            }
            catch (Exception ex)
            {
                Log("AutoStart Scripts Fehler: " + ex.Message);
                AppLogService.WriteException("AutoStartScripts", ex);
            }
        }

        if (Settings.AutoStartChatCommands)
        {
            try
            {
                await StartChatCommandsAsync(persistAutoStart: false);
            }
            catch (Exception ex)
            {
                Log("AutoStart Chat Commands Fehler: " + ex.Message);
                AppLogService.WriteException("AutoStartChatCommands", ex);
            }
        }

        if (Settings.AutoStartJoinCommands)
        {
            try
            {
                await StartJoinCommandsAsync(persistAutoStart: false);
            }
            catch (Exception ex)
            {
                Log("AutoStart Join Commands Fehler: " + ex.Message);
                AppLogService.WriteException("AutoStartJoinCommands", ex);
            }
        }

        if (Settings.AutoStartKillFeed)
        {
            try
            {
                await StartKillFeedAsync(persistAutoStart: false);
            }
            catch (Exception ex)
            {
                Log("AutoStart Killfeed Fehler: " + ex.Message);
                AppLogService.WriteException("AutoStartKillFeed", ex);
            }
        }

        if (Settings.AutoStartWeeklyTasks)
        {
            try
            {
                await StartWeeklyTasksAsync(persistAutoStart: false);
            }
            catch (Exception ex)
            {
                Log("AutoStart Weekly Tasks Fehler: " + ex.Message);
                AppLogService.WriteException("AutoStartWeeklyTasks", ex);
            }
        }

        if (Settings.AutoStartAutoMessages)
        {
            try
            {
                await StartAutoMessagesAsync(persistAutoStart: false);
            }
            catch (Exception ex)
            {
                Log("AutoStart Auto Messages Fehler: " + ex.Message);
                AppLogService.WriteException("AutoStartAutoMessages", ex);
            }
        }


        var needsDiscord = Settings.AutoStartDiscordServerStatusMessage ||
                           Settings.AutoStartDiscordBotStatus ||
                           Settings.AutoStartDiscordChatLogs ||
                           Settings.DiscordGameBridgeEnabled;

        if (needsDiscord)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await StartDiscordAsync();
                }
                catch (Exception ex)
                {
                    Log("AutoStart Discord Fehler: " + ex.Message);
                    AppLogService.WriteException("AutoStartDiscord", ex);
                }
            });
        }
    }


    private void LogAutoStartFlags()
    {
        Log("AutoStart Flags: " +
            $"ServerStatusMessage={Settings.AutoStartDiscordServerStatusMessage}, " +
            $"BotStatus={Settings.AutoStartDiscordBotStatus}, " +
            $"ChatLogs={Settings.AutoStartDiscordChatLogs}, " +
            $"GameBridge={Settings.DiscordGameBridgeEnabled}, " +
            $"ChatCommands={Settings.AutoStartChatCommands}, " +
            $"JoinCommands={Settings.AutoStartJoinCommands}, " +
            $"KillFeed={Settings.AutoStartKillFeed}, " +
            $"WeeklyTasks={Settings.AutoStartWeeklyTasks}, " +
            $"AutoMessages={Settings.AutoStartAutoMessages}, " +
            $"Scripts={Settings.AutoStartScripts}");
    }

    private void StartDiscordServerStatusMessageLoop()
    {
        if (_discordServerStatusMessageLoopStarted && _discordServerStatusMessageCts is not null && !_discordServerStatusMessageCts.IsCancellationRequested)
        {
            return;
        }

        _discordServerStatusMessageCts?.Cancel();
        _discordServerStatusMessageCts = new CancellationTokenSource();
        _discordServerStatusMessageLoopStarted = true;
        var token = _discordServerStatusMessageCts.Token;
        var intervalSeconds = Math.Max(60, Settings.DiscordPollSeconds <= 0 ? 60 : Settings.DiscordPollSeconds);
        Log($"Discord Serverstatus/Botstatus Loop gestartet. Poll: {intervalSeconds}s.");

        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await UpdateDiscordServerStatusMessageAsync(token);
                    await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Log("Discord Serverstatus/Botstatus Loop Fehler: " + ex.Message);
                    AppLogService.WriteException("DiscordServerStatusMessageLoop", ex);
                    await SafeDelayAsync(TimeSpan.FromSeconds(10), token);
                }
            }
        }, token);
    }

    private static async Task SafeDelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }


    private async Task UpdateDiscordServerStatusMessageManualAsync(CancellationToken cancellationToken = default)
    {
        if (_discord is null || !_discord.IsReady)
        {
            await StartDiscordAsync();
        }

        await UpdateDiscordServerStatusMessageAsync(cancellationToken);
    }

    private async Task UpdateDiscordServerStatusMessageAsync(CancellationToken cancellationToken = default)
    {
        if (_discord is null || !_discord.IsReady) return;

        IReadOnlyCollection<ScumPlayer> players = Array.Empty<ScumPlayer>();
        try
        {
            players = await FetchPlayersAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            Log("Discord Serverstatus: Spieler konnten nicht gelesen werden: " + ex.Message);
            AppLogService.WriteException("DiscordServerStatusPlayers", ex);
        }

        GgconWeatherResponse? weather = null;
        try
        {
            var weatherService = new GgconHttpApiService(Settings);
            weather = await weatherService.GetWeatherAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            Log("Discord Serverstatus: Wetter konnte nicht gelesen werden: " + ex.Message);
            AppLogService.WriteException("DiscordServerStatusWeather", ex);
        }

        var maxPlayers = Settings.DiscordMaxPlayers > 0 ? Settings.DiscordMaxPlayers : 64;
        var serverName = string.IsNullOrWhiteSpace(Settings.DiscordServerName) ? "SCUM Server" : Settings.DiscordServerName;
        var serverAddress = !string.IsNullOrWhiteSpace(Settings.DiscordServerAddress)
            ? Settings.DiscordServerAddress.Trim()
            : Settings.Host;

        if (Settings.AutoStartDiscordBotStatus)
        {
            try
            {
                var statusText = FormatDiscordBotStatus(Settings.DiscordBotStatusTemplate, players.Count, maxPlayers, serverName);
                await _discord.SetStatusAsync(statusText);
                Log($"Discord Botstatus aktualisiert: {statusText}");
            }
            catch (Exception ex)
            {
                Log("Discord Botstatus konnte nicht aktualisiert werden: " + ex.Message);
                AppLogService.WriteException("DiscordBotStatusUpdate", ex);
            }
        }

        if (Settings.AutoStartDiscordServerStatusMessage)
        {
            if (Settings.DiscordServerStatusChannelId == 0)
            {
                Log("Discord Serverstatus: Channel-ID fehlt.");
            }
            else
            {
                await _discord.SendOrUpdateServerStatusAsync(
                    Settings.DiscordServerStatusChannelId,
                    Settings.DiscordServerStatusTitle,
                    serverName,
                    serverAddress,
                    players,
                    maxPlayers,
                    weather,
                    _eventEngine?.Events ?? Array.Empty<EventRuntime>());

                Log($"Discord Serverstatus-Nachricht aktualisiert: {players.Count}/{maxPlayers} Spieler, Wetter={(weather is null ? "n/a" : weather.GetWeatherScore().ToString("0.00", System.Globalization.CultureInfo.InvariantCulture))}.");
            }
        }
    }

    private static string FormatDiscordBotStatus(string template, int players, int maxPlayers, string serverName)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            template = "SCUM {players}/{max} Spieler online";
        }

        var text = template
            .Replace("{players}", players.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("{playeramount}", players.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("{max}", maxPlayers.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("{maxplayers}", maxPlayers.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("{servername}", serverName, StringComparison.OrdinalIgnoreCase)
            .Trim();

        return string.IsNullOrWhiteSpace(text) ? $"SCUM {players}/{maxPlayers} Spieler online" : text;
    }

    private async Task<List<ScumPlayer>> FetchPlayersAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        if (_cachedPlayersUtc != DateTime.MinValue && now - _cachedPlayersUtc < _playerCacheDuration)
        {
            return _cachedPlayers.ToList();
        }

        await _playerScanLock.WaitAsync(cancellationToken);
        try
        {
            now = DateTime.UtcNow;
            if (_cachedPlayersUtc != DateTime.MinValue && now - _cachedPlayersUtc < _playerCacheDuration)
            {
                return _cachedPlayers.ToList();
            }

            if (_rcon is null || !_rcon.IsConnected) await ConnectRconAsync();
            if (_rcon is null) return new List<ScumPlayer>();

            var response = await _rcon.SendCommandAsync(CommandRegistry.ListPlayersJson(), cancellationToken);
            var players = PlayerParser.ParseListPlayersJson(response)
                .Where(x => !string.IsNullOrWhiteSpace(x.DisplayName))
                .ToList();

            _cachedPlayers = players;
            _cachedPlayersUtc = DateTime.UtcNow;
            UpdateCurrentPlayersStatus(players.Count);
            return players.ToList();
        }
        finally
        {
            _playerScanLock.Release();
        }
    }

    private void ClearPlayerCache()
    {
        _cachedPlayers = new List<ScumPlayer>();
        _cachedPlayersUtc = DateTime.MinValue;
    }

    private int GetConfiguredMaxPlayers()
    {
        return Settings.DiscordMaxPlayers > 0 ? Settings.DiscordMaxPlayers : 64;
    }

    private void UpdateCurrentPlayersStatus(int? playerCount)
    {
        void Apply()
        {
            CurrentPlayersStatus = playerCount.HasValue
                ? $"{playerCount.Value}/{GetConfiguredMaxPlayers()}"
                : $"-/{GetConfiguredMaxPlayers()}";
        }

        var dispatcher = App.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(Apply);
        }
        else
        {
            Apply();
        }
    }

    private void StartPlayerStatusLoop()
    {
        if (_playerStatusCts is not null && !_playerStatusCts.IsCancellationRequested)
        {
            return;
        }

        _playerStatusCts?.Dispose();
        _playerStatusCts = new CancellationTokenSource();
        var token = _playerStatusCts.Token;

        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var players = await FetchPlayersAsync(token);
                    UpdateCurrentPlayersStatus(players.Count);
                    ClearInactiveWeeklyRewardCodes();
                    await NotifyPendingWeeklyRewardsAsync(players, token);
                    await Task.Delay(TimeSpan.FromSeconds(30), token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    UpdateCurrentPlayersStatus(null);
                    AppLogService.WriteException("PlayerStatusLoop", ex);
                    await SafeDelayAsync(TimeSpan.FromSeconds(10), token);
                }
            }
        }, token);
    }


    private void ClearInactiveWeeklyRewardCodes()
    {
        var removed = _weeklyRewardStore.ClearInactiveCodes(Settings.GetWeeklyTaskDefinitions(), DateTime.UtcNow);
        if (removed <= 0) return;
        void Apply()
        {
            WeeklyRewardStatus = $"{removed} offene Reward-Code(s) deaktivierter oder geloeschter Cards wurden entfernt.";
            RefreshWeeklyRewardClaims();
        }
        var dispatcher = App.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess()) dispatcher.Invoke(Apply); else Apply();
        Log($"Weekly Rewards: {removed} offene Codes deaktivierter oder geloeschter Cards entfernt.");
    }

    private void AcknowledgeWeeklyReward(WeeklyRewardClaimViewModel? claim)
    {
        if (claim is null || !_weeklyRewardStore.Acknowledge(claim.Id)) return;
        RefreshWeeklyRewardClaims();
        WeeklyRewardStatus = $"Reward für {claim.PlayerName} wurde durch einen Admin quittiert.";
        Log($"Weekly Reward durch Admin quittiert: {claim.TaskTitle} -> {claim.PlayerName}/{claim.SteamId}.");
    }

    private async Task ProcessWeeklyRewardsAsync(IReadOnlyList<WeeklyCommunityTaskProgress> progresses, CancellationToken cancellationToken = default)
    {
        try
        {
            ClearInactiveWeeklyRewardCodes();
            static bool HasReward(WeeklyCommunityTaskProgress x) =>
                x.Definition.RewardMoney > 0 ||
                WeeklyRewardItems.GetConfigured(x.Definition).Count > 0 ||
                !string.IsNullOrWhiteSpace(x.Definition.RewardText);

            var playerRewards = progresses
                .Where(x => string.Equals(x.Definition.GoalScope, "PerPlayer", StringComparison.OrdinalIgnoreCase))
                .Where(HasReward)
                .ToList();
            var communityRewards = progresses
                .Where(x => !string.Equals(x.Definition.GoalScope, "PerPlayer", StringComparison.OrdinalIgnoreCase) && x.IsCompleted)
                .Where(HasReward)
                .ToList();

            var created = false;
            foreach (var progress in playerRewards)
            {
                created |= _weeklyRewardStore.EnsurePlayerClaims(progress, Log);
            }

            if (communityRewards.Count > 0)
            {
                var api = new GgconHttpApiService(Settings);
                var squadsResponse = await api.GetSquadsAsync(cancellationToken);
                foreach (var progress in communityRewards)
                {
                    created |= _weeklyRewardStore.EnsureClaims(progress, squadsResponse.Squads, Log);
                }
            }

            RefreshWeeklyRewardClaims();
            WeeklyRewardStatus = $"{_weeklyRewardStore.GetAll().Count} Reward-Empfaenger gespeichert.";

            if ((created || _weeklyRewardStore.HasPendingClaims()) && _chatCommands?.IsRunning != true)
            {
                await StartChatCommandsAsync(persistAutoStart: false, ensureDefaultRules: false);
            }

            if (created)
            {
                Log("Weekly Rewards: Neue Empfaenger und Claim-Codes erstellt.");
            }
        }
        catch (Exception ex)
        {
            WeeklyRewardStatus = "Rewards konnten nicht geladen werden: " + ex.Message;
            Log("Weekly Rewards: Empfaenger konnten nicht geladen werden: " + ex.Message);
            AppLogService.WriteException("WeeklyRewards.Process", ex);
        }
    }
    private async Task NotifyPendingWeeklyRewardsAsync(IReadOnlyCollection<ScumPlayer> players, CancellationToken cancellationToken)
    {
        if (!await _weeklyRewardNotificationLock.WaitAsync(0, cancellationToken)) return;
        try
        {
            var onlineIds = players.Select(x => x.UserId ?? string.Empty).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
            var claims = _weeklyRewardStore.GetUnnotifiedFor(onlineIds);
            foreach (var claim in claims)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    await SendWeeklyRewardNoticeAsync(claim.SteamId,
                        $"[Reward] {claim.TaskTitle}: {claim.RewardSummary}. Zum Erhalten Code im Chat eingeben: {claim.Code}", cancellationToken);
                    claim.NotifiedUtc = DateTime.UtcNow;
                    claim.LastError = string.Empty;
                    _weeklyRewardStore.Save();
                    Log($"Weekly Reward: Claim-Code an {claim.PlayerName}/{claim.SteamId} gesendet.");
                }
                catch (Exception ex)
                {
                    claim.LastError = "Code-DM fehlgeschlagen: " + ex.Message;
                    _weeklyRewardStore.Save();
                    AppLogService.WriteException("WeeklyRewards.Notify", ex);
                }
            }
            RefreshWeeklyRewardClaims();
        }
        finally
        {
            _weeklyRewardNotificationLock.Release();
        }
    }

    private async Task<bool> HandleWeeklyRewardClaimAsync(ChatLogMessage message, CancellationToken cancellationToken)
    {
        var code = (message.Message ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(code) || !code.StartsWith("/reward-", StringComparison.OrdinalIgnoreCase)) return false;

        await _weeklyRewardClaimLock.WaitAsync(cancellationToken);
        try
        {
            var claim = _weeklyRewardStore.FindByCode(code);
            if (claim is null)
            {
                await SendWeeklyRewardNoticeAsync(message.SteamId, "[Reward] Dieser Code ist unbekannt oder bereits eingeloest.", cancellationToken);
                return true;
            }

            if (string.IsNullOrWhiteSpace(message.SteamId) || !claim.SteamId.Equals(message.SteamId.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                await SendWeeklyRewardNoticeAsync(message.SteamId, "[Reward] Dieser Code gehoert einem anderen Spieler.", cancellationToken);
                return true;
            }

            try
            {
                if (claim.NeedsItem)
                {
                    if (_rcon is null || !_rcon.IsConnected) await ConnectRconAsync();
                    if (_rcon is null) throw new InvalidOperationException("RCON ist nicht verbunden.");

                    foreach (var item in claim.GetOrCreateRewardItems().Where(x => !string.IsNullOrWhiteSpace(x.Item) && !x.DeliveredUtc.HasValue))
                    {
                        var command = $"#GiveItem {claim.SteamId} {item.Item} {Math.Max(1, item.Quantity)}";
                        if (item.StackCount > 0) command += $" StackCount {item.StackCount}";
                        var response = await _rcon.SendCommandAsync(command, cancellationToken);
                        if (!IsSuccessfulGgconResponse(response)) throw new InvalidOperationException($"Item-Ausgabe für {item.Item} wurde von ggCON abgelehnt: " + TrimForLog(response));
                        item.DeliveredUtc = DateTime.UtcNow;
                        _weeklyRewardStore.Save();
                    }

                    if (claim.GetOrCreateRewardItems().Where(x => !string.IsNullOrWhiteSpace(x.Item)).All(x => x.DeliveredUtc.HasValue))
                    {
                        claim.ItemDeliveredUtc ??= DateTime.UtcNow;
                        _weeklyRewardStore.Save();
                    }
                }

                if (claim.NeedsMoney && !claim.MoneyDeliveredUtc.HasValue)
                {
                    await new GgconHttpApiService(Settings).AddPlayerCurrencyAsync(claim.SteamId, claim.RewardMoney, cancellationToken);
                    claim.MoneyDeliveredUtc = DateTime.UtcNow;
                    _weeklyRewardStore.Save();
                }

                if (claim.NeedsText && !claim.TextClaimedUtc.HasValue)
                {
                    claim.TextClaimedUtc = DateTime.UtcNow;
                    _weeklyRewardStore.Save();
                }

                if (claim.IsComplete)
                {
                    claim.ClaimedUtc = DateTime.UtcNow;
                    claim.LastError = string.Empty;
                    _weeklyRewardStore.Save();
                    await SendWeeklyRewardNoticeAsync(claim.SteamId, $"[Reward] Erfolgreich erhalten: {claim.RewardSummary}", cancellationToken);
                    Log($"Weekly Reward eingeloest: {claim.TaskTitle} -> {claim.PlayerName}/{claim.SteamId}: {claim.RewardSummary}");
                }
            }
            catch (Exception ex)
            {
                claim.LastError = ex.Message;
                _weeklyRewardStore.Save();
                await SendWeeklyRewardNoticeAsync(claim.SteamId, "[Reward] Auszahlung noch nicht vollstaendig. Bitte Code spaeter erneut eingeben.", cancellationToken);
                AppLogService.WriteException("WeeklyRewards.Claim", ex);
            }

            RefreshWeeklyRewardClaims();
            return true;
        }
        finally
        {
            _weeklyRewardClaimLock.Release();
        }
    }

    private async Task SendWeeklyRewardNoticeAsync(string steamId, string text, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(steamId)) return;
        if (_rcon is null || !_rcon.IsConnected) await ConnectRconAsync();
        if (_rcon is null) throw new InvalidOperationException("RCON ist nicht verbunden.");
        await _rcon.SendCommandAsync(CommandRegistry.MessagePlayer(steamId.Trim(), "Cyan", text.Replace("\r", " ").Replace("\n", " ").Trim()), cancellationToken);
    }

    private static bool IsSuccessfulGgconResponse(string response)
    {
        if (string.IsNullOrWhiteSpace(response)) return false;
        try
        {
            var start = response.IndexOf('{');
            var end = response.LastIndexOf('}');
            if (start < 0 || end <= start) return false;
            using var document = JsonDocument.Parse(response.Substring(start, end - start + 1));
            return document.RootElement.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True;
        }
        catch
        {
            return false;
        }
    }

    private void RefreshWeeklySquadOverview(IEnumerable<GgconSquadResponse> squads)
    {
        void Apply()
        {
            WeeklySquadOverview.Clear();
            foreach (var squad in squads.OrderByDescending(x => x.Score).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
            {
                WeeklySquadOverview.Add(new WeeklySquadOverviewViewModel(squad));
            }
        }
        var dispatcher = App.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess()) dispatcher.Invoke(Apply); else Apply();
    }

    private void RefreshWeeklyRewardClaims()
    {
        void Apply()
        {
            WeeklyRewardClaims.Clear();
            foreach (var claim in _weeklyRewardStore.GetAll()) WeeklyRewardClaims.Add(new WeeklyRewardClaimViewModel(claim));
        }
        var dispatcher = App.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess()) dispatcher.Invoke(Apply); else Apply();
    }

    private async Task StartChatCommandsAsync(bool persistAutoStart = true, bool ensureDefaultRules = true)
    {
        EnsureLocalLogDirectories();
        SyncChatCommandRulesToSettings();
        SyncRedeemCodesToSettings();

        if (persistAutoStart)
        {
            Settings.AutoStartChatCommands = true;
            SettingsStore.Save(Settings);
        }

        if (ensureDefaultRules && string.IsNullOrWhiteSpace(Settings.ChatAutomationRulesJson))
        {
            Settings.ChatAutomationRulesJson = ChatCommandAutomationService.BuildDefaultRulesJson();
            SettingsStore.Save(Settings);
            Log("Chat Commands: Beispiel-Regeln wurden eingefuegt. Bitte pruefen und speichern.");
        }

        // Wichtig: Beim AutoStart darf kein harter RCON-Connect erzwungen werden.
        // Nach einem Gameserver-Neustart ist RCON beim App-Start oft noch nicht erreichbar.
        // Der Service pollt trotzdem weiter und verbindet RCON erst dann, wenn eine passende Chat-Regel wirklich ausgefuehrt werden muss.
        _chatCommands?.Stop();
        _chatCommands = new ChatCommandAutomationService(
            new SftpLogService(Settings),
            async command =>
            {
                if (_rcon is null || !_rcon.IsConnected) await ConnectRconAsync();
                if (_rcon is null) return string.Empty;
                var response = await _rcon.SendCommandAsync(command);
                if (!string.IsNullOrWhiteSpace(response)) Log("RCON Antwort: " + TrimForLog(response));
                return response;
            },
            Log,
            BuildChatCommandDynamicPlaceholders,
            BuildAutoMessageChallengeTextAsync,
            HandleBuyEventCommandAsync,
            BuildRedeemCodeRules,
            MarkRedeemCodeUsedAsync,
            () => _weeklyRewardStore.HasPendingClaims(),
            HandleWeeklyRewardClaimAsync);

        _chatCommands.Start(Settings);
        ChatCommandsRunning = true;
        Log("Chat Commands AutoStart: " + Settings.AutoStartChatCommands);
    }

    private void StopChatCommands(bool persistAutoStart = true)
    {
        if (persistAutoStart)
        {
            Settings.AutoStartChatCommands = false;
            SettingsStore.Save(Settings);
        }
        _chatCommands?.Stop();
        _chatCommands = null;
        ChatCommandsRunning = false;
    }

    private async Task ScanChatCommandsOnceAsync()
    {
        EnsureLocalLogDirectories();
        SyncChatCommandRulesToSettings();
        SyncRedeemCodesToSettings();
        if (_rcon is null || !_rcon.IsConnected) await ConnectRconAsync();
        if (_rcon is null) return;

        var service = _chatCommands ?? new ChatCommandAutomationService(
            new SftpLogService(Settings),
            async command =>
            {
                if (_rcon is null || !_rcon.IsConnected) await ConnectRconAsync();
                if (_rcon is null) return string.Empty;
                var response = await _rcon.SendCommandAsync(command);
                if (!string.IsNullOrWhiteSpace(response)) Log("RCON Antwort: " + TrimForLog(response));
                return response;
            },
            Log,
            BuildChatCommandDynamicPlaceholders,
            BuildAutoMessageChallengeTextAsync,
            HandleBuyEventCommandAsync,
            BuildRedeemCodeRules,
            MarkRedeemCodeUsedAsync,
            () => _weeklyRewardStore.HasPendingClaims(),
            HandleWeeklyRewardClaimAsync);

        await service.ScanOnceAsync(Settings);
    }

    private IReadOnlyDictionary<string, string> BuildChatCommandDynamicPlaceholders()
    {
        var activeRandomEvents = (_eventEngine?.Events ?? Array.Empty<EventRuntime>())
            .Where(x => x.Definition.Enabled)
            .Where(IsRandomizedEventRuntime)
            .Where(x => x.State is EventRuntimeState.Initiated or EventRuntimeState.Live or EventRuntimeState.CleanupPending)
            .OrderByDescending(x => x.State == EventRuntimeState.Live)
            .ThenBy(x => x.Definition.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var randomEventsText = BuildRandomEventsText(activeRandomEvents);
        var randomEventNamesText = activeRandomEvents.Count == 0
            ? "Keine"
            : BuildLimitedList(activeRandomEvents.Select(x => x.Definition.Name));

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["randomEvents"] = randomEventsText,
            ["startedRandomEvents"] = randomEventsText,
            ["activeRandomEvents"] = randomEventsText,
            ["randomEventNames"] = randomEventNamesText,
            ["randomEventsCount"] = activeRandomEvents.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["randomEventsLive"] = activeRandomEvents.Count(x => x.State == EventRuntimeState.Live).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["randomEventsInitiated"] = activeRandomEvents.Count(x => x.State == EventRuntimeState.Initiated).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["randomEventsCleanup"] = activeRandomEvents.Count(x => x.State == EventRuntimeState.CleanupPending).ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
    }

    private async Task<string> HandleBuyEventCommandAsync(ChatLogMessage message, string? requestedEvent, CancellationToken cancellationToken)
    {
        await EnsureBuyEventEngineAsync();
        var buyEvents = _eventEngine?.GetBuyableEvents() ?? Array.Empty<BuyableEventSummary>();

        if (string.IsNullOrWhiteSpace(requestedEvent))
        {
            return BuildBuyEventListResponse(buyEvents);
        }

        if (string.IsNullOrWhiteSpace(message.SteamId))
        {
            Log($"BuyEvent: {message.PlayerName} kann nicht kaufen, weil die Chat-Zeile keine SteamID enthaelt.");
            return string.Empty;
        }

        var selected = FindBuyableEvent(buyEvents, requestedEvent);
        if (selected is null)
        {
            return "[Server] Event nicht gefunden. Nutze /buyevent fuer die Liste.";
        }

        var unavailable = BuildBuyEventUnavailableReason(selected);
        if (!string.IsNullOrWhiteSpace(unavailable))
        {
            return unavailable;
        }

        var price = Math.Max(0, selected.Price);
        var charged = false;
        var balance = 0d;
        var api = new GgconHttpApiService(Settings);

        try
        {
            if (price > 0)
            {
                var player = await api.GetPlayerAccountAsync(message.SteamId, cancellationToken);
                if (!player.AccountBalance.HasValue)
                {
                    return "[Server] Kontostand konnte nicht gelesen werden. Bitte spaeter erneut versuchen.";
                }

                balance = player.AccountBalance.Value;
                if (balance < price)
                {
                    return $"[Server] {message.PlayerName}, du hast nicht genug Geld. {selected.DisplayName} kostet {price}$, dein Kontostand: {FormatMoney(balance)}$.";
                }

                await api.RemovePlayerCurrencyAsync(message.SteamId, price, cancellationToken);
                charged = true;
            }

            var result = await _eventEngine!.ActivateBuyEventAsync(requestedEvent, message.SteamId, message.PlayerName, cancellationToken);
            if (!result.Success)
            {
                if (charged)
                {
                    await api.AddPlayerCurrencyAsync(message.SteamId, price, cancellationToken);
                    Log($"BuyEvent: {price}$ fuer {message.PlayerName} erstattet, weil Aktivierung fehlschlug: {result.Message}");
                }

                return "[Server] " + result.Message;
            }

            Log($"BuyEvent: {message.PlayerName}/{message.SteamId} hat {selected.DisplayName} fuer {price}$ gekauft.");
            return price > 0
                ? $"[Server] {selected.DisplayName} gekauft und aktiviert. Bezahlt: {price}$."
                : $"[Server] {selected.DisplayName} aktiviert.";
        }
        catch (Exception ex)
        {
            if (charged)
            {
                try
                {
                    await api.AddPlayerCurrencyAsync(message.SteamId, price, cancellationToken);
                    Log($"BuyEvent: {price}$ fuer {message.PlayerName} nach Fehler erstattet.");
                }
                catch (Exception refundEx)
                {
                    AppLogService.WriteException("BuyEventRefund", refundEx);
                    Log($"BuyEvent: Erstattung fuer {message.PlayerName} fehlgeschlagen: {refundEx.Message}");
                }
            }

            AppLogService.WriteException("BuyEvent", ex);
            Log("BuyEvent Fehler: " + ex.Message);
            return "[Server] Event-Kauf konnte nicht abgeschlossen werden. Bitte spaeter erneut versuchen.";
        }
    }

    private async Task EnsureBuyEventEngineAsync()
    {
        if (_eventEngine?.IsRunning == true)
        {
            return;
        }

        await StartScriptsAsync();
    }

    private static string BuildBuyEventListResponse(IReadOnlyList<BuyableEventSummary> buyEvents)
    {
        var available = buyEvents
            .Where(x => x.State is EventRuntimeState.Stopped or EventRuntimeState.Cooldown)
            .ToList();

        if (available.Count == 0)
        {
            return "[Server] Aktuell sind keine kaufbaren Events konfiguriert.";
        }

        var items = available.Select(x => x.Price > 0 ? $"{x.DisplayName} ({x.Price}$)" : $"{x.DisplayName} (gratis)");
        return "[Server] Kaufbare Events: " + BuildLimitedList(items, maxItems: 8, maxLength: 210) + ". Nutze /buyevent <Name>.";
    }

    private static BuyableEventSummary? FindBuyableEvent(IReadOnlyList<BuyableEventSummary> buyEvents, string requestedEvent)
    {
        var key = (requestedEvent ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(key)) return null;

        return buyEvents.FirstOrDefault(x =>
                   x.DisplayName.Equals(key, StringComparison.OrdinalIgnoreCase) ||
                   x.Name.Equals(key, StringComparison.OrdinalIgnoreCase) ||
                   x.Id.Equals(key, StringComparison.OrdinalIgnoreCase))
               ?? buyEvents.FirstOrDefault(x =>
                   NormalizeBuyEventKey(x.DisplayName).Equals(NormalizeBuyEventKey(key), StringComparison.OrdinalIgnoreCase) ||
                   NormalizeBuyEventKey(x.Name).Equals(NormalizeBuyEventKey(key), StringComparison.OrdinalIgnoreCase) ||
                   NormalizeBuyEventKey(x.Id).Equals(NormalizeBuyEventKey(key), StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeBuyEventKey(string? value) =>
        System.Text.RegularExpressions.Regex.Replace((value ?? string.Empty).Trim(), @"[\s_\-]+", "", System.Text.RegularExpressions.RegexOptions.CultureInvariant).ToLowerInvariant();

    private static string BuildBuyEventUnavailableReason(BuyableEventSummary summary)
    {
        if (summary.State is EventRuntimeState.Initiated or EventRuntimeState.Live or EventRuntimeState.CleanupPending)
        {
            return $"[Server] {summary.DisplayName} ist bereits aktiv.";
        }

        if (summary.State == EventRuntimeState.Cooldown && summary.CooldownUntilUtc > DateTime.UtcNow)
        {
            return $"[Server] {summary.DisplayName} ist noch im Cooldown bis {summary.CooldownUntilUtc.ToLocalTime():HH:mm:ss}.";
        }

        return string.Empty;
    }

    private static string FormatMoney(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return "0";
        }

        return Math.Round(value, 0, MidpointRounding.AwayFromZero).ToString("0", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static bool IsRandomizedEventRuntime(EventRuntime runtime)
        => runtime.Definition.IncludeInRandomizer &&
           (runtime.Definition.Mode.Equals("RandomAnnouncedZone", StringComparison.OrdinalIgnoreCase) ||
            runtime.Definition.Mode.Equals("Random", StringComparison.OrdinalIgnoreCase) ||
            runtime.Definition.Mode.Equals("RandomActivated", StringComparison.OrdinalIgnoreCase));

    private static string BuildRandomEventsText(IReadOnlyList<EventRuntime> runtimes)
    {
        if (runtimes.Count == 0)
        {
            return "Keine randomisierten Events aktiv.";
        }

        return BuildLimitedList(runtimes.Select(x => $"{x.Definition.Name} ({FormatEventStateForChat(x.State)})"));
    }

    private static string BuildLimitedList(IEnumerable<string?> values, int maxItems = 8, int maxLength = 220)
    {
        var clean = values
            .Select(x => (x ?? string.Empty).Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();

        if (clean.Count == 0)
        {
            return "Keine";
        }

        var shown = clean.Take(maxItems).ToList();
        var text = string.Join(", ", shown);
        if (clean.Count > shown.Count)
        {
            text += $" +{clean.Count - shown.Count} weitere";
        }

        return text.Length <= maxLength ? text : text[..Math.Max(0, maxLength - 3)] + "...";
    }

    private static string FormatEventStateForChat(EventRuntimeState state) => state switch
    {
        EventRuntimeState.Initiated => "wartet",
        EventRuntimeState.Live => "live",
        EventRuntimeState.CleanupPending => "cleanup",
        _ => state.ToString()
    };

    private void InsertDefaultChatCommands()
    {
        Settings.ChatAutomationRulesJson = ChatCommandAutomationService.BuildDefaultRulesJson();
        LoadChatCommandRulesFromSettings();
        OnPropertyChanged(nameof(Settings));
        SettingsStore.Save(Settings);
        Log("Chat Commands: Beispiel-Regeln eingefuegt.");
    }

    private void AddChatCommandRule()
    {
        ChatCommandRules.Add(new ChatCommandRuleEditorViewModel
        {
            Enabled = true,
            Trigger = "/command",
            MatchMode = "equals",
            DelaySeconds = 0,
            Command = "#ShowNameplates true",
            ExecuteAsChatPlayer = true,
            Response = string.Empty,
            CooldownSeconds = 300,
            CooldownScope = "player",
            GlobalCooldownSeconds = 10
        });
    }

    private void RemoveChatCommandRule(ChatCommandRuleEditorViewModel? rule)
    {
        if (rule is null) return;
        ChatCommandRules.Remove(rule);
    }

    private void LoadChatCommandRulesFromSettings()
    {
        ChatCommandRules.Clear();

        var json = string.IsNullOrWhiteSpace(Settings.ChatAutomationRulesJson)
            ? ChatCommandAutomationService.BuildDefaultRulesJson()
            : Settings.ChatAutomationRulesJson;

        try
        {
            var rules = JsonSerializer.Deserialize<List<ChatAutomationRule>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            }) ?? new List<ChatAutomationRule>();

            foreach (var rule in rules)
            {
                ChatCommandRules.Add(ChatCommandRuleEditorViewModel.FromRule(rule));
            }
        }
        catch (Exception ex)
        {
            AppLogService.WriteException("ChatCommandEditorLoad", ex);
            Log("Chat Commands: Regeln konnten nicht geladen werden, Beispiel wurde verwendet: " + ex.Message);
            ChatCommandRules.Add(ChatCommandRuleEditorViewModel.FromRule(new ChatAutomationRule
            {
                Enabled = true,
                Trigger = "/help",
                MatchMode = "equals",
                Response = "[Server] Befehle: /help, /discord",
                CooldownSeconds = 30,
                GlobalCooldownSeconds = 3
            }));
        }
    }

    private void SyncChatCommandRulesToSettings()
    {
        var rules = ChatCommandRules
            .Where(x => !string.IsNullOrWhiteSpace(x.Trigger) && (!string.IsNullOrWhiteSpace(x.Command) || !string.IsNullOrWhiteSpace(x.Response)))
            .Select(x => x.ToRule())
            .ToList();

        Settings.ChatAutomationRulesJson = JsonSerializer.Serialize(rules, new JsonSerializerOptions { WriteIndented = true });
    }

    private void InsertDefaultRedeemCodes()
    {
        var example = new List<RedeemCodeRule>
        {
            new()
            {
                Enabled = true,
                Code = "/starter",
                Command = "#SpawnItem Weapon_SKS 1",
                Response = "[Server] {name}, dein Starter-Code wurde eingeloest.",
                ExecuteAsChatPlayer = true,
                MaxUses = 1,
                Uses = 0
            }
        };

        Settings.RedeemCodeRulesJson = JsonSerializer.Serialize(example, new JsonSerializerOptions { WriteIndented = true });
        LoadRedeemCodesFromSettings();
        OnPropertyChanged(nameof(Settings));
        SettingsStore.Save(Settings);
        Log("RedeemCodes: Beispiel-Code eingefuegt.");
    }

    private void AddRedeemCode()
    {
        RedeemCodeRules.Add(new RedeemCodeEditorViewModel
        {
            Enabled = true,
            Code = "/starter",
            Command = "#SpawnItem Weapon_SKS 1",
            Response = "[Server] {name}, dein Code wurde eingeloest.",
            ExecuteAsChatPlayer = true,
            DelaySeconds = 0,
            MaxUses = 1,
            Uses = 0
        });
    }

    private void RemoveRedeemCode(RedeemCodeEditorViewModel? rule)
    {
        if (rule is null) return;
        RedeemCodeRules.Remove(rule);
    }

    private void LoadRedeemCodesFromSettings()
    {
        RedeemCodeRules.Clear();
        if (string.IsNullOrWhiteSpace(Settings.RedeemCodeRulesJson))
        {
            return;
        }

        try
        {
            foreach (var rule in DeserializeRedeemCodes(Settings.RedeemCodeRulesJson))
            {
                RedeemCodeRules.Add(RedeemCodeEditorViewModel.FromRule(rule));
            }
        }
        catch (Exception ex)
        {
            AppLogService.WriteException("RedeemCodeEditorLoad", ex);
            Log("RedeemCodes: Codes konnten nicht geladen werden: " + ex.Message);
        }
    }

    private void SyncRedeemCodesToSettings()
    {
        var rules = RedeemCodeRules
            .Where(x => !string.IsNullOrWhiteSpace(x.Code) && (!string.IsNullOrWhiteSpace(x.Command) || !string.IsNullOrWhiteSpace(x.Response)))
            .Select(x => x.ToRule())
            .ToList();

        Settings.RedeemCodeRulesJson = JsonSerializer.Serialize(rules, new JsonSerializerOptions { WriteIndented = true });
    }

    private IReadOnlyList<RedeemCodeRule> BuildRedeemCodeRules()
    {
        try
        {
            return DeserializeRedeemCodes(Settings.RedeemCodeRulesJson);
        }
        catch (Exception ex)
        {
            AppLogService.WriteException("RedeemCodeRuntimeLoad", ex);
            Log("RedeemCodes: Runtime-Laden fehlgeschlagen: " + ex.Message);
            return Array.Empty<RedeemCodeRule>();
        }
    }

    private async Task MarkRedeemCodeUsedAsync(RedeemCodeRule rule, ChatLogMessage message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var dispatcher = App.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return;
        }

        await dispatcher.InvokeAsync(() =>
        {
            var code = (rule.Code ?? string.Empty).Trim();
            var editor = RedeemCodeRules.FirstOrDefault(x => string.Equals((x.Code ?? string.Empty).Trim(), code, StringComparison.OrdinalIgnoreCase));
            if (editor is not null)
            {
                editor.Uses++;
                SyncRedeemCodesToSettings();
                SettingsStore.Save(Settings);
                Log($"RedeemCodes: '{code}' eingeloest von {message.PlayerName}. Nutzung: {editor.Uses}/{(editor.MaxUses <= 0 ? "unbegrenzt" : editor.MaxUses.ToString(CultureInfo.InvariantCulture))}.");
                return;
            }

            var rules = DeserializeRedeemCodes(Settings.RedeemCodeRulesJson);
            var savedRule = rules.FirstOrDefault(x => string.Equals((x.Code ?? string.Empty).Trim(), code, StringComparison.OrdinalIgnoreCase));
            if (savedRule is null)
            {
                return;
            }

            savedRule.Uses = Math.Max(0, savedRule.Uses) + 1;
            Settings.RedeemCodeRulesJson = JsonSerializer.Serialize(rules, new JsonSerializerOptions { WriteIndented = true });
            SettingsStore.Save(Settings);
        });
    }

    private static List<RedeemCodeRule> DeserializeRedeemCodes(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<RedeemCodeRule>();
        return JsonSerializer.Deserialize<List<RedeemCodeRule>>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        }) ?? new List<RedeemCodeRule>();
    }


    private async Task StartJoinCommandsAsync(bool persistAutoStart = true)
    {
        EnsureLocalLogDirectories();
        SyncJoinCommandRulesToSettings();

        if (persistAutoStart)
        {
            Settings.AutoStartJoinCommands = true;
            SettingsStore.Save(Settings);
        }

        if (string.IsNullOrWhiteSpace(Settings.JoinAutomationRulesJson))
        {
            Settings.JoinAutomationRulesJson = JoinCommandAutomationService.BuildDefaultRulesJson();
            SettingsStore.Save(Settings);
            Log("Join Commands: Beispiel-Regel wurde eingefuegt. Bitte pruefen und speichern.");
        }

        _joinCommands?.Stop();
        _joinCommands = new JoinCommandAutomationService(
            new SftpLogService(Settings),
            async command =>
            {
                if (_rcon is null || !_rcon.IsConnected) await ConnectRconAsync();
                if (_rcon is null) return string.Empty;
                var response = await _rcon.SendCommandAsync(command);
                if (!string.IsNullOrWhiteSpace(response)) Log("RCON Antwort: " + TrimForLog(response));
                return response;
            },
            Log);

        _joinCommands.Start(Settings);
        JoinCommandsRunning = true;
        Log("Join Commands AutoStart: " + Settings.AutoStartJoinCommands);
    }

    private void StopJoinCommands(bool persistAutoStart = true)
    {
        if (persistAutoStart)
        {
            Settings.AutoStartJoinCommands = false;
            SettingsStore.Save(Settings);
        }
        _joinCommands?.Stop();
        _joinCommands = null;
        JoinCommandsRunning = false;
    }

    private async Task ScanJoinCommandsOnceAsync()
    {
        EnsureLocalLogDirectories();
        SyncJoinCommandRulesToSettings();

        var service = _joinCommands ?? new JoinCommandAutomationService(
            new SftpLogService(Settings),
            async command =>
            {
                if (_rcon is null || !_rcon.IsConnected) await ConnectRconAsync();
                if (_rcon is null) return string.Empty;
                var response = await _rcon.SendCommandAsync(command);
                if (!string.IsNullOrWhiteSpace(response)) Log("RCON Antwort: " + TrimForLog(response));
                return response;
            },
            Log);

        await service.ScanOnceAsync(Settings);
    }

    private async Task ExecuteJoinCommandsForOnlinePlayersAsync()
    {
        EnsureLocalLogDirectories();
        SyncJoinCommandRulesToSettings();

        var service = _joinCommands ?? new JoinCommandAutomationService(
            new SftpLogService(Settings),
            async command =>
            {
                if (_rcon is null || !_rcon.IsConnected) await ConnectRconAsync();
                if (_rcon is null) return string.Empty;
                var response = await _rcon.SendCommandAsync(command);
                if (!string.IsNullOrWhiteSpace(response)) Log("RCON Antwort: " + TrimForLog(response));
                return response;
            },
            Log);

        await service.ExecuteForOnlinePlayersAsync(Settings);
    }

    private void InsertDefaultJoinCommands()
    {
        Settings.JoinAutomationRulesJson = JoinCommandAutomationService.BuildDefaultRulesJson();
        LoadJoinCommandRulesFromSettings();
        LoadWeeklyTaskEditorsFromSettings();
        OnPropertyChanged(nameof(Settings));
        SettingsStore.Save(Settings);
        Log("Join Commands: Beispiel-Regel eingefuegt.");
    }

    private void AddJoinCommandRule()
    {
        JoinCommandRules.Add(new JoinCommandRuleEditorViewModel
        {
            Enabled = true,
            DelaySeconds = 300,
            Command = "#ShowNameplates true",
            ExecuteAsJoinedPlayer = true,
            OnlyOncePerSession = true,
            CooldownSeconds = 300
        });
    }

    private void RemoveJoinCommandRule(JoinCommandRuleEditorViewModel? rule)
    {
        if (rule is null) return;
        JoinCommandRules.Remove(rule);
    }

    private void LoadJoinCommandRulesFromSettings()
    {
        JoinCommandRules.Clear();

        var json = string.IsNullOrWhiteSpace(Settings.JoinAutomationRulesJson)
            ? JoinCommandAutomationService.BuildDefaultRulesJson()
            : Settings.JoinAutomationRulesJson;

        try
        {
            var rules = JsonSerializer.Deserialize<List<JoinAutomationRule>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            }) ?? new List<JoinAutomationRule>();

            foreach (var rule in rules)
            {
                JoinCommandRules.Add(JoinCommandRuleEditorViewModel.FromRule(rule));
            }
        }
        catch (Exception ex)
        {
            AppLogService.WriteException("JoinCommandEditorLoad", ex);
            Log("Join Commands: Regeln konnten nicht geladen werden, Beispiel wurde verwendet: " + ex.Message);
            JoinCommandRules.Add(JoinCommandRuleEditorViewModel.FromRule(new JoinAutomationRule
            {
                Enabled = true,
                DelaySeconds = 300,
                Command = "#ShowNameplates true",
                ExecuteAsJoinedPlayer = true,
                OnlyOncePerSession = true,
                CooldownSeconds = 300
            }));
        }
    }

    private void SyncJoinCommandRulesToSettings()
    {
        var rules = JoinCommandRules
            .Where(x => !string.IsNullOrWhiteSpace(x.Command))
            .Select(x => x.ToRule())
            .ToList();

        Settings.JoinAutomationRulesJson = JsonSerializer.Serialize(rules, new JsonSerializerOptions { WriteIndented = true });
    }

    private async Task StartKillFeedAsync(bool persistAutoStart = true)
    {
        EnsureLocalLogDirectories();

        if (persistAutoStart)
        {
            Settings.AutoStartKillFeed = true;
            SettingsStore.Save(Settings);
        }

        _killFeed?.Stop();
        _killFeed = new KillFeedAutomationService(
            new SftpLogService(Settings),
            async command =>
            {
                if (_rcon is null || !_rcon.IsConnected) await ConnectRconAsync();
                if (_rcon is null) return string.Empty;
                var response = await _rcon.SendCommandAsync(command);
                if (!string.IsNullOrWhiteSpace(response)) Log("RCON Antwort: " + TrimForLog(response));
                return response;
            },
            Log);

        _killFeed.Start(Settings);
        KillFeedRunning = true;
        Log("Killfeed AutoStart: " + Settings.AutoStartKillFeed);
    }

    private void StopKillFeed(bool persistAutoStart = true)
    {
        if (persistAutoStart)
        {
            Settings.AutoStartKillFeed = false;
            SettingsStore.Save(Settings);
        }
        _killFeed?.Stop();
        _killFeed = null;
        KillFeedRunning = false;
    }

    private async Task ScanKillFeedOnceAsync()
    {
        EnsureLocalLogDirectories();

        var service = _killFeed ?? new KillFeedAutomationService(
            new SftpLogService(Settings),
            async command =>
            {
                if (_rcon is null || !_rcon.IsConnected) await ConnectRconAsync();
                if (_rcon is null) return string.Empty;
                var response = await _rcon.SendCommandAsync(command);
                if (!string.IsNullOrWhiteSpace(response)) Log("RCON Antwort: " + TrimForLog(response));
                return response;
            },
            Log);

        await service.ScanOnceAsync(Settings);
    }


    private async Task StartWeeklyTasksAsync(bool persistAutoStart = true)
    {
        EnsureLocalLogDirectories();
        if (persistAutoStart)
        {
            Settings.AutoStartWeeklyTasks = true;
            SettingsStore.Save(Settings);
        }

        if (_discord is null || !_discord.IsReady)
        {
            Log("Weekly Tasks: Discord ist noch nicht bereit. Scan startet trotzdem; Discord-Posts werden gesendet, sobald der Bot bereit ist.");
        }

        _weeklyTasks?.Stop();
        _weeklyTasks = new WeeklyCommunityTaskService(new SftpLogService(Settings), Log);
        _weeklyTasks.ScanStateChanged += ApplyWeeklyTaskScanState;
        _weeklyTasks.Start(
            Settings,
            async token =>
            {
                var players = await FetchPlayersAsync(token);
                return players.Count;
            },
            async progresses =>
            {
                var progressList = progresses.ToList();
                App.Current?.Dispatcher.Invoke(() =>
                {
                    SetWeeklyTaskProgresses(progressList);
                });

                await ProcessWeeklyRewardsAsync(progressList);

                if (_discord is null || !_discord.IsReady) return;
                var channelId = Settings.WeeklyTaskDiscordChannelId != 0
                    ? Settings.WeeklyTaskDiscordChannelId
                    : Settings.DiscordServerStatusChannelId;

                foreach (var progress in progressList)
                {
                    await _discord.SendOrUpdateWeeklyTaskAsync(channelId, progress);
                    Log("Weekly/Daily Task Discord aktualisiert: " + FormatWeeklyTaskStatus(progress));
                }
            });

        WeeklyTasksRunning = true;
        Log("Weekly Tasks AutoStart: " + Settings.AutoStartWeeklyTasks);
    }

    private void ApplyWeeklyTaskScanState(bool isScanning, DateTime? nextScanUtc)
    {
        void Apply()
        {
            WeeklyTaskScanInProgress = isScanning;
            if (isScanning)
            {
                WeeklyTaskNextScanText = "Scan laeuft gerade ...";
            }
            else if (nextScanUtc.HasValue)
            {
                var local = nextScanUtc.Value.ToLocalTime();
                var remaining = nextScanUtc.Value - DateTime.UtcNow;
                var minutes = Math.Max(0, (int)Math.Ceiling(remaining.TotalMinutes));
                WeeklyTaskNextScanText = $"Naechster Scan: {local:dd.MM.yyyy HH:mm:ss} (in ca. {minutes} Min.)";
            }
            else
            {
                WeeklyTaskNextScanText = WeeklyTasksRunning ? "Naechster Scan wird geplant ..." : "Naechster automatischer Scan: gestoppt";
            }
        }

        var dispatcher = App.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess()) dispatcher.Invoke(Apply); else Apply();
    }
    private void StopWeeklyTasks(bool persistAutoStart = true)
    {
        if (persistAutoStart)
        {
            Settings.AutoStartWeeklyTasks = false;
            SettingsStore.Save(Settings);
        }
        _weeklyTasks?.Stop();
        _weeklyTasks = null;
        WeeklyTasksRunning = false;
        Log("Weekly Tasks deaktiviert.");
    }

    private async Task ScanWeeklyTasksOnceAsync()
    {
        EnsureLocalLogDirectories();
        var service = _weeklyTasks ?? new WeeklyCommunityTaskService(new SftpLogService(Settings), Log);
        if (_weeklyTasks is null) service.ScanStateChanged += ApplyWeeklyTaskScanState;
        var progresses = await service.ScanAllOnceAsync(Settings);
        await ProcessWeeklyRewardsAsync(progresses);
        if (progresses.Count == 0) return;

        SetWeeklyTaskProgresses(progresses);

        if (_discord is null || !_discord.IsReady) await StartDiscordAsync();
        if (_discord is not null && _discord.IsReady)
        {
            var channelId = Settings.WeeklyTaskDiscordChannelId != 0
                ? Settings.WeeklyTaskDiscordChannelId
                : Settings.DiscordServerStatusChannelId;
            foreach (var progress in progresses)
            {
                await _discord.SendOrUpdateWeeklyTaskAsync(channelId, progress);
            }
        }

        Log("Weekly/Daily Tasks manuell aktualisiert: " + WeeklyTaskStatus);
    }

    private async Task ResetWeeklyTaskBaselineAsync()
    {
        EnsureLocalLogDirectories();
        var service = _weeklyTasks ?? new WeeklyCommunityTaskService(new SftpLogService(Settings), Log);
        var progresses = await service.ResetAllBaselinesAsync(Settings);
        if (progresses.Count == 0) return;

        SetWeeklyTaskProgresses(progresses);
        await ProcessWeeklyRewardsAsync(progresses);

        if (_discord is null || !_discord.IsReady) await StartDiscordAsync();
        if (_discord is not null && _discord.IsReady)
        {
            var channelId = Settings.WeeklyTaskDiscordChannelId != 0
                ? Settings.WeeklyTaskDiscordChannelId
                : Settings.DiscordServerStatusChannelId;
            foreach (var progress in progresses)
            {
                await _discord.SendOrUpdateWeeklyTaskAsync(channelId, progress);
            }
        }

        Log("Weekly/Daily Task Startwerte neu gesetzt und Discord aktualisiert: " + WeeklyTaskStatus);
    }

    private void InsertDefaultWeeklyTask()
    {
        WeeklyTaskDefinitionStore.Save(JsonSerializer.Deserialize<List<WeeklyCommunityTaskDefinition>>(WeeklyCommunityTaskService.BuildDefaultTaskJson(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new List<WeeklyCommunityTaskDefinition>());
        Settings.WeeklyTaskJson = "";
        SettingsStore.Save(Settings);
        LoadWeeklyTaskEditorsFromSettings();
        OnPropertyChanged(nameof(Settings));
        Log("Weekly Task Beispiel eingefuegt.");
    }


    private void InsertSelectedWeeklyTask()
    {
        var target = SelectedWeeklyTaskStatTarget ?? WeeklyCommunityTaskService.AvailableStatTargets.First(x => x.ColumnName == "puppets_killed");
        var taskType = target.TableName.Equals("fishing_stats", StringComparison.OrdinalIgnoreCase) ? "Daily" : "Weekly";
        var idPrefix = taskType.Equals("Daily", StringComparison.OrdinalIgnoreCase) ? "daily" : "weekly";
        var task = new WeeklyCommunityTaskDefinition
        {
            Id = $"{idPrefix}-{target.ColumnName.Replace('_', '-')}",
            Type = taskType,
            Title = target.DisplayName,
            Description = $"Community-Ziel: {target.DisplayName}.",
            StatTable = target.TableName,
            StatColumn = target.ColumnName,
            Target = target.TableName.Equals("fishing_stats", StringComparison.OrdinalIgnoreCase) ? 100 : 1000,
            DurationHours = taskType.Equals("Daily", StringComparison.OrdinalIgnoreCase) ? 24 : 168,
            MinimumParticipationPercent = 2.0,
            RewardText = "Reward wird manuell freigeschaltet.",
            CompletedText = "Krass! Ihr habt es geschafft!"
        };

        var editor = WeeklyTaskEditorViewModel.FromDefinition(task, WeeklyTaskStatTargets);
        WeeklyTaskEditors.Add(editor);
        SelectedWeeklyTaskEditor = editor;
        Log("Weekly/Daily Task Beispiel fuer Ziel in den Planer eingefuegt: " + target.Key + ". Zum Speichern 'Planer in JSON uebernehmen' klicken.");
    }

    private void LoadWeeklyTaskEditorsFromSettings()
    {
        WeeklyTaskEditors.Clear();
        var loadedDefinitions = Settings.GetWeeklyTaskDefinitions();
        foreach (var definition in loadedDefinitions)
        {
            WeeklyTaskEditors.Add(WeeklyTaskEditorViewModel.FromDefinition(definition, WeeklyTaskStatTargets));
        }

        SelectedWeeklyTaskEditor = WeeklyTaskEditors.FirstOrDefault();
        Log("Challenge-Planer geladen: " + WeeklyTaskEditors.Count + " Eintraege.");
    }

    private void SyncWeeklyTaskEditorsToSettings()
    {
        var definitions = WeeklyTaskEditors.Select(x => x.ToDefinition()).ToList();
        WeeklyTaskDefinitionStore.Save(definitions);
        Settings.WeeklyTaskJson = "";
        OnPropertyChanged(nameof(Settings));
    }

    private void AddWeeklyTaskEditor()
    {
        var target = SelectedWeeklyTaskStatTarget ?? WeeklyTaskStatTargets.First(x => x.ColumnName == "puppets_killed");
        var definition = new WeeklyCommunityTaskDefinition
        {
            Enabled = true,
            Id = "daily-" + target.ColumnName.Replace('_', '-'),
            Type = "Daily",
            Title = target.DisplayName,
            Description = "Community-Ziel: " + target.DisplayName + ".",
            StatTable = target.TableName,
            StatColumn = target.ColumnName,
            Target = target.TableName.Equals("fishing_stats", StringComparison.OrdinalIgnoreCase) ? 100 : 1000,
            DurationHours = 24,
            MinimumParticipationPercent = 2.0,
            RewardText = "Reward wird manuell freigeschaltet.",
            CompletedText = "Krass! Ihr habt es geschafft!"
        };
        var editor = WeeklyTaskEditorViewModel.FromDefinition(definition, WeeklyTaskStatTargets);
        WeeklyTaskEditors.Add(editor);
        SelectedWeeklyTaskEditor = editor;
        Log("Neue Challenge im Planer angelegt. Zum Uebernehmen 'Planer in JSON uebernehmen' klicken.");
    }

    private void DuplicateWeeklyTaskEditor(WeeklyTaskEditorViewModel? source = null)
    {
        source ??= SelectedWeeklyTaskEditor;
        if (source is null) return;
        var definition = source.ToDefinition();
        definition.Id = definition.Id + "-copy";
        definition.Title = definition.Title + " Kopie";
        var editor = WeeklyTaskEditorViewModel.FromDefinition(definition, WeeklyTaskStatTargets);
        WeeklyTaskEditors.Add(editor);
        SelectedWeeklyTaskEditor = editor;
        Log("Challenge dupliziert. Bitte ID/Startzeit pruefen.");
    }

    private void DeleteWeeklyTaskEditor(WeeklyTaskEditorViewModel? source = null)
    {
        source ??= SelectedWeeklyTaskEditor;
        if (source is null) return;
        var removed = source;
        WeeklyTaskEditors.Remove(removed);
        SelectedWeeklyTaskEditor = WeeklyTaskEditors.FirstOrDefault();
        Log("Challenge aus Planer entfernt: " + removed.Id);
    }

    private void AddWeeklyRewardItem(WeeklyTaskEditorViewModel? task)
    {
        task ??= SelectedWeeklyTaskEditor;
        task?.RewardItems.Add(new WeeklyRewardItemEditorViewModel());
    }

    private void RemoveWeeklyRewardItem(WeeklyRewardItemEditorViewModel? item)
    {
        if (item is null) return;
        foreach (var task in WeeklyTaskEditors)
        {
            if (task.RewardItems.Remove(item)) return;
        }
    }

    private void ApplyWeeklyTaskEditorToJson()
    {
        SyncWeeklyTaskEditorsToSettings();
        SettingsStore.Save(Settings);
        Log("Challenge-Planer in Data/weekly_tasks.json gespeichert. Eintraege: " + WeeklyTaskEditors.Count);
    }

    private void SetWeeklyTaskProgresses(IReadOnlyList<WeeklyCommunityTaskProgress> progresses)
    {
        _lastWeeklyTaskProgresses = progresses.ToList();
        WeeklyTaskProgress = _lastWeeklyTaskProgresses.FirstOrDefault();
        WeeklyTaskStatus = FormatWeeklyTaskStatus(_lastWeeklyTaskProgresses);
    }

    private static string FormatWeeklyTaskStatus(IReadOnlyList<WeeklyCommunityTaskProgress> progresses)
    {
        if (progresses.Count == 0) return "No active Weekly/Daily Tasks.";
        return string.Join(" | ", progresses.Select(FormatWeeklyTaskStatus));
    }

    private static string FormatWeeklyTaskStatus(WeeklyCommunityTaskProgress progress)
    {
        var title = string.IsNullOrWhiteSpace(progress.Definition.Title) ? progress.Definition.Id : progress.Definition.Title;
        var kind = WeeklyCommunityTaskService.GetTaskKind(progress.Definition);
        return string.Equals(progress.Definition.GoalScope, "PerPlayer", StringComparison.OrdinalIgnoreCase)
            ? $"{kind} {title}: {progress.CompletedPlayerCount} Spieler erreicht, bester Stand {progress.Progress:N0}/{progress.Definition.Target:N0}"
            : $"{kind} {title}: {progress.Progress:N0}/{progress.Definition.Target:N0} ({progress.Percent:0.0}%)" + (progress.IsCompleted ? " - erreicht" : "");
    }


    private async Task StartAutoMessagesAsync(bool persistAutoStart = true)
    {
        SyncAutoMessageEditorsToSettings();
        if (persistAutoStart)
        {
            Settings.AutoStartAutoMessages = true;
            SettingsStore.Save(Settings);
        }

        _autoMessages?.Stop();
        _autoMessages = new AutoMessageService(Log);
        _autoMessages.Start(
            Settings,
            async token =>
            {
                var players = await FetchPlayersAsync(token);
                return players.Count;
            },
            BuildAutoMessageChallengeTextAsync,
            async (messageType, text, token) =>
            {
                if (_rcon is null || !_rcon.IsConnected) await ConnectRconAsync();
                if (_rcon is null) return;
                var command = CommandRegistry.Broadcast(messageType, text);
                await _rcon.SendCommandAsync(command, token);
            });

        AutoMessagesRunning = true;
        AutoMessageStatus = Tf("AutoMessagesRunningStatus", Math.Max(1, Settings.AutoMessagesIntervalMinutes));
        Log("Auto Messages gestartet.");
    }

    private void StopAutoMessages(bool persistAutoStart = true)
    {
        if (persistAutoStart)
        {
            Settings.AutoStartAutoMessages = false;
            SettingsStore.Save(Settings);
        }
        _autoMessages?.Stop();
        _autoMessages = null;
        AutoMessagesRunning = false;
        AutoMessageStatus = T("AutoMessagesStopped");
        Log("Auto Messages deaktiviert.");
    }

    private async Task SendAutoMessageNowAsync()
    {
        SyncAutoMessageEditorsToSettings();
        var service = _autoMessages ?? new AutoMessageService(Log);
        await service.TickAsync(
            Settings,
            async token =>
            {
                var players = await FetchPlayersAsync(token);
                return players.Count;
            },
            BuildAutoMessageChallengeTextAsync,
            async (messageType, text, token) =>
            {
                if (_rcon is null || !_rcon.IsConnected) await ConnectRconAsync();
                if (_rcon is null) return;
                var command = CommandRegistry.Broadcast(messageType, text);
                await _rcon.SendCommandAsync(command, token);
            });

        AutoMessageStatus = T("AutoMessageManualSent");
    }

    private void InsertDefaultAutoMessages()
    {
        Settings.AutoMessagesFlowJson = AutoMessageFlow.BuildDefaultJson();
        LoadAutoMessageEditorsFromSettings();
        SettingsStore.Save(Settings);
        OnPropertyChanged(nameof(Settings));
        Log("Auto Messages Beispiel-Flow eingefuegt.");
    }

    private void AddAutoMessage()
    {
        AutoMessageEditors.Add(new AutoMessageEditorViewModel
        {
            Name = "Neue Nachricht",
            Enabled = true,
            Mode = "Queue",
            Type = "Text",
            MessageType = Settings.AutoMessagesBroadcastType,
            IntervalMinutes = Math.Max(1, Settings.AutoMessagesIntervalMinutes),
            Text = "Neue Auto Message"
        });
        SyncAutoMessageEditorsToSettings();
    }

    private void RemoveAutoMessage(AutoMessageEditorViewModel? message)
    {
        if (message is null) return;
        AutoMessageEditors.Remove(message);
        SyncAutoMessageEditorsToSettings();
    }

    private void LoadAutoMessageEditorsFromSettings()
    {
        AutoMessageEditors.Clear();
        foreach (var step in AutoMessageFlow.Parse(Settings.AutoMessagesFlowJson, Settings.AutoMessagesBroadcastType, includeDisabled: true))
        {
            AutoMessageEditors.Add(AutoMessageEditorViewModel.FromStep(step, Settings.AutoMessagesBroadcastType, Settings.AutoMessagesIntervalMinutes));
        }
    }

    private void SyncAutoMessageEditorsToSettings()
    {
        var steps = AutoMessageEditors.Select(x => x.ToStep()).ToList();
        Settings.AutoMessagesFlowJson = JsonSerializer.Serialize(steps, new JsonSerializerOptions { WriteIndented = true });
    }

    private void ResetAutoMessageFlow()
    {
        _autoMessages?.ResetFlow();
        AutoMessageStatus = T("AutoMessagesFlowReset");
    }

    private async Task<string> BuildAutoMessageChallengeTextAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var configuredTasks = Settings.GetWeeklyTaskDefinitions()
                .Where(x => x.Enabled)
                .ToList();

            if (configuredTasks.Count == 0)
            {
                Log("Auto Messages: Challenge-Schritt gefunden, aber keine aktive Weekly/Daily Aufgabe im JSON geladen.");
                return BuildAutoMessageChallengeTextFromCache() ?? Settings.AutoMessagesNoChallengeText;
            }

            var service = _weeklyTasks ?? new WeeklyCommunityTaskService(new SftpLogService(Settings), Log);
            var progresses = await service.ScanAllOnceAsync(Settings, cancellationToken);
            if (progresses.Count == 0)
            {
                Log($"Auto Messages: Challenge-Scan lieferte 0 Ergebnisse, konfigurierte Aufgaben={configuredTasks.Count}. Nutze letzten bekannten Stand, falls vorhanden.");
                return BuildAutoMessageChallengeTextFromCache() ?? Settings.AutoMessagesNoChallengeText;
            }

            SetWeeklyTaskProgresses(progresses);
            return BuildAutoMessageChallengeText(progresses) ?? Settings.AutoMessagesNoChallengeText;
        }
        catch (Exception ex)
        {
            Log("Auto Messages: Challenge-Text konnte nicht gebaut werden: " + ex.Message);
            AppLogService.WriteException("AutoMessages.BuildChallengeText", ex);
            return BuildAutoMessageChallengeTextFromCache() ?? Settings.AutoMessagesNoChallengeText;
        }
    }

    private string? BuildAutoMessageChallengeTextFromCache()
    {
        return _lastWeeklyTaskProgresses.Count == 0
            ? null
            : BuildAutoMessageChallengeText(_lastWeeklyTaskProgresses);
    }

    private static string? BuildAutoMessageChallengeText(IReadOnlyList<WeeklyCommunityTaskProgress> progresses)
    {
        var lines = progresses
            .Where(x => x.Definition.Enabled)
            .Select(x =>
            {
                var title = string.IsNullOrWhiteSpace(x.Definition.Title) ? x.Definition.Id : x.Definition.Title;
                var kind = WeeklyCommunityTaskService.GetTaskKind(x.Definition);
                var done = x.IsCompleted ? " - geschafft" : string.Empty;
                return $"{kind}: {title} - {x.Progress:N0}/{x.Definition.Target:N0}{done}";
            })
            .ToList();

        return lines.Count == 0 ? null : string.Join(" | ", lines);
    }

    private Task StartScriptsAsync()
    {
        if (_eventEngine?.IsRunning == true)
        {
            StartPlayerStatusLoop();
            _eventEngine.Start(Settings.ScriptPollSeconds);
            ScriptEngineRunning = true;
            RefreshScriptRuntimeStatuses();
            return Task.CompletedTask;
        }

        _rcon ??= new SourceRconClient(Settings.Host, Settings.Port, Settings.Password);
        StartPlayerStatusLoop();

        var definitions = EventDefinitionStore.Load();
        _eventEngine?.Dispose();
        _eventEngine = new EventEngine(_rcon, definitions, Log, OnScriptEngineStateChanged, Settings, GlobalLootPacks.Select(pack => pack.ToPack()));
        _eventEngine.Start(Settings.ScriptPollSeconds);
        ScriptEngineRunning = true;
        RefreshScriptRuntimeStatuses();
        Log($"Script Engine mit {definitions.Count} Scripts gestartet. RCON wird beim ersten Scan automatisch verbunden.");
        return Task.CompletedTask;
    }

    private void StopScripts()
    {
        _eventEngine?.Stop();
        _eventEngine?.Dispose();
        _eventEngine = null;
        ScriptEngineRunning = false;
        RefreshScriptRuntimeStatuses();
        Log("Script Engine gestoppt.");
    }

    private async Task ScanScriptsOnceAsync()
    {
        if (_rcon is null || !_rcon.IsConnected) await ConnectRconAsync();
        if (_rcon is null) return;

        if (_eventEngine is null)
        {
            var definitions = EventDefinitionStore.Load();
            _eventEngine = new EventEngine(_rcon, definitions, Log, OnScriptEngineStateChanged, Settings, GlobalLootPacks.Select(pack => pack.ToPack()));
            Log($"Script Engine geladen: {definitions.Count} Scripts.");
        }

        await _eventEngine.ManualScanAsync();
        RefreshScriptRuntimeStatuses();
        Log("Script Scan einmal ausgefuehrt.");
    }


    private void OnScriptEngineStateChanged()
    {
        App.Current?.Dispatcher.Invoke(() =>
        {
            ScriptEngineRunning = _eventEngine?.IsRunning == true;
            RefreshScriptRuntimeStatuses();
        });
    }

    private void RefreshScriptRuntimeStatuses()
    {
        App.Current?.Dispatcher.Invoke(() =>
        {
            ScriptRuntimeStatuses.Clear();
            ScriptZoneMapItems.Clear();

            if (_eventEngine is null)
            {
                ScriptRuntimeSummary = T("ScriptEngineNotStarted");
                return;
            }

            var runtimes = _eventEngine.Events
                .Where(x => x.Definition.Enabled)
                .OrderBy(x => x.Definition.Mode)
                .ThenBy(x => x.Definition.Name)
                .ToList();

            foreach (var runtime in runtimes)
            {
                ScriptRuntimeStatuses.Add(new ScriptRuntimeStatusViewModel(runtime));
                ScriptZoneMapItems.Add(new ScriptZoneMapItemViewModel(runtime));
            }

            var initiated = runtimes.Count(x => x.State == EventRuntimeState.Initiated);
            var live = runtimes.Count(x => x.State == EventRuntimeState.Live);
            var cleanup = runtimes.Count(x => x.State == EventRuntimeState.CleanupPending);
            var cooldown = runtimes.Count(x => x.State == EventRuntimeState.Cooldown);
            ScriptRuntimeSummary = Tf("ScriptRuntimeSummaryFormat", initiated, live, cleanup, cooldown);
        });
    }

    private async Task StartChatLogForwarderAsync()
    {
        Settings.AutoStartDiscordChatLogs = true;
        SettingsStore.Save(Settings);
        _chatForwarderRequested = true;

        if (_discord is null || !_discord.IsReady)
        {
            Log("Discord Chatlog Forwarder: Discord ist noch nicht bereit, starte Bot...");
            await StartDiscordAsync();
        }

        if (_discord is not null && _discord.IsReady)
        {
            StartChatForwarder();
        }
        else
        {
            Log("Discord Chatlog Forwarder: wartet auf Discord Ready. Sobald der Bot online ist, startet der Forwarder automatisch.");
        }
    }

    private void StopChatLogForwarder(bool persistAutoStart = true)
    {
        _chatForwarderRequested = false;
        if (persistAutoStart)
        {
            Settings.AutoStartDiscordChatLogs = false;
            SettingsStore.Save(Settings);
        }
        _chatForwarder?.Stop();
        ChatLogForwarderRunning = false;
        Log("Discord Chatlog Forwarder deaktiviert.");
    }

    private void StartChatForwarder()
    {
        EnsureLocalLogDirectories();
        if (!Settings.DiscordChatLogEmbedsEnabled && !Settings.DiscordVehicleLogEmbedsEnabled)
        {
            Log("Discord Chatlog/Vehicle-Log Forwarder: Chatlog- und Vehicle-Embeds sind deaktiviert.");
            return;
        }
        if (Settings.DiscordChatLogChannelId == 0)
        {
            Log("Discord Chatlog Forwarder: Chatlog Channel-ID fehlt.");
            return;
        }

        var forwarder = GetChatForwarder();
        forwarder.Start(Settings, Settings.DiscordChatLogChannelId);
        ChatLogForwarderRunning = forwarder.IsRunning;
    }

    private ChatLogDiscordForwarder GetChatForwarder()
    {
        if (_discord is null) throw new InvalidOperationException("Discord Bot ist nicht verbunden.");
        _chatForwarder ??= new ChatLogDiscordForwarder(new SftpLogService(Settings), _discord, Log);
        return _chatForwarder;
    }

    private void LoadStructuredScriptFromJson()
    {
        try
        {
            var definition = JsonSerializer.Deserialize<EventDefinition>(ScriptJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (definition is not null)
            {
                ImportLegacyLootPacksIntoGlobalStore(definition.LootPacks ?? new List<LootPack>());
            }

            ScriptEditorModel = definition is null ? null : ScriptStructuredEditorViewModel.FromDefinition(definition);
        }
        catch
        {
            ScriptEditorModel = null;
        }
    }

    private void SyncStructuredScriptToJson()
    {
        if (ScriptEditorModel is null) return;
        var definition = ScriptEditorModel.ToDefinition();
        ScriptJson = JsonSerializer.Serialize(definition, new JsonSerializerOptions { WriteIndented = true });
    }

    public void MarkScriptDirty()
    {
        if (SelectedScript is not null)
        {
            ScriptHasUnsavedChanges = true;
        }

        ScriptEditorModel?.RefreshLocalVariablePlaceholders();
    }

    public bool ConfirmScriptChangeAllowed()
    {
        if (!ScriptHasUnsavedChanges)
        {
            return true;
        }

        var result = ShowUnsavedScriptDialog();

        if (result == MessageBoxResult.Cancel)
        {
            return false;
        }

        if (result == MessageBoxResult.Yes)
        {
            return TrySaveScript();
        }

        ScriptHasUnsavedChanges = false;
        return true;
    }

    private MessageBoxResult ShowUnsavedScriptDialog()
    {
        var result = MessageBoxResult.Cancel;
        var window = new Window
        {
            Title = T("UnsavedScriptDialogTitle"),
            Width = 460,
            Height = 210,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Owner = App.Current?.MainWindow,
            Background = App.Current?.TryFindResource("PanelBrush") as System.Windows.Media.Brush,
            Foreground = App.Current?.TryFindResource("AppTextBrush") as System.Windows.Media.Brush,
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI")
        };

        var root = new DockPanel { Margin = new Thickness(18) };
        var text = new TextBlock
        {
            Text = T("UnsavedScriptDialogText"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 18),
            FontSize = 15
        };
        DockPanel.SetDock(text, Dock.Top);
        root.Children.Add(text);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        Button BuildButton(string label, MessageBoxResult value)
        {
            var button = new Button
            {
                Content = label,
                MinWidth = 112,
                Margin = new Thickness(8, 0, 0, 0),
                Padding = new Thickness(10, 7, 10, 7)
            };
            button.Click += (_, _) =>
            {
                result = value;
                window.DialogResult = true;
                window.Close();
            };
            return button;
        }

        buttons.Children.Add(BuildButton(Texts["Save"], MessageBoxResult.Yes));
        buttons.Children.Add(BuildButton(Texts["DoNotSave"], MessageBoxResult.No));
        buttons.Children.Add(BuildButton(Texts["Cancel"], MessageBoxResult.Cancel));
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        window.Content = root;
        window.ShowDialog();
        return result;
    }

    private void AddScriptCommand(ScriptBlockEditorViewModel? block)
    {
        if (block is null) return;
        block.Commands.Add(new ScriptCommandEditorViewModel { Name = T("NewCommand"), DelayMs = 50 });
        MarkScriptDirty();
        SyncStructuredScriptToJson();
    }

    private void RemoveScriptCommand(ScriptCommandEditorViewModel? command)
    {
        if (ScriptEditorModel is null || command is null) return;
        foreach (var block in ScriptEditorModel.Blocks)
        {
            if (block.Commands.Remove(command)) break;
        }
        MarkScriptDirty();
        SyncStructuredScriptToJson();
    }

    private void AddSpawnBlock()
    {
        if (ScriptEditorModel is null) return;
        var defaultLocation = ScriptEditorModel.NpcLocationPlaceholders
            .FirstOrDefault(x => !string.Equals(x, "{triggerZone}", StringComparison.OrdinalIgnoreCase))
            ?? ScriptEditorModel.NpcLocationPlaceholders.FirstOrDefault()
            ?? "{triggerZone}";
        var block = new SpawnBlockEditorViewModel
        {
            Name = "Neuer Spawn",
            Type = "ArmedNPC",
            Asset = "BP_Guard_Lvl_1",
            Quantity = 1,
            Location = defaultLocation,
            DelayMs = 250
        };
        ScriptEditorModel.SpawnBlocks.Add(block);
        ScriptEditorModel.RebuildFlow();
        ScriptEditorModel.SelectFlowTarget(block);
        MarkScriptDirty();
        SyncStructuredScriptToJson();
    }

    private void RemoveSpawnBlock(SpawnBlockEditorViewModel? block)
    {
        if (ScriptEditorModel is null || block is null) return;
        ScriptEditorModel.SpawnBlocks.Remove(block);
        ScriptEditorModel.RebuildFlow();
        MarkScriptDirty();
        SyncStructuredScriptToJson();
    }

    private void AddLootLocationVariable()
    {
        if (ScriptEditorModel is null) return;
        ScriptEditorModel.LootSpawnLocations.Add(new ScriptLocationVariableEditorViewModel("loot", "loot_" + (ScriptEditorModel.LootSpawnLocations.Count + 1), "[{X=0 Y=0 Z=0}]"));
        ScriptEditorModel.RefreshLocalVariablePlaceholders();
        MarkScriptDirty();
        SyncStructuredScriptToJson();
    }

    private void AddNpcLocationVariable()
    {
        if (ScriptEditorModel is null) return;
        var variable = new ScriptLocationVariableEditorViewModel("npc", "npc_" + (ScriptEditorModel.NpcSpawnLocations.Count + 1), "[{X=0 Y=0 Z=0}]");
        ScriptEditorModel.NpcSpawnLocations.Add(variable);
        ScriptEditorModel.RefreshLocalVariablePlaceholders();
        foreach (var block in ScriptEditorModel.SpawnBlocks.Where(block => string.IsNullOrWhiteSpace(block.Location)))
        {
            block.Location = variable.Placeholder;
        }
        MarkScriptDirty();
        SyncStructuredScriptToJson();
    }

    private void PasteLocationVariableFromClipboard(ScriptLocationVariableEditorViewModel? location)
    {
        if (ScriptEditorModel is null || location is null) return;

        string text;
        try
        {
            if (!Clipboard.ContainsText())
            {
                Log("Koordinaten einfuegen: Zwischenablage enthaelt keinen Text.");
                return;
            }

            text = Clipboard.GetText()?.Trim() ?? string.Empty;
        }
        catch (Exception ex)
        {
            Log("Koordinaten einfuegen: Zwischenablage konnte nicht gelesen werden: " + ex.Message);
            AppLogService.WriteException("PasteLocationVariableFromClipboard.Read", ex);
            return;
        }

        if (!location.TrySetFromText(text))
        {
            Log("Koordinaten einfuegen: Format nicht erkannt. Erwartet z.B. {X=-861133.875 Y=-861688.938 Z=1541.331|P=345.912994 Y=106.210098 R=0.000000}");
            return;
        }

        MarkScriptDirty();
        SyncStructuredScriptToJson();
        Log($"Koordinaten eingefuegt fuer {location.Name}: X={location.X:0.###}, Y={location.Y:0.###}, Z={location.Z:0.###}");
    }

    private void RemoveLocationVariable(ScriptLocationVariableEditorViewModel? location)
    {
        if (ScriptEditorModel is null || location is null) return;
        if (!ScriptEditorModel.LootSpawnLocations.Remove(location))
        {
            ScriptEditorModel.NpcSpawnLocations.Remove(location);
        }

        ScriptEditorModel.RefreshLocalVariablePlaceholders();
        MarkScriptDirty();
        SyncStructuredScriptToJson();
    }

    private void LoadGlobalLootPacks()
    {
        GlobalLootPacks.Clear();
        foreach (var pack in LootPackStore.Load())
        {
            GlobalLootPacks.Add(LootPackEditorViewModel.FromPack(pack));
        }
    }

    private void SaveGlobalLootPacks()
    {
        LootPackStore.Save(GlobalLootPacks.Select(pack => pack.ToPack()));
        Log("Globale Lootpacks gespeichert: " + GlobalLootPacks.Count);
    }

    private void ImportLegacyLootPacksIntoGlobalStore(IEnumerable<LootPack> packs)
    {
        var imported = packs
            .Where(pack => !string.IsNullOrWhiteSpace(pack.Name))
            .Where(pack => pack.Items.Any(item => !string.IsNullOrWhiteSpace(item.Item)))
            .ToList();

        if (imported.Count == 0)
        {
            return;
        }

        var current = GlobalLootPacks.Select(pack => pack.ToPack()).ToList();
        if (!LootPackStore.MergeMissing(current, imported))
        {
            return;
        }

        LootPackStore.Save(current);
        LoadGlobalLootPacks();
        Log("Legacy-Lootpacks in globale Bibliothek uebernommen.");
    }

    private void AddLootPackReference()
    {
        if (ScriptEditorModel is null) return;
        var selectedName = ScriptEditorModel.SelectedLootPackNameToAdd?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(selectedName))
        {
            selectedName = GlobalLootPacks.FirstOrDefault(pack => pack.Enabled)?.Name ?? GlobalLootPacks.FirstOrDefault()?.Name ?? "";
        }

        if (string.IsNullOrWhiteSpace(selectedName))
        {
            return;
        }

        if (ScriptEditorModel.LootPackNames.Any(name => string.Equals(name, selectedName, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        ScriptEditorModel.LootPackNames.Add(selectedName);
        ScriptEditorModel.RebuildFlow();
        MarkScriptDirty();
        SyncStructuredScriptToJson();
    }

    private void RemoveLootPackReference(string? packName)
    {
        if (ScriptEditorModel is null || string.IsNullOrWhiteSpace(packName)) return;
        var match = ScriptEditorModel.LootPackNames.FirstOrDefault(name => string.Equals(name, packName, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            return;
        }

        ScriptEditorModel.LootPackNames.Remove(match);
        ScriptEditorModel.RebuildFlow();
        MarkScriptDirty();
        SyncStructuredScriptToJson();
    }

    private void AddGlobalLootPack()
    {
        var pack = new LootPackEditorViewModel { Name = NextLootPackName(), Weight = 1 };
        GlobalLootPacks.Add(pack);
        SaveGlobalLootPacks();
    }

    private void RemoveGlobalLootPack(LootPackEditorViewModel? pack)
    {
        if (pack is null) return;
        GlobalLootPacks.Remove(pack);
        if (ScriptEditorModel?.LootPackNames.Remove(pack.Name) == true)
        {
            MarkScriptDirty();
            SyncStructuredScriptToJson();
        }
        SaveGlobalLootPacks();
    }

    private void AddLootItem(LootPackEditorViewModel? pack) => AddGlobalLootItem(pack);

    private void AddGlobalLootItem(LootPackEditorViewModel? pack)
    {
        if (pack is null) return;
        pack.Items.Add(new LootItemEditorViewModel { Quantity = 1, DelayMs = 50 });
    }

    private void RemoveLootItem(LootItemEditorViewModel? item) => RemoveGlobalLootItem(item);

    private void RemoveGlobalLootItem(LootItemEditorViewModel? item)
    {
        if (item is null) return;
        foreach (var pack in GlobalLootPacks)
        {
            if (pack.Items.Remove(item)) break;
        }
    }

    private string NextLootPackName()
    {
        var index = GlobalLootPacks.Count + 1;
        var name = "LootPack_" + index;
        while (GlobalLootPacks.Any(pack => string.Equals(pack.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            name = "LootPack_" + ++index;
        }

        return name;
    }

    private void AddLootCommandPack()
    {
        if (ScriptEditorModel is null) return;
        var pack = new LootCommandPackEditorViewModel { Name = "Neuer LootCommand", Weight = 1, DelayMs = 50 };
        ScriptEditorModel.LootCommandPacks.Add(pack);
        ScriptEditorModel.RebuildFlow();
        ScriptEditorModel.SelectFlowTarget(pack);
        MarkScriptDirty();
        SyncStructuredScriptToJson();
    }

    private void RemoveLootCommandPack(LootCommandPackEditorViewModel? pack)
    {
        if (ScriptEditorModel is null || pack is null) return;
        ScriptEditorModel.LootCommandPacks.Remove(pack);
        ScriptEditorModel.RebuildFlow();
        MarkScriptDirty();
        SyncStructuredScriptToJson();
    }

    private void AddLootCleanupCommands()
    {
        if (ScriptEditorModel is null) return;

        var locations = ScriptEditorModel.LootSpawnLocations
            .Where(location => !string.IsNullOrWhiteSpace(location.Placeholder))
            .ToList();

        if (locations.Count == 0)
        {
            locations.Add(new ScriptLocationVariableEditorViewModel("loot", "triggerZone", "{triggerZone}"));
        }

        var existingCommands = new HashSet<string>(
            ScriptEditorModel.CleanupBlock.Commands.Select(command => command.Command?.Trim() ?? string.Empty),
            StringComparer.OrdinalIgnoreCase);

        var added = false;
        foreach (var location in locations)
        {
            var target = location.Name.Equals("triggerZone", StringComparison.OrdinalIgnoreCase) ? "{triggerZone}" : location.Placeholder;
            var command = $"#DestroyAllItemsWithinRadius all 20 Location \"{target}\"";
            if (!existingCommands.Add(command))
            {
                continue;
            }

            ScriptEditorModel.CleanupBlock.Commands.Add(new ScriptCommandEditorViewModel
            {
                Name = "Cleanup " + (location.Name.Equals("triggerZone", StringComparison.OrdinalIgnoreCase) ? "Triggerzone" : location.Name),
                Command = command,
                Repeat = 1,
                DelayMs = 50
            });
            added = true;
        }

        if (!added)
        {
            return;
        }

        MarkScriptDirty();
        SyncStructuredScriptToJson();
    }

    public void SelectScriptFlowNode(ScriptFlowNodeViewModel? node)
    {
        ScriptEditorModel?.SelectFlowNode(node);
    }

    public void RefreshScripts()
    {
        Scripts.Clear();
        var dir = GetScriptsDirectory();
        Directory.CreateDirectory(dir);
        foreach (var file in Directory.EnumerateFiles(dir, "*.json").OrderBy(Path.GetFileName))
        {
            Scripts.Add(new ScriptFileViewModel(file));
        }

        SelectedScript = Scripts.FirstOrDefault();
        RefreshScriptZoneMapFromFiles();
        Log($"{Scripts.Count} Script-Dateien gefunden.");
    }

    private void CreateScript()
    {
        var definition = EventDefinitionStore.CreateTemplate();
        var path = EventDefinitionStore.Save(definition);
        RefreshScripts();
        SelectedScript = Scripts.FirstOrDefault(x => string.Equals(x.Path, path, StringComparison.OrdinalIgnoreCase));
        Log("Neues Script angelegt: " + Path.GetFileName(path));
    }

    private void RefreshScriptZoneMapFromFiles()
    {
        ScriptZoneMapItems.Clear();
        foreach (var script in Scripts)
        {
            try
            {
                var json = File.ReadAllText(script.Path);
                var definition = JsonSerializer.Deserialize<EventDefinition>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (definition is not null) ScriptZoneMapItems.Add(new ScriptZoneMapItemViewModel(definition));
            }
            catch
            {
                // Defekte Scripts sollen die Kartenanzeige nicht abbrechen.
            }
        }
    }

    private void LoadSelectedScript()
    {
        if (SelectedScript is null)
        {
            ScriptJson = string.Empty;
            ScriptEditorModel = null;
            return;
        }

        ScriptJson = File.ReadAllText(SelectedScript.Path);
        LoadStructuredScriptFromJson();
        ValidateScript();
        ScriptHasUnsavedChanges = false;
    }

    public void ValidateScript()
    {
        try
        {
            using var _ = JsonDocument.Parse(ScriptJson);
            if (ScriptEditorModel is null) LoadStructuredScriptFromJson();
            ScriptValidation = T("JsonValid");
            if (SelectedScript is not null) SelectedScript.HasErrors = false;
        }
        catch (Exception ex)
        {
            ScriptValidation = Tf("JsonErrorFormat", ex.Message);
            if (SelectedScript is not null) SelectedScript.HasErrors = true;
        }
    }

    public void FormatScript()
    {
        try
        {
            var element = JsonSerializer.Deserialize<JsonElement>(ScriptJson);
            ScriptJson = JsonSerializer.Serialize(element, new JsonSerializerOptions { WriteIndented = true });
            LoadStructuredScriptFromJson();
            ValidateScript();
            Log("Script formatiert.");
        }
        catch (Exception ex)
        {
            ScriptValidation = Tf("FormatFailedFormat", ex.Message);
        }
    }

    public void SaveScript()
    {
        TrySaveScript();
    }

    private bool TrySaveScript()
    {
        if (SelectedScript is null) return true;
        SyncStructuredScriptToJson();
        ValidateScript();
        if (SelectedScript.HasErrors) return false;
        File.WriteAllText(SelectedScript.Path, ScriptJson);
        Log("Script gespeichert: " + SelectedScript.Name);
        ScriptHasUnsavedChanges = false;
        if (ScriptEngineRunning) Log("Hinweis: Script Engine neu starten, damit diese Aenderung aktiv wird.");
        return true;
    }

    private void DuplicateScript()
    {
        if (SelectedScript is null) return;
        var source = SelectedScript.Path;
        var dir = Path.GetDirectoryName(source)!;
        var name = Path.GetFileNameWithoutExtension(source);
        var target = Path.Combine(dir, name + "_copy.json");
        var i = 2;
        while (File.Exists(target)) target = Path.Combine(dir, name + "_copy" + i++ + ".json");
        File.WriteAllText(target, ScriptJson);
        RefreshScripts();
        SelectedScript = Scripts.FirstOrDefault(x => string.Equals(x.Path, target, StringComparison.OrdinalIgnoreCase));
        Log("Script dupliziert: " + Path.GetFileName(target));
    }

    private static string GetScriptsDirectory()
    {
        var appDir = AppContext.BaseDirectory;
        var local = Path.Combine(appDir, "Data", "Scripts");
        if (Directory.Exists(local)) return local;
        return Path.Combine(Directory.GetCurrentDirectory(), "Data", "Scripts");
    }

    private void Log(string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {message}";

        try
        {
            AppLogService.Write(message);
        }
        catch
        {
            // Logging darf die App niemals abbrechen. UI-Log laeuft trotzdem weiter.
        }

        App.Current?.Dispatcher.Invoke(() =>
        {
            LogLines.Insert(0, line);
            while (LogLines.Count > 1000) LogLines.RemoveAt(LogLines.Count - 1);
        });
    }

    private void ClearLog()
    {
        App.Current?.Dispatcher.Invoke(() => LogLines.Clear());
        try
        {
            AppLogService.ClearCurrentFile();
        }
        catch (Exception ex)
        {
            Log("Logdatei konnte nicht geleert werden: " + ex.Message);
            return;
        }

        Log("Log geleert.");
    }

    private void OpenLogFolder()
    {
        try
        {
            EnsureLogDirectory();
            Process.Start(new ProcessStartInfo
            {
                FileName = LogDirectory,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Log("Log-Ordner konnte nicht geoeffnet werden: " + ex.Message);
        }
    }

    private void EnsureLogDirectory()
    {
        Directory.CreateDirectory(LogDirectory);
    }

    private void EnsureLocalLogDirectories()
    {
        var chat = SftpLogService.EnsureLocalDirectory(Settings, "Chat");
        var kill = SftpLogService.EnsureLocalDirectory(Settings, "Kill");
        SftpLogService.EnsureLocalDirectory(Settings, "Login");
        SftpLogService.EnsureLocalDirectory(Settings, "Db");
        Log("Lokaler Chatlog-Ordner: " + chat);
        Log("Lokaler Killlog-Ordner: " + kill);
    }

    private static string TrimForLog(string value)
    {
        value = value.Replace("\r", " ").Replace("\n", " ").Trim();
        return value.Length <= 600 ? value : value[..600] + " ...";
    }

    public async ValueTask DisposeAsync()
    {
        _discordServerStatusMessageCts?.Cancel();
        _discordServerStatusMessageCts?.Dispose();
        _playerStatusCts?.Cancel();
        _playerStatusCts?.Dispose();
        _chatForwarder?.Stop();
        _chatCommands?.Stop();
        _joinCommands?.Stop();
        _killFeed?.Stop();
        _weeklyTasks?.Stop();
        _autoMessages?.Stop();
        _eventEngine?.Dispose();
        _usageDirectory.Dispose();
        if (_discord is not null) await _discord.DisposeAsync();
        if (_rcon is not null) await _rcon.DisposeAsync();
        _playerScanLock.Dispose();
        _weeklyRewardClaimLock.Dispose();
        _weeklyRewardNotificationLock.Dispose();
    }
}


public sealed record LootSpawnModeOption(string Value, string Name);

public sealed class ScriptStructuredEditorViewModel : ObservableObject
{
    private string _mode = "RandomAnnouncedZone";

    public ScriptStructuredEditorViewModel()
    {
        LootRandomBlock = new ScriptLootRandomBlockEditorViewModel(this);
    }

    public string Id { get; set; } = "script";
    public string Name { get; set; } = "Script";
    public bool Enabled { get; set; } = true;
    public string Mode
    {
        get => _mode;
        set
        {
            var next = string.IsNullOrWhiteSpace(value) ? "RandomAnnouncedZone" : value.Trim();
            if (!SetProperty(ref _mode, next))
            {
                return;
            }

            OnPropertyChanged(nameof(IsRandomAnnouncedMode));
            OnPropertyChanged(nameof(IsRandomActivatedMode));
            OnPropertyChanged(nameof(IsBuyzoneMode));
            OnPropertyChanged(nameof(ShowsRandomizerSettings));
            OnPropertyChanged(nameof(ShowsRandomActivatedSettings));
            OnPropertyChanged(nameof(ShowsBuySettings));
            OnPropertyChanged(nameof(ShowsInitiatorRepeatSettings));
            if (FlowNodes.Count > 0)
            {
                RebuildFlow();
            }
        }
    }
    public bool IncludeInRandomizer { get; set; } = true;
    public int RandomizerEveryMinutes { get; set; } = 360;
    public int InitiatorRepeatEveryMinutes { get; set; }
    public int MaxConcurrentRandomEvents { get; set; } = 1;
    public int RandomActivationChancePercent { get; set; } = 25;
    public string EventGroup { get; set; } = "";
    public int MaxConcurrentInGroup { get; set; }
    public int BuyPrice { get; set; }
    public string BuyAlias { get; set; } = "";
    public int ActivationDelayMs { get; set; }
    public string TriggerServerMessageType { get; set; } = "Yellow";
    public string TriggerServerMessage { get; set; } = "";
    public string LootPackSpawnMode { get; set; } = "OneTotal";
    public int CleanupWhenEmptySeconds { get; set; } = 300;
    public int CooldownMinutes { get; set; } = 60;
    public string ZoneName { get; set; } = "Zone";
    public double ZoneX { get; set; }
    public double ZoneY { get; set; }
    public double ZoneZ { get; set; }
    public double ZoneRadius { get; set; } = 75000;
    public string InitiatorMessage { get; set; } = "";

    public ScriptBlockEditorViewModel PreLiveCleanupBlock { get; set; } = new() { Name = "Cleanup vor Live" };
    public ScriptBlockEditorViewModel InitiatorBlock { get; set; } = new() { Name = "Initiator Block" };
    public ScriptBlockEditorViewModel LiveBlock { get; set; } = new() { Name = "Liveblock" };
    public ScriptBlockEditorViewModel EmptyBlock { get; set; } = new() { Name = "Wenn Zone leer" };
    public ScriptBlockEditorViewModel CleanupBlock { get; set; } = new() { Name = "Cleanup Block" };
    public ObservableCollection<SpawnBlockEditorViewModel> SpawnBlocks { get; } = new();
    public ObservableCollection<ScriptLocationVariableEditorViewModel> LootSpawnLocations { get; } = new();
    public ObservableCollection<ScriptLocationVariableEditorViewModel> NpcSpawnLocations { get; } = new();
    public ObservableCollection<string> LocationPlaceholders { get; } = new();
    public ObservableCollection<string> LootLocationPlaceholders { get; } = new();
    public ObservableCollection<string> NpcLocationPlaceholders { get; } = new();
    public ObservableCollection<string> LootPackNames { get; } = new();
    public ObservableCollection<LootCommandPackEditorViewModel> LootCommandPacks { get; } = new();
    public string SelectedLootPackNameToAdd { get; set; } = "";
    public IReadOnlyList<ScriptBlockEditorViewModel> Blocks => new[] { PreLiveCleanupBlock, InitiatorBlock, LiveBlock, EmptyBlock, CleanupBlock };
    public ScriptLootRandomBlockEditorViewModel LootRandomBlock { get; }
    public ObservableCollection<ScriptFlowNodeViewModel> FlowNodes { get; } = new();
    public ObservableCollection<ScriptFlowConnectionViewModel> FlowConnections { get; } = new();

    public bool IsRandomAnnouncedMode => IsMode("RandomAnnouncedZone") || IsMode("Random");
    public bool IsRandomActivatedMode => IsMode("RandomActivated") || IsMode("RandomActivatedZone");
    public bool IsBuyzoneMode => IsMode("Buyzone") || IsMode("BuyZone") || IsMode("BuyEvent");
    public bool ShowsRandomizerSettings => IsRandomAnnouncedMode || IsRandomActivatedMode;
    public bool ShowsRandomActivatedSettings => IsRandomActivatedMode;
    public bool ShowsBuySettings => IsBuyzoneMode;
    public bool ShowsInitiatorRepeatSettings => IsRandomAnnouncedMode;

    private ScriptFlowNodeViewModel? _selectedFlowNode;
    public ScriptFlowNodeViewModel? SelectedFlowNode
    {
        get => _selectedFlowNode;
        set => SelectFlowNode(value);
    }

    public object? SelectedFlowTarget => SelectedFlowNode?.Target;
    public string SelectedFlowTitle => SelectedFlowNode?.Title ?? "Baustein";
    public string SelectedFlowSubtitle => SelectedFlowNode?.Subtitle ?? "";

    public static ScriptStructuredEditorViewModel FromDefinition(EventDefinition definition)
    {
        var zone = definition.ActivationZone ?? definition.Zone ?? new EventZone();
        var model = new ScriptStructuredEditorViewModel
        {
            Id = definition.Id ?? "script",
            Name = definition.Name ?? "Script",
            Enabled = definition.Enabled,
            Mode = string.IsNullOrWhiteSpace(definition.Mode) ? "RandomAnnouncedZone" : definition.Mode,
            IncludeInRandomizer = definition.IncludeInRandomizer,
            RandomizerEveryMinutes = definition.RandomizerEveryMinutes,
            InitiatorRepeatEveryMinutes = definition.InitiatorRepeatEveryMinutes,
            MaxConcurrentRandomEvents = definition.MaxConcurrentRandomEvents,
            RandomActivationChancePercent = Math.Clamp(definition.RandomActivationChancePercent, 0, 100),
            EventGroup = definition.EventGroup ?? "",
            MaxConcurrentInGroup = definition.MaxConcurrentInGroup,
            BuyPrice = Math.Max(0, definition.BuyPrice),
            BuyAlias = definition.BuyAlias ?? "",
            ActivationDelayMs = Math.Max(0, definition.ActivationDelayMs),
            TriggerServerMessageType = string.IsNullOrWhiteSpace(definition.TriggerServerMessageType) ? "Yellow" : definition.TriggerServerMessageType,
            TriggerServerMessage = definition.TriggerServerMessage ?? "",
            LootPackSpawnMode = NormalizeLootPackSpawnMode(definition.LootPackSpawnMode),
            CleanupWhenEmptySeconds = definition.CleanupWhenEmptySeconds,
            CooldownMinutes = definition.CooldownMinutes,
            ZoneName = zone.Name ?? "Zone",
            ZoneX = zone.CenterX,
            ZoneY = zone.CenterY,
            ZoneZ = zone.CenterZ,
            ZoneRadius = zone.Radius,
            InitiatorMessage = FirstNonEmpty(definition.LocalVariables?.InitiatorMessage, definition.Announcement),
            PreLiveCleanupBlock = ScriptBlockEditorViewModel.FromBlock(definition.PreLiveCleanupBlock, "Cleanup vor Live"),
            InitiatorBlock = ScriptBlockEditorViewModel.FromBlock(definition.InitiatorBlock, "Initiator Block"),
            LiveBlock = ScriptBlockEditorViewModel.FromBlock(definition.LiveBlock, "Liveblock"),
            EmptyBlock = ScriptBlockEditorViewModel.FromBlock(definition.EmptyBlock, "Wenn Zone leer"),
            CleanupBlock = ScriptBlockEditorViewModel.FromBlock(definition.CleanupBlock, "Cleanup Block")
        };

        var legacyLootPacks = definition.LootPacks ?? new List<LootPack>();
        var selectedLootPackNames = definition.LootPackNames?.Where(name => !string.IsNullOrWhiteSpace(name)).ToList() ?? new List<string>();
        if (selectedLootPackNames.Count == 0)
        {
            selectedLootPackNames = legacyLootPacks
                .Select(pack => pack.Name?.Trim() ?? "")
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        foreach (var name in selectedLootPackNames)
        {
            model.LootPackNames.Add(name);
        }

        foreach (var block in definition.SpawnBlocks ?? new List<SpawnBlock>()) model.SpawnBlocks.Add(SpawnBlockEditorViewModel.FromBlock(block));
        foreach (var location in definition.LocalVariables?.LootSpawnLocations ?? new List<ScriptLocationVariable>()) model.LootSpawnLocations.Add(ScriptLocationVariableEditorViewModel.FromVariable("loot", location));
        foreach (var location in definition.LocalVariables?.NpcSpawnLocations ?? new List<ScriptLocationVariable>()) model.NpcSpawnLocations.Add(ScriptLocationVariableEditorViewModel.FromVariable("npc", location));
        if (model.LootSpawnLocations.Count == 0)
        {
            var legacyLootLocations = legacyLootPacks
                .Select(pack => pack.Location?.Trim() ?? "")
                .Where(location => !string.IsNullOrWhiteSpace(location))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var location in legacyLootLocations)
            {
                model.LootSpawnLocations.Add(new ScriptLocationVariableEditorViewModel("loot", "loot_" + (model.LootSpawnLocations.Count + 1), location));
            }
        }

        model.RefreshLocalVariablePlaceholders();
        model.EnsureSpawnBlockLocations();
        return model;
    }

    public EventDefinition ToDefinition() => new()
    {
        Id = string.IsNullOrWhiteSpace(Id) ? "script" : Id.Trim(),
        Name = string.IsNullOrWhiteSpace(Name) ? "Script" : Name.Trim(),
        Enabled = Enabled,
        Mode = string.IsNullOrWhiteSpace(Mode) ? "RandomAnnouncedZone" : Mode.Trim(),
        IncludeInRandomizer = IncludeInRandomizer,
        RandomizerEveryMinutes = Math.Max(0, RandomizerEveryMinutes),
        InitiatorRepeatEveryMinutes = Math.Max(0, InitiatorRepeatEveryMinutes),
        MaxConcurrentRandomEvents = Math.Max(0, MaxConcurrentRandomEvents),
        RandomActivationChancePercent = Math.Clamp(RandomActivationChancePercent, 0, 100),
        EventGroup = EventGroup?.Trim() ?? "",
        MaxConcurrentInGroup = Math.Max(0, MaxConcurrentInGroup),
        BuyPrice = Math.Max(0, BuyPrice),
        BuyAlias = BuyAlias?.Trim() ?? "",
        ActivationDelayMs = Math.Max(0, ActivationDelayMs),
        TriggerServerMessageType = string.IsNullOrWhiteSpace(TriggerServerMessageType) ? "Yellow" : TriggerServerMessageType.Trim(),
        TriggerServerMessage = TriggerServerMessage?.Trim() ?? "",
        LootPackSpawnMode = NormalizeLootPackSpawnMode(LootPackSpawnMode),
        LootPackNames = LootPackNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() is { Count: > 0 } names ? names : null,
        Announcement = InitiatorMessage?.Trim() ?? "",
        Zone = new EventZone { Name = string.IsNullOrWhiteSpace(ZoneName) ? "Zone" : ZoneName.Trim(), CenterX = ZoneX, CenterY = ZoneY, CenterZ = ZoneZ, Radius = Math.Max(0, ZoneRadius) },
        LocalVariables = new ScriptLocalVariables
        {
            InitiatorMessage = InitiatorMessage?.Trim() ?? "",
            LootSpawnLocations = LootSpawnLocations.Select(x => x.ToVariable()).ToList(),
            NpcSpawnLocations = NpcSpawnLocations.Select(x => x.ToVariable()).ToList()
        },
        InitiatorBlock = InitiatorBlock.ToBlock(),
        PreLiveCleanupBlock = PreLiveCleanupBlock.ToBlock(),
        LiveBlock = LiveBlock.ToBlock(),
        SpawnBlocks = SpawnBlocks.Select(x => x.ToBlock()).ToList(),
        EmptyBlock = EmptyBlock.ToBlock(),
        CleanupBlock = CleanupBlock.ToBlock(),
        LootPacks = null,
        LootCommandPacks = null,
        CleanupWhenEmptySeconds = Math.Max(0, CleanupWhenEmptySeconds),
        CooldownMinutes = Math.Max(0, CooldownMinutes)
    };

    public void RefreshLocalVariablePlaceholders()
    {
        var sharedValues = new List<string>
        {
            "{triggerZone}"
        };

        var lootValues = sharedValues.Concat(LootSpawnLocations.Select(x => x.Placeholder)).ToList();
        var npcValues = sharedValues.Concat(NpcSpawnLocations.Select(x => x.Placeholder)).ToList();

        ReplacePlaceholders(LootLocationPlaceholders, lootValues);
        ReplacePlaceholders(NpcLocationPlaceholders, npcValues);
        ReplacePlaceholders(LocationPlaceholders, lootValues.Concat(NpcSpawnLocations.Select(x => x.Placeholder)));
    }

    public void EnsureSpawnBlockLocations()
    {
        var defaultLocation = NpcLocationPlaceholders
            .FirstOrDefault(x => !string.Equals(x, "{triggerZone}", StringComparison.OrdinalIgnoreCase))
            ?? NpcLocationPlaceholders.FirstOrDefault()
            ?? "{triggerZone}";

        foreach (var spawn in SpawnBlocks.Where(spawn => string.IsNullOrWhiteSpace(spawn.Location)))
        {
            spawn.Location = defaultLocation;
        }
    }

    private static void ReplacePlaceholders(ObservableCollection<string> target, IEnumerable<string> values)
    {
        var nextValues = values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (target.SequenceEqual(nextValues, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        target.Clear();
        foreach (var value in nextValues)
        {
            target.Add(value);
        }
    }

    public void EnsureActivationDelayMs(int fallbackDelayMs)
    {
        if (ActivationDelayMs <= 0)
        {
            ActivationDelayMs = Math.Max(0, fallbackDelayMs);
            OnPropertyChanged(nameof(ActivationDelayMs));
            RebuildFlow();
        }
    }

    public void RebuildFlow()
    {
        var oldPositions = FlowNodes.ToDictionary(n => n.NodeId, n => (n.X, n.Y), StringComparer.OrdinalIgnoreCase);

        foreach (var connection in FlowConnections)
        {
            connection.Dispose();
        }

        FlowConnections.Clear();
        FlowNodes.Clear();

        var triggerInfo = Mode.Equals("RandomActivated", StringComparison.OrdinalIgnoreCase)
            ? $"Chance {RandomActivationChancePercent}% | Cooldown {CooldownMinutes}m"
            : $"{Mode} | Cooldown {CooldownMinutes}m";
        var zone = AddFlowNode("zone", "Trigger", "Zone / Server Message", triggerInfo, this, 24, 24, oldPositions);
        var initiator = AddFlowNode("initiator", "Initiierung", "Servernachricht", $"{InitiatorBlock.Commands.Count} Commands", InitiatorBlock, 24, 130, oldPositions);
        var preLive = AddFlowNode("prelive", "Block", "Cleanup vor Live", $"{PreLiveCleanupBlock.Commands.Count} Commands", PreLiveCleanupBlock, 24, 236, oldPositions);
        var timer = AddFlowNode("timer", "Timer", "Optional Timer", ActivationDelayMs > 0 ? $"{ActivationDelayMs}ms" : "sofort", this, 24, 342, oldPositions);
        var live = AddFlowNode("live", "Block", "Live", $"{LiveBlock.Commands.Count} Commands", LiveBlock, 24, 448, oldPositions);
        var empty = AddFlowNode("empty", "Block", "Zone leer", $"{EmptyBlock.Commands.Count} Commands", EmptyBlock, 708, 448, oldPositions);
        var cleanup = AddFlowNode("cleanup", "Block", "Cleanup", $"{CleanupBlock.Commands.Count} Commands", CleanupBlock, 708, 554, oldPositions);

        Connect(zone, initiator);
        Connect(initiator, preLive);
        Connect(preLive, timer);
        Connect(timer, live);

        var branchTargets = new List<ScriptFlowNodeViewModel>();
        var branchY = 24d;
        var branchIndex = 0;
        foreach (var spawn in SpawnBlocks)
        {
            var wait = spawn.StartDelayMs > 0 ? $"{spawn.StartDelayMs}ms" : (spawn.StartDelaySeconds > 0 ? $"{spawn.StartDelaySeconds}s" : "sofort");
            branchTargets.Add(AddFlowNode(TargetNodeId("spawn", spawn), "Spawns", spawn.Name, $"{wait} | {spawn.Type} x{spawn.Quantity}", spawn, 252, branchY + branchIndex++ * 106, oldPositions));
        }

        var randomLootY = branchY + branchIndex++ * 106;
        var randomLoot = AddFlowNode("random_loot", "Optional Loot", "Lootpacks", $"{LootPackNames.Count} Packs | {LootPackSpawnMode}", LootRandomBlock, 252, randomLootY, oldPositions);
        branchTargets.Add(randomLoot);

        if (branchTargets.Count == 0)
        {
            Connect(live, empty);
        }
        else
        {
            foreach (var branch in branchTargets)
            {
                Connect(live, branch);
                Connect(branch, empty);
            }
        }

        Connect(empty, cleanup);

        var oldSelectedId = _selectedFlowNode?.NodeId;
        SelectFlowNode(FlowNodes.FirstOrDefault(n => string.Equals(n.NodeId, oldSelectedId, StringComparison.OrdinalIgnoreCase)) ?? FlowNodes.FirstOrDefault());
    }

    public void SelectFlowTarget(object target)
    {
        SelectFlowNode(FlowNodes.FirstOrDefault(n => ReferenceEquals(n.Target, target)));
    }

    public void SelectFlowNode(ScriptFlowNodeViewModel? node)
    {
        if (ReferenceEquals(_selectedFlowNode, node))
        {
            return;
        }

        if (_selectedFlowNode is not null)
        {
            _selectedFlowNode.IsSelected = false;
        }

        _selectedFlowNode = node;

        if (_selectedFlowNode is not null)
        {
            _selectedFlowNode.IsSelected = true;
        }

        OnPropertyChanged(nameof(SelectedFlowNode));
        OnPropertyChanged(nameof(SelectedFlowTarget));
        OnPropertyChanged(nameof(SelectedFlowTitle));
        OnPropertyChanged(nameof(SelectedFlowSubtitle));
    }

    private ScriptFlowNodeViewModel AddFlowNode(
        string nodeId,
        string kind,
        string title,
        string subtitle,
        object target,
        double x,
        double y,
        Dictionary<string, (double X, double Y)> oldPositions)
    {
        if (oldPositions.TryGetValue(nodeId, out var position))
        {
            x = position.X;
            y = position.Y;
        }

        var node = new ScriptFlowNodeViewModel(nodeId, kind, title, subtitle, target)
        {
            X = x,
            Y = y
        };
        FlowNodes.Add(node);
        return node;
    }

    private void Connect(ScriptFlowNodeViewModel from, ScriptFlowNodeViewModel to)
    {
        FlowConnections.Add(new ScriptFlowConnectionViewModel(from, to));
    }

    private static string TargetNodeId(string prefix, object target)
    {
        return prefix + "_" + RuntimeHelpers.GetHashCode(target).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return string.Empty;
    }

    private bool IsMode(string value) => Mode.Equals(value, StringComparison.OrdinalIgnoreCase);

    private static string NormalizeLootPackSpawnMode(string? value)
    {
        var mode = (value ?? string.Empty).Trim();
        if (mode.Equals("OnePerLocation", StringComparison.OrdinalIgnoreCase) ||
            mode.Equals("PerLocation", StringComparison.OrdinalIgnoreCase) ||
            mode.Equals("AllPoints", StringComparison.OrdinalIgnoreCase) ||
            mode.Equals("AllePunkte", StringComparison.OrdinalIgnoreCase))
        {
            return "OnePerLocation";
        }

        return "OneTotal";
    }
}

public sealed class ScriptBlockEditorViewModel : ObservableObject
{
    public string Name { get; set; } = "Block";
    public bool Enabled { get; set; } = true;
    public ObservableCollection<ScriptCommandEditorViewModel> Commands { get; } = new();
    public string Summary => $"{Commands.Count} Commands";

    public static ScriptBlockEditorViewModel FromBlock(ScriptBlock? block, string fallbackName)
    {
        var model = new ScriptBlockEditorViewModel { Name = string.IsNullOrWhiteSpace(block?.Name) ? fallbackName : block!.Name, Enabled = true };
        foreach (var command in block?.Commands ?? new List<EventCommand>()) model.Commands.Add(ScriptCommandEditorViewModel.FromCommand(command));
        return model;
    }

    public ScriptBlock ToBlock() => new()
    {
        Name = string.IsNullOrWhiteSpace(Name) ? "Block" : Name.Trim(),
        Enabled = true,
        Commands = Commands.Select(x => x.ToCommand()).ToList()
    };
}

public sealed class ScriptCommandEditorViewModel : ObservableObject
{
    public string Name { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public string Command { get; set; } = "";
    public int Repeat { get; set; } = 1;
    public int DelayMs { get; set; } = 50;

    public static ScriptCommandEditorViewModel FromCommand(EventCommand command) => new()
    {
        Name = command.Name ?? "",
        Enabled = command.Enabled,
        Command = command.Command ?? "",
        Repeat = Math.Max(1, command.Repeat),
        DelayMs = command.DelayMs <= 0 ? 50 : command.DelayMs
    };

    public EventCommand ToCommand() => new()
    {
        Name = Name?.Trim() ?? "",
        Enabled = Enabled,
        Command = Command?.Trim() ?? "",
        Repeat = Math.Max(1, Repeat),
        DelayMs = Math.Max(0, DelayMs)
    };
}

public sealed class ScriptLocationVariableEditorViewModel : ObservableObject
{
    private string _name;
    private double _x;
    private double _y;
    private double _z;

    public ScriptLocationVariableEditorViewModel(string prefix, string name, string location)
    {
        Prefix = prefix;
        _name = name;
        SetCoordinatesFromLocation(location);
    }

    public string Prefix { get; }

    public string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, value))
            {
                OnPropertyChanged(nameof(Placeholder));
            }
        }
    }

    public string Location
    {
        get => BuildLocation(X, Y, Z);
        set
        {
            if (TryParseLocation(value, out var x, out var y, out var z))
            {
                X = x;
                Y = y;
                Z = z;
            }
        }
    }

    public double X
    {
        get => _x;
        set
        {
            if (SetProperty(ref _x, value))
            {
                OnPropertyChanged(nameof(Location));
            }
        }
    }

    public double Y
    {
        get => _y;
        set
        {
            if (SetProperty(ref _y, value))
            {
                OnPropertyChanged(nameof(Location));
            }
        }
    }

    public double Z
    {
        get => _z;
        set
        {
            if (SetProperty(ref _z, value))
            {
                OnPropertyChanged(nameof(Location));
            }
        }
    }

    public string Placeholder => "{" + Prefix + "_" + SanitizePlaceholderName(Name) + "}";

    public static ScriptLocationVariableEditorViewModel FromVariable(string prefix, ScriptLocationVariable variable) =>
        new(prefix, variable.Name ?? "position", variable.Location ?? "");

    public bool TrySetFromText(string? text)
    {
        if (!TryParseLocation(text, out var x, out var y, out var z))
        {
            return false;
        }

        X = x;
        Y = y;
        Z = z;
        return true;
    }

    public ScriptLocationVariable ToVariable() => new()
    {
        Name = string.IsNullOrWhiteSpace(Name) ? "position" : Name.Trim(),
        Location = Location
    };

    private void SetCoordinatesFromLocation(string? location)
    {
        if (TryParseLocation(location, out var x, out var y, out var z))
        {
            _x = x;
            _y = y;
            _z = z;
        }
    }

    private static bool TryParseLocation(string? location, out double x, out double y, out double z)
    {
        x = 0;
        y = 0;
        z = 0;

        if (string.IsNullOrWhiteSpace(location))
        {
            return false;
        }

        var coordinatePart = location.Split('|')[0];
        return TryReadCoordinate(coordinatePart, "X", out x)
               && TryReadCoordinate(coordinatePart, "Y", out y)
               && TryReadCoordinate(coordinatePart, "Z", out z);
    }

    private static bool TryReadCoordinate(string source, string key, out double value)
    {
        value = 0;
        var match = System.Text.RegularExpressions.Regex.Match(source, @"\b" + key + @"\s*=\s*(-?\d+(?:[\.,]\d+)?)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return match.Success && double.TryParse(match.Groups[1].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static string BuildLocation(double x, double y, double z) =>
        string.Create(CultureInfo.InvariantCulture, $"[{{X={x} Y={y} Z={z}}}]");

    private static string SanitizePlaceholderName(string? value)
    {
        var cleaned = System.Text.RegularExpressions.Regex.Replace((value ?? string.Empty).Trim(), "[^A-Za-z0-9_]+", "_");
        cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, "_+", "_").Trim('_');
        return string.IsNullOrWhiteSpace(cleaned) ? "value" : cleaned;
    }
}

public sealed class AutoMessageEditorViewModel : ObservableObject
{
    private string _name = "Auto Message";
    private bool _enabled = true;
    private string _mode = "Queue";
    private string _type = "Text";
    private string _messageType = "Yellow";
    private int _intervalMinutes = 15;
    private string _text = string.Empty;

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public bool Enabled
    {
        get => _enabled;
        set => SetProperty(ref _enabled, value);
    }

    public string Mode
    {
        get => _mode;
        set
        {
            if (SetProperty(ref _mode, string.IsNullOrWhiteSpace(value) ? "Queue" : value.Trim()))
            {
                OnPropertyChanged(nameof(IsStandalone));
                OnPropertyChanged(nameof(TimingLabel));
            }
        }
    }

    public string Type
    {
        get => _type;
        set
        {
            if (SetProperty(ref _type, string.IsNullOrWhiteSpace(value) ? "Text" : value.Trim()))
            {
                OnPropertyChanged(nameof(IsChallenge));
                OnPropertyChanged(nameof(PreviewText));
            }
        }
    }

    public string MessageType
    {
        get => _messageType;
        set => SetProperty(ref _messageType, string.IsNullOrWhiteSpace(value) ? "Yellow" : value.Trim());
    }

    public int IntervalMinutes
    {
        get => _intervalMinutes;
        set => SetProperty(ref _intervalMinutes, Math.Max(1, value));
    }

    public string Text
    {
        get => _text;
        set
        {
            if (SetProperty(ref _text, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(PreviewText));
            }
        }
    }

    public bool IsStandalone => AutoMessageFlow.IsStandalone(Mode);
    public bool IsChallenge => AutoMessageFlow.IsChallengeStep(Type);
    public string TimingLabel => IsStandalone ? "Eigenes Intervall" : "Queue-Reihenfolge";
    public string PreviewText => IsChallenge ? "Aktueller Community-Challenge-Status" : Text;

    public static AutoMessageEditorViewModel FromStep(AutoMessageStep step, string fallbackMessageType, int fallbackIntervalMinutes) => new()
    {
        Name = string.IsNullOrWhiteSpace(step.Name) ? (AutoMessageFlow.IsChallengeStep(step.Type) ? "Challenge Status" : "Auto Message") : step.Name,
        Enabled = step.Enabled,
        Mode = string.IsNullOrWhiteSpace(step.Mode) ? "Queue" : step.Mode,
        Type = string.IsNullOrWhiteSpace(step.Type) ? "Text" : step.Type,
        MessageType = string.IsNullOrWhiteSpace(step.MessageType) ? fallbackMessageType : step.MessageType,
        IntervalMinutes = step.IntervalMinutes <= 0 ? Math.Max(1, fallbackIntervalMinutes) : step.IntervalMinutes,
        Text = step.Text ?? string.Empty
    };

    public AutoMessageStep ToStep() => new()
    {
        Name = string.IsNullOrWhiteSpace(Name) ? "Auto Message" : Name.Trim(),
        Enabled = Enabled,
        Mode = string.IsNullOrWhiteSpace(Mode) ? "Queue" : Mode.Trim(),
        Type = string.IsNullOrWhiteSpace(Type) ? "Text" : Type.Trim(),
        MessageType = string.IsNullOrWhiteSpace(MessageType) ? "Yellow" : MessageType.Trim(),
        IntervalMinutes = Math.Max(1, IntervalMinutes),
        Text = Text?.Trim() ?? string.Empty
    };
}

public sealed class ScriptLootRandomBlockEditorViewModel : ObservableObject
{
    private readonly ScriptStructuredEditorViewModel _owner;

    public ScriptLootRandomBlockEditorViewModel(ScriptStructuredEditorViewModel owner)
    {
        _owner = owner;
    }

    public string Name => "Random Loot";
    public ObservableCollection<string> LootPackNames => _owner.LootPackNames;

    public string SelectedLootPackNameToAdd
    {
        get => _owner.SelectedLootPackNameToAdd;
        set => _owner.SelectedLootPackNameToAdd = value ?? string.Empty;
    }

    public string LootPackSpawnMode
    {
        get => _owner.LootPackSpawnMode;
        set
        {
            var next = string.IsNullOrWhiteSpace(value) ? "OneTotal" : value.Trim();
            if (next.Equals("OnePerLocation", StringComparison.OrdinalIgnoreCase) ||
                next.Equals("PerLocation", StringComparison.OrdinalIgnoreCase) ||
                next.Equals("AllPoints", StringComparison.OrdinalIgnoreCase) ||
                next.Equals("AllePunkte", StringComparison.OrdinalIgnoreCase))
            {
                next = "OnePerLocation";
            }
            else
            {
                next = "OneTotal";
            }

            if (string.Equals(_owner.LootPackSpawnMode, next, StringComparison.Ordinal))
            {
                return;
            }

            _owner.LootPackSpawnMode = next;
            OnPropertyChanged();
        }
    }

    public string Summary => $"{LootPackNames.Count} LootPacks";
}

public sealed class ScriptFlowNodeViewModel : ObservableObject
{
    private double _x;
    private double _y;
    private bool _isSelected;

    public ScriptFlowNodeViewModel(string nodeId, string kind, string title, string subtitle, object target)
    {
        NodeId = nodeId;
        Kind = kind;
        Title = title;
        Subtitle = subtitle;
        Target = target;
    }

    public string NodeId { get; }
    public string Kind { get; }
    public string Title { get; }
    public string Subtitle { get; }
    public object Target { get; }

    public double X
    {
        get => _x;
        set
        {
            if (SetProperty(ref _x, value))
            {
                OnPropertyChanged(nameof(CenterX));
            }
        }
    }

    public double Y
    {
        get => _y;
        set
        {
            if (SetProperty(ref _y, value))
            {
                OnPropertyChanged(nameof(CenterY));
            }
        }
    }

    public double CenterX => X + 88;
    public double CenterY => Y + 42;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

public sealed class ScriptFlowConnectionViewModel : ObservableObject, IDisposable
{
    public ScriptFlowConnectionViewModel(ScriptFlowNodeViewModel from, ScriptFlowNodeViewModel to)
    {
        From = from;
        To = to;
        From.PropertyChanged += NodeOnPropertyChanged;
        To.PropertyChanged += NodeOnPropertyChanged;
    }

    public ScriptFlowNodeViewModel From { get; }
    public ScriptFlowNodeViewModel To { get; }
    public double X1 => From.CenterX;
    public double Y1 => From.CenterY;
    public double X2 => To.CenterX;
    public double Y2 => To.CenterY;

    private void NodeOnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ScriptFlowNodeViewModel.X) or nameof(ScriptFlowNodeViewModel.Y) or nameof(ScriptFlowNodeViewModel.CenterX) or nameof(ScriptFlowNodeViewModel.CenterY))
        {
            OnPropertyChanged(nameof(X1));
            OnPropertyChanged(nameof(Y1));
            OnPropertyChanged(nameof(X2));
            OnPropertyChanged(nameof(Y2));
        }
    }

    public void Dispose()
    {
        From.PropertyChanged -= NodeOnPropertyChanged;
        To.PropertyChanged -= NodeOnPropertyChanged;
    }
}

public sealed class SpawnBlockEditorViewModel : ObservableObject
{
    private const string RandomZombieType = "Random Zombie";
    private const string LootPuppetType = "Lootpuppet";
    private const string LootPuppetAsset = "BP_Zombie_Civilian_Skinny_Loot";

    private static readonly IReadOnlyList<string> ZombieSuggestions = new[]
    {
        "BP_Zombie2",
        "BP_Zombie_Civilian",
        "BP_Zombie_Civilian_Fat_Female",
        "BP_Zombie_Civilian_Fat_Male",
        "BP_Zombie_Civilian_Muscular_Female",
        "BP_Zombie_Civilian_Muscular_Male",
        "BP_Zombie_Civilian_Normal_Male",
        "BP_Zombie_Civilian_Skinny_Female",
        "BP_Zombie_Civilian_Skinny_Male",
        "BP_Zombie_Hospital",
        "BP_Zombie_Hospital_Fat",
        "BP_Zombie_Hospital_Female",
        "BP_Zombie_Hospital_Muscle",
        "BP_Zombie_Hospital_Normal",
        "BP_Zombie_Military",
        "BP_Zombie_Military_Armored",
        "BP_Zombie_Military_Female",
        "BP_Zombie_Military_Muscle",
        "BP_Zombie_Nuclear",
        "BP_Zombie_Nuclear_Fat_Female",
        "BP_Zombie_Nuclear_Fat_Male",
        "BP_Zombie_Nuclear_Muscular_Female",
        "BP_Zombie_Nuclear_Muscular_Male",
        "BP_Zombie_Police",
        "BP_Zombie_Police_Armored",
        "BP_Zombie_Police_Fat",
        "BP_Zombie_Police_Female",
        "BP_Zombie_Police_Muscle",
        "BP_Zombie_SuicideVest"
    };

    private static readonly IReadOnlyList<string> ArmedNpcSuggestions = new[]
    {
        "BP_Drifter_Lvl_1",
        "BP_Drifter_Lvl_2",
        "BP_Drifter_Lvl_3",
        "BP_Drifter_Lvl_3_Radiation",
        "BP_Drifter_Lvl_4",
        "BP_Drifter_Lvl_4_AbandonedBunker",
        "BP_Drifter_Lvl_4_Radiation",
        "BP_Drifter_Lvl_5",
        "BP_Drifter_Lvl_5_AbandonedBunker",
        "BP_Drifter_Lvl_5_Radiation",
        "BP_Guard_Lvl_1",
        "BP_Guard_Lvl_2",
        "BP_Guard_Lvl_3",
        "BP_Guard_Lvl_4",
        "BP_Guard_Lvl_4_AbandonedBunker",
        "BP_Guard_Lvl_4_Radiation",
        "BP_Guard_Lvl_5",
        "BP_Guard_Lvl_5_AbandonedBunker",
        "BP_Guard_Lvl_5_Radiation"
    };

    private static readonly IReadOnlyList<string> VehicleSuggestions = new[]
    {
        "BPC_Rager",
        "BPC_WolfsWagen",
        "BPC_Laika",
        "BPC_Kinglet_Duster",
        "BPC_Dirtbike"
    };

    private static readonly IReadOnlyList<string> ItemSuggestions = new[]
    {
        "Weapon_SKS",
        "Magazine_Clip_SKS",
        "Cal_7_62x39mm_Ammobox",
        "Copper_Coins",
        "MRE_Stew",
        "Bandage",
        "Fireplace",
        "Tent"
    };

    private static readonly IReadOnlyList<string> CustomCommandSuggestions = new[]
    {
        "#SpawnItem Weapon_SKS 1 Location \"{location}\"",
        "#SpawnRandomZombie 5 Location \"{location}\"",
        "#SpawnVehicle BPC_Rager 1 Location \"{location}\" Modifier minimalfunctional",
        "#ScheduleWorldEvent BP_CargoDropEvent {worldLocation}"
    };

    private string _type = "ArmedNPC";
    private string _asset = "BP_Guard_Lvl_1";
    private string _location = "";

    public string Name { get; set; } = "Spawn";
    public bool Enabled { get; set; } = true;
    public string Type
    {
        get => _type;
        set
        {
            var next = string.IsNullOrWhiteSpace(value) ? "ArmedNPC" : value.Trim();
            if (SetProperty(ref _type, next))
            {
                OnPropertyChanged(nameof(AssetSuggestions));
                OnPropertyChanged(nameof(IsAssetInputEnabled));
                OnPropertyChanged(nameof(IsQuantityInputEnabled));
                OnPropertyChanged(nameof(IsDespawnLifetimeInputEnabled));
                OnPropertyChanged(nameof(IsExtraInputEnabled));
                OnPropertyChanged(nameof(AssetFieldLabel));
                OnPropertyChanged(nameof(AssetFieldHint));
                OnPropertyChanged(nameof(LocationFieldHint));
                OnPropertyChanged(nameof(CommandModeHint));
                if (IsCustomCommandType(next) || IsCargoDropType(next) || IsRandomZombieType(next) || IsLootPuppetType(next))
                {
                    Asset = "";
                }
                else
                {
                    var suggestions = AssetSuggestions;
                    Asset = suggestions.Count > 0 ? suggestions[0] : "";
                }
            }
        }
    }

    public string Asset
    {
        get => _asset;
        set => SetProperty(ref _asset, NormalizeEditorAsset(Type, value));
    }

    public IReadOnlyList<string> AssetSuggestions => GetAssetSuggestions(Type);
    public bool IsAssetInputEnabled => RequiresAssetInput(Type);
    public bool IsQuantityInputEnabled => !IsCustomCommandType(Type) && !IsCargoDropType(Type);
    public bool IsDespawnLifetimeInputEnabled => !IsCustomCommandType(Type) && !IsCargoDropType(Type);
    public bool IsExtraInputEnabled => !IsCustomCommandType(Type) && !IsCargoDropType(Type);
    public string AssetFieldLabel => IsCustomCommandType(Type)
        ? "Command"
        : IsCargoDropType(Type)
            ? "Command automatisch"
            : IsZombieType(Type) || IsRandomZombieType(Type) || IsLootPuppetType(Type)
                ? "Zombie"
                : "Asset";
    public string AssetFieldHint
    {
        get
        {
            if (IsCustomCommandType(Type))
            {
                return "Custom: kompletter RCON-Befehl. #Spawn... kann mit oder ohne # eingegeben werden; {location} nutzt die gewaehlte Location, {worldLocation} erzeugt X=... Y=... Z=...";
            }

            if (IsCargoDropType(Type))
            {
                return "CargoDrop: keinen #Schedule-Command eingeben. Das Tool baut #ScheduleWorldEvent BP_CargoDropEvent automatisch aus der Location.";
            }

            if (IsRandomZombieType(Type))
            {
                return "Random Zombie nutzt #SpawnRandomZombie und benoetigt keine Variante.";
            }

            if (IsLootPuppetType(Type))
            {
                return "Lootpuppet nutzt fest #SpawnZombie " + LootPuppetAsset + ".";
            }

            if (IsZombieType(Type))
            {
                return "Zombie nutzt #SpawnZombie mit einer Variante aus dem Katalog.";
            }

            return "Keinen #Spawn-Command eingeben: Typ, Asset, Menge und Location bauen den Spawn-Befehl automatisch.";
        }
    }
    public string LocationFieldHint => IsCustomCommandType(Type)
        ? "Optional fuer Custom: im Command mit {location} oder {worldLocation} verwenden."
        : IsCargoDropType(Type)
            ? "Wird als X=... Y=... Z=... an #ScheduleWorldEvent BP_CargoDropEvent angehaengt."
            : "Wird automatisch als Location \"...\" in den Spawn-Befehl eingesetzt.";
    public string CommandModeHint => IsCargoDropType(Type)
        ? "CargoDrop sendet automatisch: #ScheduleWorldEvent BP_CargoDropEvent X=... Y=... Z=..."
        : AssetFieldHint;
    public int Quantity { get; set; } = 1;
    public string Location
    {
        get => _location;
        set => SetProperty(ref _location, value ?? string.Empty);
    }
    public string Extra { get; set; } = "";
    public int DespawnLifetimeSeconds { get; set; }
    public int StartDelaySeconds { get; set; }
    public int StartDelayMs { get; set; }
    public int Repeat { get; set; } = 1;
    public int RepeatEverySeconds { get; set; }
    public int RepeatEveryMs { get; set; }
    public int DelayMs { get; set; } = 250;
    public bool UseTriggerPlayer { get; set; } = true;

    public static SpawnBlockEditorViewModel FromBlock(SpawnBlock block) => new()
    {
        Name = string.IsNullOrWhiteSpace(block.Name) ? "Spawn" : block.Name,
        Enabled = block.Enabled,
        Type = NormalizeEditorSpawnType(block.Type, block.Asset),
        Asset = block.Asset ?? "",
        Quantity = Math.Max(1, block.Quantity),
        Location = block.Location ?? "",
        Extra = block.Extra ?? "",
        DespawnLifetimeSeconds = Math.Max(0, block.DespawnLifetimeSeconds),
        StartDelaySeconds = Math.Max(0, block.StartDelaySeconds),
        StartDelayMs = Math.Max(0, block.StartDelayMs),
        Repeat = Math.Max(1, block.Repeat),
        RepeatEverySeconds = Math.Max(0, block.RepeatEverySeconds),
        RepeatEveryMs = Math.Max(0, block.RepeatEveryMs),
        DelayMs = Math.Max(0, block.DelayMs),
        UseTriggerPlayer = block.UseTriggerPlayer
    };

    public SpawnBlock ToBlock()
    {
        var type = string.IsNullOrWhiteSpace(Type) ? "ArmedNPC" : Type.Trim();
        return new SpawnBlock
        {
            Name = string.IsNullOrWhiteSpace(Name) ? "Spawn" : Name.Trim(),
            Enabled = Enabled,
            Type = type,
            Asset = NormalizeStoredAsset(type, Asset),
            Quantity = Math.Max(1, Quantity),
            Location = Location?.Trim() ?? "",
            Extra = Extra?.Trim() ?? "",
            DespawnLifetimeSeconds = Math.Max(0, DespawnLifetimeSeconds),
            StartDelaySeconds = Math.Max(0, StartDelaySeconds),
            StartDelayMs = Math.Max(0, StartDelayMs),
            Repeat = Math.Max(1, Repeat),
            RepeatEverySeconds = Math.Max(0, RepeatEverySeconds),
            RepeatEveryMs = Math.Max(0, RepeatEveryMs),
            DelayMs = Math.Max(0, DelayMs),
            UseTriggerPlayer = UseTriggerPlayer
        };
    }

    public void RefreshAssetSuggestions()
    {
        OnPropertyChanged(nameof(AssetSuggestions));
        OnPropertyChanged(nameof(IsAssetInputEnabled));
        OnPropertyChanged(nameof(AssetFieldLabel));
        OnPropertyChanged(nameof(AssetFieldHint));

        if (IsZombieType(Type) && string.IsNullOrWhiteSpace(Asset) && ZombieSuggestions.Count > 0)
        {
            Asset = ZombieSuggestions[0];
        }
    }

    private static IReadOnlyList<string> GetAssetSuggestions(string? type)
    {
        var value = (type ?? string.Empty).Trim();
        if (value.Equals("ArmedNPC", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("Armed NPC", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("NPC", StringComparison.OrdinalIgnoreCase))
        {
            return ArmedNpcSuggestions;
        }

        if (IsZombieType(value))
        {
            return ZombieSuggestions;
        }

        if (IsRandomZombieType(value))
        {
            return Array.Empty<string>();
        }

        if (IsLootPuppetType(value))
        {
            return Array.Empty<string>();
        }

        if (value.Equals("Vehicle", StringComparison.OrdinalIgnoreCase))
        {
            return VehicleSuggestions;
        }

        if (value.Equals("Item", StringComparison.OrdinalIgnoreCase))
        {
            return ItemSuggestions;
        }

        if (IsCustomCommandType(value))
        {
            return CustomCommandSuggestions;
        }

        if (IsCargoDropType(value))
        {
            return Array.Empty<string>();
        }

        return ArmedNpcSuggestions;
    }

    private static bool RequiresAssetInput(string? type)
    {
        var value = (type ?? string.Empty).Trim();
        return value.Equals("ArmedNPC", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("Armed NPC", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("NPC", StringComparison.OrdinalIgnoreCase) ||
               IsZombieType(value) ||
               value.Equals("Vehicle", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("Item", StringComparison.OrdinalIgnoreCase) ||
               IsCustomCommandType(value);
    }

    private static string NormalizeEditorAsset(string? type, string? asset)
    {
        var value = asset?.Trim() ?? string.Empty;
        return IsRandomZombieType(type) || IsLootPuppetType(type)
            ? string.Empty
            : value;
    }

    private static string NormalizeStoredAsset(string? type, string? asset)
    {
        var value = asset?.Trim() ?? string.Empty;
        return IsRandomZombieType(type) || IsLootPuppetType(type)
            ? string.Empty
            : value;
    }

    private static bool IsRandomZombieType(string? type)
    {
        var value = (type ?? string.Empty).Trim();
        return value.Equals(RandomZombieType, StringComparison.OrdinalIgnoreCase) ||
               value.Equals("RandomZombie", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLootPuppetType(string? type)
    {
        var value = (type ?? string.Empty).Trim();
        return value.Equals(LootPuppetType, StringComparison.OrdinalIgnoreCase) ||
               value.Equals("Loot Puppet", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("LootPuppet", StringComparison.OrdinalIgnoreCase) ||
               value.Equals(LootPuppetAsset, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsZombieType(string? type)
    {
        var value = (type ?? string.Empty).Trim();
        return value.Equals("Puppet", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("Puppets", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("Zombie", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeEditorSpawnType(string? type, string? asset = null)
    {
        var value = (type ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return "ArmedNPC";
        }

        if (IsRandomZombieType(value))
        {
            return RandomZombieType;
        }

        if (IsLootPuppetType(value) ||
            (IsZombieType(value) && string.Equals(asset?.Trim(), LootPuppetAsset, StringComparison.OrdinalIgnoreCase)))
        {
            return LootPuppetType;
        }

        if (IsZombieType(value) && string.IsNullOrWhiteSpace(asset))
        {
            return RandomZombieType;
        }

        if (IsZombieType(value))
        {
            return "Zombie";
        }

        if (IsCustomCommandType(value))
        {
            return "Custom";
        }

        if (IsCargoDropType(value))
        {
            return "CargoDrop";
        }

        return value;
    }

    private static bool IsCustomCommandType(string? type)
    {
        var value = (type ?? string.Empty).Trim();
        return value.Equals("Custom", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("Command", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("RawCommand", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("Raw Command", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCargoDropType(string? type)
    {
        var value = (type ?? string.Empty).Trim();
        return value.Equals("CargoDrop", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("Cargo Drop", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("ScheduleCargoDrop", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("BP_CargoDropEvent", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class LootPackEditorViewModel : ObservableObject
{
    private string _name = "LootPack";
    private bool _enabled = true;
    private int _weight = 1;
    private string _location = "";

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, string.IsNullOrWhiteSpace(value) ? "LootPack" : value);
    }

    public bool Enabled
    {
        get => _enabled;
        set => SetProperty(ref _enabled, value);
    }

    public int Weight
    {
        get => _weight;
        set => SetProperty(ref _weight, Math.Max(1, value));
    }

    public string Location
    {
        get => _location;
        set => SetProperty(ref _location, value ?? string.Empty);
    }
    public ObservableCollection<LootItemEditorViewModel> Items { get; } = new();

    public static LootPackEditorViewModel FromPack(LootPack pack)
    {
        var model = new LootPackEditorViewModel { Name = pack.Name ?? "LootPack", Enabled = pack.Enabled, Weight = Math.Max(1, pack.Weight), Location = pack.Location ?? "" };
        foreach (var item in pack.Items) model.Items.Add(LootItemEditorViewModel.FromItem(item));
        return model;
    }

    public LootPack ToPack() => new()
    {
        Name = string.IsNullOrWhiteSpace(Name) ? "LootPack" : Name.Trim(),
        Enabled = Enabled,
        Weight = Math.Max(1, Weight),
        Location = null,
        Items = Items.Select(x => x.ToItem()).ToList()
    };
}

public sealed class LootItemEditorViewModel : ObservableObject
{
    private string _item = "";
    private int _quantity = 1;
    private int _delayMs = 50;

    public string Item
    {
        get => _item;
        set => SetProperty(ref _item, value ?? string.Empty);
    }

    public int Quantity
    {
        get => _quantity;
        set => SetProperty(ref _quantity, Math.Max(1, value));
    }

    public int DelayMs
    {
        get => _delayMs;
        set => SetProperty(ref _delayMs, Math.Max(0, value));
    }

    public static LootItemEditorViewModel FromItem(LootItem item) => new() { Item = item.Item ?? "", Quantity = Math.Max(1, item.Quantity), DelayMs = item.DelayMs <= 0 ? 50 : item.DelayMs };
    public LootItem ToItem() => new() { Item = Item?.Trim() ?? "", Quantity = Math.Max(1, Quantity), DelayMs = Math.Max(0, DelayMs) };
}

public sealed class LootCommandPackEditorViewModel : ObservableObject
{
    public string Name { get; set; } = "LootCommandPack";
    public bool Enabled { get; set; } = true;
    public int Weight { get; set; } = 1;
    public string Location { get; set; } = "";
    public string Command { get; set; } = "";
    public int DelayMs { get; set; } = 50;

    public static LootCommandPackEditorViewModel FromPack(LootCommandPack pack) => new()
    {
        Name = pack.Name ?? "LootCommandPack",
        Enabled = pack.Enabled,
        Weight = Math.Max(1, pack.Weight),
        Location = pack.Location ?? "",
        Command = pack.Command ?? "",
        DelayMs = pack.DelayMs <= 0 ? 50 : pack.DelayMs
    };

    public LootCommandPack ToPack() => new()
    {
        Name = string.IsNullOrWhiteSpace(Name) ? "LootCommandPack" : Name.Trim(),
        Enabled = Enabled,
        Weight = Math.Max(1, Weight),
        Location = Location?.Trim() ?? "",
        Command = Command?.Trim() ?? "",
        DelayMs = Math.Max(0, DelayMs)
    };
}

public sealed class ScriptZoneMapItemViewModel
{
    public ScriptZoneMapItemViewModel(EventRuntime runtime)
        : this(runtime.Definition, runtime.State.ToString())
    {
    }

    public ScriptZoneMapItemViewModel(EventDefinition definition)
        : this(definition, definition.Enabled ? "Bereit" : "Deaktiviert")
    {
    }

    private ScriptZoneMapItemViewModel(EventDefinition definition, string state)
    {
        Name = definition.Name;
        State = state;
        var zone = definition.EffectiveZone;
        ZoneName = zone.Name;
        X = zone.CenterX;
        Y = zone.CenterY;
        Z = zone.CenterZ;
        Radius = zone.Radius;
    }

    public string Name { get; }
    public string State { get; }
    public string ZoneName { get; }
    public double X { get; }
    public double Y { get; }
    public double Z { get; }
    public double Radius { get; }
    public string Position => $"X {X:0} / Y {Y:0} / Z {Z:0}";
}

public sealed class RedeemCodeEditorViewModel : ObservableObject
{
    private bool _enabled = true;
    private string _code = string.Empty;
    private string _command = string.Empty;
    private string _response = string.Empty;
    private bool _executeAsChatPlayer = true;
    private int _delaySeconds;
    private int _maxUses = 1;
    private int _uses;

    public bool Enabled
    {
        get => _enabled;
        set => SetProperty(ref _enabled, value);
    }

    public string Code
    {
        get => _code;
        set
        {
            if (SetProperty(ref _code, value ?? string.Empty)) OnPropertyChanged(nameof(Summary));
        }
    }

    public string Command
    {
        get => _command;
        set
        {
            if (SetProperty(ref _command, value ?? string.Empty)) OnPropertyChanged(nameof(Summary));
        }
    }

    public string Response
    {
        get => _response;
        set => SetProperty(ref _response, value ?? string.Empty);
    }

    public bool ExecuteAsChatPlayer
    {
        get => _executeAsChatPlayer;
        set
        {
            if (SetProperty(ref _executeAsChatPlayer, value)) OnPropertyChanged(nameof(Summary));
        }
    }

    public int DelaySeconds
    {
        get => _delaySeconds;
        set => SetProperty(ref _delaySeconds, Math.Max(0, value));
    }

    public int MaxUses
    {
        get => _maxUses;
        set
        {
            if (SetProperty(ref _maxUses, Math.Max(0, value)))
            {
                OnPropertyChanged(nameof(UsageText));
                OnPropertyChanged(nameof(Summary));
            }
        }
    }

    public int Uses
    {
        get => _uses;
        set
        {
            if (SetProperty(ref _uses, Math.Max(0, value)))
            {
                OnPropertyChanged(nameof(UsageText));
                OnPropertyChanged(nameof(Summary));
            }
        }
    }

    public string UsageText => MaxUses <= 0
        ? $"{Uses} used / unlimited"
        : $"{Uses}/{MaxUses} used, {Math.Max(0, MaxUses - Uses)} left";

    public string Summary
    {
        get
        {
            var code = string.IsNullOrWhiteSpace(Code) ? "New code" : Code.Trim();
            var action = !string.IsNullOrWhiteSpace(Command) ? Command.Trim() : (!string.IsNullOrWhiteSpace(Response) ? "Send response" : "No action");
            var mode = ExecuteAsChatPlayer ? "ExecAs player" : "Direct";
            return $"{code} -> {mode}: {action} ({UsageText})";
        }
    }

    public static RedeemCodeEditorViewModel FromRule(RedeemCodeRule rule)
    {
        var command = rule.Command ?? string.Empty;
        var execAs = rule.ExecuteAsChatPlayer || command.TrimStart().StartsWith("#execas", StringComparison.OrdinalIgnoreCase);
        if (command.TrimStart().StartsWith("#execas", StringComparison.OrdinalIgnoreCase))
        {
            command = StripExecAsPrefix(command);
        }

        return new RedeemCodeEditorViewModel
        {
            Enabled = rule.Enabled,
            Code = rule.Code ?? string.Empty,
            Command = command,
            Response = rule.Response ?? string.Empty,
            ExecuteAsChatPlayer = execAs,
            DelaySeconds = Math.Max(0, rule.DelaySeconds),
            MaxUses = Math.Max(0, rule.MaxUses),
            Uses = Math.Max(0, rule.Uses)
        };
    }

    public RedeemCodeRule ToRule() => new()
    {
        Enabled = Enabled,
        Code = Code?.Trim() ?? string.Empty,
        Command = Command?.Trim() ?? string.Empty,
        Response = Response?.Trim() ?? string.Empty,
        ExecuteAsChatPlayer = ExecuteAsChatPlayer,
        DelaySeconds = Math.Max(0, DelaySeconds),
        MaxUses = Math.Max(0, MaxUses),
        Uses = Math.Max(0, Uses)
    };

    private static string StripExecAsPrefix(string command)
    {
        var parts = command.Trim().Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1) return string.Empty;
        if (parts.Length >= 2 && parts[1].StartsWith("765611", StringComparison.OrdinalIgnoreCase))
        {
            return parts.Length == 3 ? parts[2] : string.Empty;
        }

        return command.Trim()[parts[0].Length..].Trim();
    }
}

public sealed class ChatCommandRuleEditorViewModel : ObservableObject
{
    private bool _enabled = true;
    private string _trigger = string.Empty;
    private string _matchMode = "equals";
    private int _delaySeconds;
    private string _command = string.Empty;
    private string _response = string.Empty;
    private bool _executeAsChatPlayer;
    private int _cooldownSeconds = 300;
    private string _cooldownScope = "player";
    private int _globalCooldownSeconds = 10;

    public bool Enabled
    {
        get => _enabled;
        set => SetProperty(ref _enabled, value);
    }

    public string Trigger
    {
        get => _trigger;
        set
        {
            if (SetProperty(ref _trigger, value)) OnPropertyChanged(nameof(Summary));
        }
    }

    public string MatchMode
    {
        get => _matchMode;
        set
        {
            if (SetProperty(ref _matchMode, string.IsNullOrWhiteSpace(value) ? "equals" : value)) OnPropertyChanged(nameof(Summary));
        }
    }

    public int DelaySeconds
    {
        get => _delaySeconds;
        set
        {
            if (SetProperty(ref _delaySeconds, Math.Max(0, value))) OnPropertyChanged(nameof(Summary));
        }
    }

    public string Command
    {
        get => _command;
        set
        {
            if (SetProperty(ref _command, value)) OnPropertyChanged(nameof(Summary));
        }
    }

    public string Response
    {
        get => _response;
        set => SetProperty(ref _response, value);
    }

    public bool ExecuteAsChatPlayer
    {
        get => _executeAsChatPlayer;
        set
        {
            if (SetProperty(ref _executeAsChatPlayer, value)) OnPropertyChanged(nameof(Summary));
        }
    }

    public int CooldownSeconds
    {
        get => _cooldownSeconds;
        set => SetProperty(ref _cooldownSeconds, Math.Max(0, value));
    }

    public string CooldownScope
    {
        get => _cooldownScope;
        set => SetProperty(ref _cooldownScope, string.IsNullOrWhiteSpace(value) ? "player" : value);
    }

    public int GlobalCooldownSeconds
    {
        get => _globalCooldownSeconds;
        set => SetProperty(ref _globalCooldownSeconds, Math.Max(0, value));
    }

    public string Summary
    {
        get
        {
            var trigger = string.IsNullOrWhiteSpace(Trigger) ? "New trigger" : Trigger.Trim();
            var action = !string.IsNullOrWhiteSpace(Command) ? Command.Trim() : (!string.IsNullOrWhiteSpace(Response) ? "Send response" : "No action");
            var mode = ExecuteAsChatPlayer ? "ExecAs" : "Direct";
            return $"{trigger} ({MatchMode}) -> {mode} after {DelaySeconds}s: {action}";
        }
    }

    public static ChatCommandRuleEditorViewModel FromRule(ChatAutomationRule rule)
    {
        var command = rule.Command ?? string.Empty;
        var execAs = rule.ExecuteAsChatPlayer || command.TrimStart().StartsWith("#execas", StringComparison.OrdinalIgnoreCase);
        if (command.TrimStart().StartsWith("#execas", StringComparison.OrdinalIgnoreCase))
        {
            command = StripExecAsPrefix(command);
        }

        return new ChatCommandRuleEditorViewModel
        {
            Enabled = rule.Enabled,
            Trigger = rule.Trigger ?? string.Empty,
            MatchMode = string.IsNullOrWhiteSpace(rule.MatchMode) ? "equals" : rule.MatchMode,
            DelaySeconds = rule.DelaySeconds,
            Command = command,
            Response = rule.Response ?? string.Empty,
            ExecuteAsChatPlayer = execAs,
            CooldownSeconds = rule.CooldownSeconds,
            CooldownScope = string.IsNullOrWhiteSpace(rule.CooldownScope) ? "player" : rule.CooldownScope,
            GlobalCooldownSeconds = rule.GlobalCooldownSeconds
        };
    }

    public ChatAutomationRule ToRule() => new()
    {
        Enabled = Enabled,
        Trigger = Trigger?.Trim() ?? string.Empty,
        MatchMode = string.IsNullOrWhiteSpace(MatchMode) ? "equals" : MatchMode.Trim(),
        DelaySeconds = DelaySeconds,
        Command = Command?.Trim() ?? string.Empty,
        Response = Response?.Trim() ?? string.Empty,
        ExecuteAsChatPlayer = ExecuteAsChatPlayer,
        CooldownSeconds = CooldownSeconds,
        CooldownScope = string.IsNullOrWhiteSpace(CooldownScope) ? "player" : CooldownScope.Trim(),
        GlobalCooldownSeconds = GlobalCooldownSeconds,
        AutoInsertSteamIdForExecas = true,
        RequireSteamIdForExecas = ExecuteAsChatPlayer
    };

    private static string StripExecAsPrefix(string command)
    {
        var parts = command.Trim().Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1) return string.Empty;
        if (parts.Length >= 2 && parts[1].StartsWith("765611", StringComparison.OrdinalIgnoreCase))
        {
            return parts.Length == 3 ? parts[2] : string.Empty;
        }

        return command.Trim()[parts[0].Length..].Trim();
    }
}

public sealed class JoinCommandRuleEditorViewModel : ObservableObject
{
    private bool _enabled = true;
    private int _delaySeconds = 300;
    private string _command = string.Empty;
    private string _targetSteamId = string.Empty;
    private bool _executeAsJoinedPlayer = true;
    private bool _onlyOncePerSession = true;
    private int _cooldownSeconds = 300;

    public bool Enabled
    {
        get => _enabled;
        set => SetProperty(ref _enabled, value);
    }

    public int DelaySeconds
    {
        get => _delaySeconds;
        set
        {
            if (SetProperty(ref _delaySeconds, Math.Max(0, value))) OnPropertyChanged(nameof(Summary));
        }
    }

    public string Command
    {
        get => _command;
        set
        {
            if (SetProperty(ref _command, value)) OnPropertyChanged(nameof(Summary));
        }
    }

    public string TargetSteamId
    {
        get => _targetSteamId;
        set
        {
            if (SetProperty(ref _targetSteamId, value)) OnPropertyChanged(nameof(Summary));
        }
    }

    public bool ExecuteAsJoinedPlayer
    {
        get => _executeAsJoinedPlayer;
        set
        {
            if (SetProperty(ref _executeAsJoinedPlayer, value)) OnPropertyChanged(nameof(Summary));
        }
    }

    public bool OnlyOncePerSession
    {
        get => _onlyOncePerSession;
        set => SetProperty(ref _onlyOncePerSession, value);
    }

    public int CooldownSeconds
    {
        get => _cooldownSeconds;
        set => SetProperty(ref _cooldownSeconds, Math.Max(0, value));
    }

    public string Summary
    {
        get
        {
            var command = string.IsNullOrWhiteSpace(Command) ? "New command" : Command.Trim();
            var mode = ExecuteAsJoinedPlayer ? "ExecAs" : "Direct";
            var target = string.IsNullOrWhiteSpace(TargetSteamId) ? string.Empty : $" | only {TargetSteamId.Trim()}";
            return $"{mode} after {DelaySeconds}s{target}: {command}";
        }
    }

    public static JoinCommandRuleEditorViewModel FromRule(JoinAutomationRule rule)
    {
        var command = rule.Command ?? string.Empty;
        var execAs = rule.ExecuteAsJoinedPlayer || command.TrimStart().StartsWith("#execas", StringComparison.OrdinalIgnoreCase);
        if (command.TrimStart().StartsWith("#execas", StringComparison.OrdinalIgnoreCase))
        {
            command = StripExecAsPrefix(command);
        }

        return new JoinCommandRuleEditorViewModel
        {
            Enabled = rule.Enabled,
            DelaySeconds = rule.DelaySeconds,
            Command = command,
            TargetSteamId = rule.TargetSteamId ?? string.Empty,
            ExecuteAsJoinedPlayer = execAs,
            OnlyOncePerSession = rule.OnlyOncePerSession,
            CooldownSeconds = rule.CooldownSeconds
        };
    }

    public JoinAutomationRule ToRule() => new()
    {
        Enabled = Enabled,
        DelaySeconds = DelaySeconds,
        Command = Command?.Trim() ?? string.Empty,
        TargetSteamId = TargetSteamId?.Trim() ?? string.Empty,
        OnlyOncePerSession = OnlyOncePerSession,
        CooldownSeconds = CooldownSeconds,
        ExecuteAsJoinedPlayer = ExecuteAsJoinedPlayer,
        AutoInsertSteamIdForExecas = true,
        RequireSteamIdForExecas = ExecuteAsJoinedPlayer
    };

    private static string StripExecAsPrefix(string command)
    {
        var parts = command.Trim().Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1) return string.Empty;
        if (parts.Length >= 2 && parts[1].StartsWith("765611", StringComparison.OrdinalIgnoreCase))
        {
            return parts.Length == 3 ? parts[2] : string.Empty;
        }

        return command.Trim()[parts[0].Length..].Trim();
    }
}

public sealed class WeeklySquadOverviewViewModel
{
    public WeeklySquadOverviewViewModel(GgconSquadResponse squad)
    {
        Id = squad.Id.ToString(CultureInfo.InvariantCulture);
        Name = string.IsNullOrWhiteSpace(squad.Name) ? "Squad " + Id : squad.Name;
        Score = squad.Score;
        Members = squad.Members
            .OrderByDescending(x => x.Rank)
            .ThenBy(x => x.CharacterName, StringComparer.OrdinalIgnoreCase)
            .Select(x => $"{x.CharacterName} ({x.RankName}, {x.SteamId})" + (x.Online ? " - online" : ""))
            .ToList();
    }

    public string Id { get; }
    public string Name { get; }
    public double Score { get; }
    public IReadOnlyList<string> Members { get; }
    public string Header => $"{Name} | ID {Id} | Score {Score:N0} | {Members.Count} Spieler";
}

public sealed class WeeklyRewardClaimViewModel
{
    public WeeklyRewardClaimViewModel(WeeklyRewardClaim claim)
    {
        Id = claim.Id;
        TaskTitle = claim.TaskTitle;
        SquadName = claim.SquadName;
        PlayerName = claim.PlayerName;
        SteamId = claim.SteamId;
        Code = claim.Code;
        Reward = claim.RewardSummary;
        Error = claim.LastError;
        Status = claim.IsComplete
            ? "Eingeloest " + claim.ClaimedUtc?.ToLocalTime().ToString("dd.MM.yyyy HH:mm")
            : claim.ItemDeliveredUtc.HasValue || claim.MoneyDeliveredUtc.HasValue || claim.TextClaimedUtc.HasValue
                ? "Teilweise ausgezahlt"
                : claim.NotifiedUtc.HasValue ? "Code gesendet" : "Offen";
    }

    public string Id { get; }
    public string TaskTitle { get; }
    public string SquadName { get; }
    public string PlayerName { get; }
    public string SteamId { get; }
    public string Code { get; }
    public string Reward { get; }
    public string Status { get; }
    public string Error { get; }
    public string Recipient => string.IsNullOrWhiteSpace(SquadName) ? $"{PlayerName} ({SteamId})" : $"{PlayerName} ({SteamId}) | Squad: {SquadName}";
    public bool CanAcknowledge => !Status.StartsWith("Eingeloest", StringComparison.OrdinalIgnoreCase);
}

public sealed class ScriptRuntimeStatusViewModel
{
    public ScriptRuntimeStatusViewModel(EventRuntime runtime)
    {
        Name = runtime.Definition.Name;
        Mode = runtime.Definition.Mode;
        State = runtime.State.ToString();
        Group = runtime.Definition.EventGroup;
        IsRandom = runtime.Definition.IncludeInRandomizer && runtime.Definition.Mode.Equals("RandomAnnouncedZone", StringComparison.OrdinalIgnoreCase);
        CooldownUntil = runtime.CooldownUntilUtc > DateTime.MinValue ? runtime.CooldownUntilUtc.ToLocalTime().ToString("HH:mm:ss") : "";
        LastAction = runtime.LastAction;
        LastRawCommand = runtime.LastRawCommand;
        LastLootSummary = runtime.LastLootSummary;
        SpawnedLootCount = runtime.SpawnedLootCount;
        LastUpdated = runtime.LastUpdatedUtc > DateTime.MinValue ? runtime.LastUpdatedUtc.ToLocalTime().ToString("HH:mm:ss") : "";
    }

    public string Name { get; }
    public string Mode { get; }
    public string State { get; }
    public string Group { get; }
    public bool IsRandom { get; }
    public string CooldownUntil { get; }
    public string LastAction { get; }
    public string LastRawCommand { get; }
    public string LastLootSummary { get; }
    public int SpawnedLootCount { get; }
    public string LastUpdated { get; }
    public string StateLabel => State switch
    {
        nameof(EventRuntimeState.Initiated) => "Initiated",
        nameof(EventRuntimeState.Live) => "Started",
        nameof(EventRuntimeState.CleanupPending) => "Cleanup",
        nameof(EventRuntimeState.Cooldown) => "Cooldown",
        _ => "Stopped"
    };
}

public sealed class WeeklyRewardItemEditorViewModel : ObservableObject
{
    private string _item = string.Empty;
    public string Item { get => _item; set => SetProperty(ref _item, value); }

    private int _quantity = 1;
    public int Quantity { get => _quantity; set => SetProperty(ref _quantity, value); }

    private int _stackCount;
    public int StackCount { get => _stackCount; set => SetProperty(ref _stackCount, value); }
}

public sealed class WeeklyTaskEditorViewModel : ObservableObject
{
    private readonly IReadOnlyList<WeeklyCommunityTaskStatTarget> _targets;

    private bool _enabled;
    public bool Enabled { get => _enabled; set => SetProperty(ref _enabled, value); }

    private string _id = string.Empty;
    public string Id { get => _id; set => SetProperty(ref _id, value); }

    private string _type = "Daily";
    public string Type { get => _type; set { if (SetProperty(ref _type, value)) ApplyTypeDefaults(); } }

    private string _title = string.Empty;
    public string Title { get => _title; set => SetProperty(ref _title, value); }

    private string _description = string.Empty;
    public string Description { get => _description; set => SetProperty(ref _description, value); }

    private string _goalScope = "Community";
    public string GoalScope { get => _goalScope; set => SetProperty(ref _goalScope, value); }

    private WeeklyCommunityTaskStatTarget? _selectedTarget;
    public WeeklyCommunityTaskStatTarget? SelectedTarget
    {
        get => _selectedTarget;
        set
        {
            if (SetProperty(ref _selectedTarget, value) && value is not null)
            {
                StatTable = value.TableName;
                StatColumn = value.ColumnName;
                if (string.IsNullOrWhiteSpace(Title)) Title = value.DisplayName;
                if (string.IsNullOrWhiteSpace(Id)) Id = (Type.Equals("Weekly", StringComparison.OrdinalIgnoreCase) ? "weekly-" : "daily-") + value.ColumnName.Replace('_', '-');
            }
        }
    }

    private string _statTable = "survival_stats";
    public string StatTable { get => _statTable; set => SetProperty(ref _statTable, value); }

    private string _statColumn = "puppets_killed";
    public string StatColumn { get => _statColumn; set => SetProperty(ref _statColumn, value); }

    private long _target = 1000;
    public long Target { get => _target; set => SetProperty(ref _target, value); }

    private int _durationHours = 24;
    public int DurationHours
    {
        get => _durationHours;
        set
        {
            if (SetProperty(ref _durationHours, value)) OnPropertyChanged(nameof(SchedulePreview));
        }
    }

    private double _minimumParticipationPercent = 2.0;
    public double MinimumParticipationPercent { get => _minimumParticipationPercent; set => SetProperty(ref _minimumParticipationPercent, value); }

    private string _startLocalText = string.Empty;
    public string StartLocalText
    {
        get => _startLocalText;
        set
        {
            if (SetProperty(ref _startLocalText, value)) OnPropertyChanged(nameof(SchedulePreview));
        }
    }

    private string _endUtc = string.Empty;
    public string EndUtc { get => _endUtc; set => SetProperty(ref _endUtc, value); }

    private string _rewardText = string.Empty;
    public string RewardText { get => _rewardText; set => SetProperty(ref _rewardText, value); }

    private string _rewardMode = "FreeText";
    public string RewardMode { get => _rewardMode; set => SetProperty(ref _rewardMode, value); }

    private string _rewardDistribution = "PerParticipant";
    public string RewardDistribution { get => _rewardDistribution; set => SetProperty(ref _rewardDistribution, value); }

    private string _rewardItem = string.Empty;
    public string RewardItem { get => _rewardItem; set => SetProperty(ref _rewardItem, value); }

    private int _rewardItemQuantity = 1;
    public int RewardItemQuantity { get => _rewardItemQuantity; set => SetProperty(ref _rewardItemQuantity, value); }

    private int _rewardItemStackCount;
    public int RewardItemStackCount { get => _rewardItemStackCount; set => SetProperty(ref _rewardItemStackCount, value); }

    public ObservableCollection<WeeklyRewardItemEditorViewModel> RewardItems { get; } = new();

    private int _rewardMoney;
    public int RewardMoney { get => _rewardMoney; set => SetProperty(ref _rewardMoney, value); }

    private string _completedText = string.Empty;
    public string CompletedText { get => _completedText; set => SetProperty(ref _completedText, value); }

    public string SchedulePreview
    {
        get
        {
            var start = TryParseLocalStart(out var startLocal) ? startLocal.ToString("dd.MM.yyyy HH:mm") : "sofort / beim naechsten Startwert";
            var duration = DurationHours > 0 ? DurationHours + "h" : "Auto";
            return $"Start: {start} | Laufzeit: {duration}";
        }
    }

    private WeeklyTaskEditorViewModel(IReadOnlyList<WeeklyCommunityTaskStatTarget> targets)
    {
        _targets = targets;
    }

    public static WeeklyTaskEditorViewModel FromDefinition(WeeklyCommunityTaskDefinition definition, IReadOnlyList<WeeklyCommunityTaskStatTarget> targets)
    {
        var editor = new WeeklyTaskEditorViewModel(targets)
        {
            Enabled = definition.Enabled,
            Id = definition.Id,
            Type = string.IsNullOrWhiteSpace(definition.Type) ? "Weekly" : definition.Type,
            Title = definition.Title,
            Description = definition.Description,
            GoalScope = string.IsNullOrWhiteSpace(definition.GoalScope) ? "Community" : definition.GoalScope,
            StatTable = string.IsNullOrWhiteSpace(definition.StatTable) ? "survival_stats" : definition.StatTable,
            StatColumn = definition.StatColumn,
            Target = definition.Target,
            DurationHours = definition.DurationHours <= 0 ? (string.Equals(definition.Type, "Daily", StringComparison.OrdinalIgnoreCase) ? 24 : 168) : definition.DurationHours,
            MinimumParticipationPercent = definition.MinimumParticipationPercent <= 0 ? 2.0 : definition.MinimumParticipationPercent,
            EndUtc = definition.EndUtc,
            RewardText = definition.RewardText,
            RewardMode = string.IsNullOrWhiteSpace(definition.RewardMode) ? "FreeText" : definition.RewardMode,
            RewardDistribution = string.IsNullOrWhiteSpace(definition.RewardDistribution) ? "PerParticipant" : definition.RewardDistribution,
            RewardItem = definition.RewardItem,
            RewardItemQuantity = Math.Max(1, definition.RewardItemQuantity),
            RewardItemStackCount = Math.Max(0, definition.RewardItemStackCount),
            RewardMoney = Math.Max(0, definition.RewardMoney),
            CompletedText = definition.CompletedText
        };

        foreach (var item in WeeklyRewardItems.GetConfigured(definition))
        {
            editor.RewardItems.Add(new WeeklyRewardItemEditorViewModel
            {
                Item = item.Item,
                Quantity = item.Quantity,
                StackCount = item.StackCount
            });
        }
        if (editor.RewardItems.Count == 0) editor.RewardItems.Add(new WeeklyRewardItemEditorViewModel());

        var startUtc = WeeklyCommunityTaskService.GetTaskStartUtc(definition);
        editor.StartLocalText = startUtc.HasValue ? startUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : string.Empty;
        editor.SelectedTarget = targets.FirstOrDefault(x => x.TableName.Equals(editor.StatTable, StringComparison.OrdinalIgnoreCase) && x.ColumnName.Equals(editor.StatColumn, StringComparison.OrdinalIgnoreCase));
        return editor;
    }

    public WeeklyCommunityTaskDefinition ToDefinition()
    {
        var startUtc = string.Empty;
        if (TryParseLocalStart(out var startLocal))
        {
            startUtc = startLocal.ToUniversalTime().ToString("O");
        }

        var rewardItems = RewardItems
            .Where(x => !string.IsNullOrWhiteSpace(x.Item))
            .Select(x => new WeeklyRewardItemDefinition
            {
                Item = x.Item.Trim(),
                Quantity = Math.Max(1, x.Quantity),
                StackCount = Math.Max(0, x.StackCount)
            })
            .ToList();
        var firstRewardItem = rewardItems.FirstOrDefault();

        return new WeeklyCommunityTaskDefinition
        {
            Enabled = Enabled,
            Id = string.IsNullOrWhiteSpace(Id) ? Guid.NewGuid().ToString("N")[..8] : Id.Trim(),
            Type = string.IsNullOrWhiteSpace(Type) ? "Weekly" : Type.Trim(),
            Title = Title?.Trim() ?? string.Empty,
            Description = Description?.Trim() ?? string.Empty,
            GoalScope = string.IsNullOrWhiteSpace(GoalScope) ? "Community" : GoalScope.Trim(),
            StatTable = SelectedTarget?.TableName ?? StatTable,
            StatColumn = SelectedTarget?.ColumnName ?? StatColumn,
            Target = Math.Max(1, Target),
            StartUtc = startUtc,
            DurationHours = DurationHours,
            MinimumParticipationPercent = MinimumParticipationPercent,
            EndUtc = EndUtc?.Trim() ?? string.Empty,
            RewardText = RewardText?.Trim() ?? string.Empty,
            RewardMode = string.IsNullOrWhiteSpace(RewardMode) ? "FreeText" : RewardMode.Trim(),
            RewardDistribution = string.IsNullOrWhiteSpace(RewardDistribution) ? "PerParticipant" : RewardDistribution.Trim(),
            RewardItem = firstRewardItem?.Item ?? string.Empty,
            RewardItemQuantity = firstRewardItem?.Quantity ?? 1,
            RewardItemStackCount = firstRewardItem?.StackCount ?? 0,
            RewardItems = rewardItems,
            RewardMoney = Math.Max(0, RewardMoney),
            CompletedText = CompletedText?.Trim() ?? string.Empty
        };
    }

    private void ApplyTypeDefaults()
    {
        if (DurationHours <= 0 || DurationHours == 24 || DurationHours == 168)
        {
            DurationHours = Type.Equals("Daily", StringComparison.OrdinalIgnoreCase) ? 24 : 168;
        }
        OnPropertyChanged(nameof(SchedulePreview));
    }

    private bool TryParseLocalStart(out DateTime startLocal)
    {
        if (string.IsNullOrWhiteSpace(StartLocalText))
        {
            startLocal = default;
            return false;
        }

        return DateTime.TryParse(StartLocalText.Trim(), out startLocal);
    }
}

internal static class AsyncDisposableExtensions
{
    public static async Task DisposeIfNotNullAsync(this IAsyncDisposable? disposable)
    {
        if (disposable is not null) await disposable.DisposeAsync();
    }
}
