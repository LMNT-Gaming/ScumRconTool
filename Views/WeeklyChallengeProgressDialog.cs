using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using ScumRconTool.Services;

namespace ScumRconTool.Views;

public sealed class WeeklyChallengeProgressDialog : Window
{
    private WeeklyChallengeProgressDialog(WeeklyCommunityTaskProgress progress, bool isGerman, bool participantsOnly)
    {
        var definition = progress.Definition;
        var title = string.IsNullOrWhiteSpace(definition.Title) ? definition.Id : definition.Title;
        Title = (participantsOnly
            ? (isGerman ? "Teilnehmer – " : "Participants – ")
            : (isGerman ? "Aktueller Stand – " : "Current progress – ")) + title;
        Width = 1120;
        Height = 760;
        MinWidth = 820;
        MinHeight = 540;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("AppBackgroundBrush");
        Foreground = Brush("AppTextBrush");
        FontFamily = new FontFamily("Segoe UI");

        var root = new DockPanel { Margin = new Thickness(22) };
        var footer = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
        var updated = new TextBlock
        {
            Text = (isGerman ? "Stand: " : "Updated: ") + progress.UpdatedUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss"),
            Foreground = Brush("MutedTextBrush"),
            VerticalAlignment = VerticalAlignment.Center
        };
        var close = new Button
        {
            Content = isGerman ? "Schließen" : "Close",
            IsCancel = true,
            MinWidth = 120,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        DockPanel.SetDock(close, Dock.Right);
        footer.Children.Add(close);
        footer.Children.Add(updated);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        heading.Children.Add(new TextBlock { Text = title, FontSize = 27, FontWeight = FontWeights.SemiBold });
        heading.Children.Add(new TextBlock
        {
            Text = participantsOnly
                ? (isGerman
                    ? "Nur Spieler mit einem Beitrag größer 0 werden angezeigt. Bei mehreren Zielen steht jedes Ziel in einem eigenen Tab."
                    : "Only players with a contribution greater than 0 are shown. Multiple goals are displayed in separate tabs.")
                : WeeklyCommunityTaskService.RequiresAllGoals(definition)
                ? (isGerman
                    ? "Live-Stand aus der zuletzt eingelesenen SCUM.db. Alle Ziele müssen erreicht werden."
                    : "Live progress from the latest SCUM.db scan. All goals must be completed.")
                : (isGerman
                    ? "Live-Stand aus der zuletzt eingelesenen SCUM.db. Eines der Ziele reicht aus."
                    : "Live progress from the latest SCUM.db scan. Completing any one goal is sufficient."),
            Foreground = Brush("MutedTextBrush"),
            Margin = new Thickness(0, 4, 0, 0),
            TextWrapping = TextWrapping.Wrap
        });
        DockPanel.SetDock(heading, Dock.Top);
        root.Children.Add(heading);

        var tabs = new TabControl();
        var goals = progress.GoalProgresses.Count > 0
            ? progress.GoalProgresses
            : new List<WeeklyCommunityTaskGoalProgress>
            {
                new()
                {
                    StatTable = definition.StatTable,
                    StatColumn = definition.StatColumn,
                    DisplayName = definition.StatColumn,
                    Target = Math.Max(1, definition.Target),
                    Progress = progress.Progress,
                    Percent = progress.Percent,
                    IsCompleted = progress.IsCompleted,
                    PlayerProgress = progress.PlayerProgress
                }
            };

        foreach (var goal in goals)
        {
            tabs.Items.Add(new TabItem
            {
                Header = goal.DisplayName,
                Content = BuildGoalContent(goal, isGerman, participantsOnly)
            });
        }

        root.Children.Add(tabs);
        Content = root;
    }

    public static void Show(WeeklyCommunityTaskProgress progress, bool isGerman)
    {
        var dialog = new WeeklyChallengeProgressDialog(progress, isGerman, participantsOnly: false)
        {
            Owner = Application.Current?.MainWindow
        };
        dialog.ShowDialog();
    }

    public static void ShowParticipants(WeeklyCommunityTaskProgress progress, bool isGerman)
    {
        var dialog = new WeeklyChallengeProgressDialog(progress, isGerman, participantsOnly: true)
        {
            Owner = Application.Current?.MainWindow
        };
        dialog.ShowDialog();
    }

    private static UIElement BuildGoalContent(WeeklyCommunityTaskGoalProgress goal, bool isGerman, bool participantsOnly)
    {
        var root = new Grid { Margin = new Thickness(12) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());

        var summary = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        summary.ColumnDefinitions.Add(new ColumnDefinition());
        summary.ColumnDefinitions.Add(new ColumnDefinition());
        summary.ColumnDefinitions.Add(new ColumnDefinition());
        summary.Children.Add(SummaryCard(isGerman ? "Fortschritt" : "Progress", $"{goal.Progress:N0} / {Math.Max(1, goal.Target):N0}", 0));
        summary.Children.Add(SummaryCard(isGerman ? "Prozent" : "Percent", $"{goal.Percent:0.0}%", 1));
        summary.Children.Add(SummaryCard(isGerman ? "Status" : "Status", goal.IsCompleted ? (isGerman ? "Erreicht ✓" : "Completed ✓") : (isGerman ? "Aktiv" : "Active"), 2));
        root.Children.Add(summary);

        var players = goal.PlayerProgress
            .Where(x => !participantsOnly || x.Progress > 0)
            .OrderByDescending(x => x.IsCompleted)
            .ThenByDescending(x => x.Percent)
            .ThenByDescending(x => x.Progress)
            .ThenBy(x => x.PlayerName, StringComparer.OrdinalIgnoreCase)
            .Select(x => new PlayerRow(x, goal.Target, isGerman))
            .ToList();

        UIElement content;
        if (players.Count == 0)
        {
            content = new Border
            {
                Background = Brush("PanelBrush"),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(18),
                Child = new TextBlock
                {
                    Text = participantsOnly
                        ? (isGerman ? "Noch kein Spieler hat zu diesem Ziel beigetragen." : "No player has contributed to this goal yet.")
                        : (isGerman ? "Noch keine Spielerwerte für dieses Ziel vorhanden." : "No player values are available for this goal yet."),
                    Foreground = Brush("MutedTextBrush")
                }
            };
        }
        else
        {
            var table = new DataGrid
            {
                ItemsSource = players,
                AutoGenerateColumns = false,
                IsReadOnly = true,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                CanUserReorderColumns = true,
                CanUserSortColumns = true,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                RowHeaderWidth = 0,
                AlternationCount = 2
            };
            table.Columns.Add(Column(isGerman ? "Spieler" : "Player", nameof(PlayerRow.PlayerName), 2.0));
            table.Columns.Add(Column("Steam ID", nameof(PlayerRow.SteamId), 1.55));
            table.Columns.Add(Column("Squad", nameof(PlayerRow.SquadName), 1.35));
            table.Columns.Add(Column(isGerman ? "Fortschritt" : "Progress", nameof(PlayerRow.Progress), 1.0));
            table.Columns.Add(Column(isGerman ? "Ziel" : "Target", nameof(PlayerRow.Target), 0.85));
            table.Columns.Add(Column(isGerman ? "Prozent" : "Percent", nameof(PlayerRow.Percent), 0.8));
            table.Columns.Add(Column("Status", nameof(PlayerRow.Status), 0.95));
            content = table;
        }

        Grid.SetRow(content, 1);
        root.Children.Add(content);
        return root;
    }

    private static Border SummaryCard(string label, string value, int column)
    {
        var card = new Border
        {
            Background = Brush("PanelBrush"),
            BorderBrush = Brush("AccentDarkBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14),
            Margin = new Thickness(column == 0 ? 0 : 6, 0, column == 2 ? 0 : 6, 0),
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = label, Foreground = Brush("MutedTextBrush") },
                    new TextBlock { Text = value, FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 3, 0, 0) }
                }
            }
        };
        Grid.SetColumn(card, column);
        return card;
    }

    private static DataGridTextColumn Column(string header, string property, double width) => new()
    {
        Header = header,
        Binding = new Binding(property),
        Width = new DataGridLength(width, DataGridLengthUnitType.Star)
    };

    private static Brush Brush(string key) =>
        Application.Current?.TryFindResource(key) as Brush ?? Brushes.Transparent;

    private sealed class PlayerRow
    {
        public PlayerRow(WeeklyCommunityTaskPlayerProgress player, long fallbackTarget, bool isGerman)
        {
            PlayerName = string.IsNullOrWhiteSpace(player.PlayerName) ? "–" : player.PlayerName;
            SteamId = string.IsNullOrWhiteSpace(player.SteamId) ? "–" : player.SteamId;
            SquadName = string.IsNullOrWhiteSpace(player.SquadName) ? (isGerman ? "Ohne Squad" : "No squad") : player.SquadName;
            Progress = player.Progress.ToString("N0");
            Target = Math.Max(1, player.Target > 0 ? player.Target : fallbackTarget).ToString("N0");
            Percent = player.Percent.ToString("0.0") + "%";
            Status = player.IsCompleted ? (isGerman ? "Erreicht ✓" : "Completed ✓") : (isGerman ? "Aktiv" : "Active");
        }

        public string PlayerName { get; }
        public string SteamId { get; }
        public string SquadName { get; }
        public string Progress { get; }
        public string Target { get; }
        public string Percent { get; }
        public string Status { get; }
    }
}
