using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using ScumRconTool.ViewModels;

namespace ScumRconTool;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();
    private bool _updatingEditor;
    private bool _loadingPasswords;
    private ScriptFlowNodeViewModel? _draggingFlowNode;
    private Point _flowNodeDragOffset;
    private int _lastMainTabIndex;
    private bool _handlingMainTabSelection;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        _viewModel.PropertyChanged += ViewModelOnPropertyChanged;
        ScriptEditor.Text = _viewModel.ScriptJson;
        ScriptEditor.TextChanged += ScriptEditor_TextChanged;
        ScriptEditorTabs.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler(ScriptEditorValueChanged), true);
        ScriptEditorTabs.AddHandler(ToggleButton.CheckedEvent, new RoutedEventHandler(ScriptEditorValueChanged), true);
        ScriptEditorTabs.AddHandler(ToggleButton.UncheckedEvent, new RoutedEventHandler(ScriptEditorValueChanged), true);
        ScriptEditorTabs.AddHandler(Selector.SelectionChangedEvent, new SelectionChangedEventHandler(ScriptEditorSelectionChanged), true);
        MainTabs.SelectionChanged += MainTabs_SelectionChanged;
        _lastMainTabIndex = MainTabs.SelectedIndex;
        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _loadingPasswords = true;
        RconPasswordBox.Password = _viewModel.Settings.Password ?? string.Empty;
        SftpPasswordBox.Password = _viewModel.Settings.FtpPassword ?? string.Empty;
        DiscordTokenBox.Password = _viewModel.Settings.DiscordBotToken ?? string.Empty;
        _loadingPasswords = false;

        if (_viewModel.Settings.AutoCheckForUpdates)
        {
            _ = _viewModel.CheckForUpdatesAsync(showMessage: true, silentIfCurrent: true);
        }

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

    private void ScriptEditor_TextChanged(object? sender, EventArgs e)
    {
        if (!_updatingEditor && MainTabs.SelectedIndex == 8 && ScriptEditor.IsKeyboardFocusWithin)
        {
            _viewModel.MarkScriptDirty();
        }
    }

    private void ScriptEditorValueChanged(object sender, RoutedEventArgs e)
    {
        if (MainTabs.SelectedIndex == 8 && ScriptEditorTabs.IsKeyboardFocusWithin)
        {
            _viewModel.MarkScriptDirty();
        }
    }

    private void ScriptEditorSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.OriginalSource is TabControl)
        {
            return;
        }

        if (MainTabs.SelectedIndex == 8 && ScriptEditorTabs.IsKeyboardFocusWithin)
        {
            _viewModel.MarkScriptDirty();
        }
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

    private void NavDashboard_Click(object sender, RoutedEventArgs e) => SetMainTab(0);
    private void NavRcon_Click(object sender, RoutedEventArgs e) => SetMainTab(1);
    private void NavDiscord_Click(object sender, RoutedEventArgs e) => SetMainTab(2);
    private void NavChatCommands_Click(object sender, RoutedEventArgs e) => SetMainTab(3);
    private void NavJoinCommands_Click(object sender, RoutedEventArgs e) => SetMainTab(4);
    private void NavKillFeed_Click(object sender, RoutedEventArgs e) => SetMainTab(5);
    private void NavWeeklyTasks_Click(object sender, RoutedEventArgs e) => SetMainTab(6);
    private void NavAutoMessages_Click(object sender, RoutedEventArgs e) => SetMainTab(7);
    private void NavScripts_Click(object sender, RoutedEventArgs e) => SetMainTab(8);
    private void NavLogs_Click(object sender, RoutedEventArgs e) => SetMainTab(9);
    private void NavSettings_Click(object sender, RoutedEventArgs e) => SetMainTab(10);

    private void SetMainTab(int index)
    {
        if (!CanLeaveMainTab(MainTabs.SelectedIndex, index))
        {
            return;
        }

        MainTabs.SelectedIndex = index;
    }

    private void MainTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_handlingMainTabSelection ||
            e.AddedItems.Count == 0 ||
            e.AddedItems[0] is not TabItem ||
            (e.RemovedItems.Count > 0 && e.RemovedItems[0] is not TabItem))
        {
            return;
        }

        var nextIndex = MainTabs.SelectedIndex;
        if (!CanLeaveMainTab(_lastMainTabIndex, nextIndex))
        {
            _handlingMainTabSelection = true;
            MainTabs.SelectedIndex = _lastMainTabIndex;
            _handlingMainTabSelection = false;
            return;
        }

        _lastMainTabIndex = nextIndex;
    }

    private bool CanLeaveMainTab(int currentIndex, int nextIndex)
    {
        return currentIndex != 8 || nextIndex == 8 || _viewModel.ConfirmScriptChangeAllowed();
    }

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

    private void FlowNode_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not ScriptFlowNodeViewModel node)
        {
            return;
        }

        _viewModel.SelectScriptFlowNode(node);
        _draggingFlowNode = node;
        var pointer = e.GetPosition(FlowCanvas);
        _flowNodeDragOffset = new Point(pointer.X - node.X, pointer.Y - node.Y);
        element.CaptureMouse();
        e.Handled = true;
    }

    private void FlowNode_MouseMove(object sender, MouseEventArgs e)
    {
        if (_draggingFlowNode is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var pointer = e.GetPosition(FlowCanvas);
        _draggingFlowNode.X = Math.Max(0, pointer.X - _flowNodeDragOffset.X);
        _draggingFlowNode.Y = Math.Max(0, pointer.Y - _flowNodeDragOffset.Y);
        e.Handled = true;
    }

    private void FlowNode_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_draggingFlowNode is null)
        {
            return;
        }

        _draggingFlowNode = null;
        if (sender is FrameworkElement element)
        {
            element.ReleaseMouseCapture();
        }

        e.Handled = true;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_viewModel.ConfirmScriptChangeAllowed())
        {
            e.Cancel = true;
            return;
        }

        base.OnClosing(e);
    }

    protected override async void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        Loaded -= MainWindow_Loaded;
        ScriptEditor.TextChanged -= ScriptEditor_TextChanged;
        MainTabs.SelectionChanged -= MainTabs_SelectionChanged;
        _viewModel.PropertyChanged -= ViewModelOnPropertyChanged;
        await _viewModel.DisposeAsync();
    }
}
