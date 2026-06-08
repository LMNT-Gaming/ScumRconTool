using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows.Input;
using ScumRconTool.Services;
using ScumRconTool.Models;
using System.IO;
using System.Diagnostics;
using System.Threading;

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
    private EventEngine? _eventEngine;
    private CancellationTokenSource? _discordStatusCts;
    private bool _chatForwarderRequested;
    private readonly SemaphoreSlim _playerScanLock = new(1, 1);
    private List<ScumPlayer> _cachedPlayers = new();
    private DateTime _cachedPlayersUtc = DateTime.MinValue;
    private readonly TimeSpan _playerCacheDuration = TimeSpan.FromSeconds(25);

    public BotSettings Settings { get; } = SettingsStore.Load();
    public ObservableCollection<string> LogLines { get; } = new();
    public ObservableCollection<ScriptFileViewModel> Scripts { get; } = new();
    public ObservableCollection<ScriptRuntimeStatusViewModel> ScriptRuntimeStatuses { get; } = new();
    public ObservableCollection<ScriptZoneMapItemViewModel> ScriptZoneMapItems { get; } = new();
    public ObservableCollection<ChatCommandRuleEditorViewModel> ChatCommandRules { get; } = new();
    public ObservableCollection<JoinCommandRuleEditorViewModel> JoinCommandRules { get; } = new();
    public IReadOnlyList<string> ChatMatchModes { get; } = new[] { "equals", "startswith", "contains", "regex" };
    public IReadOnlyList<string> ChatCooldownScopes { get; } = new[] { "player", "global" };
    public IReadOnlyList<string> ScumCommandSuggestions { get; } = ScumCommandCatalog.Commands;
    public IReadOnlyList<string> BroadcastMessageTypes { get; } = new[] { "Yellow", "White", "Cyan", "Green", "Red", "ServerMessage", "Error" };
    public IReadOnlyList<string> ScriptModes { get; } = new[] { "RandomAnnouncedZone", "SilentZone", "DirectLive" };
    public IReadOnlyList<string> LootSpawnModes { get; } = new[] { "OneTotal", "OnePerLocation" };
    public IReadOnlyList<WeeklyCommunityTaskStatTarget> WeeklyTaskStatTargets { get; } = WeeklyCommunityTaskService.AvailableStatTargets;

    public ObservableCollection<WeeklyTaskEditorViewModel> WeeklyTaskEditors { get; } = new();

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

    private string _scriptRuntimeSummary = "Script Engine nicht gestartet.";
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
        set => SetProperty(ref _scriptEditorModel, value);
    }

    private string _scriptValidation = "Noch nicht validiert.";
    public string ScriptValidation
    {
        get => _scriptValidation;
        set => SetProperty(ref _scriptValidation, value);
    }

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

    private string _weeklyTaskStatus = "Weekly Tasks nicht gestartet.";
    public string WeeklyTaskStatus
    {
        get => _weeklyTaskStatus;
        set => SetProperty(ref _weeklyTaskStatus, value);
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

    private string _autoMessageStatus = "Auto Messages nicht gestartet.";
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

    public ICommand SaveSettingsCommand { get; }
    public ICommand ConnectRconCommand { get; }
    public ICommand SendRconCommand { get; }
    public ICommand StartDiscordCommand { get; }
    public ICommand UpdatePlayerListCommand { get; }
    public ICommand ScanChatLogCommand { get; }
    public ICommand StartChatLogForwarderCommand { get; }
    public ICommand StopChatLogForwarderCommand { get; }
    public ICommand StartChatCommandsCommand { get; }
    public ICommand StopChatCommandsCommand { get; }
    public ICommand ScanChatCommandsCommand { get; }
    public ICommand InsertDefaultChatCommandsCommand { get; }
    public ICommand AddChatCommandRuleCommand { get; }
    public ICommand RemoveChatCommandRuleCommand { get; }
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
    public ICommand StartAutoMessagesCommand { get; }
    public ICommand StopAutoMessagesCommand { get; }
    public ICommand SendAutoMessageNowCommand { get; }
    public ICommand InsertDefaultAutoMessagesCommand { get; }
    public ICommand ResetAutoMessageFlowCommand { get; }
    public ICommand StartScriptsCommand { get; }
    public ICommand StopScriptsCommand { get; }
    public ICommand ScanScriptsCommand { get; }
    public ICommand RefreshScriptsCommand { get; }
    public ICommand ValidateScriptCommand { get; }
    public ICommand FormatScriptCommand { get; }
    public ICommand SaveScriptCommand { get; }
    public ICommand DuplicateScriptCommand { get; }
    public ICommand AddScriptCommandCommand { get; }
    public ICommand RemoveScriptCommandCommand { get; }
    public ICommand AddLootPackCommand { get; }
    public ICommand RemoveLootPackCommand { get; }
    public ICommand AddLootItemCommand { get; }
    public ICommand RemoveLootItemCommand { get; }
    public ICommand AddLootCommandPackCommand { get; }
    public ICommand RemoveLootCommandPackCommand { get; }
    public ICommand ClearLogCommand { get; }
    public ICommand OpenLogFolderCommand { get; }

    public MainViewModel()
    {
        SaveSettingsCommand = new RelayCommand(_ => SaveSettings());
        ConnectRconCommand = new RelayCommand(async _ => await ConnectRconAsync());
        SendRconCommand = new RelayCommand(async _ => await SendRconAsync());
        StartDiscordCommand = new RelayCommand(async _ => await StartDiscordAsync());
        UpdatePlayerListCommand = new RelayCommand(async _ => await UpdateDiscordPlayerListOnceAsync());
        ScanChatLogCommand = new RelayCommand(async _ => await ScanChatLogAsync());
        StartChatLogForwarderCommand = new RelayCommand(async _ => await StartChatLogForwarderAsync());
        StopChatLogForwarderCommand = new RelayCommand(_ => StopChatLogForwarder());
        StartChatCommandsCommand = new RelayCommand(async _ => await StartChatCommandsAsync());
        StopChatCommandsCommand = new RelayCommand(_ => StopChatCommands());
        ScanChatCommandsCommand = new RelayCommand(async _ => await ScanChatCommandsOnceAsync());
        InsertDefaultChatCommandsCommand = new RelayCommand(_ => InsertDefaultChatCommands());
        AddChatCommandRuleCommand = new RelayCommand(_ => AddChatCommandRule());
        RemoveChatCommandRuleCommand = new RelayCommand(rule => RemoveChatCommandRule(rule as ChatCommandRuleEditorViewModel));
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
        StartAutoMessagesCommand = new RelayCommand(async _ => await StartAutoMessagesAsync());
        StopAutoMessagesCommand = new RelayCommand(_ => StopAutoMessages());
        SendAutoMessageNowCommand = new RelayCommand(async _ => await SendAutoMessageNowAsync());
        InsertDefaultAutoMessagesCommand = new RelayCommand(_ => InsertDefaultAutoMessages());
        ResetAutoMessageFlowCommand = new RelayCommand(_ => ResetAutoMessageFlow());
        StartScriptsCommand = new RelayCommand(async _ => await StartScriptsAsync());
        StopScriptsCommand = new RelayCommand(_ => StopScripts());
        ScanScriptsCommand = new RelayCommand(async _ => await ScanScriptsOnceAsync());
        RefreshScriptsCommand = new RelayCommand(_ => RefreshScripts());
        ValidateScriptCommand = new RelayCommand(_ => ValidateScript());
        FormatScriptCommand = new RelayCommand(_ => FormatScript());
        SaveScriptCommand = new RelayCommand(_ => SaveScript());
        DuplicateScriptCommand = new RelayCommand(_ => DuplicateScript());
        AddScriptCommandCommand = new RelayCommand(block => AddScriptCommand(block as ScriptBlockEditorViewModel));
        RemoveScriptCommandCommand = new RelayCommand(command => RemoveScriptCommand(command as ScriptCommandEditorViewModel));
        AddLootPackCommand = new RelayCommand(_ => AddLootPack());
        RemoveLootPackCommand = new RelayCommand(pack => RemoveLootPack(pack as LootPackEditorViewModel));
        AddLootItemCommand = new RelayCommand(pack => AddLootItem(pack as LootPackEditorViewModel));
        RemoveLootItemCommand = new RelayCommand(item => RemoveLootItem(item as LootItemEditorViewModel));
        AddLootCommandPackCommand = new RelayCommand(_ => AddLootCommandPack());
        RemoveLootCommandPackCommand = new RelayCommand(pack => RemoveLootCommandPack(pack as LootCommandPackEditorViewModel));
        ClearLogCommand = new RelayCommand(_ => ClearLog());
        OpenLogFolderCommand = new RelayCommand(_ => OpenLogFolder());

        EnsureLogDirectory();
        EnsureLocalLogDirectories();
        RefreshScripts();
        LoadChatCommandRulesFromSettings();
        LoadJoinCommandRulesFromSettings();
        LoadWeeklyTaskEditorsFromSettings();
        Log("Red Raven Rcon Tool geladen. Logdatei: " + LogFilePath);
    }

    private void SaveSettings()
    {
        SyncChatCommandRulesToSettings();
        SyncJoinCommandRulesToSettings();
        SyncWeeklyTaskEditorsToSettings();
        SettingsStore.Save(Settings);
        Log("Einstellungen gespeichert.");
    }

    private async Task ConnectRconAsync()
    {
        if (_rcon is not null && !_rcon.Matches(Settings.Host, Settings.Port, Settings.Password))
        {
            // Bei geaenderten RCON-Zugangsdaten alle RCON-Nutzer zuerst stoppen,
            // damit keine alte Client-Instanz weiter pollt und ggCON mit Auth-Versuchen flutet.
            StopScripts();
            StopAutoMessages();
            StopWeeklyTasks();
            await _rcon.DisposeAsync();
            _rcon = null;
            RconConnected = false;
            Log("RCON Zugangsdaten geaendert: alte Verbindung und RCON-Services wurden sauber beendet.");
        }

        _rcon ??= new SourceRconClient(Settings.Host, Settings.Port, Settings.Password);
        await _rcon.ReconnectAsync();
        RconConnected = true;
        ClearPlayerCache();
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
        if (_discord is not null) await _discord.DisposeAsync();
        _discord = new DiscordBridgeService(Log, isReady =>
        {
            App.Current?.Dispatcher.Invoke(() =>
            {
                DiscordConnected = isReady;
                DiscordStatus = isReady ? "Online" : "Nicht bereit";
            });

            if (isReady)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        if (Settings.AutoStartDiscordStatus || Settings.AutoStartDiscordPlayerList)
                        {
                            await UpdateDiscordStatusAndPlayerListOnceAsync();
                            StartDiscordStatusLoop();
                        }

                        if ((Settings.AutoStartDiscordChatLogs || _chatForwarderRequested) && Settings.DiscordChatLogEmbedsEnabled)
                        {
                            StartChatForwarder();
                        }

                        if (Settings.AutoStartWeeklyTasks && !WeeklyTasksRunning)
                        {
                            await StartWeeklyTasksAsync(persistAutoStart: false);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log("Discord Ready-Aktion Fehler: " + ex.Message);
                        AppLogService.WriteException("DiscordReadyAction", ex);
                    }
                });
            }
            else
            {
                Log("Discord: Verbindung aktuell nicht bereit. Status-Loop bleibt aktiv und versucht beim naechsten Poll weiter.");
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
        DiscordStatus = _discord.IsReady ? "Online" : "Gestartet, nicht bereit";
        if (_discord.IsReady && (Settings.AutoStartDiscordStatus || Settings.AutoStartDiscordPlayerList))
        {
            await UpdateDiscordStatusAndPlayerListOnceAsync();
            StartDiscordStatusLoop();
        }
        Log(_discord.IsReady ? "Discord Bot verbunden." : "Discord Bot gestartet, wartet aber noch auf Ready. Details stehen im Debug Log.");

        if ((Settings.AutoStartDiscordChatLogs || _chatForwarderRequested) && Settings.DiscordChatLogEmbedsEnabled)
        {
            StartChatForwarder();
        }

        if (Settings.AutoStartWeeklyTasks && _discord.IsReady && !WeeklyTasksRunning)
        {
            await StartWeeklyTasksAsync(persistAutoStart: false);
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

        if (Settings.AutoStartDiscordStatus || Settings.AutoStartDiscordPlayerList || Settings.AutoStartDiscordChatLogs || Settings.DiscordGameBridgeEnabled || Settings.AutoStartWeeklyTasks)
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
        }

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
    }


    private void StartDiscordStatusLoop()
    {
        _discordStatusCts?.Cancel();
        _discordStatusCts = new CancellationTokenSource();
        var token = _discordStatusCts.Token;
        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(Math.Max(30, Settings.DiscordPollSeconds <= 0 ? 60 : Settings.DiscordPollSeconds)), token);
                    await UpdateDiscordStatusAndPlayerListOnceAsync(token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Log("Discord Status Update Fehler: " + ex.Message);
                    AppLogService.WriteException("DiscordStatusLoop", ex);
                }
            }
        }, token);
    }

    private async Task UpdateDiscordStatusAndPlayerListOnceAsync(CancellationToken cancellationToken = default)
    {
        if (_discord is null || !_discord.IsReady) return;

        var players = new List<ScumPlayer>();
        try
        {
            players = await FetchPlayersAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            Log("Discord: Spieler konnten nicht gelesen werden: " + ex.Message);
            AppLogService.WriteException("DiscordFetchPlayers", ex);
        }

        var maxPlayers = Settings.DiscordMaxPlayers > 0 ? Settings.DiscordMaxPlayers : 64;

        if (Settings.AutoStartDiscordStatus)
        {
            var statusText = DiscordBridgeService.FormatStatus(Settings.DiscordStatusTemplate, players.Count, maxPlayers, DateTime.Now);
            await _discord.SetStatusAsync(statusText);
            Log("Discord Status gesetzt: " + statusText);
        }

        if (Settings.AutoStartDiscordPlayerList)
        {
            if (Settings.DiscordPlayerListChannelId == 0)
            {
                Log("Discord Playerlist: Channel-ID fehlt.");
            }
            else
            {
                await _discord.SendOrUpdatePlayerListAsync(Settings.DiscordPlayerListChannelId, players, maxPlayers);
                Log($"Discord Playerlist aktualisiert: {players.Count}/{maxPlayers} Spieler, ohne Steam IDs.");

                if (Settings.AutoStartDiscordRandomEvents)
                {
                    await _discord.SendOrUpdateRandomEventsAsync(Settings.DiscordPlayerListChannelId, _eventEngine?.Events ?? Array.Empty<EventRuntime>());
                    Log("Discord Random-Events aktualisiert.");
                }
            }
        }
    }

    private async Task UpdateDiscordPlayerListOnceAsync(CancellationToken cancellationToken = default)
    {
        if (_discord is null || !_discord.IsReady) await StartDiscordAsync();
        if (_discord is null || !_discord.IsReady) return;

        var players = await FetchPlayersAsync(cancellationToken);
        var maxPlayers = Settings.DiscordMaxPlayers > 0 ? Settings.DiscordMaxPlayers : 64;
        await _discord.SendOrUpdatePlayerListAsync(Settings.DiscordPlayerListChannelId, players, maxPlayers);
        if (Settings.AutoStartDiscordRandomEvents)
        {
            await _discord.SendOrUpdateRandomEventsAsync(Settings.DiscordPlayerListChannelId, _eventEngine?.Events ?? Array.Empty<EventRuntime>());
            Log("Discord Random-Events manuell aktualisiert.");
        }
        Log($"Discord Playerlist manuell aktualisiert: {players.Count}/{maxPlayers} Spieler, ohne Steam IDs.");
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


    private async Task StartChatCommandsAsync(bool persistAutoStart = true)
    {
        EnsureLocalLogDirectories();
        SyncChatCommandRulesToSettings();

        if (persistAutoStart)
        {
            Settings.AutoStartChatCommands = true;
            SettingsStore.Save(Settings);
        }

        if (string.IsNullOrWhiteSpace(Settings.ChatAutomationRulesJson))
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
            Log);

        _chatCommands.Start(Settings);
        ChatCommandsRunning = true;
        Log("Chat Commands AutoStart: " + Settings.AutoStartChatCommands);
    }

    private void StopChatCommands()
    {
        Settings.AutoStartChatCommands = false;
        SettingsStore.Save(Settings);
        _chatCommands?.Stop();
        _chatCommands = null;
        ChatCommandsRunning = false;
    }

    private async Task ScanChatCommandsOnceAsync()
    {
        EnsureLocalLogDirectories();
        SyncChatCommandRulesToSettings();
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
            Log);

        await service.ScanOnceAsync(Settings);
    }

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

    private void StopJoinCommands()
    {
        Settings.AutoStartJoinCommands = false;
        SettingsStore.Save(Settings);
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

    private void StopKillFeed()
    {
        Settings.AutoStartKillFeed = false;
        SettingsStore.Save(Settings);
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
            Log("Weekly Tasks: Discord ist noch nicht bereit, starte Bot...");
            await StartDiscordAsync();
            if (WeeklyTasksRunning) return;
        }

        _weeklyTasks?.Stop();
        _weeklyTasks = new WeeklyCommunityTaskService(new SftpLogService(Settings), Log);
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

                if (_discord is null || !_discord.IsReady) return;
                var channelId = Settings.WeeklyTaskDiscordChannelId != 0
                    ? Settings.WeeklyTaskDiscordChannelId
                    : Settings.DiscordPlayerListChannelId;

                foreach (var progress in progressList)
                {
                    await _discord.SendOrUpdateWeeklyTaskAsync(channelId, progress);
                    Log("Weekly/Daily Task Discord aktualisiert: " + FormatWeeklyTaskStatus(progress));
                }
            });

        WeeklyTasksRunning = true;
        Log("Weekly Tasks AutoStart: " + Settings.AutoStartWeeklyTasks);
    }

    private void StopWeeklyTasks()
    {
        Settings.AutoStartWeeklyTasks = false;
        SettingsStore.Save(Settings);
        _weeklyTasks?.Stop();
        _weeklyTasks = null;
        WeeklyTasksRunning = false;
        Log("Weekly Tasks deaktiviert.");
    }

    private async Task ScanWeeklyTasksOnceAsync()
    {
        EnsureLocalLogDirectories();
        var service = _weeklyTasks ?? new WeeklyCommunityTaskService(new SftpLogService(Settings), Log);
        var progresses = await service.ScanAllOnceAsync(Settings);
        if (progresses.Count == 0) return;

        SetWeeklyTaskProgresses(progresses);

        if (_discord is null || !_discord.IsReady) await StartDiscordAsync();
        if (_discord is not null && _discord.IsReady)
        {
            var channelId = Settings.WeeklyTaskDiscordChannelId != 0
                ? Settings.WeeklyTaskDiscordChannelId
                : Settings.DiscordPlayerListChannelId;
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

        if (_discord is null || !_discord.IsReady) await StartDiscordAsync();
        if (_discord is not null && _discord.IsReady)
        {
            var channelId = Settings.WeeklyTaskDiscordChannelId != 0
                ? Settings.WeeklyTaskDiscordChannelId
                : Settings.DiscordPlayerListChannelId;
            foreach (var progress in progresses)
            {
                await _discord.SendOrUpdateWeeklyTaskAsync(channelId, progress);
            }
        }

        Log("Weekly/Daily Task Startwerte neu gesetzt und Discord aktualisiert: " + WeeklyTaskStatus);
    }

    private void InsertDefaultWeeklyTask()
    {
        Settings.WeeklyTaskJson = WeeklyCommunityTaskService.BuildDefaultTaskJson();
        SettingsStore.Save(Settings);
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

        var existingIds = new HashSet<string>(WeeklyTaskEditors.Select(x => x.Id), StringComparer.OrdinalIgnoreCase);
        var recoveredCount = 0;
        foreach (var baseline in WeeklyCommunityTaskService.LoadSavedBaselines())
        {
            if (string.IsNullOrWhiteSpace(baseline.TaskId) || existingIds.Contains(baseline.TaskId)) continue;

            var recovered = WeeklyCommunityTaskService.CreateDefinitionFromBaseline(baseline);
            WeeklyTaskEditors.Add(WeeklyTaskEditorViewModel.FromDefinition(recovered, WeeklyTaskStatTargets));
            existingIds.Add(recovered.Id);
            recoveredCount++;
        }

        if (WeeklyTaskEditors.Count == 0)
        {
            WeeklyTaskEditors.Add(WeeklyTaskEditorViewModel.FromDefinition(new WeeklyCommunityTaskDefinition(), WeeklyTaskStatTargets));
        }

        SelectedWeeklyTaskEditor = WeeklyTaskEditors.FirstOrDefault();
        Log("Challenge-Planer geladen: " + WeeklyTaskEditors.Count + " Eintraege" + (recoveredCount > 0 ? " (" + recoveredCount + " aus vorhandenen Startwerten wiederhergestellt)." : "."));
    }

    private void SyncWeeklyTaskEditorsToSettings()
    {
        if (WeeklyTaskEditors.Count == 0) return;
        var definitions = WeeklyTaskEditors.Select(x => x.ToDefinition()).ToList();
        Settings.WeeklyTaskJson = JsonSerializer.Serialize(definitions, new JsonSerializerOptions { WriteIndented = true });
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

    private void ApplyWeeklyTaskEditorToJson()
    {
        SyncWeeklyTaskEditorsToSettings();
        SettingsStore.Save(Settings);
        Log("Challenge-Planer in JSON uebernommen. Eintraege: " + WeeklyTaskEditors.Count);
    }

    private void SetWeeklyTaskProgresses(IReadOnlyList<WeeklyCommunityTaskProgress> progresses)
    {
        _lastWeeklyTaskProgresses = progresses.ToList();
        WeeklyTaskProgress = _lastWeeklyTaskProgresses.FirstOrDefault();
        WeeklyTaskStatus = FormatWeeklyTaskStatus(_lastWeeklyTaskProgresses);
    }

    private static string FormatWeeklyTaskStatus(IReadOnlyList<WeeklyCommunityTaskProgress> progresses)
    {
        if (progresses.Count == 0) return "Keine aktiven Weekly/Daily Tasks.";
        return string.Join(" | ", progresses.Select(FormatWeeklyTaskStatus));
    }

    private static string FormatWeeklyTaskStatus(WeeklyCommunityTaskProgress progress)
    {
        var title = string.IsNullOrWhiteSpace(progress.Definition.Title) ? progress.Definition.Id : progress.Definition.Title;
        var kind = WeeklyCommunityTaskService.GetTaskKind(progress.Definition);
        return $"{kind} {title}: {progress.Progress:N0}/{progress.Definition.Target:N0} ({progress.Percent:0.0}%)" + (progress.IsCompleted ? " - erreicht" : "");
    }


    private async Task StartAutoMessagesAsync(bool persistAutoStart = true)
    {
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
        AutoMessageStatus = $"Auto Messages laufen alle {Math.Max(1, Settings.AutoMessagesIntervalMinutes)} Minuten.";
        Log("Auto Messages gestartet.");
    }

    private void StopAutoMessages()
    {
        Settings.AutoStartAutoMessages = false;
        SettingsStore.Save(Settings);
        _autoMessages?.Stop();
        _autoMessages = null;
        AutoMessagesRunning = false;
        AutoMessageStatus = "Auto Messages gestoppt.";
        Log("Auto Messages deaktiviert.");
    }

    private async Task SendAutoMessageNowAsync()
    {
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

        AutoMessageStatus = "Auto Message manuell gesendet/ausgefuehrt.";
    }

    private void InsertDefaultAutoMessages()
    {
        Settings.AutoMessagesFlowJson = AutoMessageFlow.BuildDefaultJson();
        SettingsStore.Save(Settings);
        OnPropertyChanged(nameof(Settings));
        Log("Auto Messages Beispiel-Flow eingefuegt.");
    }

    private void ResetAutoMessageFlow()
    {
        _autoMessages?.ResetFlow();
        AutoMessageStatus = "Auto Messages Flow auf Anfang gesetzt.";
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
            _eventEngine.Start(Settings.ScriptPollSeconds);
            ScriptEngineRunning = true;
            RefreshScriptRuntimeStatuses();
            return Task.CompletedTask;
        }

        _rcon ??= new SourceRconClient(Settings.Host, Settings.Port, Settings.Password);

        var definitions = EventDefinitionStore.Load();
        _eventEngine?.Dispose();
        _eventEngine = new EventEngine(_rcon, definitions, Log, OnScriptEngineStateChanged, Settings);
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
            _eventEngine = new EventEngine(_rcon, definitions, Log, OnScriptEngineStateChanged, Settings);
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
                ScriptRuntimeSummary = "Script Engine nicht gestartet.";
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
            ScriptRuntimeSummary = $"{initiated} initiiert, {live} gestartet/live, {cleanup} cleanup, {cooldown} cooldown.";
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

    private void StopChatLogForwarder()
    {
        _chatForwarderRequested = false;
        Settings.AutoStartDiscordChatLogs = false;
        SettingsStore.Save(Settings);
        _chatForwarder?.Stop();
        ChatLogForwarderRunning = false;
        Log("Discord Chatlog Forwarder deaktiviert.");
    }

    private void StartChatForwarder()
    {
        EnsureLocalLogDirectories();
        if (!Settings.DiscordChatLogEmbedsEnabled)
        {
            Log("Discord Chatlog Forwarder: Chatlog-Embeds sind deaktiviert.");
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

    private void AddScriptCommand(ScriptBlockEditorViewModel? block)
    {
        if (block is null) return;
        block.Commands.Add(new ScriptCommandEditorViewModel { Name = "Neuer Command", DelayMs = 50 });
        SyncStructuredScriptToJson();
    }

    private void RemoveScriptCommand(ScriptCommandEditorViewModel? command)
    {
        if (ScriptEditorModel is null || command is null) return;
        foreach (var block in ScriptEditorModel.Blocks)
        {
            if (block.Commands.Remove(command)) break;
        }
        SyncStructuredScriptToJson();
    }

    private void AddLootPack()
    {
        if (ScriptEditorModel is null) return;
        ScriptEditorModel.LootPacks.Add(new LootPackEditorViewModel { Name = "Neues LootPack", Weight = 1 });
        SyncStructuredScriptToJson();
    }

    private void RemoveLootPack(LootPackEditorViewModel? pack)
    {
        if (ScriptEditorModel is null || pack is null) return;
        ScriptEditorModel.LootPacks.Remove(pack);
        SyncStructuredScriptToJson();
    }

    private void AddLootItem(LootPackEditorViewModel? pack)
    {
        if (pack is null) return;
        pack.Items.Add(new LootItemEditorViewModel { Quantity = 1, DelayMs = 50 });
        SyncStructuredScriptToJson();
    }

    private void RemoveLootItem(LootItemEditorViewModel? item)
    {
        if (ScriptEditorModel is null || item is null) return;
        foreach (var pack in ScriptEditorModel.LootPacks)
        {
            if (pack.Items.Remove(item)) break;
        }
        SyncStructuredScriptToJson();
    }

    private void AddLootCommandPack()
    {
        if (ScriptEditorModel is null) return;
        ScriptEditorModel.LootCommandPacks.Add(new LootCommandPackEditorViewModel { Name = "Neuer LootCommand", Weight = 1, DelayMs = 50 });
        SyncStructuredScriptToJson();
    }

    private void RemoveLootCommandPack(LootCommandPackEditorViewModel? pack)
    {
        if (ScriptEditorModel is null || pack is null) return;
        ScriptEditorModel.LootCommandPacks.Remove(pack);
        SyncStructuredScriptToJson();
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
    }

    public void ValidateScript()
    {
        try
        {
            using var _ = JsonDocument.Parse(ScriptJson);
            if (ScriptEditorModel is null) LoadStructuredScriptFromJson();
            ScriptValidation = "JSON ist gueltig.";
            if (SelectedScript is not null) SelectedScript.HasErrors = false;
        }
        catch (Exception ex)
        {
            ScriptValidation = "JSON Fehler: " + ex.Message;
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
            ScriptValidation = "Formatieren fehlgeschlagen: " + ex.Message;
        }
    }

    public void SaveScript()
    {
        if (SelectedScript is null) return;
        SyncStructuredScriptToJson();
        ValidateScript();
        if (SelectedScript.HasErrors) return;
        File.WriteAllText(SelectedScript.Path, ScriptJson);
        Log("Script gespeichert: " + SelectedScript.Name);
        if (ScriptEngineRunning) Log("Hinweis: Script Engine neu starten, damit diese Aenderung aktiv wird.");
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
        _discordStatusCts?.Cancel();
        _discordStatusCts?.Dispose();
        _chatForwarder?.Stop();
        _chatCommands?.Stop();
        _joinCommands?.Stop();
        _killFeed?.Stop();
        _weeklyTasks?.Stop();
        _autoMessages?.Stop();
        _eventEngine?.Dispose();
        if (_discord is not null) await _discord.DisposeAsync();
        if (_rcon is not null) await _rcon.DisposeAsync();
        _playerScanLock.Dispose();
    }
}


public sealed class ScriptStructuredEditorViewModel : ObservableObject
{
    public string Id { get; set; } = "script";
    public string Name { get; set; } = "Script";
    public bool Enabled { get; set; } = true;
    public string Mode { get; set; } = "RandomAnnouncedZone";
    public bool IncludeInRandomizer { get; set; } = true;
    public int RandomizerEveryMinutes { get; set; } = 360;
    public int InitiatorRepeatEveryMinutes { get; set; }
    public int MaxConcurrentRandomEvents { get; set; } = 1;
    public string EventGroup { get; set; } = "";
    public int MaxConcurrentInGroup { get; set; }
    public string LootPackSpawnMode { get; set; } = "OneTotal";
    public int CleanupWhenEmptySeconds { get; set; } = 300;
    public int CooldownMinutes { get; set; } = 60;
    public string ZoneName { get; set; } = "Zone";
    public double ZoneX { get; set; }
    public double ZoneY { get; set; }
    public double ZoneZ { get; set; }
    public double ZoneRadius { get; set; } = 75000;

    public ScriptBlockEditorViewModel PreLiveCleanupBlock { get; set; } = new() { Name = "Cleanup vor Live" };
    public ScriptBlockEditorViewModel InitiatorBlock { get; set; } = new() { Name = "Initiator Block" };
    public ScriptBlockEditorViewModel LiveBlock { get; set; } = new() { Name = "Liveblock" };
    public ScriptBlockEditorViewModel EmptyBlock { get; set; } = new() { Name = "Wenn Zone leer" };
    public ScriptBlockEditorViewModel CleanupBlock { get; set; } = new() { Name = "Cleanup Block" };
    public ObservableCollection<LootPackEditorViewModel> LootPacks { get; } = new();
    public ObservableCollection<LootCommandPackEditorViewModel> LootCommandPacks { get; } = new();
    public IReadOnlyList<ScriptBlockEditorViewModel> Blocks => new[] { PreLiveCleanupBlock, InitiatorBlock, LiveBlock, EmptyBlock, CleanupBlock };

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
            EventGroup = definition.EventGroup ?? "",
            MaxConcurrentInGroup = definition.MaxConcurrentInGroup,
            LootPackSpawnMode = string.IsNullOrWhiteSpace(definition.LootPackSpawnMode) ? "OneTotal" : definition.LootPackSpawnMode,
            CleanupWhenEmptySeconds = definition.CleanupWhenEmptySeconds,
            CooldownMinutes = definition.CooldownMinutes,
            ZoneName = zone.Name ?? "Zone",
            ZoneX = zone.CenterX,
            ZoneY = zone.CenterY,
            ZoneZ = zone.CenterZ,
            ZoneRadius = zone.Radius,
            PreLiveCleanupBlock = ScriptBlockEditorViewModel.FromBlock(definition.PreLiveCleanupBlock, "Cleanup vor Live"),
            InitiatorBlock = ScriptBlockEditorViewModel.FromBlock(definition.InitiatorBlock, "Initiator Block"),
            LiveBlock = ScriptBlockEditorViewModel.FromBlock(definition.LiveBlock, "Liveblock"),
            EmptyBlock = ScriptBlockEditorViewModel.FromBlock(definition.EmptyBlock, "Wenn Zone leer"),
            CleanupBlock = ScriptBlockEditorViewModel.FromBlock(definition.CleanupBlock, "Cleanup Block")
        };

        foreach (var pack in definition.LootPacks) model.LootPacks.Add(LootPackEditorViewModel.FromPack(pack));
        foreach (var pack in definition.LootCommandPacks) model.LootCommandPacks.Add(LootCommandPackEditorViewModel.FromPack(pack));
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
        EventGroup = EventGroup?.Trim() ?? "",
        MaxConcurrentInGroup = Math.Max(0, MaxConcurrentInGroup),
        LootPackSpawnMode = string.IsNullOrWhiteSpace(LootPackSpawnMode) ? "OneTotal" : LootPackSpawnMode.Trim(),
        Zone = new EventZone { Name = string.IsNullOrWhiteSpace(ZoneName) ? "Zone" : ZoneName.Trim(), CenterX = ZoneX, CenterY = ZoneY, CenterZ = ZoneZ, Radius = Math.Max(0, ZoneRadius) },
        InitiatorBlock = InitiatorBlock.ToBlock(),
        PreLiveCleanupBlock = PreLiveCleanupBlock.ToBlock(),
        LiveBlock = LiveBlock.ToBlock(),
        EmptyBlock = EmptyBlock.ToBlock(),
        CleanupBlock = CleanupBlock.ToBlock(),
        LootPacks = LootPacks.Select(x => x.ToPack()).ToList(),
        LootCommandPacks = LootCommandPacks.Select(x => x.ToPack()).ToList(),
        CleanupWhenEmptySeconds = Math.Max(0, CleanupWhenEmptySeconds),
        CooldownMinutes = Math.Max(0, CooldownMinutes)
    };
}

public sealed class ScriptBlockEditorViewModel : ObservableObject
{
    public string Name { get; set; } = "Block";
    public bool Enabled { get; set; } = true;
    public ObservableCollection<ScriptCommandEditorViewModel> Commands { get; } = new();
    public string Summary => $"{Commands.Count} Commands";

    public static ScriptBlockEditorViewModel FromBlock(ScriptBlock? block, string fallbackName)
    {
        var model = new ScriptBlockEditorViewModel { Name = string.IsNullOrWhiteSpace(block?.Name) ? fallbackName : block!.Name, Enabled = block?.Enabled ?? true };
        foreach (var command in block?.Commands ?? new List<EventCommand>()) model.Commands.Add(ScriptCommandEditorViewModel.FromCommand(command));
        return model;
    }

    public ScriptBlock ToBlock() => new()
    {
        Name = string.IsNullOrWhiteSpace(Name) ? "Block" : Name.Trim(),
        Enabled = Enabled,
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

public sealed class LootPackEditorViewModel : ObservableObject
{
    public string Name { get; set; } = "LootPack";
    public bool Enabled { get; set; } = true;
    public int Weight { get; set; } = 1;
    public string Location { get; set; } = "";
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
        Location = Location?.Trim() ?? "",
        Items = Items.Select(x => x.ToItem()).ToList()
    };
}

public sealed class LootItemEditorViewModel : ObservableObject
{
    public string Item { get; set; } = "";
    public int Quantity { get; set; } = 1;
    public int DelayMs { get; set; } = 50;

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
            var trigger = string.IsNullOrWhiteSpace(Trigger) ? "Neuer Trigger" : Trigger.Trim();
            var action = !string.IsNullOrWhiteSpace(Command) ? Command.Trim() : (!string.IsNullOrWhiteSpace(Response) ? "Antwort senden" : "Keine Aktion");
            var mode = ExecuteAsChatPlayer ? "ExecAs" : "Direkt";
            return $"{trigger} ({MatchMode}) -> {mode} nach {DelaySeconds}s: {action}";
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
            var command = string.IsNullOrWhiteSpace(Command) ? "Neuer Command" : Command.Trim();
            var mode = ExecuteAsJoinedPlayer ? "ExecAs" : "Direkt";
            return $"{mode} nach {DelaySeconds}s: {command}";
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
        nameof(EventRuntimeState.Initiated) => "Initialisiert",
        nameof(EventRuntimeState.Live) => "Gestartet",
        nameof(EventRuntimeState.CleanupPending) => "Cleanup",
        nameof(EventRuntimeState.Cooldown) => "Cooldown",
        _ => "Gestoppt"
    };
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
            StatTable = string.IsNullOrWhiteSpace(definition.StatTable) ? "survival_stats" : definition.StatTable,
            StatColumn = definition.StatColumn,
            Target = definition.Target,
            DurationHours = definition.DurationHours <= 0 ? (string.Equals(definition.Type, "Daily", StringComparison.OrdinalIgnoreCase) ? 24 : 168) : definition.DurationHours,
            MinimumParticipationPercent = definition.MinimumParticipationPercent <= 0 ? 2.0 : definition.MinimumParticipationPercent,
            EndUtc = definition.EndUtc,
            RewardText = definition.RewardText,
            CompletedText = definition.CompletedText
        };

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

        return new WeeklyCommunityTaskDefinition
        {
            Enabled = Enabled,
            Id = string.IsNullOrWhiteSpace(Id) ? Guid.NewGuid().ToString("N")[..8] : Id.Trim(),
            Type = string.IsNullOrWhiteSpace(Type) ? "Weekly" : Type.Trim(),
            Title = Title?.Trim() ?? string.Empty,
            Description = Description?.Trim() ?? string.Empty,
            StatTable = SelectedTarget?.TableName ?? StatTable,
            StatColumn = SelectedTarget?.ColumnName ?? StatColumn,
            Target = Math.Max(1, Target),
            StartUtc = startUtc,
            DurationHours = DurationHours,
            MinimumParticipationPercent = MinimumParticipationPercent,
            EndUtc = EndUtc?.Trim() ?? string.Empty,
            RewardText = RewardText?.Trim() ?? string.Empty,
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
