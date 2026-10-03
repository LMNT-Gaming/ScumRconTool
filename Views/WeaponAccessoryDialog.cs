using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ScumRconTool.Services;

namespace ScumRconTool.Views;

public sealed record WeaponAccessorySelection(string ItemId, int Quantity);

public sealed class WeaponAccessoryDialog : Window
{
    private readonly List<Suggestion> _all;
    private readonly bool _isGerman;
    private readonly TextBox _search = new();
    private readonly StackPanel _rows = new();

    private WeaponAccessoryDialog(IEnumerable<WeaponCompatibilityEntry> weapons, IEnumerable<string> existingItems, bool isGerman)
    {
        _isGerman = isGerman;
        var existing = existingItems.ToHashSet(StringComparer.OrdinalIgnoreCase);
        _all = BuildSuggestions(weapons, existing);

        Title = isGerman ? "Passendes Zubehör" : "Matching accessories";
        Width = 980;
        Height = 740;
        MinWidth = 760;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("AppBackgroundBrush");
        Foreground = Brush("AppTextBrush");

        var root = new Grid { Margin = new Thickness(22) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        heading.Children.Add(new TextBlock { Text = Title, FontSize = 25, FontWeight = FontWeights.SemiBold });
        heading.Children.Add(new TextBlock
        {
            Text = isGerman
                ? "Erkannt werden Waffen im Pack. Magazin, Munition, Rails und geprüfte Attachments stammen aus der Excel-Matrix. Benötigte Rails werden automatisch mit hinzugefügt."
                : "Weapons in the pack are detected automatically. Magazines, ammo, rails and verified attachments come from the Excel matrix. Required rails are added automatically.",
            Foreground = Brush("MutedTextBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0)
        });
        root.Children.Add(heading);
        _search.MinHeight = 38;
        _search.Margin = new Thickness(0, 0, 0, 12);
        _search.ToolTip = isGerman ? "Waffe, Typ, Name oder Spawn-ID suchen" : "Search weapon, type, name or spawn ID";
        _search.TextChanged += (_, _) => RenderRows();
        Grid.SetRow(_search, 1);
        root.Children.Add(_search);
        var scroll = new ScrollViewer { Content = _rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(scroll, 2);
        root.Children.Add(scroll);
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        footer.Children.Add(new Button { Content = isGerman ? "Abbrechen" : "Cancel", IsCancel = true, MinWidth = 105 });
        var apply = new Button { Content = isGerman ? "Auswahl hinzufügen" : "Add selection", MinWidth = 155, FontWeight = FontWeights.SemiBold };
        apply.Click += (_, _) => DialogResult = true;
        footer.Children.Add(apply);
        Grid.SetRow(footer, 3);
        root.Children.Add(footer);
        Content = root;
        Loaded += (_, _) => { RenderRows(); _search.Focus(); };
    }

    public IReadOnlyList<WeaponAccessorySelection> SelectedItems
    {
        get
        {
            var selected = _all.Where(item => item.Selected && !item.AlreadyPresent).ToList();
            var result = selected
                .GroupBy(item => item.ItemId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Max(item => Math.Max(1, item.Quantity)), StringComparer.OrdinalIgnoreCase);
            foreach (var item in selected.Where(item => !string.IsNullOrWhiteSpace(item.RequiredRailId)))
            {
                result.TryAdd(item.RequiredRailId, 1);
            }
            return result.Select(pair => new WeaponAccessorySelection(pair.Key, pair.Value)).ToList();
        }
    }

    public static IReadOnlyList<WeaponAccessorySelection>? Pick(
        IEnumerable<WeaponCompatibilityEntry> weapons,
        IEnumerable<string> existingItems,
        bool isGerman)
    {
        var dialog = new WeaponAccessoryDialog(weapons, existingItems, isGerman) { Owner = Application.Current?.MainWindow };
        return dialog.ShowDialog() == true ? dialog.SelectedItems : null;
    }

    private static List<Suggestion> BuildSuggestions(IEnumerable<WeaponCompatibilityEntry> weapons, HashSet<string> existing)
    {
        var result = new List<Suggestion>();
        foreach (var weapon in weapons)
        {
            if (!string.IsNullOrWhiteSpace(weapon.MagazineId)) result.Add(new Suggestion(weapon.Name, "Magazine", weapon.MagazineName, weapon.MagazineId, string.Empty, existing.Contains(weapon.MagazineId)) { Quantity = 2 });
            if (!string.IsNullOrWhiteSpace(weapon.AmmoBoxId)) result.Add(new Suggestion(weapon.Name, "Ammo", weapon.Caliber, weapon.AmmoBoxId, string.Empty, existing.Contains(weapon.AmmoBoxId)));
            result.AddRange(weapon.Options.Select(option => new Suggestion(weapon.Name, option.Group, option.Name, option.Id, option.RequiredRailId, existing.Contains(option.Id))));
        }
        return result
            .GroupBy(item => item.Weapon + "|" + item.ItemId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(item => item.Weapon)
            .ThenBy(item => item.Type)
            .ThenBy(item => item.Name)
            .ToList();
    }

    private void RenderRows()
    {
        _rows.Children.Clear();
        var query = _search.Text.Trim();
        foreach (var item in _all.Where(item => string.IsNullOrWhiteSpace(query)
                     || item.Weapon.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                     || item.Type.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                     || item.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                     || item.ItemId.Contains(query, StringComparison.OrdinalIgnoreCase)))
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 7), Background = Brush("PanelBrush") };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(135) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            var check = new CheckBox { IsChecked = item.Selected, IsEnabled = !item.AlreadyPresent, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 8, 4, 8) };
            check.Checked += (_, _) => item.Selected = true;
            check.Unchecked += (_, _) => item.Selected = false;
            row.Children.Add(check);
            AddText(row, item.Weapon, 1, FontWeights.SemiBold);
            AddText(row, TypeLabel(item.Type), 2, FontWeights.Normal);
            AddText(row, item.Name, 3, FontWeights.Normal);
            AddText(row, item.AlreadyPresent ? (_isGerman ? "Bereits im Pack" : "Already in pack") : item.ItemId, 4, FontWeights.Normal, item.AlreadyPresent ? "AccentBrush" : "MutedTextBrush");
            var quantity = new TextBox { Text = item.Quantity.ToString(), Margin = new Thickness(5), IsEnabled = !item.AlreadyPresent };
            quantity.TextChanged += (_, _) => { if (int.TryParse(quantity.Text, out var value)) item.Quantity = Math.Max(1, value); };
            Grid.SetColumn(quantity, 5);
            row.Children.Add(quantity);
            _rows.Children.Add(row);
        }
    }

