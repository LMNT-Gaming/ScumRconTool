using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScumRconTool.Services;

namespace ScumRconTool.Views;

public sealed record ItemCatalogSelection(string Code, int Quantity);

public sealed class ItemCatalogDialog : Window
{
    private const int PageSize = 48;
    private readonly IReadOnlyList<ItemCatalogEntry> _all;
    private readonly Dictionary<int, int> _selected = new();
    private readonly TextBox _search = new();
    private readonly ComboBox _category = new();
    private readonly CheckBox _showNoSpawn = new() { Content = "No-Spawn anzeigen", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0) };
    private readonly WrapPanel _cards = new();
    private readonly TextBlock _result = new();
    private readonly TextBlock _pageLabel = new() { MinWidth = 110, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _selection = new() { FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 3, 0, 0) };
    private readonly Button _previous = new() { Content = "‹ Zurück", MinWidth = 95 };
    private readonly Button _next = new() { Content = "Weiter ›", MinWidth = 95 };
    private List<ItemCatalogEntry> _filtered = new();
    private int _page;

    private ItemCatalogDialog(IReadOnlyList<ItemCatalogEntry> items, string purpose)
    {
        _all = items;
        Title = "Items auswählen – " + purpose;
        Width = 1180; Height = 800; MinWidth = 900; MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("AppBackgroundBrush");
        Foreground = Brush("AppTextBrush");
        var root = new Grid { Margin = new Thickness(22), Background = Brush("AppBackgroundBrush") };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        heading.Children.Add(new TextBlock { Text = "Item-Katalog", FontSize = 25, FontWeight = FontWeights.SemiBold });
        heading.Children.Add(new TextBlock { Text = "Wähle ein oder mehrere Items. Übernommen wird der exakte SCUM-Spawn-Code.", Foreground = Brush("MutedTextBrush"), Margin = new Thickness(0, 5, 0, 0) });
        root.Children.Add(heading);

        var filters = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        filters.ColumnDefinitions.Add(new ColumnDefinition());
        filters.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });
        filters.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _search.Margin = new Thickness(0, 0, 12, 0); _search.MinHeight = 40; _search.ToolTip = "Name oder Spawn-Code suchen";
        _search.TextChanged += (_, _) => ApplyFilter();
        filters.Children.Add(_search);
        _category.Margin = new Thickness(0, 0, 12, 0); _category.MinHeight = 40;
        _category.Items.Add("Alle Kategorien");
        foreach (var value in items.Select(x => x.Category).Distinct(StringComparer.CurrentCultureIgnoreCase).OrderBy(x => x)) _category.Items.Add(value);
        _category.SelectedIndex = 0; _category.SelectionChanged += (_, _) => ApplyFilter();
        Grid.SetColumn(_category, 1); filters.Children.Add(_category);
        _showNoSpawn.Checked += (_, _) => ApplyFilter(); _showNoSpawn.Unchecked += (_, _) => ApplyFilter();
        Grid.SetColumn(_showNoSpawn, 2); filters.Children.Add(_showNoSpawn);
        Grid.SetRow(filters, 1); root.Children.Add(filters);

        var scroll = new ScrollViewer { Content = _cards, Background = Brush("AppBackgroundBrush"), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll, 2); root.Children.Add(scroll);

        var footer = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition());
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        _result.Foreground = Brush("MutedTextBrush"); info.Children.Add(_result); info.Children.Add(_selection); footer.Children.Add(info);
        var paging = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _previous.Click += (_, _) => { _page--; RenderPage(); }; _next.Click += (_, _) => { _page++; RenderPage(); };
        paging.Children.Add(_previous); paging.Children.Add(_pageLabel); paging.Children.Add(_next); Grid.SetColumn(paging, 1); footer.Children.Add(paging);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(20, 0, 0, 0) };
        var cancel = new Button { Content = "Abbrechen", IsCancel = true, MinWidth = 110 };
        var accept = new Button { Content = "Auswahl übernehmen", MinWidth = 175, FontWeight = FontWeights.SemiBold };
        accept.Click += (_, _) =>
        {
            if (_selected.Count == 0) { MessageBox.Show(this, "Bitte mindestens ein Item auswählen.", "Item-Katalog", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            DialogResult = true;
        };
        actions.Children.Add(cancel); actions.Children.Add(accept); Grid.SetColumn(actions, 2); footer.Children.Add(actions);
        Grid.SetRow(footer, 3); root.Children.Add(footer);
        Content = root;
        Loaded += (_, _) => { ApplyFilter(); _search.Focus(); };
    }

    public IReadOnlyList<ItemCatalogSelection> SelectedItems => _selected
        .Select(pair => new ItemCatalogSelection(_all.First(item => item.Id == pair.Key).Code, Math.Max(1, pair.Value))).ToList();

    public static IReadOnlyList<ItemCatalogSelection>? Pick(string purpose)
    {
        try
        {
            var dialog = new ItemCatalogDialog(new ItemCatalogService().Load(), purpose) { Owner = Application.Current?.MainWindow };
            return dialog.ShowDialog() == true ? dialog.SelectedItems : null;
        }
        catch (Exception ex)
        {
            MessageBox.Show(Application.Current?.MainWindow, ex.Message, "Item-Katalog konnte nicht geladen werden", MessageBoxButton.OK, MessageBoxImage.Error);
            return null;
        }
    }

    private void ApplyFilter()
    {
        var search = _search.Text.Trim(); var category = _category.SelectedItem as string;
        _filtered = _all.Where(item => (_showNoSpawn.IsChecked == true || item.IsSpawnable)
            && (search.Length == 0 || item.Code.Contains(search, StringComparison.OrdinalIgnoreCase) || item.DisplayName.Contains(search, StringComparison.CurrentCultureIgnoreCase))
            && (string.IsNullOrEmpty(category) || category == "Alle Kategorien" || string.Equals(item.Category, category, StringComparison.CurrentCultureIgnoreCase))).ToList();
        _page = 0; RenderPage();
    }

    private void RenderPage()
    {
        var pageCount = Math.Max(1, (int)Math.Ceiling(_filtered.Count / (double)PageSize));
        _page = Math.Clamp(_page, 0, pageCount - 1); _cards.Children.Clear();
        foreach (var item in _filtered.Skip(_page * PageSize).Take(PageSize)) _cards.Children.Add(CreateCard(item));
        _result.Text = $"{_filtered.Count:N0} Items gefunden"; _pageLabel.Text = $"Seite {_page + 1} / {pageCount}";
        _previous.IsEnabled = _page > 0; _next.IsEnabled = _page + 1 < pageCount; UpdateSelection();
    }

    private UIElement CreateCard(ItemCatalogEntry item)
    {
        var isSelected = _selected.TryGetValue(item.Id, out var selectedQuantity);
        var border = new Border { Width = 205, Height = 245, Margin = new Thickness(0, 0, 12, 12), Padding = new Thickness(12), CornerRadius = new CornerRadius(12), Background = Brush(isSelected ? "PanelAltBrush" : "PanelBrush"), BorderBrush = Brush(isSelected ? "AccentBrush" : "AppBorderBrush"), BorderThickness = new Thickness(isSelected ? 2 : 1) };
        var panel = new Grid();
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(112) });
        panel.RowDefinitions.Add(new RowDefinition()); panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var choose = new CheckBox { Content = item.IsSpawnable ? "Auswählen" : "No Spawn", IsChecked = isSelected, Margin = new Thickness(0, 0, 0, 5) };
        panel.Children.Add(choose);
        var image = new Image { Width = 105, Height = 105, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center };
        try { var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.UriSource = new Uri(item.ImageUrl); bitmap.CacheOption = BitmapCacheOption.OnDemand; bitmap.EndInit(); image.Source = bitmap; } catch { }
        Grid.SetRow(image, 1); panel.Children.Add(image);
        var labels = new StackPanel { Margin = new Thickness(0, 5, 0, 4) };
        labels.Children.Add(new TextBlock { Text = item.DisplayName, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = item.Code });
        labels.Children.Add(new TextBlock { Text = item.Code, FontSize = 11, Foreground = Brush("MutedTextBrush"), TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = item.Code });
        labels.Children.Add(new TextBlock { Text = $"{item.Category} · {item.TierLabel}", FontSize = 11, Foreground = Brush(item.IsSpawnable ? "MutedTextBrush" : "AccentBrush"), TextTrimming = TextTrimming.CharacterEllipsis });
        Grid.SetRow(labels, 2); panel.Children.Add(labels);
        var quantityPanel = new StackPanel { Orientation = Orientation.Horizontal };
        quantityPanel.Children.Add(new TextBlock { Text = "Menge", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        var quantity = new TextBox { Text = (isSelected ? selectedQuantity : 1).ToString(), Width = 62, Margin = new Thickness(0), Padding = new Thickness(6, 3, 6, 3) };
        quantity.PreviewTextInput += (_, args) => args.Handled = args.Text.Any(ch => !char.IsDigit(ch));
        quantity.TextChanged += (_, _) => { if (choose.IsChecked == true && int.TryParse(quantity.Text, out var amount)) _selected[item.Id] = Math.Max(1, amount); };
        quantityPanel.Children.Add(quantity); Grid.SetRow(quantityPanel, 3); panel.Children.Add(quantityPanel);
        choose.Checked += (_, _) => { _selected[item.Id] = int.TryParse(quantity.Text, out var amount) ? Math.Max(1, amount) : 1; SelectStyle(border, true); UpdateSelection(); };
        choose.Unchecked += (_, _) => { _selected.Remove(item.Id); SelectStyle(border, false); UpdateSelection(); };
        border.Child = panel; return border;
    }

    private void SelectStyle(Border border, bool selected)
    {
        border.BorderBrush = Brush(selected ? "AccentBrush" : "AppBorderBrush"); border.BorderThickness = new Thickness(selected ? 2 : 1);
        border.Background = Brush(selected ? "PanelAltBrush" : "PanelBrush");
    }

    private void UpdateSelection() => _selection.Text = _selected.Count == 0 ? "Noch nichts ausgewählt" : $"{_selected.Count} Item(s) ausgewählt";
    private Brush Brush(string key) => (Brush)FindResource(key);
}
