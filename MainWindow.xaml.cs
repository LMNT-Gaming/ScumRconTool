using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using ScumRconTool.Models;
using ScumRconTool.ViewModels;

namespace ScumRconTool;

public partial class MainWindow : Window
{
    private const int RedeemCodesTabIndex = 4;
    private const int ScriptsTabIndex = 9;
    private const int LogsTabIndex = 10;
    private const int SettingsTabIndex = 11;
    private const double MapWorldLeftX = 618000;
    private const double MapWorldRightX = -898000;
    private const double MapWorldTopY = 618000;
    private const double MapWorldBottomY = -900000;
    private const double ScriptMapMaxScale = 7.5;

    private readonly MainViewModel _viewModel = new();
    private readonly ObservableCollection<ScumScriptMapMarker> _scriptMapMarkers = new();
    private bool _updatingEditor;
    private bool _loadingPasswords;
    private ScriptFlowNodeViewModel? _draggingFlowNode;
    private Point _flowNodeDragOffset;
    private int _lastMainTabIndex;
    private bool _handlingMainTabSelection;
    private bool _scriptMapImageLoaded;
    private bool _scriptMapViewInitialized;
    private bool _scriptMapIsPanning;
    private Point _scriptMapPanStart;
    private double _scriptMapPanStartX;
    private double _scriptMapPanStartY;
    private double _scriptMapScale = 1;
    private double _scriptMapImageWidth = 1;
    private double _scriptMapImageHeight = 1;
    private double _scriptMapTranslateX;
    private double _scriptMapTranslateY;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        InitializeScriptMap();
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

        await _viewModel.InitializeUsageDirectoryAsync();

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

