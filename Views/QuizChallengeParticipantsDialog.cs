using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using ScumRconTool.Services;

namespace ScumRconTool.Views;

public sealed class QuizChallengeParticipantsDialog : Window
{
    private QuizChallengeParticipantsDialog(QuizChallengeState quiz, bool isGerman)
    {
        Title = (isGerman ? "Quiz-Teilnehmer – " : "Quiz participants – ") + quiz.Title;
        Width = 850;
        Height = 620;
        MinWidth = 650;
        MinHeight = 430;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("AppBackgroundBrush");
        Foreground = Brush("AppTextBrush");
        FontFamily = new FontFamily("Segoe UI");

        var rows = quiz.AttemptsBySteamId
            .Where(entry => entry.Value > 0)
            .OrderByDescending(entry => quiz.Winners.Contains(entry.Key))
            .ThenByDescending(entry => entry.Value)
            .ThenBy(entry => GetPlayerName(quiz, entry.Key), StringComparer.OrdinalIgnoreCase)
            .Select(entry => new ParticipantRow(
                GetPlayerName(quiz, entry.Key),
                entry.Key,
                entry.Value,
                quiz.Winners.Contains(entry.Key),
                isGerman))
            .ToList();

        var root = new DockPanel { Margin = new Thickness(22) };
        var footer = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
        var close = new Button
        {
            Content = isGerman ? "Schließen" : "Close",
            IsCancel = true,
            MinWidth = 120,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        DockPanel.SetDock(close, Dock.Right);
        footer.Children.Add(close);
        footer.Children.Add(new TextBlock
        {
            Text = isGerman
                ? $"{rows.Count} Teilnehmer · {quiz.Winners.Count} Gewinner"
                : $"{rows.Count} participants · {quiz.Winners.Count} winners",
            Foreground = Brush("MutedTextBrush"),
            VerticalAlignment = VerticalAlignment.Center
        });
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        heading.Children.Add(new TextBlock { Text = quiz.Title, FontSize = 27, FontWeight = FontWeights.SemiBold });
        heading.Children.Add(new TextBlock
        {
            Text = isGerman
                ? $"Quiz {quiz.QuizNumber}: Spieler mit mindestens einem Antwortversuch."
                : $"Quiz {quiz.QuizNumber}: players with at least one answer attempt.",
            Foreground = Brush("MutedTextBrush"),
            Margin = new Thickness(0, 4, 0, 0)
        });
        DockPanel.SetDock(heading, Dock.Top);
        root.Children.Add(heading);

        if (rows.Count == 0)
        {
            root.Children.Add(new Border
            {
                Background = Brush("PanelBrush"),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(18),
                Child = new TextBlock
                {
                    Text = isGerman ? "Noch niemand hat an diesem Quiz teilgenommen." : "Nobody has participated in this quiz yet.",
                    Foreground = Brush("MutedTextBrush")
                }
            });
        }
        else
        {
            var table = new DataGrid
            {
                ItemsSource = rows,
                AutoGenerateColumns = false,
                IsReadOnly = true,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                CanUserSortColumns = true,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                RowHeaderWidth = 0,
                AlternationCount = 2
            };
            table.Columns.Add(Column(isGerman ? "Spieler" : "Player", nameof(ParticipantRow.PlayerName), 2.0));
            table.Columns.Add(Column("Steam ID", nameof(ParticipantRow.SteamId), 1.7));
            table.Columns.Add(Column(isGerman ? "Versuche" : "Attempts", nameof(ParticipantRow.Attempts), 0.8));
            table.Columns.Add(Column("Status", nameof(ParticipantRow.Status), 1.0));
            root.Children.Add(table);
        }

        Content = root;
    }

    public static void Show(QuizChallengeState quiz, bool isGerman)
    {
        new QuizChallengeParticipantsDialog(quiz, isGerman)
        {
            Owner = Application.Current?.MainWindow
        }.ShowDialog();
    }

    private static string GetPlayerName(QuizChallengeState quiz, string steamId) =>
        quiz.PlayerNamesBySteamId.TryGetValue(steamId, out var name) && !string.IsNullOrWhiteSpace(name) ? name : steamId;

    private static DataGridTextColumn Column(string header, string property, double width) => new()
    {
        Header = header,
        Binding = new Binding(property),
        Width = new DataGridLength(width, DataGridLengthUnitType.Star)
    };

    private static Brush Brush(string key) =>
        Application.Current?.TryFindResource(key) as Brush ?? Brushes.Transparent;

    private sealed record ParticipantRow(string PlayerName, string SteamId, int Attempts, bool IsWinner, bool IsGerman)
    {
        public string Status => IsWinner ? (IsGerman ? "Gewonnen ✓" : "Winner ✓") : (IsGerman ? "Teilgenommen" : "Participated");
    }
}