    private string TypeLabel(string type) => type switch
    {
        "Magazine" => _isGerman ? "Magazin" : "Magazine",
        "Ammo" => _isGerman ? "Munition" : "Ammo",
        "Rail" => "Rail",
        "Rail Attachment" => _isGerman ? "Rail-Attachment" : "Rail attachment",
        "Direct Attachment" => _isGerman ? "Direktes Attachment" : "Direct attachment",
        _ => type
    };

    private void AddText(Grid row, string text, int column, FontWeight weight, string brush = "AppTextBrush")
    {
        var block = new TextBlock { Text = text, FontWeight = weight, Foreground = Brush(brush), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8), TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = text };
        Grid.SetColumn(block, column);
        row.Children.Add(block);
    }

    private Brush Brush(string key) => Application.Current?.TryFindResource(key) as Brush ?? Brushes.Transparent;

    private sealed class Suggestion
    {
        public Suggestion(string weapon, string type, string name, string itemId, string requiredRailId, bool alreadyPresent)
        {
            Weapon = weapon;
            Type = type;
            Name = name;
            ItemId = itemId;
            RequiredRailId = requiredRailId;
            AlreadyPresent = alreadyPresent;
        }
        public string Weapon { get; }
        public string Type { get; }
        public string Name { get; }
        public string ItemId { get; }
        public string RequiredRailId { get; }
        public bool AlreadyPresent { get; }
        public bool Selected { get; set; }
        public int Quantity { get; set; } = 1;
    }
}
