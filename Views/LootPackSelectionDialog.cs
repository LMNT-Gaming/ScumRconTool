using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScumRconTool.ViewModels;

namespace ScumRconTool.Views;

public sealed class LootPackSelectionDialog : Window
{
    private readonly IReadOnlyList<LootPackEditorViewModel> _all;
    private readonly HashSet<string> _selected;
    private readonly bool _allowMultiple;
    private readonly bool _isGerman;
    private readonly TextBox _search = new();
    private readonly ComboBox _category = new();
    private readonly WrapPanel _cards = new();
    private readonly TextBlock _selection = new();

    private LootPackSelectionDialog(
        IReadOnlyList<LootPackEditorViewModel> packs,
        IEnumerable<string> selectedNames,
        bool allowMultiple,
        bool isGerman)
    {
        _all = packs.Where(pack => pack.Enabled).OrderBy(pack => pack.Category).ThenBy(pack => pack.Name).ToList();
        var availableNames = _all.Select(pack => pack.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _selected = selectedNames
            .Where(name => !string.IsNullOrWhiteSpace(name) && availableNames.Contains(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        _allowMultiple = allowMultiple;
        _isGerman = isGerman;

        Title = isGerman ? "Lootpacks auswählen" : "Select loot packs";
        Width = 1120;
        Height = 760;
        MinWidth = 820;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("AppBackgroundBrush");
        Foreground = Brush("AppTextBrush");

        var root = new Grid { Margin = new Thickness(22) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        heading.Children.Add(new TextBlock { Text = Title, FontSize = 25, FontWeight = FontWeights.SemiBold });
        heading.Children.Add(new TextBlock
        {
            Text = isGerman
                ? "Suche nach Name oder Item und filtere die Packs nach Kategorie."
                : "Search by name or item and filter packs by category.",
            Foreground = Brush("MutedTextBrush"),
            Margin = new Thickness(0, 4, 0, 0)
        });
        root.Children.Add(heading);

        var filters = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        filters.ColumnDefinitions.Add(new ColumnDefinition());
        filters.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });
        _search.Margin = new Thickness(0, 0, 12, 0);
        _search.MinHeight = 38;
        _search.ToolTip = isGerman ? "Packname oder enthaltenes Item" : "Pack name or contained item";
        _search.TextChanged += (_, _) => RenderCards();
        filters.Children.Add(_search);
        _category.Items.Add(isGerman ? "Alle Kategorien" : "All categories");
        foreach (var value in new[] { "Weapons", "Ammunition", "Equipment", "Consumables", "Mixed" })
        {
            _category.Items.Add(CategoryLabel(value, isGerman));
        }
        _category.SelectedIndex = 0;
        _category.MinHeight = 38;
        _category.SelectionChanged += (_, _) => RenderCards();
        Grid.SetColumn(_category, 1);
        filters.Children.Add(_category);
        Grid.SetRow(filters, 1);
        root.Children.Add(filters);

        var scroll = new ScrollViewer
        {
            Content = _cards,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        Grid.SetRow(scroll, 2);
        root.Children.Add(scroll);

        var footer = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition());
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _selection.VerticalAlignment = VerticalAlignment.Center;
        _selection.Foreground = Brush("MutedTextBrush");
        footer.Children.Add(_selection);
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        var clear = new Button { Content = isGerman ? "Auswahl leeren" : "Clear selection", MinWidth = 120 };
        clear.Click += (_, _) => { _selected.Clear(); RenderCards(); };
        var cancel = new Button { Content = isGerman ? "Abbrechen" : "Cancel", IsCancel = true, MinWidth = 105 };
        var apply = new Button { Content = isGerman ? "Übernehmen" : "Apply", MinWidth = 120, FontWeight = FontWeights.SemiBold };
        apply.Click += (_, _) => DialogResult = true;
        actions.Children.Add(clear);
        actions.Children.Add(cancel);
        actions.Children.Add(apply);
        Grid.SetColumn(actions, 1);
        footer.Children.Add(actions);
        Grid.SetRow(footer, 3);
        root.Children.Add(footer);

        Content = root;
        Loaded += (_, _) => { RenderCards(); _search.Focus(); };
    }

    public IReadOnlyList<string> SelectedNames => _selected.OrderBy(name => name).ToList();

    public static IReadOnlyList<string>? Pick(
        IEnumerable<LootPackEditorViewModel> packs,
        IEnumerable<string> selectedNames,
        bool allowMultiple,
        bool isGerman)
    {
        var dialog = new LootPackSelectionDialog(packs.ToList(), selectedNames, allowMultiple, isGerman)
        {
            Owner = Application.Current?.MainWindow
        };
        return dialog.ShowDialog() == true ? dialog.SelectedNames : null;
    }

    public static string CategoryLabel(string? category, bool isGerman) => (category ?? "Mixed") switch
    {
        "Weapons" => isGerman ? "Waffen" : "Weapons",
        "Ammunition" => isGerman ? "Munition & Magazine" : "Ammo & magazines",
        "Equipment" => isGerman ? "Ausrüstung" : "Equipment",
        "Consumables" => isGerman ? "Verbrauchsgüter" : "Consumables",
        _ => "Mixed"
    };

    private void RenderCards()
    {
        _cards.Children.Clear();
        var query = _search.Text.Trim();
        var categoryIndex = _category.SelectedIndex;
        var category = categoryIndex <= 0 ? string.Empty : new[] { "Weapons", "Ammunition", "Equipment", "Consumables", "Mixed" }[categoryIndex - 1];
        var filtered = _all.Where(pack =>
            (string.IsNullOrWhiteSpace(category) || string.Equals(pack.Category, category, StringComparison.OrdinalIgnoreCase))
            && (string.IsNullOrWhiteSpace(query)
                || pack.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                || pack.Items.Any(item => item.Item.Contains(query, StringComparison.OrdinalIgnoreCase))));

        foreach (var pack in filtered) _cards.Children.Add(CreateCard(pack));
        _selection.Text = _selected.Count == 0
            ? (_isGerman ? "Kein Lootpack ausgewählt" : "No loot pack selected")
            : (_isGerman ? $"{_selected.Count} Lootpack(s) ausgewählt" : $"{_selected.Count} loot pack(s) selected");
    }

    private UIElement CreateCard(LootPackEditorViewModel pack)
    {
        var selected = _selected.Contains(pack.Name);
        var border = new Border
        {
            Width = 330,
            Height = 118,
            Margin = new Thickness(0, 0, 12, 12),
            Padding = new Thickness(11),
            CornerRadius = new CornerRadius(10),
            Background = Brush(selected ? "PanelAltBrush" : "PanelBrush"),
            BorderBrush = Brush(selected ? "AccentBrush" : "AppBorderBrush"),
            BorderThickness = new Thickness(selected ? 2 : 1)
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var image = new Image { Width = 58, Height = 58, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Top };
        if (!string.IsNullOrWhiteSpace(pack.PreviewImageUrl))
        {
            try { image.Source = new BitmapImage(new Uri(pack.PreviewImageUrl)); } catch { }
        }
        grid.Children.Add(image);
        var text = new StackPanel();
        var check = new CheckBox { Content = pack.Name, IsChecked = selected, FontWeight = FontWeights.SemiBold };
        check.Checked += (_, _) => Select(pack.Name, true);
        check.Unchecked += (_, _) => Select(pack.Name, false);
        text.Children.Add(check);
        text.Children.Add(new TextBlock { Text = CategoryLabel(pack.Category, _isGerman), Foreground = Brush("AccentBrush"), FontSize = 11, Margin = new Thickness(0, 3, 0, 0) });
        text.Children.Add(new TextBlock { Text = pack.PreviewItemName, Foreground = Brush("MutedTextBrush"), TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = pack.PreviewItemName });
        text.Children.Add(new TextBlock { Text = $"{pack.ItemCount} Items · {pack.TotalQuantity}", Foreground = Brush("MutedTextBrush"), FontSize = 11 });
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        border.Child = grid;
        return border;
    }

    private void Select(string name, bool selected)
    {
        if (selected)
        {
            if (!_allowMultiple) _selected.Clear();
            _selected.Add(name);
        }
        else
        {
            _selected.Remove(name);
        }
        RenderCards();
    }

    private Brush Brush(string key) => Application.Current?.TryFindResource(key) as Brush ?? Brushes.Transparent;
}
