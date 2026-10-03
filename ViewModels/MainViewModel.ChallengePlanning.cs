using System.Windows.Input;
using ScumRconTool.Services;
using ScumRconTool.Views;

namespace ScumRconTool.ViewModels;

public sealed partial class MainViewModel
{
    private async Task ReplanChallengeAsync(WeeklyTaskEditorViewModel? source)
    {
        if (source is null) return;
        var definition = source.ToDefinition();
        var start = ChallengePlanningDialog.Replan(definition, Texts.IsGerman);
        if (!start.HasValue) return;
        if (source.IsQuiz) _quizChallengeService.Reset(definition.QuizNumber, definition.Id);
        ChallengePlanningService.Schedule(definition, start.Value);
        source.ApplyPlanningState(definition);
        source.Enabled = true;
        SyncWeeklyTaskEditorsToSettings();
        SettingsStore.Save(Settings);
        ClearCachedWeeklyTaskProgress(source.Id);
        _loadedChallengeDefinitions = Settings.GetWeeklyTaskDefinitions();
        WeeklyTaskEditorsView.Refresh();
        if (!WeeklyTasksRunning) await StartWeeklyTasksAsync();
        await PublishWeeklyTaskDiscordAsync(_lastWeeklyTaskProgresses);
        Log($"Herausforderung erneut eingeplant: {source.Title}, Start {start.Value.ToLocalTime():dd.MM.yyyy HH:mm}, neuer Nullpunkt automatisch.");
    }

    private async Task ConfigureChallengeRotationAsync()
    {
        var definitions = WeeklyTaskEditors.Select(x => x.ToDefinition()).ToList();
        var choice = ChallengePlanningDialog.Rotation(Settings, definitions, Texts.IsGerman);
        if (choice is null) return;
        Settings.ChallengeRotationEnabled = choice.Enabled;
        Settings.ChallengeRotationMaximum = choice.Maximum;
        Settings.ChallengeRotationPauseHours = choice.PauseHours;
        Settings.ChallengeCompletedVisibleMinutes = choice.VisibleMinutes;
        var ids = choice.TemplateIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var editor in WeeklyTaskEditors) editor.AutoRotate = ids.Contains(editor.Id);
        SyncWeeklyTaskEditorsToSettings();
        SettingsStore.Save(Settings);
        _loadedChallengeDefinitions = Settings.GetWeeklyTaskDefinitions();
        OnPropertyChanged(nameof(Settings));
        if (!WeeklyTasksRunning) await StartWeeklyTasksAsync();
        await ScanWeeklyTasksOnceAsync();
        Log("Automatische Herausforderungsplanung gespeichert.");
    }
}
