using System.ComponentModel;
using System.Windows;
using ScumRconTool.ViewModels;

namespace ScumRconTool;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();
    private bool _updatingEditor;
    private bool _loadingPasswords;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        _viewModel.PropertyChanged += ViewModelOnPropertyChanged;
        ScriptEditor.Text = _viewModel.ScriptJson;
        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _loadingPasswords = true;
        RconPasswordBox.Password = _viewModel.Settings.Password ?? string.Empty;
        SftpPasswordBox.Password = _viewModel.Settings.FtpPassword ?? string.Empty;
        DiscordTokenBox.Password = _viewModel.Settings.DiscordBotToken ?? string.Empty;
        _loadingPasswords = false;

        await _viewModel.AutoStartConfiguredAsync();
    }

    private void ViewModelOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.ScriptJson) && !_updatingEditor)
        {
            _updatingEditor = true;
            ScriptEditor.Text = _viewModel.ScriptJson;
            _updatingEditor = false;
        }
    }

    private void SyncEditorToViewModel()
    {
        if (_updatingEditor) return;
        _viewModel.ScriptJson = ScriptEditor.Text;
    }

    private void RconPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (!_loadingPasswords) _viewModel.Settings.Password = RconPasswordBox.Password;
    }

    private void SftpPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (!_loadingPasswords) _viewModel.Settings.FtpPassword = SftpPasswordBox.Password;
    }

    private void DiscordTokenBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (!_loadingPasswords) _viewModel.Settings.DiscordBotToken = DiscordTokenBox.Password;
    }

    private void NavDashboard_Click(object sender, RoutedEventArgs e) => MainTabs.SelectedIndex = 0;
    private void NavRcon_Click(object sender, RoutedEventArgs e) => MainTabs.SelectedIndex = 1;
    private void NavDiscord_Click(object sender, RoutedEventArgs e) => MainTabs.SelectedIndex = 2;
    private void NavChatCommands_Click(object sender, RoutedEventArgs e) => MainTabs.SelectedIndex = 3;
    private void NavJoinCommands_Click(object sender, RoutedEventArgs e) => MainTabs.SelectedIndex = 4;
    private void NavKillFeed_Click(object sender, RoutedEventArgs e) => MainTabs.SelectedIndex = 5;
    private void NavWeeklyTasks_Click(object sender, RoutedEventArgs e) => MainTabs.SelectedIndex = 6;
    private void NavAutoMessages_Click(object sender, RoutedEventArgs e) => MainTabs.SelectedIndex = 7;
    private void NavScripts_Click(object sender, RoutedEventArgs e) => MainTabs.SelectedIndex = 8;
    private void NavLogs_Click(object sender, RoutedEventArgs e) => MainTabs.SelectedIndex = 9;
    private void NavSettings_Click(object sender, RoutedEventArgs e) => MainTabs.SelectedIndex = 10;

    private void ValidateScript_Click(object sender, RoutedEventArgs e)
    {
        SyncEditorToViewModel();
        _viewModel.ValidateScript();
    }

    private void FormatScript_Click(object sender, RoutedEventArgs e)
    {
        SyncEditorToViewModel();
        _viewModel.FormatScript();
    }

    private void SaveScript_Click(object sender, RoutedEventArgs e)
    {
        SyncEditorToViewModel();
        _viewModel.SaveScript();
    }

    private void DuplicateScript_Click(object sender, RoutedEventArgs e)
    {
        SyncEditorToViewModel();
        _viewModel.DuplicateScriptCommand.Execute(null);
    }

    protected override async void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        Loaded -= MainWindow_Loaded;
        _viewModel.PropertyChanged -= ViewModelOnPropertyChanged;
        await _viewModel.DisposeAsync();
    }
}
