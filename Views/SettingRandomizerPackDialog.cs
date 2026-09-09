using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ScumRconTool.Services;

namespace ScumRconTool.Views;

public sealed record SettingRandomizerPackDialogResult(
    string SetName,
    int VariantCount,
    string Section,
    string Key,
    string DisplayName,
    string Value);

public sealed class SettingRandomizerPackDialog : Window
{
    private readonly bool _german;
    private readonly bool _createSet;
    private readonly TextBox _name = new();
    private readonly TextBox _variantCount = new() { Text = "2" };
    private readonly ComboBox _value = new() { IsEditable = true, MinWidth = 220 };
    private readonly TextBox _search = new();
    private readonly List<CategoryTabState> _categoryTabs = new();
    private TabControl? _tabs;
    private SettingRandomizerCatalogEntry? _selectedEntry;

    private SettingRandomizerPackDialog(IReadOnlyList<SettingRandomizerCatalogEntry> catalog, bool german, bool createSet)
    {
        _german = german;
        _createSet = createSet;
        Title = createSet
            ? (german ? "Neues Würfelset" : "New dice set")
            : (german ? "Einstellung hinzufügen" : "Add setting");
        Width = 760;
        Height = 650;
        MinWidth = 620;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("AppBackgroundBrush");
        Foreground = Brush("AppTextBrush");

        var root = new DockPanel { Margin = new Thickness(24) };
        var actions = BuildActions();
        DockPanel.SetDock(actions, Dock.Bottom);
        root.Children.Add(actions);

        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = createSet
                ? (german ? "Würfelset erstellen" : "Create dice set")
                : (german ? "Weitere Einstellung" : "Additional setting"),
            FontSize = 24,
            FontWeight = FontWeights.SemiBold
        });
        panel.Children.Add(new TextBlock
        {
            Text = createSet
                ? (german
                    ? "Dieses Set wird unabhängig von allen anderen Sets gewürfelt. Wähle die erste Einstellung und bearbeite ihren Wert später je Variante."
                    : "This set is rolled independently of every other set. Select its first setting and edit its value for each variant afterwards.")
                : (german
                    ? "Die Einstellung wird zu jeder Variante des Sets hinzugefügt und kann danach in jedem Varianten-Tab separat bearbeitet werden."
                    : "The setting is added to every variant in the set and can then be edited separately in each variant tab."),
            Foreground = Brush("MutedTextBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 5, 0, 16)
        });

        if (createSet)
        {
            var setGrid = new Grid { Margin = new Thickness(0, 0, 0, 14) };
            setGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            setGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var namePanel = Labeled(german ? "Name des Sets" : "Set name", _name);
            namePanel.Margin = new Thickness(0, 0, 12, 0);
            setGrid.Children.Add(namePanel);
            var countPanel = Labeled(german ? "Anzahl Varianten" : "Number of variants", _variantCount);
            Grid.SetColumn(countPanel, 1);
            setGrid.Children.Add(countPanel);
            _variantCount.PreviewTextInput += (_, args) => args.Handled = args.Text.Any(ch => !char.IsDigit(ch));
            panel.Children.Add(setGrid);
        }

        panel.Children.Add(new TextBlock
        {
            Text = german ? "Einstellung nach Kategorie auswählen" : "Select a setting by category",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6)
        });

        panel.Children.Add(new TextBlock
        {
            Text = german ? "Suche nach Name oder INI-Key" : "Search by name or INI key",
            Foreground = Brush("MutedTextBrush")
        });
        _search.ToolTip = german
            ? "Durchsucht gleichzeitig den Anzeigenamen und den vollständigen Backend-Key, z. B. scum.MaxAllowedPuppets."
            : "Searches both the display name and the full backend key, e.g. scum.MaxAllowedPuppets.";
        _search.Margin = new Thickness(0, 0, 0, 8);
        _search.TextChanged += (_, _) => ApplyCatalogFilter();
        panel.Children.Add(_search);

        _tabs = new TabControl { Height = 300 };
        foreach (var group in catalog.GroupBy(x => x.Section))
        {
            var entries = group.ToList();
            var list = new ListBox
            {
                ItemsSource = entries,
                DisplayMemberPath = nameof(SettingRandomizerCatalogEntry.Label),
                Margin = new Thickness(8)
            };
            list.SelectionChanged += (_, _) =>
            {
                if (list.SelectedItem is SettingRandomizerCatalogEntry entry) SelectEntry(entry);
            };
            var tab = new TabItem { Header = BuildTabHeader(group.Key, entries.Count, entries.Count), Content = list };
            _categoryTabs.Add(new CategoryTabState(group.Key, entries, tab, list));
            _tabs.Items.Add(tab);
        }
        _tabs.SelectionChanged += (_, _) =>
        {
            if (_tabs.SelectedItem is TabItem { Content: ListBox list } && list.SelectedIndex < 0 && list.Items.Count > 0)
                list.SelectedIndex = 0;
        };
        panel.Children.Add(_tabs);

        var valuePanel = Labeled(german ? "Startwert" : "Initial value", _value);
        valuePanel.Margin = new Thickness(0, 14, 0, 0);
        panel.Children.Add(valuePanel);
        panel.Children.Add(new TextBlock
        {
            Text = german
                ? "Die bekannten Werte stehen zur Auswahl; eigene Werte können direkt eingegeben werden."
                : "Known values are available for selection; custom values can be entered directly.",
            Foreground = Brush("MutedTextBrush"),
            TextWrapping = TextWrapping.Wrap
        });

        root.Children.Add(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = root;
        Loaded += (_, _) =>
        {
            SelectFirstVisibleEntry();
            if (createSet) _name.Focus();
            else _search.Focus();
        };
    }

    public SettingRandomizerPackDialogResult? Result { get; private set; }

    public static SettingRandomizerPackDialogResult? Create(IReadOnlyList<SettingRandomizerCatalogEntry> catalog, bool german)
    {
        var dialog = new SettingRandomizerPackDialog(catalog, german, createSet: true) { Owner = Application.Current?.MainWindow };
        return dialog.ShowDialog() == true ? dialog.Result : null;
    }

    public static SettingRandomizerPackDialogResult? PickSetting(IReadOnlyList<SettingRandomizerCatalogEntry> catalog, bool german)
    {
        var dialog = new SettingRandomizerPackDialog(catalog, german, createSet: false) { Owner = Application.Current?.MainWindow };
        return dialog.ShowDialog() == true ? dialog.Result : null;
    }

    private StackPanel BuildActions()
    {
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0)
        };
        actions.Children.Add(new Button { Content = _german ? "Abbrechen" : "Cancel", IsCancel = true, MinWidth = 105 });
        var confirm = new Button
        {
            Content = _createSet ? (_german ? "Set erstellen" : "Create set") : (_german ? "Hinzufügen" : "Add"),
            IsDefault = true,
            MinWidth = 130,
            FontWeight = FontWeights.SemiBold
        };
        confirm.Click += (_, _) => Confirm();
        actions.Children.Add(confirm);
        return actions;
    }

    private void ApplyCatalogFilter()
    {
        var tokens = (_search.Text ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var category in _categoryTabs)
        {
            var matches = tokens.Length == 0
                ? category.AllEntries
                : category.AllEntries.Where(entry => tokens.All(token =>
                    entry.DisplayName.Contains(token, StringComparison.CurrentCultureIgnoreCase) ||
                    entry.Key.Contains(token, StringComparison.OrdinalIgnoreCase))).ToList();
            category.List.ItemsSource = matches;
            category.Tab.Header = BuildTabHeader(category.Section, matches.Count, category.AllEntries.Count);
        }

        var selectedStillVisible = _selectedEntry is not null && _categoryTabs.Any(category =>
            category.List.Items.Cast<SettingRandomizerCatalogEntry>().Any(entry => entry.Id.Equals(_selectedEntry.Id, StringComparison.OrdinalIgnoreCase)));
        if (!selectedStillVisible)
        {
            _selectedEntry = null;
            _value.ItemsSource = Array.Empty<string>();
            _value.Text = string.Empty;
            SelectFirstVisibleEntry();
        }
    }

    private void SelectFirstVisibleEntry()
    {
        if (_tabs is null) return;
        var selectedCategory = _categoryTabs.FirstOrDefault(x => ReferenceEquals(x.Tab, _tabs.SelectedItem));
        var category = selectedCategory is not null && selectedCategory.List.Items.Count > 0
            ? selectedCategory
            : _categoryTabs.FirstOrDefault(x => x.List.Items.Count > 0);
        if (category is null) return;
        _tabs.SelectedItem = category.Tab;
        if (category.List.SelectedIndex < 0) category.List.SelectedIndex = 0;
    }

    private static string BuildTabHeader(string section, int visible, int total) =>
        visible == total ? $"{section} ({total})" : $"{section} ({visible}/{total})";
    private void SelectEntry(SettingRandomizerCatalogEntry entry)
    {
        _selectedEntry = entry;
        var values = entry.DefaultOptions.Select(x => x.Value).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        _value.ItemsSource = values;
        _value.Text = entry.DefaultOptions.OrderByDescending(x => x.ChancePercent).FirstOrDefault()?.Value ?? string.Empty;
    }

    private void Confirm()
    {
        if (_selectedEntry is null)
        {
            ShowInfo(_german ? "Bitte eine Einstellung auswählen." : "Select a setting.");
            return;
        }
        if (string.IsNullOrWhiteSpace(_value.Text))
        {
            ShowInfo(_german ? "Bitte einen Startwert eingeben." : "Enter an initial value.");
            _value.Focus();
            return;
        }

        var count = 1;
        var name = string.Empty;
        if (_createSet)
        {
            name = _name.Text.Trim();
            if (name.Length == 0)
            {
                ShowInfo(_german ? "Bitte einen Namen für das Set eingeben." : "Enter a name for the set.");
                _name.Focus();
                return;
            }
            if (!int.TryParse(_variantCount.Text, out count) || count is < 1 or > 20)
            {
                ShowInfo(_german ? "Die Variantenanzahl muss zwischen 1 und 20 liegen." : "The variant count must be between 1 and 20.");
                _variantCount.Focus();
                return;
            }
        }

        Result = new SettingRandomizerPackDialogResult(
            name,
            count,
            _selectedEntry.Section,
            _selectedEntry.Key,
            _selectedEntry.DisplayName,
            _value.Text.Trim());
        DialogResult = true;
    }

    private StackPanel Labeled(string label, Control control)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = label, Foreground = Brush("MutedTextBrush") });
        panel.Children.Add(control);
        return panel;
    }

    private void ShowInfo(string message) =>
        MessageBox.Show(this, message, Title, MessageBoxButton.OK, MessageBoxImage.Information);

    private Brush Brush(string key) => (Brush)FindResource(key);

    private sealed record CategoryTabState(
        string Section,
        List<SettingRandomizerCatalogEntry> AllEntries,
        TabItem Tab,
        ListBox List);}