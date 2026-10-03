using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using ScumRconTool.Services;

namespace ScumRconTool.ViewModels;

public sealed partial class MainViewModel
{
    private LocalChallengeAdminService? _localChallengeAdmin;
    private List<WeeklyCommunityTaskDefinition> _loadedChallengeDefinitions = [];
    private string _localChallengeAdminStatus = string.Empty;
    private string _localChallengeAdminNetworkUrl = string.Empty;
    private bool _localChallengeAdminRunning;

    public ICommand StartLocalChallengeAdminCommand { get; private set; } = null!;
    public ICommand StopLocalChallengeAdminCommand { get; private set; } = null!;

    public string LocalChallengeAdminStatus
    {
        get => _localChallengeAdminStatus;
        private set => SetProperty(ref _localChallengeAdminStatus, value);
    }

    public bool LocalChallengeAdminRunning
    {
        get => _localChallengeAdminRunning;
        private set => SetProperty(ref _localChallengeAdminRunning, value);
    }

    public string LocalChallengeAdminNetworkUrl
    {
        get => _localChallengeAdminNetworkUrl;
        private set => SetProperty(ref _localChallengeAdminNetworkUrl, value);
    }

    private void InitializeLocalChallengeAdmin()
    {
        LocalChallengeAdminStatus = T("LocalWebAdminStopped");
        StartLocalChallengeAdminCommand = new RelayCommand(_ => StartOrOpenLocalChallengeAdmin());
        StopLocalChallengeAdminCommand = new RelayCommand(async _ => await StopLocalChallengeAdminAsync());
    }

    private void StartOrOpenLocalChallengeAdmin()
    {
        try
        {
            if (_localChallengeAdmin is null || !_localChallengeAdmin.IsRunning)
            {
                // Der Browser beginnt mit dem aktuellen Stand des Desktop-Editors.
                SyncWeeklyTaskEditorsToSettings();
                var port = Settings.LocalWebAdminPort;
                _localChallengeAdmin = new LocalChallengeAdminService(Log, OnChallengesSavedFromWeb);
                _localChallengeAdmin.Start(port);
                LocalChallengeAdminRunning = true;
                LocalChallengeAdminNetworkUrl = _localChallengeAdmin.NetworkUrl;
                LocalChallengeAdminStatus = Tf("LocalWebAdminRunning", LocalChallengeAdminNetworkUrl);
                SettingsStore.Save(Settings);
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = _localChallengeAdmin.Url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            LocalChallengeAdminRunning = false;
            LocalChallengeAdminStatus = Tf("LocalWebAdminError", ex.Message);
            Log("Lokale Challenge-Verwaltung konnte nicht gestartet werden: " + ex.Message);
            MessageBox.Show(LocalChallengeAdminStatus, T("LocalWebAdmin"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnChallengesSavedFromWeb(IReadOnlyList<string> resetIds)
    {
        var dispatcher = App.Current?.Dispatcher;
        if (dispatcher is null) return;
        dispatcher.Invoke(() =>
        {
            var definitions = Settings.GetWeeklyTaskDefinitions();
            var scanRequired = ResetChallengeHistories(definitions, resetIds);
            LoadWeeklyTaskEditorsFromSettings();
            RefreshChallengeCalendarEntries();
            LocalChallengeAdminStatus = Tf("LocalWebAdminSaved", DateTime.Now.ToString("HH:mm:ss"));
            if (scanRequired) _ = RefreshReactivatedChallengesAsync();
        });
    }

    private bool ResetChallengeHistories(IReadOnlyCollection<WeeklyCommunityTaskDefinition> definitions, IReadOnlyCollection<string> resetIds)
    {
        if (resetIds.Count == 0) return false;
        var ids = resetIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var scanRequired = false;
        foreach (var definition in definitions.Where(x => ids.Contains(x.Id))) definition.CompletedRunUtc = null;
        WeeklyTaskDefinitionStore.Save(definitions);
        foreach (var definition in definitions.Where(x => ids.Contains(x.Id)))
        {
            definition.CompletedRunUtc = null;
            WeeklyTaskEditors.FirstOrDefault(x => x.Id == definition.Id)?.ApplyPlanningState(definition);
            if (string.Equals(definition.Type, "Quiz", StringComparison.OrdinalIgnoreCase))
            {
                var removed = _quizChallengeService.Reset(definition.QuizNumber, definition.Id);
                Log($"Quiz '{definition.Title}' fuer Reaktivierung zurueckgesetzt ({removed} Zustand/Zustaende)." );
            }
            else
            {
                var removed = WeeklyCommunityTaskService.DeleteSavedBaseline(definition);
                scanRequired = true;
                Log($"Challenge '{definition.Title}' fuer Reaktivierung zurueckgesetzt ({removed} Baseline-Datei(en))." );
            }
            ClearCachedWeeklyTaskProgress(definition.Id);
            WeeklyTaskEditors.FirstOrDefault(x => x.Id.Equals(definition.Id, StringComparison.OrdinalIgnoreCase))?.SetBaselineStart(null);
        }
        WeeklyTaskDefinitionStore.Save(definitions);
        return scanRequired;
    }

    private async Task RefreshReactivatedChallengesAsync()
    {
        try { await ScanWeeklyTasksOnceAsync(); }
        catch (Exception ex)
        {
            Log("Neuer Challenge-Nullpunkt konnte noch nicht gelesen werden: " + ex.Message);
            AppLogService.WriteException("WeeklyChallenges.ReactivationScan", ex);
        }
    }

    private async Task StopLocalChallengeAdminAsync()
    {
        var service = _localChallengeAdmin;
        _localChallengeAdmin = null;
        if (service is not null) await service.DisposeAsync();
        LocalChallengeAdminRunning = false;
        LocalChallengeAdminNetworkUrl = string.Empty;
        LocalChallengeAdminStatus = T("LocalWebAdminStopped");
    }

    private async Task DisposeLocalChallengeAdminAsync()
    {
        try { await StopLocalChallengeAdminAsync(); }
        catch (Exception ex) { AppLogService.WriteException("LocalWebAdmin.Stop", ex); }
    }
}
