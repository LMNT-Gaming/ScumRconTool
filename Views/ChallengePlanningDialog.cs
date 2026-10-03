using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ScumRconTool.Services;

namespace ScumRconTool.Views;

public sealed record ChallengeRotationChoice(bool Enabled, int Maximum, int PauseHours, int VisibleMinutes, IReadOnlyList<string> TemplateIds);

public sealed class ChallengePlanningDialog : Window
{
    private readonly StackPanel _body = new() { Margin = new Thickness(22) };
    private ChallengePlanningDialog(string title)
    {
        Title = title;
        Width = 660;
        Height = 660;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Owner = Application.Current?.MainWindow;
        Background = (Brush?)Application.Current?.TryFindResource("AppBackgroundBrush") ?? Brushes.Black;
        Foreground = (Brush?)Application.Current?.TryFindResource("AppTextBrush") ?? Brushes.White;
        Content = new ScrollViewer { Content = _body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        _body.Children.Add(new TextBlock { Text = title, FontSize = 24, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0,0,0,14) });
    }
    private void Label(string text) => _body.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,10,0,5) });
    private TextBox Number(string label, int value)
    {
        Label(label);
        var input = new TextBox { Text = value.ToString(), Width = 100, HorizontalAlignment = HorizontalAlignment.Left };
        _body.Children.Add(input);
        return input;
    }
    private void SaveButton(Action save, bool de)
    {
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0,20,0,0) };
        var apply = new Button { Content = de ? "Einplanung speichern" : "Save schedule", IsDefault = true };
        apply.Click += (_, _) => save();
        buttons.Children.Add(apply);
        buttons.Children.Add(new Button { Content = de ? "Abbrechen" : "Cancel", IsCancel = true });
        _body.Children.Add(buttons);
    }
    public static DateTime? Replan(WeeklyCommunityTaskDefinition task, bool de)
    {
        var window = new ChallengePlanningDialog(de ? "Erneut einplanen" : "Schedule again") { Height = 430 };
        window.Label(task.Title + " · " + (WeeklyCommunityTaskService.IsPersonalGoal(task) ? (de ? "Persönlich" : "Personal") : "Community") + " · " + task.DurationHours + " h");
        window.Label(de ? "Neuer Start" : "New start");
        var date = new DatePicker { SelectedDate = DateTime.Now.AddMinutes(2).Date, DisplayDateStart = DateTime.Today };
        var time = new TextBox { Text = DateTime.Now.AddMinutes(2).ToString("HH:mm"), Width = 100, HorizontalAlignment = HorizontalAlignment.Left };
        window._body.Children.Add(date);
        window._body.Children.Add(time);
        window.Label(de ? "Zähler starten automatisch bei 0. Ziele, Laufzeit und Loot bleiben erhalten. Bereits erzeugte Redeem-Codes bleiben gültig." : "Counters restart automatically at zero. Goals, duration and loot are retained. Existing redeem codes remain valid.");
        DateTime? result = null;
        window.SaveButton(() =>
        {
            if (!date.SelectedDate.HasValue || !TimeSpan.TryParse(time.Text, out var clock) || clock < TimeSpan.Zero || clock >= TimeSpan.FromDays(1))
            { MessageBox.Show(window, de ? "Bitte Datum und Uhrzeit (HH:mm) prüfen." : "Please check the date and time (HH:mm)."); return; }
            var start = date.SelectedDate.Value.Date.Add(clock);
            if (start < DateTime.Now.AddMinutes(-1))
            { MessageBox.Show(window, de ? "Der Start muss in der Zukunft liegen." : "The start must be in the future."); return; }
            result = start.ToUniversalTime();
            window.DialogResult = true;
        }, de);
        return window.ShowDialog() == true ? result : null;
    }
    public static ChallengeRotationChoice? Rotation(BotSettings settings, IReadOnlyList<WeeklyCommunityTaskDefinition> tasks, bool de)
    {
        var window = new ChallengePlanningDialog(de ? "Automatische Herausforderungsplanung" : "Automatic challenge planning");
        var enabled = new CheckBox { Content = de ? "Künftig automatisch neu einplanen" : "Automatically schedule future runs", IsChecked = settings.ChallengeRotationEnabled };
        window._body.Children.Add(enabled);
        var maximum = window.Number(de ? "Maximal gleichzeitig (manuelle Planungen zählen mit)" : "Maximum simultaneous challenges (including manual schedules)", settings.ChallengeRotationMaximum);
        var pause = window.Number(de ? "Pause vor Wiederholung derselben Herausforderung (Stunden, mindestens 1)" : "Minimum repeat interval for the same challenge (hours, at least 1)", settings.ChallengeRotationPauseHours);
        var visible = window.Number(de ? "Community nach Abschluss noch anzeigen (Minuten)" : "Keep completed community challenges visible (minutes)", settings.ChallengeCompletedVisibleMinutes);
        window.Label(de ? "Rotation: Selten eingesetzte Vorlagen zuerst, bei Gleichstand zufällig. Persönliche Herausforderungen laufen bis zum Zeitende. Der automatische Dienst muss laufen; die Abschlussfrist wird minütlich geprüft." : "Rotation: least recently scheduled templates first, random ties. Personal challenges run until their deadline. The automatic service must run; completion visibility is checked every minute.");
        window.Label(de ? "Vorlagen für die Rotation (Quiz wird weiterhin manuell geplant)" : "Rotation templates (quizzes remain manually scheduled)");
        var choices = tasks.Where(x => !x.Type.Equals("Quiz", StringComparison.OrdinalIgnoreCase)).Select(x =>
            (Task: x, Check: new CheckBox { Content = x.Title + " · " + (WeeklyCommunityTaskService.IsPersonalGoal(x) ? (de ? "Persönlich" : "Personal") : "Community") + " · " + x.DurationHours + " h", IsChecked = x.AutoRotate, Margin = new Thickness(0,4,0,4) })).ToList();
        foreach (var choice in choices) window._body.Children.Add(choice.Check);
        ChallengeRotationChoice? result = null;
        window.SaveButton(() =>
        {
            if (!int.TryParse(maximum.Text, out var max) || max < 1 || max > 50 ||
                !int.TryParse(pause.Text, out var gap) || gap < 1 || gap > 8760 ||
                !int.TryParse(visible.Text, out var minutes) || minutes < 0 || minutes > 10080)
            { MessageBox.Show(window, de ? "Bitte Werte prüfen: Anzahl 1–50, Pause 1–8760 h, Anzeige 0–10080 min." : "Check values: count 1–50, repeat interval 1–8760 h, visibility 0–10080 min."); return; }
            var ids = choices.Where(x => x.Check.IsChecked == true).Select(x => x.Task.Id).ToList();
            if (enabled.IsChecked == true && ids.Count == 0)
            { MessageBox.Show(window, de ? "Bitte mindestens eine Vorlage auswählen." : "Please select at least one template."); return; }
            result = new(enabled.IsChecked == true, max, gap, minutes, ids);
            window.DialogResult = true;
        }, de);
        return window.ShowDialog() == true ? result : null;
    }
}
