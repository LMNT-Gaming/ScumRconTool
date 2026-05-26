using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Linq;
using Discord;
using System.Threading.Tasks;
using System.Windows.Forms;
using ScumRconTool.Models;
using ScumRconTool.Services;

namespace ScumRconTool;

public sealed class MainForm : Form
{
    private readonly TextBox _hostTextBox = new();
    private readonly NumericUpDown _portNumeric = new();
    private readonly TextBox _passwordTextBox = new();
    private readonly Button _connectButton = new();
    private readonly Button _disconnectButton = new();
    private readonly Label _statusLabel = new();

    private readonly TextBox _commandTextBox = new();
    private readonly Button _sendButton = new();
    private readonly Button _listPlayersButton = new();
    private readonly Button _listPlayersJsonButton = new();
    private readonly Button _serverButton = new();
    private readonly TextBox _logTextBox = new();

    private readonly DataGridView _playersGrid = new();
    private readonly Button _refreshPlayersButton = new();
    private BindingList<PlayerRow> _playerRows = new();

    private readonly ComboBox _broadcastTypeCombo = new();
    private readonly TextBox _broadcastTextBox = new();
    private readonly Button _broadcastButton = new();

    private readonly TextBox _targetSteamIdTextBox = new();
    private readonly ComboBox _messageTypeCombo = new();
    private readonly TextBox _privateMessageTextBox = new();
    private readonly Button _messagePlayerButton = new();

    private readonly TextBox _itemNameTextBox = new();
    private readonly NumericUpDown _itemQtyNumeric = new();
    private readonly Button _giveItemButton = new();

    private readonly ComboBox _spawnVerbCombo = new();
    private readonly TextBox _entityNameTextBox = new();
    private readonly Button _spawnEntityButton = new();

    private readonly TextBox _ftpHostTextBox = new();
    private readonly NumericUpDown _ftpPortNumeric = new();
    private readonly TextBox _ftpUserTextBox = new();
    private readonly TextBox _ftpPasswordTextBox = new();
    private readonly CheckBox _ftpSslCheckBox = new();
    private readonly TextBox _ftpRemoteDirTextBox = new();
    private readonly TextBox _ftpKillLogPatternTextBox = new();
    private readonly TextBox _ftpLocalDirTextBox = new();
    private readonly NumericUpDown _killPollSecondsNumeric = new();
    private readonly TextBox _killAnnounceTemplateTextBox = new();
    private readonly CheckBox _autoStartKillFeedCheckBox = new();
    private readonly Button _downloadKillLogsButton = new();
    private readonly Button _startKillFeedButton = new();
    private readonly Button _stopKillFeedButton = new();
    private readonly DataGridView _killLogGrid = new();
    private readonly BindingList<KillLogRow> _killLogRows = new();
    private System.Windows.Forms.Timer? _killFeedTimer;
    private bool _killFeedBusy;

    private readonly TextBox _discordTokenTextBox = new();
    private readonly NumericUpDown _discordPollSecondsNumeric = new();
    private readonly NumericUpDown _discordMaxPlayersNumeric = new();
    private readonly TextBox _discordStatusTemplateTextBox = new();
    private readonly Label _discordStatusLabel = new();
    private readonly CheckBox _autoStartDiscordStatusCheckBox = new();
    private readonly Button _discordUpdateNowButton = new();
    private readonly Button _startDiscordStatusButton = new();
    private readonly Button _stopDiscordStatusButton = new();
    private System.Windows.Forms.Timer? _discordStatusTimer;
    private DiscordStatusService? _discordStatusService;
    private bool _discordStatusBusy;

    private readonly ListBox _eventListBox = new();
    private readonly NumericUpDown _pollSecondsNumeric = new();
    private readonly Button _loadEventsButton = new();
    private readonly Button _startEventsButton = new();
    private readonly Button _stopEventsButton = new();
    private readonly Button _manualAnnounceButton = new();
    private readonly Button _manualScanButton = new();
    private readonly Button _saveScriptsButton = new();
    private readonly Button _validateScriptsButton = new();
    private readonly Button _openScriptsFolderButton = new();
    private readonly Button _refreshScriptStatusButton = new();
    private readonly Button _formatScriptButton = new();
    private readonly Button _newScriptButton = new();
    private readonly Button _duplicateScriptButton = new();
    private readonly Button _deleteScriptButton = new();
    private readonly TextBox _scriptsEditorTextBox = new();
    private readonly TextBox _scriptInfoTextBox = new();
    private readonly Label _eventsFileLabel = new();
    private readonly CheckBox _autoStartScriptsCheckBox = new();

    private readonly TextBox _ftpChatLogPatternTextBox = new();
    private readonly TextBox _ftpLoginLogPatternTextBox = new();
    private readonly NumericUpDown _automationPollSecondsNumeric = new();
    private readonly TextBox _chatRulesTextBox = new();
    private readonly TextBox _joinRulesTextBox = new();
    private readonly CheckBox _autoStartAutomationCheckBox = new();
    private readonly Button _startAutomationButton = new();
    private readonly Button _stopAutomationButton = new();
    private readonly Button _runAutomationOnceButton = new();
    private readonly Button _formatChatRulesButton = new();
    private readonly Button _formatJoinRulesButton = new();
    private System.Windows.Forms.Timer? _automationTimer;
    private System.Windows.Forms.Timer? _rconReconnectTimer;
    private bool _automationBusy;
    private bool _rconReconnectBusy;
    private bool _rconWatchdogEnabled;
    private string? _chatLogFile;
    private int _chatLogLineCount;
    private bool _chatLogPrimed;
    private string? _loginLogFile;
    private int _loginLogLineCount;
    private bool _loginLogPrimed;
    private readonly AutomationLimiter _chatPlayerLimiter = new();
    private readonly AutomationLimiter _chatGlobalLimiter = new();
    private readonly AutomationLimiter _joinLimiter = new();
    private readonly HashSet<string> _joinSessionKeys = new(StringComparer.OrdinalIgnoreCase);

    private const int MaxSupervisorCrashes = 10;
    private int _killFeedCrashCount;
    private int _discordStatusCrashCount;
    private int _eventEngineCrashCount;
    private bool _autoStartInProgress;
    private bool _manualShutdown;
    private System.Windows.Forms.Timer? _eventEngineSupervisorTimer;

    private SourceRconClient? _rconClient;
    private EventEngine? _eventEngine;
    private List<EventDefinition> _eventDefinitions = new();
    private string? _selectedScriptFilePath;