        if (e.PropertyName is nameof(MainViewModel.SelectedScript) or nameof(MainViewModel.ScriptEditorModel) or nameof(MainViewModel.ScriptJson))
        {
            RefreshScriptMap();
        }
    }

    private void SyncEditorToViewModel()
    {
        if (_updatingEditor) return;
        _viewModel.ScriptJson = ScriptEditor.Text;
    }

    private void ScriptEditor_TextChanged(object? sender, EventArgs e)
    {
        if (!_updatingEditor && IsScriptEditingDirtyTrackingActive() && ScriptEditor.IsKeyboardFocusWithin)
        {
            _viewModel.MarkScriptDirty();
            RefreshScriptMap();
        }
    }

    private void ScriptEditorValueChanged(object sender, RoutedEventArgs e)
    {
        if (IsScriptEditingDirtyTrackingActive() && ScriptEditorTabs.IsKeyboardFocusWithin)
        {
            _viewModel.MarkScriptDirty();
            RefreshScriptMap();
        }
    }

    private void ScriptEditorSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.OriginalSource is TabControl)
        {
            if (IsScriptMapEditorTabSelected())
            {
                RefreshScriptMap();
                Dispatcher.BeginInvoke(new Action(ResetScriptMapView), System.Windows.Threading.DispatcherPriority.Loaded);
            }

            return;
        }

        if (IsScriptEditingDirtyTrackingActive() && ScriptEditorTabs.IsKeyboardFocusWithin)
        {
            _viewModel.MarkScriptDirty();
            RefreshScriptMap();
        }
    }

    private bool IsScriptEditingDirtyTrackingActive()
    {
        if (MainTabs.SelectedIndex != ScriptsTabIndex)
        {
            return false;
        }

        return ScriptEditorTabs.SelectedItem is not TabItem tab ||
               !IsNonDirtyScriptEditorTab(tab);
    }

    private bool IsScriptMapEditorTabSelected() =>
        ScriptEditorTabs.SelectedItem is TabItem tab &&
        string.Equals(tab.Tag?.ToString(), "ScriptMap", StringComparison.Ordinal);

    private static bool IsNonDirtyScriptEditorTab(TabItem tab)
    {
        var tag = tab.Tag?.ToString();
        return string.Equals(tag, "GlobalLootPacks", StringComparison.Ordinal) ||
               string.Equals(tag, "ScriptMap", StringComparison.Ordinal);
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
    private void NavRedeemCodes_Click(object sender, RoutedEventArgs e) => SetMainTab(RedeemCodesTabIndex);
    private void NavJoinCommands_Click(object sender, RoutedEventArgs e) => SetMainTab(5);
    private void NavKillFeed_Click(object sender, RoutedEventArgs e) => SetMainTab(6);
    private void NavWeeklyTasks_Click(object sender, RoutedEventArgs e) => SetMainTab(7);
    private void NavAutoMessages_Click(object sender, RoutedEventArgs e) => SetMainTab(8);
    private void NavScripts_Click(object sender, RoutedEventArgs e) => SetMainTab(ScriptsTabIndex);
    private void NavLogs_Click(object sender, RoutedEventArgs e) => SetMainTab(LogsTabIndex);
    private void NavSettings_Click(object sender, RoutedEventArgs e) => SetMainTab(SettingsTabIndex);

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
        return currentIndex != ScriptsTabIndex || nextIndex == ScriptsTabIndex || _viewModel.ConfirmScriptChangeAllowed();
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
        var pointer = e.GetPosition(this);
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

        var pointer = e.GetPosition(this);
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

    private void PuzzleScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer)
        {
            return;
        }

        scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - e.Delta);
        e.Handled = true;
    }

    private void PuzzleBlock_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        if (sender is FrameworkElement element && element.Tag is string blockKind && !string.IsNullOrWhiteSpace(blockKind))
        {
            DragDrop.DoDragDrop(element, blockKind, DragDropEffects.Copy);
            e.Handled = true;
        }
    }

    private void PuzzleDropZone_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = GetPuzzleBlockKind(e) is null ? DragDropEffects.None : DragDropEffects.Copy;
        e.Handled = true;
    }

    private void PuzzleDropZone_Drop(object sender, DragEventArgs e)
    {
        var blockKind = GetPuzzleBlockKind(e);
        if (string.IsNullOrWhiteSpace(blockKind) || _viewModel.ScriptEditorModel is null)
        {
            return;
        }

        switch (blockKind)
        {
            case "InitiatorCommand":
                _viewModel.AddScriptCommandCommand.Execute(_viewModel.ScriptEditorModel.InitiatorBlock);
                break;
            case "Timer":
                _viewModel.ScriptEditorModel.EnsureActivationDelayMs(60000);
                _viewModel.MarkScriptDirty();
                break;
            case "LiveCommand":
                _viewModel.AddScriptCommandCommand.Execute(_viewModel.ScriptEditorModel.LiveBlock);
                break;
            case "Spawn":
                _viewModel.AddSpawnBlockCommand.Execute(null);
                break;
            case "LootCommand":
                _viewModel.AddLootCommandPackCommand.Execute(null);
                break;
            case "NpcCoordinate":
                _viewModel.AddNpcLocationVariableCommand.Execute(null);
                break;
            case "LootCoordinate":
                _viewModel.AddLootLocationVariableCommand.Execute(null);
                break;
            case "CleanupCommand":
                _viewModel.AddScriptCommandCommand.Execute(_viewModel.ScriptEditorModel.CleanupBlock);
                break;
        }

        e.Handled = true;
    }

    private static string? GetPuzzleBlockKind(DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.StringFormat))
        {
            return e.Data.GetData(DataFormats.StringFormat) as string;
        }

        return e.Data.GetData(typeof(string)) as string;
    }

    private void InitializeScriptMap()
    {
        ScriptMapMarkerList.ItemsSource = _scriptMapMarkers;
        ScriptMapViewport.SizeChanged += ScriptMapViewport_SizeChanged;
        LoadScriptMapImage();
        RefreshScriptMap();
    }

    private void LoadScriptMapImage()
    {
        var mapPath = FindScriptMapImagePath();
        if (mapPath is null)
        {
            _scriptMapImageLoaded = false;
            ScriptMapStatusText.Text = Text("MapNotFound");
            return;
        }

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(mapPath, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();

            ScriptMapImage.Source = bitmap;
            _scriptMapImageWidth = Math.Max(1, bitmap.PixelWidth);
            _scriptMapImageHeight = Math.Max(1, bitmap.PixelHeight);
            _scriptMapImageLoaded = true;
            _scriptMapScale = 1;
            _scriptMapTranslateX = 0;
            _scriptMapTranslateY = 0;
            ApplyScriptMapView();
            ResetScriptMapView();
            DrawScriptMapOverlay();
        }
        catch (Exception ex)
        {
            _scriptMapImageLoaded = false;
            ScriptMapStatusText.Text = TextFormat("MapLoadFailed", ex.Message);
        }
    }

    private static string? FindScriptMapImagePath()
    {
        var relativePath = System.IO.Path.Combine("scum_kartographer", "assets", "scum_map.jpg");
        var roots = new[]
        {
            AppContext.BaseDirectory,
            Directory.GetCurrentDirectory()
        };

        foreach (var root in roots.Where(root => !string.IsNullOrWhiteSpace(root)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var candidate = System.IO.Path.Combine(root, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private void RefreshScriptMap()
    {
        var selectedName = _viewModel.SelectedScript?.Name ?? _viewModel.ScriptEditorModel?.Name ?? Text("NoScriptSelected");
        ScriptMapSelectedScriptText.Text = selectedName;
        _scriptMapMarkers.Clear();

        var model = _viewModel.ScriptEditorModel;
        if (model is null)
        {
            UpdateScriptMapCounts();
            DrawScriptMapOverlay();
            ScriptMapStatusText.Text = _scriptMapImageLoaded
                ? Text("NoScriptSelectedSentence")
                : Text("MapNotLoaded");
            return;
        }

        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddScriptMapMarker(keys, "Zone", model.ZoneName, model.ZoneX, model.ZoneY, model.ZoneZ, Math.Max(0, model.ZoneRadius), Text("ActivationZone"));

        foreach (var location in model.NpcSpawnLocations)
        {
            AddScriptMapMarker(keys, "Spawn", location.Name, location.X, location.Y, location.Z, 0, Text("NpcSpawnVariable"));
        }

        foreach (var location in model.LootSpawnLocations)
        {
            AddScriptMapMarker(keys, "Loot", location.Name, location.X, location.Y, location.Z, 0, Text("LootVariable"));
        }

        foreach (var block in model.SpawnBlocks.Where(block => block.Enabled))
        {
            var kind = IsLootSpawnBlock(block) ? "Loot" : "Spawn";
            foreach (var location in ResolveMapLocations(block.Location, model))
            {
                AddScriptMapMarker(keys, kind, block.Name, location.X, location.Y, location.Z, 0, Text("SpawnBlockLabel"));
            }
        }

        AddCommandMarkers(keys, model.LiveBlock.Commands, "Liveblock", model);

        var definition = TryReadCurrentEventDefinition();
        if (definition is not null)
        {
            foreach (var pack in definition.LootPacks ?? new List<LootPack>())
            {
                foreach (var location in ResolveMapLocations(pack.Location, model))
                {
                    AddScriptMapMarker(keys, "Loot", pack.Name, location.X, location.Y, location.Z, 0, "Legacy LootPack");
                }
            }

            foreach (var pack in definition.LootCommandPacks ?? new List<LootCommandPack>())
            {
                foreach (var location in ResolveMapLocations(FirstNonEmpty(pack.Location, pack.Command), model))
                {
                    AddScriptMapMarker(keys, "Loot", pack.Name, location.X, location.Y, location.Z, 0, "LootCommandPack");
                }
            }
        }

        AssignScriptMapLabels();
        UpdateScriptMapCounts();
        DrawScriptMapOverlay();
        ScriptMapStatusText.Text = _scriptMapImageLoaded
            ? TextFormat("MapPointsCurrentScript", _scriptMapMarkers.Count)
            : TextFormat("MapNotLoadedPointsRecognized", _scriptMapMarkers.Count);
    }

    private string Text(string key) => _viewModel.Texts[key];

    private string TextFormat(string key, params object[] args) =>
        string.Format(CultureInfo.CurrentCulture, _viewModel.Texts[key], args);

    private EventDefinition? TryReadCurrentEventDefinition()
    {
        try
        {
            var json = ScriptEditor.Text;
            if (string.IsNullOrWhiteSpace(json))
            {
                json = _viewModel.ScriptJson;
            }

            return string.IsNullOrWhiteSpace(json)
                ? null
                : JsonSerializer.Deserialize<EventDefinition>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            return null;
        }
    }

    private void AddCommandMarkers(HashSet<string> keys, IEnumerable<ScriptCommandEditorViewModel> commands, string blockName, ScriptStructuredEditorViewModel model)
    {
        foreach (var command in commands.Where(command => command.Enabled))
        {
            var kind = ClassifyMapCommand(command.Command);
            if (kind is null)
            {
                continue;
            }

            foreach (var location in ResolveMapLocations(command.Command, model))
            {
                AddScriptMapMarker(keys, kind, FirstNonEmpty(command.Name, command.Command, blockName), location.X, location.Y, location.Z, 0, blockName);
            }
        }
    }

    private void AddScriptMapMarker(HashSet<string> keys, string kind, string? name, double x, double y, double z, double radius, string source)
    {
        if (!IsFinite(x) || !IsFinite(y) || !IsFinite(z))
        {
            return;
        }

        var key = string.Create(CultureInfo.InvariantCulture, $"{kind}|{Math.Round(x, 1)}|{Math.Round(y, 1)}|{Math.Round(z, 1)}|{Math.Round(radius, 1)}");
        if (!keys.Add(key))
        {
            return;
        }

        _scriptMapMarkers.Add(new ScumScriptMapMarker(kind, string.IsNullOrWhiteSpace(name) ? kind : name.Trim(), source, x, y, z, radius));
    }

    private void AssignScriptMapLabels()
    {
        var spawn = 0;
        var loot = 0;
        foreach (var marker in _scriptMapMarkers)
        {
            marker.ShortLabel = marker.Kind switch
            {
                "Zone" => "Z",
                "Spawn" => "S" + (++spawn).ToString(CultureInfo.InvariantCulture),
                "Loot" => "L" + (++loot).ToString(CultureInfo.InvariantCulture),
                _ => "P"
            };
        }
    }

    private void UpdateScriptMapCounts()
    {
        ScriptMapZoneCountText.Text = _scriptMapMarkers.Count(marker => marker.Kind == "Zone").ToString(CultureInfo.InvariantCulture);
        ScriptMapSpawnCountText.Text = _scriptMapMarkers.Count(marker => marker.Kind == "Spawn").ToString(CultureInfo.InvariantCulture);
        ScriptMapLootCountText.Text = _scriptMapMarkers.Count(marker => marker.Kind == "Loot").ToString(CultureInfo.InvariantCulture);
    }

    private static string? ClassifyMapCommand(string? command)
    {
        if (string.IsNullOrWhiteSpace(command) ||
            Regex.IsMatch(command, @"#\s*DestroyAllItemsWithinRadius\b", RegexOptions.IgnoreCase))
        {
            return null;
        }

        if (Regex.IsMatch(command, @"#\s*(SpawnItem|SpawnInventory|SpawnInventoryFullOf)\b", RegexOptions.IgnoreCase))
        {
            return "Loot";
        }

        if (Regex.IsMatch(command, @"#\s*(SpawnArmedNPC|SpawnRandomZombie|SpawnZombie|SpawnRazor|SpawnVehicle|SpawnAnimal|SpawnRandomAnimal)\b", RegexOptions.IgnoreCase))
        {
            return "Spawn";
        }

        return null;
    }

    private static bool IsLootSpawnBlock(SpawnBlockEditorViewModel block) =>
        block.Type.Equals("Item", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<ScumMapCoordinate> ResolveMapLocations(string? value, ScriptStructuredEditorViewModel model)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            yield break;
        }

        foreach (var location in ParseMapCoordinates(value))
        {
            yield return location;
        }

        var placeholders = Regex.Matches(value, @"\{[A-Za-z0-9_]+\}", RegexOptions.IgnoreCase)
            .Select(match => match.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var placeholder in placeholders)
        {
            if (placeholder.Equals("{triggerZone}", StringComparison.OrdinalIgnoreCase))
            {
                yield return new ScumMapCoordinate(model.ZoneX, model.ZoneY, model.ZoneZ);
                continue;
            }

            var variable = model.NpcSpawnLocations
                .Concat(model.LootSpawnLocations)
                .FirstOrDefault(item => item.Placeholder.Equals(placeholder, StringComparison.OrdinalIgnoreCase));
            if (variable is not null)
            {
                yield return new ScumMapCoordinate(variable.X, variable.Y, variable.Z);
            }
        }
    }

    private static IEnumerable<ScumMapCoordinate> ParseMapCoordinates(string source)
    {
        var matches = Regex.Matches(
            source,
            @"\bX\s*=\s*(?<x>-?\d+(?:[\.,]\d+)?)\s+Y\s*=\s*(?<y>-?\d+(?:[\.,]\d+)?)\s+Z\s*=\s*(?<z>-?\d+(?:[\.,]\d+)?)",
            RegexOptions.IgnoreCase);

        foreach (Match match in matches)
        {
            if (TryParseMapDouble(match.Groups["x"].Value, out var x) &&
                TryParseMapDouble(match.Groups["y"].Value, out var y) &&
                TryParseMapDouble(match.Groups["z"].Value, out var z))
            {
                yield return new ScumMapCoordinate(x, y, z);
            }
        }
    }

    private static bool TryParseMapDouble(string value, out double result) =>
        double.TryParse(value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out result);

    private void DrawScriptMapOverlay()
    {
        ScriptMapOverlay.Children.Clear();
        if (!_scriptMapImageLoaded || _scriptMapScale <= 0)
        {
            return;
        }

        foreach (var marker in _scriptMapMarkers.Where(marker => marker.Kind == "Zone"))
        {
            DrawScriptMapZone(marker);
        }

        foreach (var marker in _scriptMapMarkers.Where(marker => marker.Kind != "Zone"))
        {
            DrawScriptMapPoint(marker);
        }
    }

    private void DrawScriptMapZone(ScumScriptMapMarker marker)
    {
        var point = WorldToScriptMapPixel(marker.X, marker.Y);
        var radius = WorldRadiusToScriptMapPixels(marker.Radius);
        if (radius > 0)
        {
            var zone = new Ellipse
            {
                Width = radius * 2,
                Height = radius * 2,
                Stroke = marker.Brush,
                StrokeThickness = 4,
                Fill = new SolidColorBrush(Color.FromArgb(44, marker.Color.R, marker.Color.G, marker.Color.B))
            };
            Canvas.SetLeft(zone, point.X - radius);
            Canvas.SetTop(zone, point.Y - radius);
            Canvas.SetZIndex(zone, 1);
            ScriptMapOverlay.Children.Add(zone);
        }

        DrawScriptMapPoint(marker);
    }

    private void DrawScriptMapPoint(ScumScriptMapMarker marker)
    {
        var point = WorldToScriptMapPixel(marker.X, marker.Y);
        var size = marker.Kind == "Zone" ? 22 : 16;
        var dot = new Ellipse
        {
            Width = size,
            Height = size,
            Fill = marker.Brush,
            Stroke = Brushes.Black,
            StrokeThickness = 2
        };

        Canvas.SetLeft(dot, point.X - size / 2d);
        Canvas.SetTop(dot, point.Y - size / 2d);
        Canvas.SetZIndex(dot, marker.Kind == "Zone" ? 4 : 3);
        ScriptMapOverlay.Children.Add(dot);

        var label = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(210, 15, 15, 22)),
            BorderBrush = marker.Brush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(5, 2, 5, 3),
            Child = new TextBlock
            {
                Text = marker.ShortLabel,
                Foreground = Brushes.White,
                FontSize = 13,
                FontWeight = FontWeights.Bold
            }
        };

        Canvas.SetLeft(label, point.X + 8);
        Canvas.SetTop(label, point.Y - 8);
        Canvas.SetZIndex(label, 5);
        ScriptMapOverlay.Children.Add(label);
    }

    private Point WorldToScriptMapPixel(double x, double y)
    {
        var width = GetScriptMapDisplayWidth();
        var height = GetScriptMapDisplayHeight();
        var px = ((x - MapWorldLeftX) / (MapWorldRightX - MapWorldLeftX)) * width;
        var py = ((y - MapWorldTopY) / (MapWorldBottomY - MapWorldTopY)) * height;
        return new Point(px, py);
    }

    private double WorldRadiusToScriptMapPixels(double radius)
    {
        if (radius <= 0)
        {
            return 0;
        }

        var scaleX = GetScriptMapDisplayWidth() / Math.Abs(MapWorldRightX - MapWorldLeftX);
        var scaleY = GetScriptMapDisplayHeight() / Math.Abs(MapWorldBottomY - MapWorldTopY);
        return radius * ((scaleX + scaleY) / 2d);
    }

    private void ScriptMapViewport_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_scriptMapImageLoaded)
        {
            return;
        }

        if (!_scriptMapViewInitialized)
        {
            ResetScriptMapView();
            return;
        }

        _scriptMapScale = ClampScriptMapScale(_scriptMapScale);
        ClampScriptMapTranslate();
        DrawScriptMapOverlay();
        UpdateScriptMapZoomText();
    }

    private void ScriptMapViewport_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!_scriptMapImageLoaded)
        {
            return;
        }

        var factor = e.Delta > 0 ? 1.18 : 1d / 1.18;
        SetScriptMapScale(_scriptMapScale * factor, e.GetPosition(ScriptMapViewport));
        e.Handled = true;
    }

    private void ScriptMapViewport_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_scriptMapImageLoaded)
        {
            return;
        }

        _scriptMapIsPanning = true;
        _scriptMapPanStart = e.GetPosition(ScriptMapViewport);
        _scriptMapPanStartX = _scriptMapTranslateX;
        _scriptMapPanStartY = _scriptMapTranslateY;
        ScriptMapViewport.CaptureMouse();
        ScriptMapViewport.Cursor = Cursors.SizeAll;
        e.Handled = true;
    }

    private void ScriptMapViewport_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_scriptMapIsPanning || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var current = e.GetPosition(ScriptMapViewport);
        _scriptMapTranslateX = _scriptMapPanStartX + current.X - _scriptMapPanStart.X;
        _scriptMapTranslateY = _scriptMapPanStartY + current.Y - _scriptMapPanStart.Y;
        ClampScriptMapTranslate();
        e.Handled = true;
    }

    private void ScriptMapViewport_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        EndScriptMapPan();
        e.Handled = true;
    }

    private void ScriptMapViewport_MouseLeave(object sender, MouseEventArgs e)
    {
        if (_scriptMapIsPanning && e.LeftButton != MouseButtonState.Pressed)
        {
            EndScriptMapPan();
        }
    }

    private void EndScriptMapPan()
    {
        if (!_scriptMapIsPanning)
        {
            return;
        }

        _scriptMapIsPanning = false;
        ScriptMapViewport.ReleaseMouseCapture();
        ScriptMapViewport.Cursor = Cursors.Arrow;
    }

    private void ScriptMapReset_Click(object sender, RoutedEventArgs e)
    {
        ResetScriptMapView();
    }

    private void ScriptMapZoomIn_Click(object sender, RoutedEventArgs e)
    {
        ZoomScriptMapAtCenter(1.22);
    }

    private void ScriptMapZoomOut_Click(object sender, RoutedEventArgs e)
    {
        ZoomScriptMapAtCenter(1d / 1.22);
    }

    private void ZoomScriptMapAtCenter(double factor)
    {
        if (!_scriptMapImageLoaded)
        {
            return;
        }

        SetScriptMapScale(_scriptMapScale * factor, new Point(ScriptMapViewport.ActualWidth / 2d, ScriptMapViewport.ActualHeight / 2d));
    }

    private void ScriptMapMarkerList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ScriptMapMarkerList.SelectedItem is not ScumScriptMapMarker marker || !_scriptMapImageLoaded)
        {
            return;
        }

        var targetScale = Math.Max(_scriptMapScale, GetScriptMapFitScale() * 2.4);
        CenterScriptMapOn(marker, targetScale);
    }

    private void CenterScriptMapOn(ScumScriptMapMarker marker, double scale)
    {
        _scriptMapScale = ClampScriptMapScale(scale);
        var point = WorldToScriptMapPixel(marker.X, marker.Y);
        _scriptMapTranslateX = ScriptMapViewport.ActualWidth / 2d - point.X;
        _scriptMapTranslateY = ScriptMapViewport.ActualHeight / 2d - point.Y;
        ClampScriptMapTranslate();
        DrawScriptMapOverlay();
        UpdateScriptMapZoomText();
    }

    private void SetScriptMapScale(double requestedScale, Point pivot)
    {
        var oldScale = _scriptMapScale <= 0 ? GetScriptMapFitScale() : _scriptMapScale;
        var nextScale = ClampScriptMapScale(requestedScale);
        var contentX = (pivot.X - _scriptMapTranslateX) / oldScale;
        var contentY = (pivot.Y - _scriptMapTranslateY) / oldScale;

        _scriptMapScale = nextScale;
        _scriptMapTranslateX = pivot.X - contentX * nextScale;
        _scriptMapTranslateY = pivot.Y - contentY * nextScale;
        ClampScriptMapTranslate();
        DrawScriptMapOverlay();
        UpdateScriptMapZoomText();
    }

    private double ClampScriptMapScale(double value)
    {
        var fit = GetScriptMapFitScale();
        return Math.Clamp(value, fit, Math.Max(fit, ScriptMapMaxScale));
    }

    private double GetScriptMapFitScale()
    {
        if (!_scriptMapImageLoaded ||
            _scriptMapImageWidth <= 0 ||
            _scriptMapImageHeight <= 0 ||
            ScriptMapViewport.ActualWidth <= 0 ||
            ScriptMapViewport.ActualHeight <= 0)
        {
            return 1;
        }

        return Math.Min(ScriptMapViewport.ActualWidth / _scriptMapImageWidth, ScriptMapViewport.ActualHeight / _scriptMapImageHeight);
    }

    private void ResetScriptMapView()
    {
        if (!_scriptMapImageLoaded ||
            ScriptMapViewport.ActualWidth <= 0 ||
            ScriptMapViewport.ActualHeight <= 0)
        {
            return;
        }

        _scriptMapScale = GetScriptMapFitScale();
        _scriptMapTranslateX = (ScriptMapViewport.ActualWidth - GetScriptMapDisplayWidth()) / 2d;
        _scriptMapTranslateY = (ScriptMapViewport.ActualHeight - GetScriptMapDisplayHeight()) / 2d;
        _scriptMapViewInitialized = true;
        ApplyScriptMapView();
        DrawScriptMapOverlay();
        UpdateScriptMapZoomText();
    }

    private void ClampScriptMapTranslate()
    {
        if (!_scriptMapImageLoaded ||
            ScriptMapViewport.ActualWidth <= 0 ||
            ScriptMapViewport.ActualHeight <= 0)
        {
            return;
        }

        var scaledWidth = GetScriptMapDisplayWidth();
        var scaledHeight = GetScriptMapDisplayHeight();

        _scriptMapTranslateX = scaledWidth <= ScriptMapViewport.ActualWidth
            ? (ScriptMapViewport.ActualWidth - scaledWidth) / 2d
            : Math.Clamp(_scriptMapTranslateX, ScriptMapViewport.ActualWidth - scaledWidth, 0);

        _scriptMapTranslateY = scaledHeight <= ScriptMapViewport.ActualHeight
            ? (ScriptMapViewport.ActualHeight - scaledHeight) / 2d
            : Math.Clamp(_scriptMapTranslateY, ScriptMapViewport.ActualHeight - scaledHeight, 0);

        ApplyScriptMapView();
    }

    private void ApplyScriptMapView()
    {
        var width = GetScriptMapDisplayWidth();
        var height = GetScriptMapDisplayHeight();

        ScriptMapContent.Width = width;
        ScriptMapContent.Height = height;
        ScriptMapImage.Width = width;
        ScriptMapImage.Height = height;
        ScriptMapOverlay.Width = width;
        ScriptMapOverlay.Height = height;

        Canvas.SetLeft(ScriptMapContent, _scriptMapTranslateX);
        Canvas.SetTop(ScriptMapContent, _scriptMapTranslateY);
        Canvas.SetLeft(ScriptMapImage, 0);
        Canvas.SetTop(ScriptMapImage, 0);
        Canvas.SetLeft(ScriptMapOverlay, 0);
        Canvas.SetTop(ScriptMapOverlay, 0);
    }

    private double GetScriptMapDisplayWidth() => Math.Max(1, _scriptMapImageWidth * Math.Max(_scriptMapScale, 0.0001));

    private double GetScriptMapDisplayHeight() => Math.Max(1, _scriptMapImageHeight * Math.Max(_scriptMapScale, 0.0001));

    private void UpdateScriptMapZoomText()
    {
        var fit = GetScriptMapFitScale();
        var percent = fit <= 0 ? 100 : (_scriptMapScale / fit) * 100d;
        ScriptMapZoomText.Text = "Zoom " + percent.ToString("0", CultureInfo.InvariantCulture) + "%";
    }

    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

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

internal readonly record struct ScumMapCoordinate(double X, double Y, double Z);

internal sealed class ScumScriptMapMarker
{
    public ScumScriptMapMarker(string kind, string name, string source, double x, double y, double z, double radius)
    {
        Kind = kind;
        Name = name;
        Source = source;
        X = x;
        Y = y;
        Z = z;
        Radius = radius;
    }

    public string Kind { get; }
    public string Name { get; }
    public string Source { get; }
    public double X { get; }
    public double Y { get; }
    public double Z { get; }
    public double Radius { get; }
    public string ShortLabel { get; set; } = "";

    public Color Color => Kind switch
    {
        "Zone" => Color.FromRgb(211, 21, 42),
        "Spawn" => Color.FromRgb(243, 201, 91),
        "Loot" => Color.FromRgb(86, 211, 138),
        _ => Color.FromRgb(91, 143, 216)
    };

    public Brush Brush => new SolidColorBrush(Color);
    public string Title => $"{ShortLabel} {Kind}: {Name}";
    public string Detail => Radius > 0
        ? $"{Source} / Radius {Radius:0}"
        : Source;

    public string Coordinates => string.Create(CultureInfo.InvariantCulture, $"X {X:0} / Y {Y:0} / Z {Z:0}");
}