    public MainForm()
    {
        Text = "SCUM RCON Tool - Phase 5 - Script-Dateien";
        AutoScaleMode = AutoScaleMode.Dpi;
        Width = 1240;
        Height = 900;
        MinimumSize = new Size(1100, 780);
        StartPosition = FormStartPosition.CenterScreen;

        BuildUi();
        LoadSettingsIntoUi();
        LoadEventsIntoUi();
        UpdateConnectionState(false);
        StartRconReconnectWatchdog();
        Shown += async (_, _) => await AutoStartConfiguredServicesAsync();
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(12)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 120));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 250));
        Controls.Add(root);

        root.Controls.Add(BuildConnectionGroup(), 0, 0);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(BuildRconTab());
        tabs.TabPages.Add(BuildPlayersTab());
        tabs.TabPages.Add(BuildCommandsTab());
        tabs.TabPages.Add(BuildKillLogsTab());
        tabs.TabPages.Add(BuildDiscordTab());
        tabs.TabPages.Add(BuildAutomationTab());
        tabs.TabPages.Add(BuildEventsTab());
        root.Controls.Add(tabs, 0, 1);

        var logGroup = new GroupBox { Text = "RCON Log / Antwort", Dock = DockStyle.Fill };
        _logTextBox.Dock = DockStyle.Fill;
        _logTextBox.Multiline = true;
        _logTextBox.ScrollBars = ScrollBars.Both;
        _logTextBox.Font = new Font("Consolas", 10);
        _logTextBox.WordWrap = false;
        _logTextBox.ReadOnly = true;
        logGroup.Controls.Add(_logTextBox);
        root.Controls.Add(logGroup, 0, 2);
    }

    private Control BuildConnectionGroup()
    {
        var group = new GroupBox { Text = "RCON Verbindung", Dock = DockStyle.Fill };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 8,
            RowCount = 2,
            Padding = new Padding(12)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 50));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        group.Controls.Add(layout);

        layout.Controls.Add(new Label { Text = "Host/IP", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        _hostTextBox.Dock = DockStyle.Fill;
        layout.Controls.Add(_hostTextBox, 1, 0);

        layout.Controls.Add(new Label { Text = "Port", AutoSize = true, Anchor = AnchorStyles.Left }, 2, 0);
        _portNumeric.Minimum = 1;
        _portNumeric.Maximum = 65535;
        _portNumeric.Dock = DockStyle.Fill;
        layout.Controls.Add(_portNumeric, 3, 0);

        layout.Controls.Add(new Label { Text = "Passwort", AutoSize = true, Anchor = AnchorStyles.Left }, 4, 0);
        _passwordTextBox.UseSystemPasswordChar = true;
        _passwordTextBox.Dock = DockStyle.Fill;
        layout.Controls.Add(_passwordTextBox, 5, 0);

        _connectButton.Text = "Verbinden";
        _connectButton.Dock = DockStyle.Fill;
        _connectButton.Click += async (_, _) => await ConnectAsync();
        layout.Controls.Add(_connectButton, 6, 0);

        _disconnectButton.Text = "Trennen";
        _disconnectButton.Dock = DockStyle.Fill;
        _disconnectButton.Click += (_, _) => Disconnect();
        layout.Controls.Add(_disconnectButton, 7, 0);

        _statusLabel.Text = "Status: nicht verbunden";
        _statusLabel.AutoSize = true;
        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        layout.SetColumnSpan(_statusLabel, 8);
        layout.Controls.Add(_statusLabel, 0, 1);

        return group;
    }

    private TabPage BuildRconTab()
    {
        var page = new TabPage("RCON");
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(12) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));
        page.Controls.Add(root);

        var quickGroup = new GroupBox { Text = "Schnelltests", Dock = DockStyle.Fill };
        var quick = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12, 14, 12, 8) };
        quickGroup.Controls.Add(quick);
        root.Controls.Add(quickGroup, 0, 0);

        _listPlayersButton.Text = "#ListPlayers";
        _listPlayersButton.Width = 150;
        _listPlayersButton.Height = 34;
        _listPlayersButton.Click += async (_, _) => await SendCommandAsync(CommandRegistry.ListPlayers());
        quick.Controls.Add(_listPlayersButton);

        _listPlayersJsonButton.Text = "#ListPlayersJson";
        _listPlayersJsonButton.Width = 170;
        _listPlayersJsonButton.Height = 34;
        _listPlayersJsonButton.Click += async (_, _) => await SendCommandAsync(CommandRegistry.ListPlayersJson());
        quick.Controls.Add(_listPlayersJsonButton);

        _serverButton.Text = "#Server";
        _serverButton.Width = 120;
        _serverButton.Height = 34;
        _serverButton.Click += async (_, _) => await SendCommandAsync(CommandRegistry.Server());
        quick.Controls.Add(_serverButton);

        var commandGroup = new GroupBox { Text = "Freien Command senden", Dock = DockStyle.Fill };
        var commandLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(12, 18, 12, 12) };
        commandLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        commandLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        commandGroup.Controls.Add(commandLayout);
        root.Controls.Add(commandGroup, 0, 1);

        _commandTextBox.Dock = DockStyle.Fill;
        _commandTextBox.Text = "#ListPlayersJson";
        _commandTextBox.KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                await SendCommandAsync(_commandTextBox.Text.Trim());
            }
        };
        commandLayout.Controls.Add(_commandTextBox, 0, 0);

        _sendButton.Text = "Senden";
        _sendButton.Dock = DockStyle.Fill;
        _sendButton.Click += async (_, _) => await SendCommandAsync(_commandTextBox.Text.Trim());
        commandLayout.Controls.Add(_sendButton, 1, 0);

        return page;
    }

    private TabPage BuildPlayersTab()
    {
        var page = new TabPage("Spieler");
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(12) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        page.Controls.Add(root);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(0, 8, 0, 0) };
        _refreshPlayersButton.Text = "Spieler aktualisieren (#ListPlayersJson)";
        _refreshPlayersButton.Width = 260;
        _refreshPlayersButton.Height = 34;
        _refreshPlayersButton.Click += async (_, _) => await RefreshPlayersAsync();
        buttons.Controls.Add(_refreshPlayersButton);
        root.Controls.Add(buttons, 0, 0);

        _playersGrid.Dock = DockStyle.Fill;
        _playersGrid.ReadOnly = true;
        _playersGrid.AllowUserToAddRows = false;
        _playersGrid.AllowUserToDeleteRows = false;
        _playersGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _playersGrid.MultiSelect = false;
        _playersGrid.AutoGenerateColumns = true;
        _playersGrid.DataSource = _playerRows;
        _playersGrid.SelectionChanged += (_, _) => CopySelectedPlayerToCommandFields();
        root.Controls.Add(_playersGrid, 0, 1);

        return page;
    }

    private TabPage BuildCommandsTab()
    {
        var page = new TabPage("Commands");
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(12) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 95));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 125));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 95));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 95));
        page.Controls.Add(root);

        root.Controls.Add(BuildBroadcastGroup(), 0, 0);
        root.Controls.Add(BuildMessagePlayerGroup(), 0, 1);
        root.Controls.Add(BuildGiveItemGroup(), 0, 2);
        root.Controls.Add(BuildSpawnEntityGroup(), 0, 3);
        return page;
    }

    private Control BuildBroadcastGroup()
    {
        var group = new GroupBox { Text = "Announce / Broadcast", Dock = DockStyle.Fill };
        var layout = ThreeColumnLayout();
        group.Controls.Add(layout);
        layout.Controls.Add(new Label { Text = "Typ", Anchor = AnchorStyles.Left, AutoSize = true }, 0, 0);
        FillCombo(_broadcastTypeCombo, CommandRegistry.MessageTypes, "Yellow");
        layout.Controls.Add(_broadcastTypeCombo, 1, 0);
        _broadcastTextBox.Dock = DockStyle.Fill;
        _broadcastTextBox.Text = "NPC Gruppe in B3 gesichtet.";
        layout.Controls.Add(_broadcastTextBox, 2, 0);
        _broadcastButton.Text = "Senden";
        _broadcastButton.Dock = DockStyle.Fill;
        _broadcastButton.Click += async (_, _) => await SendCommandAsync(CommandRegistry.Broadcast(_broadcastTypeCombo.Text, _broadcastTextBox.Text));
        layout.Controls.Add(_broadcastButton, 3, 0);
        return group;
    }

    private Control BuildMessagePlayerGroup()
    {
        var group = new GroupBox { Text = "Private Nachricht an Spieler", Dock = DockStyle.Fill };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 2, Padding = new Padding(12) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        group.Controls.Add(layout);

        layout.Controls.Add(new Label { Text = "SteamID", Anchor = AnchorStyles.Left, AutoSize = true }, 0, 0);
        _targetSteamIdTextBox.Dock = DockStyle.Fill;
        layout.Controls.Add(_targetSteamIdTextBox, 1, 0);
        layout.Controls.Add(new Label { Text = "Typ", Anchor = AnchorStyles.Left, AutoSize = true }, 2, 0);
        FillCombo(_messageTypeCombo, CommandRegistry.MessageTypes, "Yellow");
        layout.Controls.Add(_messageTypeCombo, 3, 0);

        layout.Controls.Add(new Label { Text = "Text", Anchor = AnchorStyles.Left, AutoSize = true }, 0, 1);
        _privateMessageTextBox.Dock = DockStyle.Fill;
        _privateMessageTextBox.Text = "Test vom Keller-Bot";
        layout.SetColumnSpan(_privateMessageTextBox, 2);
        layout.Controls.Add(_privateMessageTextBox, 1, 1);
        _messagePlayerButton.Text = "Senden";
        _messagePlayerButton.Dock = DockStyle.Fill;
        _messagePlayerButton.Click += async (_, _) => await SendCommandAsync(CommandRegistry.MessagePlayer(_targetSteamIdTextBox.Text, _messageTypeCombo.Text, _privateMessageTextBox.Text));
        layout.Controls.Add(_messagePlayerButton, 3, 1);
        return group;
    }

    private Control BuildGiveItemGroup()
    {
        var group = new GroupBox { Text = "GiveItem", Dock = DockStyle.Fill };
        var layout = ThreeColumnLayout();
        group.Controls.Add(layout);
        layout.Controls.Add(new Label { Text = "Item", Anchor = AnchorStyles.Left, AutoSize = true }, 0, 0);
        _itemNameTextBox.Dock = DockStyle.Fill;
        _itemNameTextBox.Text = "MRE_Stew";
        layout.Controls.Add(_itemNameTextBox, 1, 0);
        _itemQtyNumeric.Minimum = 1;
        _itemQtyNumeric.Maximum = 999;
        _itemQtyNumeric.Value = 1;
        _itemQtyNumeric.Dock = DockStyle.Fill;
        layout.Controls.Add(_itemQtyNumeric, 2, 0);
        _giveItemButton.Text = "Geben an SteamID oben";
        _giveItemButton.Dock = DockStyle.Fill;
        _giveItemButton.Click += async (_, _) => await SendCommandAsync(CommandRegistry.GiveItem(_targetSteamIdTextBox.Text, _itemNameTextBox.Text, (int)_itemQtyNumeric.Value));
        layout.Controls.Add(_giveItemButton, 3, 0);
        return group;
    }

    private Control BuildSpawnEntityGroup()
    {
        var group = new GroupBox { Text = "SpawnEntity", Dock = DockStyle.Fill };
        var layout = ThreeColumnLayout();
        group.Controls.Add(layout);
        layout.Controls.Add(new Label { Text = "Typ", Anchor = AnchorStyles.Left, AutoSize = true }, 0, 0);
        FillCombo(_spawnVerbCombo, new[] { "SpawnArmedNPC", "SpawnZombie", "SpawnAnimal", "SpawnBrenner", "SpawnRazor" }, "SpawnArmedNPC");
        layout.Controls.Add(_spawnVerbCombo, 1, 0);
        _entityNameTextBox.Dock = DockStyle.Fill;
        _entityNameTextBox.Text = "BP_ArmedNPC_Soldier_01";
        layout.Controls.Add(_entityNameTextBox, 2, 0);
        _spawnEntityButton.Text = "Spawn bei SteamID oben";
        _spawnEntityButton.Dock = DockStyle.Fill;
        _spawnEntityButton.Click += async (_, _) => await SendCommandAsync(CommandRegistry.SpawnEntity(_targetSteamIdTextBox.Text, _spawnVerbCombo.Text, _entityNameTextBox.Text));
        layout.Controls.Add(_spawnEntityButton, 3, 0);
        return group;
    }

    private TabPage BuildKillLogsTab()
    {
        var page = new TabPage("Kill Logs");
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(12) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 176));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        page.Controls.Add(root);

        var ftpGroup = new GroupBox { Text = "SFTP Kill-Logs", Dock = DockStyle.Fill };
        var ftp = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 8, RowCount = 4, Padding = new Padding(12) };
        ftp.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        ftp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
        ftp.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60));
        ftp.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        ftp.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 95));
        ftp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
        ftp.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        ftp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        ftp.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        ftp.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        ftp.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        ftp.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        ftpGroup.Controls.Add(ftp);
        root.Controls.Add(ftpGroup, 0, 0);

        ftp.Controls.Add(new Label { Text = "SFTP Host", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        _ftpHostTextBox.Dock = DockStyle.Fill;
        ftp.Controls.Add(_ftpHostTextBox, 1, 0);
        ftp.Controls.Add(new Label { Text = "Port", AutoSize = true, Anchor = AnchorStyles.Left }, 2, 0);
        _ftpPortNumeric.Minimum = 1;
        _ftpPortNumeric.Maximum = 65535;
        _ftpPortNumeric.Value = 22;
        _ftpPortNumeric.Dock = DockStyle.Fill;
        ftp.Controls.Add(_ftpPortNumeric, 3, 0);
        _ftpSslCheckBox.Text = "SFTP/SSH";
        _ftpSslCheckBox.Checked = true;
        _ftpSslCheckBox.Enabled = false;
        _ftpSslCheckBox.Dock = DockStyle.Fill;
        ftp.Controls.Add(_ftpSslCheckBox, 4, 0);
        ftp.Controls.Add(new Label { Text = "User", AutoSize = true, Anchor = AnchorStyles.Left }, 6, 0);
        _ftpUserTextBox.Dock = DockStyle.Fill;
        ftp.Controls.Add(_ftpUserTextBox, 7, 0);

        ftp.Controls.Add(new Label { Text = "Passwort", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        _ftpPasswordTextBox.UseSystemPasswordChar = true;
        _ftpPasswordTextBox.Dock = DockStyle.Fill;
        ftp.Controls.Add(_ftpPasswordTextBox, 1, 1);
        ftp.Controls.Add(new Label { Text = "Remote", AutoSize = true, Anchor = AnchorStyles.Left }, 2, 1);
        _ftpRemoteDirTextBox.Dock = DockStyle.Fill;
        ftp.SetColumnSpan(_ftpRemoteDirTextBox, 3);
        ftp.Controls.Add(_ftpRemoteDirTextBox, 3, 1);
        ftp.Controls.Add(new Label { Text = "Lokal", AutoSize = true, Anchor = AnchorStyles.Left }, 6, 1);
        _ftpLocalDirTextBox.Dock = DockStyle.Fill;
        ftp.Controls.Add(_ftpLocalDirTextBox, 7, 1);

        ftp.Controls.Add(new Label { Text = "Dateimuster", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);
        _ftpKillLogPatternTextBox.Dock = DockStyle.Fill;
        ftp.SetColumnSpan(_ftpKillLogPatternTextBox, 7);
        ftp.Controls.Add(_ftpKillLogPatternTextBox, 1, 2);

        ftp.Controls.Add(new Label { Text = "Template", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 3);
        _killAnnounceTemplateTextBox.Dock = DockStyle.Fill;
        ftp.SetColumnSpan(_killAnnounceTemplateTextBox, 5);
        ftp.Controls.Add(_killAnnounceTemplateTextBox, 1, 3);
        ftp.Controls.Add(new Label { Text = "Poll sek.", AutoSize = true, Anchor = AnchorStyles.Left }, 6, 3);
        _killPollSecondsNumeric.Minimum = 10;
        _killPollSecondsNumeric.Maximum = 3600;
        _killPollSecondsNumeric.Value = 30;
        _killPollSecondsNumeric.Dock = DockStyle.Fill;
        ftp.Controls.Add(_killPollSecondsNumeric, 7, 3);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(0, 10, 0, 0), AutoScroll = true, WrapContents = true };
        root.Controls.Add(actions, 0, 1);
        _downloadKillLogsButton.Text = "Kill-Logs laden + neue announce";
        _downloadKillLogsButton.Width = 250;
        _downloadKillLogsButton.Height = 34;
        _downloadKillLogsButton.Click += async (_, _) => await DownloadAndAnnounceKillLogsAsync();
        actions.Controls.Add(_downloadKillLogsButton);
        _startKillFeedButton.Text = "Auto-Killfeed starten";
        _startKillFeedButton.Width = 180;
        _startKillFeedButton.Height = 34;
        _startKillFeedButton.Click += (_, _) => StartKillFeed();
        actions.Controls.Add(_startKillFeedButton);
        _stopKillFeedButton.Text = "Auto-Killfeed stoppen";
        _stopKillFeedButton.Width = 180;
        _stopKillFeedButton.Height = 34;
        _stopKillFeedButton.Click += (_, _) => StopKillFeed();
        actions.Controls.Add(_stopKillFeedButton);

        _autoStartKillFeedCheckBox.Text = "beim Programmstart automatisch starten";
        _autoStartKillFeedCheckBox.AutoSize = true;
        _autoStartKillFeedCheckBox.Padding = new Padding(12, 8, 0, 0);
        _autoStartKillFeedCheckBox.CheckedChanged += (_, _) => SaveSettingsFromUi();
        actions.Controls.Add(_autoStartKillFeedCheckBox);

        _killLogGrid.Dock = DockStyle.Fill;
        _killLogGrid.ReadOnly = true;
        _killLogGrid.AllowUserToAddRows = false;
        _killLogGrid.AllowUserToDeleteRows = false;
        _killLogGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _killLogGrid.AutoGenerateColumns = true;
        _killLogGrid.DataSource = _killLogRows;
        root.Controls.Add(_killLogGrid, 0, 2);

        return page;
    }


    private TabPage BuildAutomationTab()
    {
        var page = new TabPage("Automation");
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(8) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 125));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        page.Controls.Add(root);

        var settingsGroup = new GroupBox { Text = "SFTP Chat-/Login-Logs", Dock = DockStyle.Fill };
        var settings = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 6, RowCount = 2, Padding = new Padding(10) };
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        settings.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        settings.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        settingsGroup.Controls.Add(settings);
        root.Controls.Add(settingsGroup, 0, 0);

        settings.Controls.Add(new Label { Text = "Chat-Muster", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        _ftpChatLogPatternTextBox.Dock = DockStyle.Fill;
        _ftpChatLogPatternTextBox.Text = "chat_*.log";
        settings.Controls.Add(_ftpChatLogPatternTextBox, 1, 0);
        settings.Controls.Add(new Label { Text = "Login-Muster", AutoSize = true, Anchor = AnchorStyles.Left }, 2, 0);
        _ftpLoginLogPatternTextBox.Dock = DockStyle.Fill;
        _ftpLoginLogPatternTextBox.Text = "login_*.log";
        settings.Controls.Add(_ftpLoginLogPatternTextBox, 3, 0);
        settings.Controls.Add(new Label { Text = "Poll sek.", AutoSize = true, Anchor = AnchorStyles.Left }, 4, 0);
        _automationPollSecondsNumeric.Minimum = 10;
        _automationPollSecondsNumeric.Maximum = 3600;
        _automationPollSecondsNumeric.Value = 30;
        _automationPollSecondsNumeric.Dock = DockStyle.Fill;
        settings.Controls.Add(_automationPollSecondsNumeric, 5, 0);

        var info = new Label
        {
            Text = "Nutzt die SFTP-Daten aus Kill Logs. Es wird jeweils die neueste chat_*.log und login_*.log geladen und nur neue Zeilen werden verarbeitet.",
            Dock = DockStyle.Fill,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft
        };
        settings.SetColumnSpan(info, 6);
        settings.Controls.Add(info, 0, 1);

        var chatGroup = new GroupBox { Text = "Chat-Regeln JSON", Dock = DockStyle.Fill };
        _chatRulesTextBox.Dock = DockStyle.Fill;
        _chatRulesTextBox.Multiline = true;
        _chatRulesTextBox.ScrollBars = ScrollBars.Both;
        _chatRulesTextBox.WordWrap = false;
        _chatRulesTextBox.Font = new Font("Consolas", 10);
        chatGroup.Controls.Add(_chatRulesTextBox);
        root.Controls.Add(chatGroup, 0, 1);

        var joinGroup = new GroupBox { Text = "Player-Join-Regeln JSON", Dock = DockStyle.Fill };
        _joinRulesTextBox.Dock = DockStyle.Fill;
        _joinRulesTextBox.Multiline = true;
        _joinRulesTextBox.ScrollBars = ScrollBars.Both;
        _joinRulesTextBox.WordWrap = false;
        _joinRulesTextBox.Font = new Font("Consolas", 10);
        joinGroup.Controls.Add(_joinRulesTextBox);
        root.Controls.Add(joinGroup, 0, 2);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(0, 10, 0, 0), AutoScroll = true, WrapContents = true };
        root.Controls.Add(actions, 0, 3);
        _runAutomationOnceButton.Text = "Jetzt pruefen";
        _runAutomationOnceButton.Width = 140;
        _runAutomationOnceButton.Height = 34;
        _runAutomationOnceButton.Click += async (_, _) => await RunAutomationOnceAsync(processCurrentFile: true);
        actions.Controls.Add(_runAutomationOnceButton);
        _formatChatRulesButton.Text = "Chat JSON formatieren";
        _formatChatRulesButton.Width = 170;
        _formatChatRulesButton.Height = 34;
        _formatChatRulesButton.Click += (_, _) => FormatAutomationJson(_chatRulesTextBox, "Chat-Regeln");
        actions.Controls.Add(_formatChatRulesButton);
        _formatJoinRulesButton.Text = "Join JSON formatieren";
        _formatJoinRulesButton.Width = 170;
        _formatJoinRulesButton.Height = 34;
        _formatJoinRulesButton.Click += (_, _) => FormatAutomationJson(_joinRulesTextBox, "Join-Regeln");
        actions.Controls.Add(_formatJoinRulesButton);
        _startAutomationButton.Text = "Automation starten";
        _startAutomationButton.Width = 160;
        _startAutomationButton.Height = 34;
        _startAutomationButton.Click += (_, _) => StartAutomationWatcher();
        actions.Controls.Add(_startAutomationButton);
        _stopAutomationButton.Text = "Automation stoppen";
        _stopAutomationButton.Width = 160;
        _stopAutomationButton.Height = 34;
        _stopAutomationButton.Click += (_, _) => StopAutomationWatcher();
        actions.Controls.Add(_stopAutomationButton);
        _autoStartAutomationCheckBox.Text = "beim Programmstart automatisch starten";
        _autoStartAutomationCheckBox.AutoSize = true;
        _autoStartAutomationCheckBox.Padding = new Padding(12, 8, 0, 0);
        _autoStartAutomationCheckBox.CheckedChanged += (_, _) => SaveSettingsFromUi();
        actions.Controls.Add(_autoStartAutomationCheckBox);

        return page;
    }

    private TabPage BuildDiscordTab()
    {
        var page = new TabPage("Discord");
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(12) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 178));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        page.Controls.Add(root);

        var group = new GroupBox { Text = "Discord Bot Status", Dock = DockStyle.Fill };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 6, RowCount = 4, Padding = new Padding(12) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        group.Controls.Add(layout);
        root.Controls.Add(group, 0, 0);

        layout.Controls.Add(new Label { Text = "Bot Token", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        _discordTokenTextBox.UseSystemPasswordChar = true;
        _discordTokenTextBox.Dock = DockStyle.Fill;
        layout.SetColumnSpan(_discordTokenTextBox, 5);
        layout.Controls.Add(_discordTokenTextBox, 1, 0);

        layout.Controls.Add(new Label { Text = "Status-Text", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        _discordStatusTemplateTextBox.Dock = DockStyle.Fill;
        layout.SetColumnSpan(_discordStatusTemplateTextBox, 5);
        layout.Controls.Add(_discordStatusTemplateTextBox, 1, 1);

        layout.Controls.Add(new Label { Text = "Poll", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);
        _discordPollSecondsNumeric.Minimum = 15;
        _discordPollSecondsNumeric.Maximum = 3600;
        _discordPollSecondsNumeric.Value = 60;
        _discordPollSecondsNumeric.Dock = DockStyle.Fill;
        layout.Controls.Add(_discordPollSecondsNumeric, 1, 2);
        layout.Controls.Add(new Label { Text = "Sek.", AutoSize = true, Anchor = AnchorStyles.Left }, 2, 2);
        layout.Controls.Add(new Label { Text = "Max Spieler", AutoSize = true, Anchor = AnchorStyles.Left }, 3, 2);
        _discordMaxPlayersNumeric.Minimum = 1;
        _discordMaxPlayersNumeric.Maximum = 999;
        _discordMaxPlayersNumeric.Value = 20;
        _discordMaxPlayersNumeric.Dock = DockStyle.Fill;
        layout.Controls.Add(_discordMaxPlayersNumeric, 4, 2);

        _discordStatusLabel.Text = "Discord: gestoppt";
        _discordStatusLabel.AutoSize = true;
        _discordStatusLabel.Dock = DockStyle.Fill;
        _discordStatusLabel.TextAlign = ContentAlignment.MiddleLeft;
        layout.SetColumnSpan(_discordStatusLabel, 6);
        layout.Controls.Add(_discordStatusLabel, 0, 3);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(0, 10, 0, 0), WrapContents = false };
        _startDiscordStatusButton.Text = "Bot-Status starten";
        _startDiscordStatusButton.Width = 150;
        _startDiscordStatusButton.Height = 34;
        _startDiscordStatusButton.Click += async (_, _) => await StartDiscordStatusAsync();
        buttons.Controls.Add(_startDiscordStatusButton);

        _stopDiscordStatusButton.Text = "Stoppen";
        _stopDiscordStatusButton.Width = 100;
        _stopDiscordStatusButton.Height = 34;
        _stopDiscordStatusButton.Click += async (_, _) => await StopDiscordStatusAsync();
        buttons.Controls.Add(_stopDiscordStatusButton);

        _discordUpdateNowButton.Text = "Jetzt aktualisieren";
        _discordUpdateNowButton.Width = 140;
        _discordUpdateNowButton.Height = 34;
        _discordUpdateNowButton.Click += async (_, _) => await UpdateDiscordStatusOnceAsync();
        buttons.Controls.Add(_discordUpdateNowButton);

        _autoStartDiscordStatusCheckBox.Text = "beim Programmstart automatisch starten";
        _autoStartDiscordStatusCheckBox.AutoSize = true;
        _autoStartDiscordStatusCheckBox.Padding = new Padding(12, 8, 0, 0);
        _autoStartDiscordStatusCheckBox.CheckedChanged += (_, _) => SaveSettingsFromUi();
        buttons.Controls.Add(_autoStartDiscordStatusCheckBox);
        root.Controls.Add(buttons, 0, 1);

        var info = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            BackColor = SystemColors.Control,
            Text = "Der Bot setzt seine Discord-Aktivitaet anhand von #ListPlayersJson.\r\n" +
                   "Template-Platzhalter: {players}, {max}, {updated}.\r\n" +
                   "Beispiel: SCUM {players}/{max} Spieler online.\r\n" +
                   "Im Discord Developer Portal braucht der Bot ein Token; fuer den reinen Status sind keine besonderen Server-Rechte noetig."
        };
        root.Controls.Add(info, 0, 2);
        return page;
    }

    private TabPage BuildEventsTab()
    {
        var page = new TabPage("Skripte");
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, Padding = new Padding(12) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 66));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 120));
        page.Controls.Add(root);

        var top = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        top.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        top.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.SetColumnSpan(top, 2);
        root.Controls.Add(top, 0, 0);

        var topButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(0, 6, 0, 0) };
        _loadEventsButton.Text = "Skripte laden";
        _loadEventsButton.Width = 120;
        _loadEventsButton.Height = 32;
        _loadEventsButton.Click += (_, _) => LoadEventsIntoUi();
        topButtons.Controls.Add(_loadEventsButton);

        _newScriptButton.Text = "Neu";
        _newScriptButton.Width = 74;
        _newScriptButton.Height = 32;
        _newScriptButton.Click += (_, _) => NewScript();
        topButtons.Controls.Add(_newScriptButton);

        _duplicateScriptButton.Text = "Duplizieren";
        _duplicateScriptButton.Width = 105;
        _duplicateScriptButton.Height = 32;
        _duplicateScriptButton.Click += (_, _) => DuplicateSelectedScript();
        topButtons.Controls.Add(_duplicateScriptButton);

        _deleteScriptButton.Text = "Loeschen";
        _deleteScriptButton.Width = 88;
        _deleteScriptButton.Height = 32;
        _deleteScriptButton.Click += (_, _) => DeleteSelectedScript();
        topButtons.Controls.Add(_deleteScriptButton);

        _saveScriptsButton.Text = "Script speichern";
        _saveScriptsButton.Width = 125;
        _saveScriptsButton.Height = 32;
        _saveScriptsButton.Click += (_, _) => SaveScriptsFromEditor();
        topButtons.Controls.Add(_saveScriptsButton);

        _validateScriptsButton.Text = "Pruefen";
        _validateScriptsButton.Width = 82;
        _validateScriptsButton.Height = 32;
        _validateScriptsButton.Click += (_, _) => ValidateScriptsEditor(showSuccess: true);
        topButtons.Controls.Add(_validateScriptsButton);

        _formatScriptButton.Text = "Formatieren";
        _formatScriptButton.Width = 100;
        _formatScriptButton.Height = 32;
        _formatScriptButton.Click += (_, _) => FormatScriptEditor();
        topButtons.Controls.Add(_formatScriptButton);

        _openScriptsFolderButton.Text = "Ordner";
        _openScriptsFolderButton.Width = 80;
        _openScriptsFolderButton.Height = 32;
        _openScriptsFolderButton.Click += (_, _) => OpenScriptsFolder();
        topButtons.Controls.Add(_openScriptsFolderButton);
        top.Controls.Add(topButtons, 0, 0);

        var pollPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(0, 6, 0, 0) };
        pollPanel.Controls.Add(new Label { Text = "Poll", AutoSize = true, Padding = new Padding(0, 8, 0, 0) });
        _pollSecondsNumeric.Minimum = 2;
        _pollSecondsNumeric.Maximum = 3600;
        _pollSecondsNumeric.Value = 10;
        _pollSecondsNumeric.Width = 72;
        pollPanel.Controls.Add(_pollSecondsNumeric);
        pollPanel.Controls.Add(new Label { Text = "Sek.", AutoSize = true, Padding = new Padding(0, 8, 0, 0) });
        top.Controls.Add(pollPanel, 1, 0);

        _eventsFileLabel.Dock = DockStyle.Fill;
        _eventsFileLabel.TextAlign = ContentAlignment.MiddleLeft;
        _eventsFileLabel.AutoEllipsis = true;
        top.SetColumnSpan(_eventsFileLabel, 2);
        top.Controls.Add(_eventsFileLabel, 0, 1);

        var left = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 170));

        var listGroup = new GroupBox { Text = "Script-Dateien / Status", Dock = DockStyle.Fill };
        _eventListBox.Dock = DockStyle.Fill;
        _eventListBox.Font = new Font("Segoe UI", 9);
        _eventListBox.SelectedIndexChanged += (_, _) => LoadSelectedScriptIntoEditor();
        listGroup.Controls.Add(_eventListBox);
        left.Controls.Add(listGroup, 0, 0);

        var infoGroup = new GroupBox { Text = "Statusmodell", Dock = DockStyle.Fill };
        _scriptInfoTextBox.Dock = DockStyle.Fill;
        _scriptInfoTextBox.Multiline = true;
        _scriptInfoTextBox.ReadOnly = true;
        _scriptInfoTextBox.BorderStyle = BorderStyle.None;
        _scriptInfoTextBox.BackColor = SystemColors.Control;
        _scriptInfoTextBox.ScrollBars = ScrollBars.Vertical;
        _scriptInfoTextBox.Text = "Stopped -> Initiated -> Live -> CleanupPending -> Cooldown\r\n\r\nRandomAnnouncedZone:\r\nRandomizer initiiert, Live bei Zoneintritt.\r\n\r\nSilentZone:\r\nDauerhaft scharf, Live bei Zoneintritt.\r\n\r\nDirectLive:\r\nLiveBlock direkt nach InitiatorBlock.";
        infoGroup.Controls.Add(_scriptInfoTextBox);
        left.Controls.Add(infoGroup, 0, 1);
        root.Controls.Add(left, 0, 1);

        var editorGroup = new GroupBox { Text = "Ausgewaehltes Script", Dock = DockStyle.Fill };
        _scriptsEditorTextBox.Dock = DockStyle.Fill;
        _scriptsEditorTextBox.Multiline = true;
        _scriptsEditorTextBox.ScrollBars = ScrollBars.Both;
        _scriptsEditorTextBox.Font = new Font("Cascadia Mono", 10);
        _scriptsEditorTextBox.WordWrap = false;
        _scriptsEditorTextBox.AcceptsTab = true;
        editorGroup.Controls.Add(_scriptsEditorTextBox);
        root.Controls.Add(editorGroup, 1, 1);

        var bottom = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(0, 10, 0, 0), WrapContents = true };
        _startEventsButton.Text = "Engine starten";
        _startEventsButton.Width = 130;
        _startEventsButton.Height = 34;
        _startEventsButton.Click += async (_, _) => await StartEventEngineAsync();
        bottom.Controls.Add(_startEventsButton);

        _stopEventsButton.Text = "Engine stoppen";
        _stopEventsButton.Width = 130;
        _stopEventsButton.Height = 34;
        _stopEventsButton.Click += (_, _) => StopEventEngine();
        bottom.Controls.Add(_stopEventsButton);

        _manualAnnounceButton.Text = "Script initiieren";
        _manualAnnounceButton.Width = 140;
        _manualAnnounceButton.Height = 34;
        _manualAnnounceButton.Click += async (_, _) => await ManualAnnounceAsync();
        bottom.Controls.Add(_manualAnnounceButton);

        _manualScanButton.Text = "Zonen scannen";
        _manualScanButton.Width = 130;
        _manualScanButton.Height = 34;
        _manualScanButton.Click += async (_, _) => await ManualScanAsync();
        bottom.Controls.Add(_manualScanButton);

        _refreshScriptStatusButton.Text = "Status aktualisieren";
        _refreshScriptStatusButton.Width = 150;
        _refreshScriptStatusButton.Height = 34;
        _refreshScriptStatusButton.Click += (_, _) => RefreshScriptList();
        bottom.Controls.Add(_refreshScriptStatusButton);

        _autoStartScriptsCheckBox.Text = "beim Programmstart automatisch starten";
        _autoStartScriptsCheckBox.AutoSize = true;
        _autoStartScriptsCheckBox.Padding = new Padding(12, 8, 0, 0);
        _autoStartScriptsCheckBox.CheckedChanged += (_, _) => SaveSettingsFromUi();
        bottom.Controls.Add(_autoStartScriptsCheckBox);

        root.SetColumnSpan(bottom, 2);
        root.Controls.Add(bottom, 0, 2);

        return page;
    }

    private static TableLayoutPanel ThreeColumnLayout()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Padding = new Padding(12) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        return layout;
    }

    private static void FillCombo(ComboBox combo, IEnumerable<string> values, string selected)
    {
        combo.DropDownStyle = ComboBoxStyle.DropDown;
        combo.Items.Clear();
        foreach (var value in values) combo.Items.Add(value);
        combo.Text = selected;
        combo.Dock = DockStyle.Fill;
    }

    private void LoadSettingsIntoUi()
    {
        var settings = SettingsStore.Load();
        _hostTextBox.Text = settings.Host;
        _portNumeric.Value = Math.Clamp(settings.Port, 1, 65535);
        _passwordTextBox.Text = settings.Password;
        _ftpHostTextBox.Text = settings.FtpHost;
        _ftpPortNumeric.Value = Math.Clamp(settings.FtpPort == 21 ? 22 : settings.FtpPort, 1, 65535);
        _ftpUserTextBox.Text = settings.FtpUser;
        _ftpPasswordTextBox.Text = settings.FtpPassword;
        _ftpSslCheckBox.Checked = true;
        _ftpRemoteDirTextBox.Text = string.IsNullOrWhiteSpace(settings.FtpRemoteDirectory) ? "/" : settings.FtpRemoteDirectory;
        _ftpKillLogPatternTextBox.Text = string.IsNullOrWhiteSpace(settings.FtpKillLogPattern) ? "kill*.log" : settings.FtpKillLogPattern;
        _ftpLocalDirTextBox.Text = string.IsNullOrWhiteSpace(settings.FtpLocalDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScumRconTool", "KillLogs")
            : settings.FtpLocalDirectory;
        _killPollSecondsNumeric.Value = Math.Clamp(settings.KillPollSeconds, 10, 3600);
        _killAnnounceTemplateTextBox.Text = string.IsNullOrWhiteSpace(settings.KillAnnounceTemplate)
            ? "{killer} killed {victim} {weapon} {distance}"
            : settings.KillAnnounceTemplate;
        _autoStartKillFeedCheckBox.Checked = settings.AutoStartKillFeed;
        _discordTokenTextBox.Text = settings.DiscordBotToken;
        _discordPollSecondsNumeric.Value = Math.Clamp(settings.DiscordPollSeconds, 15, 3600);
        _discordMaxPlayersNumeric.Value = Math.Clamp(settings.DiscordMaxPlayers <= 0 ? 20 : settings.DiscordMaxPlayers, 1, 999);
        _discordStatusTemplateTextBox.Text = string.IsNullOrWhiteSpace(settings.DiscordStatusTemplate)
            ? "SCUM {players}/{max} Spieler online"
            : settings.DiscordStatusTemplate;
        _autoStartDiscordStatusCheckBox.Checked = settings.AutoStartDiscordStatus;
        _autoStartScriptsCheckBox.Checked = settings.AutoStartScripts;
        _pollSecondsNumeric.Value = Math.Clamp(settings.ScriptPollSeconds <= 0 ? 10 : settings.ScriptPollSeconds, 2, 3600);
        _ftpChatLogPatternTextBox.Text = string.IsNullOrWhiteSpace(settings.FtpChatLogPattern) ? "chat_*.log" : settings.FtpChatLogPattern;
        _ftpLoginLogPatternTextBox.Text = string.IsNullOrWhiteSpace(settings.FtpLoginLogPattern) ? "login_*.log" : settings.FtpLoginLogPattern;
        _automationPollSecondsNumeric.Value = Math.Clamp(settings.AutomationPollSeconds <= 0 ? 30 : settings.AutomationPollSeconds, 10, 3600);
        _autoStartAutomationCheckBox.Checked = settings.AutoStartAutomation;
        _chatRulesTextBox.Text = string.IsNullOrWhiteSpace(settings.ChatAutomationRulesJson) ? DefaultChatRulesJson() : settings.ChatAutomationRulesJson;
        _joinRulesTextBox.Text = string.IsNullOrWhiteSpace(settings.JoinAutomationRulesJson) ? DefaultJoinRulesJson() : settings.JoinAutomationRulesJson;
        UpdateDiscordStatusLabel(false, "Discord: gestoppt");
    }

    private BotSettings GetSettingsFromUi() => new()
    {
        Host = _hostTextBox.Text.Trim(),
        Port = (int)_portNumeric.Value,
        Password = _passwordTextBox.Text,
        FtpHost = _ftpHostTextBox.Text.Trim(),
        FtpPort = (int)_ftpPortNumeric.Value,
        FtpUser = _ftpUserTextBox.Text.Trim(),
        FtpPassword = _ftpPasswordTextBox.Text,
        FtpUseSsl = false,
        FtpRemoteDirectory = string.IsNullOrWhiteSpace(_ftpRemoteDirTextBox.Text) ? "/" : _ftpRemoteDirTextBox.Text.Trim(),
        FtpKillLogPattern = string.IsNullOrWhiteSpace(_ftpKillLogPatternTextBox.Text) ? "kill*.log" : _ftpKillLogPatternTextBox.Text.Trim(),
        FtpLocalDirectory = _ftpLocalDirTextBox.Text.Trim(),
        KillPollSeconds = (int)_killPollSecondsNumeric.Value,
        KillAnnounceTemplate = _killAnnounceTemplateTextBox.Text.Trim(),
        AutoStartKillFeed = _autoStartKillFeedCheckBox.Checked,
        DiscordBotToken = _discordTokenTextBox.Text.Trim(),
        DiscordPollSeconds = (int)_discordPollSecondsNumeric.Value,
        DiscordMaxPlayers = (int)_discordMaxPlayersNumeric.Value,
        DiscordStatusTemplate = _discordStatusTemplateTextBox.Text.Trim(),
        AutoStartDiscordStatus = _autoStartDiscordStatusCheckBox.Checked,
        AutoStartScripts = _autoStartScriptsCheckBox.Checked,
        ScriptPollSeconds = (int)_pollSecondsNumeric.Value,
        FtpChatLogPattern = string.IsNullOrWhiteSpace(_ftpChatLogPatternTextBox.Text) ? "chat_*.log" : _ftpChatLogPatternTextBox.Text.Trim(),
        FtpLoginLogPattern = string.IsNullOrWhiteSpace(_ftpLoginLogPatternTextBox.Text) ? "login_*.log" : _ftpLoginLogPatternTextBox.Text.Trim(),
        AutomationPollSeconds = (int)_automationPollSecondsNumeric.Value,
        AutoStartAutomation = _autoStartAutomationCheckBox.Checked,
        ChatAutomationRulesJson = _chatRulesTextBox.Text.Trim(),
        JoinAutomationRulesJson = _joinRulesTextBox.Text.Trim()
    };

    private void SaveSettingsFromUi()
    {
        try
        {
            SettingsStore.Save(GetSettingsFromUi());
        }
        catch (Exception ex)
        {
            AppendLog("FEHLER beim Speichern der Einstellungen: " + ex.Message);
        }
    }

    private async Task ConnectAsync()
    {
        var settings = GetSettingsFromUi();
        if (string.IsNullOrWhiteSpace(settings.Host))
        {
            MessageBox.Show("Bitte Host/IP eintragen.", "Fehlende Eingabe", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (string.IsNullOrWhiteSpace(settings.Password))
        {
            MessageBox.Show("Bitte RCON-Passwort eintragen.", "Fehlende Eingabe", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _manualShutdown = false;
        SetBusy(true);
        AppendLog($"Verbinde mit {settings.Host}:{settings.Port} ...");
        try
        {
            SettingsStore.Save(settings);
            _rconClient?.Close();
            _rconClient = new SourceRconClient(settings.Host, settings.Port, settings.Password);
            await _rconClient.ConnectAsync();
            AppendLog("TCP verbunden. Authentifiziere ...");
            await _rconClient.AuthenticateAsync();
            AppendLog("Authentifizierung erfolgreich.");
            _rconWatchdogEnabled = true;
            UpdateConnectionState(true);
        }
        catch (Exception ex)
        {
            AppendLog("FEHLER beim Verbinden:");
            AppendLog(ex.Message);
            _rconClient?.Close();
            _rconClient = null;
            UpdateConnectionState(false);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void Disconnect()
    {
        _manualShutdown = true;
        StopKillFeed();
        StopAutomationWatcher();
        _ = StopDiscordStatusAsync();
        StopEventEngine();
        _rconWatchdogEnabled = false;
        _rconClient?.Close();
        _rconClient = null;
        AppendLog("Verbindung getrennt.");
        UpdateConnectionState(false);
    }

    private async Task SendCommandAsync(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return;
        if (_rconClient is null)
        {
            MessageBox.Show("Nicht verbunden.", "RCON", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        SetBusy(true);
        AppendLog($"> {command}");
        try
        {
            var response = await _rconClient.SendCommandAsync(command);
            UpdateConnectionState(true);
            AppendLog(response);
        }
        catch (Exception ex)
        {
            AppendLog("FEHLER beim Senden:");
            AppendLog(ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task RefreshPlayersAsync()
    {
        if (_rconClient is null)
        {
            MessageBox.Show("Nicht verbunden.", "RCON", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        SetBusy(true);
        try
        {
            AppendLog("> #ListPlayersJson");
            var response = await _rconClient.SendCommandAsync(CommandRegistry.ListPlayersJson());
            UpdateConnectionState(true);
            AppendLog(response);
            var players = PlayerParser.ParseListPlayersJson(response);
            _playerRows = new BindingList<PlayerRow>(players.Select(PlayerRow.FromPlayer).ToList());
            _playersGrid.DataSource = _playerRows;
            AppendLog($"Spielertabelle aktualisiert: {players.Count} Spieler.");
        }
        catch (Exception ex)
        {
            AppendLog("FEHLER beim Spieler aktualisieren: " + ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void CopySelectedPlayerToCommandFields()
    {
        if (_playersGrid.CurrentRow?.DataBoundItem is PlayerRow row && !string.IsNullOrWhiteSpace(row.SteamId))
        {
            _targetSteamIdTextBox.Text = row.SteamId;
        }
    }

    private void LoadEventsIntoUi()
    {
        try
        {
            EventDefinitionStore.EnsureDefaultFile();
            var previouslySelected = _selectedScriptFilePath;
            _eventDefinitions = EventDefinitionStore.Load();
            RefreshScriptList();
            SelectScriptByPath(previouslySelected);
            if (_eventListBox.SelectedIndex < 0 && _eventDefinitions.Count > 0)
            {
                _eventListBox.SelectedIndex = 0;
            }
            _eventsFileLabel.Text = "Ordner: " + EventDefinitionStore.ScriptDirectory;
            AppendLog($"Skripte geladen: {_eventDefinitions.Count} Datei(en).");
        }
        catch (Exception ex)
        {
            AppendLog("FEHLER beim Laden der Skripte: " + ex.Message);
        }
    }

    private void RefreshScriptList()
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => RefreshScriptList());
            return;
        }

        var selectedPath = _selectedScriptFilePath;
        _eventListBox.Items.Clear();
        var runtimes = _eventEngine?.Events;
        for (var i = 0; i < _eventDefinitions.Count; i++)
        {
            var definition = _eventDefinitions[i];
            var runtime = runtimes?.FirstOrDefault(r => string.Equals(r.Definition.Id, definition.Id, StringComparison.OrdinalIgnoreCase));
            var state = runtime?.State.ToString() ?? "Stopped";
            var mode = string.IsNullOrWhiteSpace(definition.Mode) ? "RandomAnnouncedZone" : definition.Mode;
            var enabled = definition.Enabled ? "ON" : "OFF";
            var random = definition.IncludeInRandomizer ? "Random" : "Manual/Silent";
            var fileName = string.IsNullOrWhiteSpace(definition.SourceFilePath) ? "<neu>" : Path.GetFileName(definition.SourceFilePath);
            _eventListBox.Items.Add($"[{state}] [{enabled}] {definition.Name} | {mode} | {random} | {fileName}");
        }
        SelectScriptByPath(selectedPath);
    }

    private void SelectScriptByPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        for (var i = 0; i < _eventDefinitions.Count; i++)
        {
            if (string.Equals(_eventDefinitions[i].SourceFilePath, path, StringComparison.OrdinalIgnoreCase))
            {
                _eventListBox.SelectedIndex = i;
                return;
            }
        }
    }

    private void LoadSelectedScriptIntoEditor()
    {
        if (_eventListBox.SelectedIndex < 0 || _eventListBox.SelectedIndex >= _eventDefinitions.Count)
        {
            return;
        }

        var script = _eventDefinitions[_eventListBox.SelectedIndex];
        _selectedScriptFilePath = script.SourceFilePath;
        try
        {
            _scriptsEditorTextBox.Text = EventDefinitionStore.GetRawJsonFor(script);
            _eventsFileLabel.Text = "Datei: " + (_selectedScriptFilePath ?? "<neu>");
        }
        catch (Exception ex)
        {
            AppendLog("FEHLER beim Oeffnen der Script-Datei: " + ex.Message);
        }
    }

    private bool ValidateScriptsEditor(bool showSuccess)
    {
        try
        {
            _ = EventDefinitionStore.DeserializeSingle(_scriptsEditorTextBox.Text);
            if (showSuccess)
            {
                MessageBox.Show("Script-JSON ist gueltig.", "Skripte", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show("JSON ist nicht gueltig:\r\n" + ex.Message, "Skripte", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    private void FormatScriptEditor()
    {
        try
        {
            _scriptsEditorTextBox.Text = EventDefinitionStore.FormatRawJson(_scriptsEditorTextBox.Text);
            AppendLog("Script-JSON formatiert.");
        }
        catch (Exception ex)
        {
            MessageBox.Show("Formatieren fehlgeschlagen:\r\n" + ex.Message, "Skripte", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void SaveScriptsFromEditor()
    {
        if (_eventEngine is not null && _eventEngine.IsRunning)
        {
            MessageBox.Show("Bitte Engine stoppen, bevor eine Script-Datei gespeichert wird.", "Skripte", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!ValidateScriptsEditor(showSuccess: false)) return;

        try
        {
            var saved = EventDefinitionStore.SaveRawJson(_scriptsEditorTextBox.Text, _selectedScriptFilePath);
            _selectedScriptFilePath = saved.SourceFilePath;
            _eventDefinitions = EventDefinitionStore.Load();
            RefreshScriptList();
            SelectScriptByPath(_selectedScriptFilePath);
            AppendLog("Script gespeichert: " + Path.GetFileName(_selectedScriptFilePath));
        }
        catch (Exception ex)
        {
            MessageBox.Show("Speichern fehlgeschlagen:\r\n" + ex.Message, "Skripte", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void NewScript()
    {
        if (_eventEngine is not null && _eventEngine.IsRunning)
        {
            MessageBox.Show("Bitte Engine stoppen, bevor ein Script angelegt wird.", "Skripte", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            var script = EventDefinitionStore.CreateTemplate();
            var path = EventDefinitionStore.Save(script);
            _selectedScriptFilePath = path;
            _eventDefinitions = EventDefinitionStore.Load();
            RefreshScriptList();
            SelectScriptByPath(path);
            AppendLog("Neues Script angelegt: " + Path.GetFileName(path));
        }
        catch (Exception ex)
        {
            MessageBox.Show("Neues Script konnte nicht angelegt werden:\r\n" + ex.Message, "Skripte", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void DuplicateSelectedScript()
    {
        if (_eventEngine is not null && _eventEngine.IsRunning)
        {
            MessageBox.Show("Bitte Engine stoppen, bevor ein Script dupliziert wird.", "Skripte", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (_eventListBox.SelectedIndex < 0 || _eventListBox.SelectedIndex >= _eventDefinitions.Count) return;

        try
        {
            var json = EventDefinitionStore.GetRawJsonFor(_eventDefinitions[_eventListBox.SelectedIndex]);
            var copy = EventDefinitionStore.DeserializeSingle(json);
            copy.Id = copy.Id + "_copy_" + DateTime.Now.ToString("HHmmss");
            copy.Name = copy.Name + " Kopie";
            copy.SourceFilePath = null;
            var path = EventDefinitionStore.Save(copy);
            _selectedScriptFilePath = path;
            _eventDefinitions = EventDefinitionStore.Load();
            RefreshScriptList();
            SelectScriptByPath(path);
            AppendLog("Script dupliziert: " + Path.GetFileName(path));
        }
        catch (Exception ex)
        {
            MessageBox.Show("Duplizieren fehlgeschlagen:\r\n" + ex.Message, "Skripte", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void DeleteSelectedScript()
    {
        if (_eventEngine is not null && _eventEngine.IsRunning)
        {
            MessageBox.Show("Bitte Engine stoppen, bevor ein Script geloescht wird.", "Skripte", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (_eventListBox.SelectedIndex < 0 || _eventListBox.SelectedIndex >= _eventDefinitions.Count) return;

        var script = _eventDefinitions[_eventListBox.SelectedIndex];
        var result = MessageBox.Show($"Script wirklich loeschen?\r\n\r\n{script.Name}", "Skripte", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (result != DialogResult.Yes) return;

        try
        {
            EventDefinitionStore.Delete(script);
            _selectedScriptFilePath = null;
            _scriptsEditorTextBox.Clear();
            _eventDefinitions = EventDefinitionStore.Load();
            RefreshScriptList();
            if (_eventDefinitions.Count > 0) _eventListBox.SelectedIndex = 0;
            AppendLog("Script geloescht: " + script.Name);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Loeschen fehlgeschlagen:\r\n" + ex.Message, "Skripte", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OpenScriptsFolder()
    {
        try
        {
            Directory.CreateDirectory(EventDefinitionStore.ScriptDirectory);
            Process.Start(new ProcessStartInfo
            {
                FileName = EventDefinitionStore.ScriptDirectory,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            AppendLog("FEHLER beim Oeffnen des Script-Ordners: " + ex.Message);
        }
    }

    private async Task StartEventEngineAsync()
    {
        if (_rconClient is null)
        {
            MessageBox.Show("Bitte zuerst RCON verbinden.", "Events", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        SetBusy(true);
        try
        {
            AppendLog("Pruefe RCON-Verbindung vor Engine-Start ...");
            await _rconClient.EnsureConnectedAsync();
            UpdateConnectionState(true);

            StopEventEngine();
            _eventEngine = new EventEngine(_rconClient, _eventDefinitions, AppendLog, RefreshScriptList);
            _eventEngine.Start((int)_pollSecondsNumeric.Value);
            _eventEngineCrashCount = 0;
            StartEventEngineSupervisor();
        }
        catch (Exception ex)
        {
            AppendLog("FEHLER beim Engine-Start: " + ex.Message);
            RegisterSupervisorCrash("Skripte", ex);
            UpdateConnectionState(false);
            MessageBox.Show("RCON konnte nicht automatisch wieder verbunden werden:\r\n" + ex.Message, "Events", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void StopEventEngine()
    {
        _eventEngineSupervisorTimer?.Stop();
        _eventEngineSupervisorTimer?.Dispose();
        _eventEngineSupervisorTimer = null;
        _eventEngine?.Stop();
        _eventEngine?.Dispose();
        _eventEngine = null;
        RefreshScriptList();
    }

    private async Task ManualAnnounceAsync()
    {
        if (_rconClient is null)
        {
            MessageBox.Show("Bitte zuerst RCON verbinden.", "Events", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        await _rconClient.EnsureConnectedAsync();
        UpdateConnectionState(true);
        var engine = _eventEngine ?? new EventEngine(_rconClient, _eventDefinitions, AppendLog, RefreshScriptList);
        var selectedIndex = _eventListBox.SelectedIndex >= 0 ? _eventListBox.SelectedIndex : 0;
        if (engine.Events.Count == 0) return;
        await engine.ManualAnnounceAsync(engine.Events[Math.Min(selectedIndex, engine.Events.Count - 1)]);
        if (_eventEngine is null) engine.Dispose();
    }

    private async Task ManualScanAsync()
    {
        if (_rconClient is null)
        {
            MessageBox.Show("Bitte zuerst RCON verbinden.", "Events", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        await _rconClient.EnsureConnectedAsync();
        UpdateConnectionState(true);
        var engine = _eventEngine ?? new EventEngine(_rconClient, _eventDefinitions, AppendLog, RefreshScriptList);
        await engine.ManualScanAsync();
        if (_eventEngine is null) engine.Dispose();
    }

    private void StartKillFeed()
    {
        var settings = GetSettingsFromUi();
        SettingsStore.Save(settings);
        _killFeedTimer?.Stop();
        _killFeedTimer?.Dispose();
        _killFeedTimer = new System.Windows.Forms.Timer
        {
            Interval = Math.Max(10, settings.KillPollSeconds) * 1000
        };
        _killFeedTimer.Tick += async (_, _) => await DownloadAndAnnounceKillLogsAsync();
        _killFeedTimer.Start();
        _killFeedCrashCount = 0;
        AppendLog("Auto-Killfeed gestartet.");
        SetBusy(false);
    }

    private void StopKillFeed()
    {
        if (_killFeedTimer is null) return;
        _killFeedTimer.Stop();
        _killFeedTimer.Dispose();
        _killFeedTimer = null;
        AppendLog("Auto-Killfeed gestoppt.");
        SetBusy(false);
    }

    private async Task DownloadAndAnnounceKillLogsAsync()
    {
        if (_killFeedBusy) return;
        _killFeedBusy = true;
        SetBusy(true);
        try
        {
            var settings = GetSettingsFromUi();
            SettingsStore.Save(settings);
            var service = new SftpKillLogService(settings);
            AppendLog("Lade Kill-Logs per SFTP ...");
            var files = await service.DownloadKillLogsAsync();
            AppendLog($"Kill-Logs geladen: {files.Count} Datei(en).");

            var entries = files.SelectMany(KillLogParser.ParseFile)
                .OrderBy(e => e.Timestamp ?? DateTime.MinValue)
                .ThenBy(e => e.SourceFile)
                .ToList();

            var seen = new KillLogSeenStore();
            if (seen.IsEmpty && entries.Count > 0)
            {
                foreach (var entry in entries) seen.Add(entry);
                seen.Save();
                FillKillGrid(entries.TakeLast(200));
                AppendLog($"Erstlauf: {entries.Count} vorhandene Kill-Zeile(n) gemerkt, ohne Announce.");
                return;
            }

            var newEntries = entries.Where(entry => !seen.Contains(entry)).ToList();
            foreach (var entry in newEntries)
            {
                seen.Add(entry);
            }
            seen.Save();
            FillKillGrid(newEntries.Count > 0 ? newEntries.TakeLast(200) : entries.TakeLast(200));

            if (newEntries.Count == 0)
            {
                AppendLog("Keine neuen Kills gefunden.");
                return;
            }

            AppendLog($"Neue Kills: {newEntries.Count}");
            foreach (var entry in newEntries)
            {
                if (_rconClient is null)
                {
                    AppendLog("RCON nicht verbunden: Announce uebersprungen fuer " + entry.Killer + " -> " + entry.Victim);
                    continue;
                }

                var text = entry.ToAnnounceText(settings.KillAnnounceTemplate);
                if (string.IsNullOrWhiteSpace(text)) continue;
                var command = CommandRegistry.Announce("Blue", text);
                AppendLog($"> {command}");
                var response = await _rconClient.SendCommandAsync(command);
                UpdateConnectionState(true);
                if (!string.IsNullOrWhiteSpace(response)) AppendLog(response);
            }
        }
        catch (Exception ex)
        {
            AppendLog("FEHLER Kill-Logs: " + ex.Message);
            if (_killFeedTimer is not null)
            {
                _killFeedCrashCount++;
                AppendLog($"Supervisor Kill-Logs: Fehler {_killFeedCrashCount}/{MaxSupervisorCrashes}. Neustart beim naechsten Poll.");
                if (_killFeedCrashCount >= MaxSupervisorCrashes)
                {
                    AppendLog("Supervisor Kill-Logs: 10 Fehler erreicht. Auto-Killfeed wird gestoppt.");
                    StopKillFeed();
                }
            }
        }
        finally
        {
            _killFeedBusy = false;
            SetBusy(false);
        }
    }

    private void FillKillGrid(IEnumerable<KillLogEntry> entries)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => FillKillGrid(entries.ToList()));
            return;
        }
        _killLogRows.Clear();
        foreach (var entry in entries)
        {
            _killLogRows.Add(KillLogRow.FromEntry(entry));
        }
    }

    private async Task StartDiscordStatusAsync()
    {
        if (_rconClient is null)
        {
            MessageBox.Show("Bitte zuerst RCON verbinden.", "Discord", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var settings = GetSettingsFromUi();
        if (string.IsNullOrWhiteSpace(settings.DiscordBotToken))
        {
            MessageBox.Show("Bitte Discord Bot Token eintragen.", "Discord", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        SettingsStore.Save(settings);
        try
        {
            SetBusy(true);
            _discordStatusService ??= new DiscordStatusService(AppendLog);
            await _discordStatusService.StartAsync(settings.DiscordBotToken);
            _discordStatusTimer?.Stop();
            _discordStatusTimer?.Dispose();
            _discordStatusTimer = new System.Windows.Forms.Timer
            {
                Interval = Math.Max(15, settings.DiscordPollSeconds) * 1000
            };
            _discordStatusTimer.Tick += async (_, _) => await UpdateDiscordStatusOnceAsync();
            _discordStatusTimer.Start();
            UpdateDiscordStatusLabel(true, "Discord: verbunden, Status laeuft");
            AppendLog("Discord Bot-Status gestartet.");
            await UpdateDiscordStatusOnceAsync();
        }
        catch (Exception ex)
        {
            UpdateDiscordStatusLabel(false, "Discord: Fehler");
            AppendLog("FEHLER Discord-Start: " + ex.Message);
            RegisterSupervisorCrash("Discord", ex);
            await StopDiscordStatusAsync();
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task StopDiscordStatusAsync()
    {
        _discordStatusTimer?.Stop();
        _discordStatusTimer?.Dispose();
        _discordStatusTimer = null;

        if (_discordStatusService is not null)
        {
            await _discordStatusService.DisposeAsync();
            _discordStatusService = null;
        }

        UpdateDiscordStatusLabel(false, "Discord: gestoppt");
        SetBusy(false);
    }

    private async Task UpdateDiscordStatusOnceAsync()
    {
        if (_discordStatusBusy) return;
        if (_rconClient is null)
        {
            AppendLog("Discord Status: RCON nicht verbunden.");
            return;
        }
        if (_discordStatusService is null || !_discordStatusService.IsReady)
        {
            AppendLog("Discord Status: Bot nicht gestartet.");
            return;
        }

        _discordStatusBusy = true;
        SetBusy(true);
        try
        {
            var settings = GetSettingsFromUi();
            SettingsStore.Save(settings);
            AppendLog("> #ListPlayersJson fuer Discord-Status");
            var response = await _rconClient.SendCommandAsync(CommandRegistry.ListPlayersJson());
            UpdateConnectionState(true);
            var players = PlayerParser.ParsePlayerCount(response);
            var statusText = DiscordStatusService.FormatStatus(settings.DiscordStatusTemplate, players, settings.DiscordMaxPlayers, DateTime.Now);
            await _discordStatusService.SetStatusAsync(statusText, ActivityType.Playing);
            UpdateDiscordStatusLabel(true, $"Discord: {statusText}");
            AppendLog($"Discord Status gesetzt: {statusText}");
        }
        catch (Exception ex)
        {
            UpdateDiscordStatusLabel(false, "Discord: Fehler beim Update");
            AppendLog("FEHLER Discord-Status: " + ex.Message);
            if (_discordStatusTimer is not null)
            {
                _discordStatusCrashCount++;
                AppendLog($"Supervisor Discord: Fehler {_discordStatusCrashCount}/{MaxSupervisorCrashes}. Bot wird neu gestartet.");
                await StopDiscordStatusAsync();
                if (_discordStatusCrashCount >= MaxSupervisorCrashes)
                {
                    AppendLog("Supervisor Discord: 10 Fehler erreicht. Discord-Autostart wird gestoppt.");
                }
                else
                {
                    await StartDiscordStatusAsync();
                }
            }
        }
        finally
        {
            _discordStatusBusy = false;
            SetBusy(false);
        }
    }



    private static string DefaultChatRulesJson() => """
[
  {
    "enabled": true,
    "trigger": "/vote day",
    "matchMode": "equals",
    "command": "#execas #vote settimeofday 5",
    "response": "Vote fuer Tag wurde gestartet.",
    "cooldownSeconds": 300,
    "cooldownScope": "player",
    "globalCooldownSeconds": 10,
    "autoInsertSteamIdForExecas": true,
    "requireSteamIdForExecas": true
  }
]
""";

    private static string DefaultJoinRulesJson() => """
[
  {
    "enabled": true,
    "delaySeconds": 300,
    "command": "#execas #shownameplates true",
    "onlyOncePerSession": true,
    "cooldownSeconds": 300,
    "autoInsertSteamIdForExecas": true,
    "requireSteamIdForExecas": true
  }
]
""";

    private static readonly JsonSerializerOptions AutomationJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private List<ChatAutomationRule> LoadChatAutomationRules()
    {
        try
        {
            return JsonSerializer.Deserialize<List<ChatAutomationRule>>(_chatRulesTextBox.Text, AutomationJsonOptions) ?? new List<ChatAutomationRule>();
        }
        catch (Exception ex)
        {
            AppendLog("FEHLER Chat-Regeln JSON: " + ex.Message);
            return new List<ChatAutomationRule>();
        }
    }

    private List<JoinAutomationRule> LoadJoinAutomationRules()
    {
        try
        {
            return JsonSerializer.Deserialize<List<JoinAutomationRule>>(_joinRulesTextBox.Text, AutomationJsonOptions) ?? new List<JoinAutomationRule>();
        }
        catch (Exception ex)
        {
            AppendLog("FEHLER Join-Regeln JSON: " + ex.Message);
            return new List<JoinAutomationRule>();
        }
    }

    private void FormatAutomationJson(TextBox textBox, string label)
    {
        try
        {
            using var document = JsonDocument.Parse(textBox.Text);
            textBox.Text = JsonSerializer.Serialize(document.RootElement, AutomationJsonOptions);
            SaveSettingsFromUi();
            AppendLog(label + ": JSON formatiert.");
        }
        catch (Exception ex)
        {
            AppendLog("FEHLER " + label + " JSON: " + ex.Message);
        }
    }

    private void StartRconReconnectWatchdog()
    {
        _rconReconnectTimer?.Stop();
        _rconReconnectTimer?.Dispose();
        _rconReconnectTimer = new System.Windows.Forms.Timer { Interval = 5 * 60 * 1000 };
        _rconReconnectTimer.Tick += async (_, _) => await CheckRconReconnectAsync();
        _rconReconnectTimer.Start();
    }

    private async Task CheckRconReconnectAsync()
    {
        if (_rconReconnectBusy || _manualShutdown || !_rconWatchdogEnabled) return;
        _rconReconnectBusy = true;
        try
        {
            var settings = GetSettingsFromUi();
            if (string.IsNullOrWhiteSpace(settings.Host) || string.IsNullOrWhiteSpace(settings.Password)) return;
            await EnsureRconConnectedForAutomationAsync(settings, "RCON-Watchdog");
        }
        catch (Exception ex)
        {
            UpdateConnectionState(false);
            AppendLog("RCON-Watchdog: Reconnect fehlgeschlagen: " + ex.Message);
        }
        finally
        {
            _rconReconnectBusy = false;
        }
    }

    private void StartAutomationWatcher()
    {
        var settings = GetSettingsFromUi();
        SettingsStore.Save(settings);
        _automationTimer?.Stop();
        _automationTimer?.Dispose();
        _automationTimer = new System.Windows.Forms.Timer
        {
            Interval = Math.Max(10, settings.AutomationPollSeconds) * 1000
        };
        _automationTimer.Tick += async (_, _) => await RunAutomationOnceAsync();
        _automationTimer.Start();
        _chatLogPrimed = false;
        _loginLogPrimed = false;
        _chatPlayerLimiter.Clear();
        _chatGlobalLimiter.Clear();
        _joinLimiter.Clear();
        _joinSessionKeys.Clear();
        _rconWatchdogEnabled = true;
        AppendLog("Automation gestartet: Chat- und Login-Logs werden ueberwacht.");
        SetBusy(false);
    }

    private void StopAutomationWatcher()
    {
        _automationTimer?.Stop();
        _automationTimer?.Dispose();
        _automationTimer = null;
        AppendLog("Automation gestoppt.");
        SetBusy(false);
    }

    private async Task RunAutomationOnceAsync(bool processCurrentFile = false)
    {
        if (_automationBusy) return;
        _automationBusy = true;
        SetBusy(true);
        try
        {
            var settings = GetSettingsFromUi();
            SettingsStore.Save(settings);
            var service = new SftpLogService(settings);

            var chatFile = await service.DownloadLatestLogAsync(settings.FtpChatLogPattern, "ChatLogs");
            if (!string.IsNullOrWhiteSpace(chatFile))
            {
                await ProcessChatLogAsync(chatFile, LoadChatAutomationRules(), processCurrentFile);
            }
            else
            {
                AppendLog("Automation: keine Chat-Logdatei gefunden fuer Muster " + settings.FtpChatLogPattern);
            }

            var loginFile = await service.DownloadLatestLogAsync(settings.FtpLoginLogPattern, "LoginLogs");
            if (!string.IsNullOrWhiteSpace(loginFile))
            {
                await ProcessLoginLogAsync(loginFile, LoadJoinAutomationRules(), processCurrentFile);
            }
            else
            {
                AppendLog("Automation: keine Login-Logdatei gefunden fuer Muster " + settings.FtpLoginLogPattern);
            }
        }
        catch (Exception ex)
        {
            AppendLog("FEHLER Automation: " + ex.Message);
        }
        finally
        {
            _automationBusy = false;
            SetBusy(false);
        }
    }


    private static async Task<string[]> ReadScumLogLinesAsync(string file)
    {
        if (string.IsNullOrWhiteSpace(file) || !File.Exists(file)) return Array.Empty<string>();

        // SCUM server logs are UTF-16 little-endian. Detect a BOM when present,
        // but default to UTF-16 LE so logs without BOM are still parsed correctly.
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var encoding = DetectScumLogEncoding(stream) ?? Encoding.Unicode;
        stream.Position = 0;
        using var reader = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: true);
        var text = await reader.ReadToEndAsync();
        return text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
    }

    private static Encoding? DetectScumLogEncoding(Stream stream)
    {
        if (!stream.CanSeek) return null;

        var originalPosition = stream.Position;
        Span<byte> bom = stackalloc byte[4];
        var read = stream.Read(bom);
        stream.Position = originalPosition;

        if (read >= 2 && bom[0] == 0xFF && bom[1] == 0xFE) return Encoding.Unicode;
        if (read >= 2 && bom[0] == 0xFE && bom[1] == 0xFF) return Encoding.BigEndianUnicode;
        if (read >= 3 && bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF) return Encoding.UTF8;
        return null;
    }

    private async Task ProcessChatLogAsync(string file, IReadOnlyList<ChatAutomationRule> rules, bool processCurrentFile = false)
    {
        var fileName = Path.GetFileName(file);
        var lines = await ReadScumLogLinesAsync(file);
        if (!string.Equals(_chatLogFile, fileName, StringComparison.OrdinalIgnoreCase))
        {
            _chatLogFile = fileName;
            _chatLogLineCount = (_chatLogPrimed || processCurrentFile) ? 0 : lines.Length;
            _chatLogPrimed = true;
            AppendLog($"Automation Chat: neueste Datei {fileName}, Start bei Zeile {_chatLogLineCount}." + (processCurrentFile ? " Aktuelle Datei wird komplett geprueft." : string.Empty));
        }
        else if (processCurrentFile)
        {
            _chatLogLineCount = 0;
            AppendLog($"Automation Chat: {fileName} wird manuell komplett geprueft.");
        }

        var newLines = lines.Skip(Math.Min(_chatLogLineCount, lines.Length)).ToList();
        _chatLogLineCount = lines.Length;
        foreach (var line in newLines)
        {
            var message = AutomationLogParser.ParseChatLine(line);
            if (message is null) continue;
            foreach (var rule in rules.Where(r => r.Enabled))
            {
                if (!AutomationLogParser.IsMatch(rule, message.Message)) continue;
                var playerCooldown = TimeSpan.FromSeconds(Math.Max(0, rule.CooldownSeconds));
                var cooldownKey = AutomationLogParser.BuildChatCooldownKey(rule, message);
                if (!_chatPlayerLimiter.TryAcquire(cooldownKey, playerCooldown, out var remaining))
                {
                    AppendLog($"Automation Chat: Limiter aktiv fuer {rule.Trigger} / {message.PlayerName}, noch {remaining.TotalSeconds:0}s.");
                    continue;
                }

                var globalCooldown = TimeSpan.FromSeconds(Math.Max(0, rule.GlobalCooldownSeconds));
                var globalKey = AutomationLogParser.BuildGlobalChatCooldownKey(rule);
                if (!_chatGlobalLimiter.TryAcquire(globalKey, globalCooldown, out var globalRemaining))
                {
                    AppendLog($"Automation Chat: globaler Limiter aktiv fuer {rule.Trigger}, noch {globalRemaining.TotalSeconds:0}s.");
                    continue;
                }

                AppendLog($"Automation Chat: {message.PlayerName} ({message.SteamId}) schrieb {message.Message}");
                if (!AutomationLogParser.TryBuildChatCommand(rule, message, out var command, out var commandError))
                {
                    AppendLog("Automation Chat: " + commandError + " Raw: " + message.RawLine);
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(command)) await ExecuteAutomationCommandAsync(command, "Chat-Regel " + rule.Trigger);
                var response = AutomationLogParser.ApplyPlaceholders(rule.Response, message);
                if (!string.IsNullOrWhiteSpace(response)) await ExecuteAutomationResponseAsync(response, "Chat-Antwort " + rule.Trigger);
            }
        }
    }

    private async Task ProcessLoginLogAsync(string file, IReadOnlyList<JoinAutomationRule> rules, bool processCurrentFile = false)
    {
        var fileName = Path.GetFileName(file);
        var lines = await ReadScumLogLinesAsync(file);
        if (!string.Equals(_loginLogFile, fileName, StringComparison.OrdinalIgnoreCase))
        {
            _loginLogFile = fileName;
            _loginLogLineCount = (_loginLogPrimed || processCurrentFile) ? 0 : lines.Length;
            _loginLogPrimed = true;
            AppendLog($"Automation Login: neueste Datei {fileName}, Start bei Zeile {_loginLogLineCount}." + (processCurrentFile ? " Aktuelle Datei wird komplett geprueft." : string.Empty));
        }
        else if (processCurrentFile)
        {
            _loginLogLineCount = 0;
            AppendLog($"Automation Login: {fileName} wird manuell komplett geprueft.");
        }

        var newLines = lines.Skip(Math.Min(_loginLogLineCount, lines.Length)).ToList();
        _loginLogLineCount = lines.Length;
        foreach (var line in newLines)
        {
            var join = AutomationLogParser.ParseJoinLine(line);
            if (join is null) continue;
            var playerLabel = string.IsNullOrWhiteSpace(join.PlayerName) ? join.SessionKey : join.PlayerName;
            AppendLog("Automation Login: Join erkannt: " + playerLabel);
            foreach (var rule in rules.Where(r => r.Enabled && !string.IsNullOrWhiteSpace(r.Command)))
            {
                var key = join.SessionKey + "|" + rule.Command;
                if (rule.OnlyOncePerSession && !_joinSessionKeys.Add(key)) continue;
                if (!_joinLimiter.TryAcquire(key, TimeSpan.FromSeconds(Math.Max(0, rule.CooldownSeconds)), out var remaining))
                {
                    AppendLog($"Automation Login: Join-Limiter aktiv fuer {playerLabel}, noch {remaining.TotalSeconds:0}s.");
                    continue;
                }

                _ = ExecuteJoinRuleAfterDelayAsync(rule, join);
            }
        }
    }

    private async Task ExecuteJoinRuleAfterDelayAsync(JoinAutomationRule rule, PlayerJoinEvent join)
    {
        try
        {
            var delay = TimeSpan.FromSeconds(Math.Max(0, rule.DelaySeconds));
            if (delay > TimeSpan.Zero)
            {
                AppendLog($"Automation Login: Befehl fuer {join.SessionKey} in {delay.TotalSeconds:0} Sekunden geplant.");
                await Task.Delay(delay);
            }

            if (!AutomationLogParser.TryBuildJoinCommand(rule, join, out var command, out var commandError))
            {
                AppendLog("Automation Login: " + commandError + " Raw: " + join.RawLine);
                return;
            }

            if (string.IsNullOrWhiteSpace(command)) return;
            AppendLog($"Automation Login: fuehre Join-Regel fuer {join.SessionKey} aus: {command}");
            await ExecuteAutomationCommandAsync(command, "Join-Regel");
        }
        catch (Exception ex)
        {
            AppendLog("FEHLER Join-Regel: " + ex.Message);
        }
    }

    private async Task ExecuteAutomationResponseAsync(string response, string source)
    {
        var command = response.TrimStart().StartsWith("#", StringComparison.Ordinal)
            ? response.Trim()
            : CommandRegistry.Announce("Yellow", response);
        await ExecuteAutomationCommandAsync(command, source);
    }

    private async Task ExecuteAutomationCommandAsync(string command, string source)
    {
        if (string.IsNullOrWhiteSpace(command)) return;
        var settings = GetSettingsFromUi();
        await EnsureRconConnectedForAutomationAsync(settings, source);
        if (_rconClient is null) return;
        AppendLog($"> [{source}] {command}");
        var response = await _rconClient.SendCommandAsync(command);
        UpdateConnectionState(true);
        if (!string.IsNullOrWhiteSpace(response)) AppendLog(response);
    }

    private async Task EnsureRconConnectedForAutomationAsync(BotSettings settings, string source)
    {
        if (_rconClient is null)
        {
            AppendLog(source + ": RCON wird verbunden.");
            _rconClient = new SourceRconClient(settings.Host, settings.Port, settings.Password);
            await _rconClient.ConnectAsync();
            await _rconClient.AuthenticateAsync();
            _rconWatchdogEnabled = true;
            UpdateConnectionState(true);
            return;
        }

        await _rconClient.EnsureConnectedAsync();
        _rconWatchdogEnabled = true;
        UpdateConnectionState(true);
    }

    private async Task AutoStartConfiguredServicesAsync()
    {
        if (_autoStartInProgress) return;
        var settings = GetSettingsFromUi();
        if (!settings.AutoStartKillFeed && !settings.AutoStartDiscordStatus && !settings.AutoStartScripts && !settings.AutoStartAutomation) return;

        _autoStartInProgress = true;
        _manualShutdown = false;
        AppendLog("Autostart: aktivierte Funktionen werden gestartet.");
        try
        {
            var needsRcon = settings.AutoStartDiscordStatus || settings.AutoStartScripts || settings.AutoStartKillFeed || settings.AutoStartAutomation;
            if (needsRcon && (_rconClient is null || !_rconClient.IsConnected))
            {
                await ConnectAsync();
            }

            if (settings.AutoStartKillFeed)
            {
                StartKillFeed();
            }

            if (settings.AutoStartDiscordStatus)
            {
                await StartDiscordStatusAsync();
            }

            if (settings.AutoStartScripts)
            {
                await StartEventEngineAsync();
            }

            if (settings.AutoStartAutomation)
            {
                StartAutomationWatcher();
            }
        }
        finally
        {
            _autoStartInProgress = false;
        }
    }

    private void StartEventEngineSupervisor()
    {
        _eventEngineSupervisorTimer?.Stop();
        _eventEngineSupervisorTimer?.Dispose();
        _eventEngineSupervisorTimer = new System.Windows.Forms.Timer { Interval = 5000 };
        _eventEngineSupervisorTimer.Tick += async (_, _) => await CheckEventEngineSupervisorAsync();
        _eventEngineSupervisorTimer.Start();
    }

    private async Task CheckEventEngineSupervisorAsync()
    {
        if (_manualShutdown || !_autoStartScriptsCheckBox.Checked) return;
        if (_eventEngine is null || _eventEngine.IsRunning) return;

        _eventEngineCrashCount++;
        AppendLog($"Supervisor Skripte: Engine nicht aktiv. Crash {_eventEngineCrashCount}/{MaxSupervisorCrashes}.");
        _eventEngine.Dispose();
        _eventEngine = null;
        RefreshScriptList();

        if (_eventEngineCrashCount >= MaxSupervisorCrashes)
        {
            AppendLog("Supervisor Skripte: 10 Crashes erreicht. Engine wird nicht mehr neu gestartet.");
            _eventEngineSupervisorTimer?.Stop();
            return;
        }

        await EnsureRconConnectedForSupervisorAsync("Skripte");
        if (_rconClient is not null)
        {
            AppendLog("Supervisor Skripte: Engine wird neu gestartet.");
            _eventEngine = new EventEngine(_rconClient, _eventDefinitions, AppendLog, RefreshScriptList);
            _eventEngine.Start((int)_pollSecondsNumeric.Value);
        }
    }

    private async Task EnsureRconConnectedForSupervisorAsync(string serviceName)
    {
        if (_rconClient is not null && _rconClient.IsConnected) return;
        AppendLog($"Supervisor {serviceName}: RCON-Verbindung wird neu aufgebaut.");
        await ConnectAsync();
    }

    private void RegisterSupervisorCrash(string serviceName, Exception ex)
    {
        AppendLog($"Supervisor {serviceName}: Crash erkannt: {ex.Message}");
    }

    private void UpdateDiscordStatusLabel(bool ok, string text)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => UpdateDiscordStatusLabel(ok, text));
            return;
        }
        _discordStatusLabel.Text = text;
        _discordStatusLabel.ForeColor = ok ? Discord.Color.DarkGreen : Discord.Color.DarkRed;
    }

    private void SetBusy(bool busy)
    {
        _connectButton.Enabled = !busy && (_rconClient is null || !_rconClient.IsConnected);
        _disconnectButton.Enabled = !busy && _rconClient is not null;
        _listPlayersButton.Enabled = !busy && _rconClient is not null;
        _listPlayersJsonButton.Enabled = !busy && _rconClient is not null;
        _serverButton.Enabled = !busy && _rconClient is not null;
        _sendButton.Enabled = !busy && _rconClient is not null;
        _refreshPlayersButton.Enabled = !busy && _rconClient is not null;
        _broadcastButton.Enabled = !busy && _rconClient is not null;
        _messagePlayerButton.Enabled = !busy && _rconClient is not null;
        _giveItemButton.Enabled = !busy && _rconClient is not null;
        _spawnEntityButton.Enabled = !busy && _rconClient is not null;
        _manualAnnounceButton.Enabled = !busy && _rconClient is not null;
        _manualScanButton.Enabled = !busy && _rconClient is not null;
        _saveScriptsButton.Enabled = !busy;
        _validateScriptsButton.Enabled = !busy;
        _openScriptsFolderButton.Enabled = !busy;
        _refreshScriptStatusButton.Enabled = !busy;
        _formatScriptButton.Enabled = !busy;
        _newScriptButton.Enabled = !busy;
        _duplicateScriptButton.Enabled = !busy;
        _deleteScriptButton.Enabled = !busy;
        _downloadKillLogsButton.Enabled = !busy;
        _startKillFeedButton.Enabled = !busy && _killFeedTimer is null;
        _stopKillFeedButton.Enabled = !busy && _killFeedTimer is not null;
        _startDiscordStatusButton.Enabled = !busy && _discordStatusTimer is null;
        _stopDiscordStatusButton.Enabled = !busy && _discordStatusTimer is not null;
        _discordUpdateNowButton.Enabled = !busy && _rconClient is not null;
        _startAutomationButton.Enabled = !busy && _automationTimer is null;
        _stopAutomationButton.Enabled = !busy && _automationTimer is not null;
        _runAutomationOnceButton.Enabled = !busy;
        _formatChatRulesButton.Enabled = !busy;
        _formatJoinRulesButton.Enabled = !busy;
    }

    private void UpdateConnectionState(bool connected)
    {
        _statusLabel.Text = connected ? "Status: verbunden" : "Status: nicht verbunden";
        _statusLabel.ForeColor = connected ? Discord.Color.DarkGreen : Discord.Color.DarkRed;
        _connectButton.Enabled = !connected;
        _disconnectButton.Enabled = connected;
        SetBusy(false);
    }

    private void AppendLog(string text)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => AppendLog(text));
            return;
        }
        if (string.IsNullOrEmpty(text)) return;
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {text}";
        _logTextBox.AppendText(line + Environment.NewLine);
        WriteLogFile(line);
        _logTextBox.SelectionStart = _logTextBox.TextLength;
        _logTextBox.ScrollToCaret();
    }

    private static void WriteLogFile(string line)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScumRconTool", "Logs");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, DateTime.Now.ToString("yyyy-MM-dd") + ".log");
            File.AppendAllText(path, line + Environment.NewLine);
        }
        catch
        {
            // Logging must never crash the UI.
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _manualShutdown = true;
        StopKillFeed();
        StopAutomationWatcher();
        _rconReconnectTimer?.Stop();
        _rconReconnectTimer?.Dispose();
        _ = StopDiscordStatusAsync();
        StopEventEngine();
        _rconClient?.Close();
        base.OnFormClosed(e);
    }

    private sealed class KillLogRow
    {
        public string Time { get; set; } = "";
        public string Killer { get; set; } = "";
        public string Victim { get; set; } = "";
        public string Weapon { get; set; } = "";
        public string Distance { get; set; } = "";
        public string SourceFile { get; set; } = "";
        public string RawLine { get; set; } = "";

        public static KillLogRow FromEntry(KillLogEntry entry) => new()
        {
            Time = entry.Timestamp?.ToString("yyyy-MM-dd HH:mm:ss") ?? "",
            Killer = entry.Killer,
            Victim = entry.Victim,
            Weapon = entry.Weapon,
            Distance = entry.Distance,
            SourceFile = entry.SourceFile,
            RawLine = entry.RawLine
        };
    }

    private sealed class PlayerRow
    {
        public string Name { get; set; } = "";
        public string SteamName { get; set; } = "";
        public string SteamId { get; set; } = "";
        public double Fame { get; set; }
        public double Money { get; set; }
        public double Gold { get; set; }
        public string Position { get; set; } = "";
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public double? Health { get; set; }
        public int? Ping { get; set; }

        public static PlayerRow FromPlayer(ScumPlayer player) => new()
        {
            Name = player.CharacterName ?? "",
            SteamName = player.SteamName ?? "",
            SteamId = player.UserId ?? "",
            Fame = player.Fame,
            Money = player.AccountBalance,
            Gold = player.GoldBalance,
            Position = player.Location?.ToString() ?? "",
            X = player.Location?.X ?? 0,
            Y = player.Location?.Y ?? 0,
            Z = player.Location?.Z ?? 0,
            Health = player.Health,
            Ping = player.Ping
        };
    }
}
