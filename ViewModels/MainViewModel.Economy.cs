using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows.Input;
using ScumRconTool.Services;

namespace ScumRconTool.ViewModels;

public sealed partial class MainViewModel
{
    private readonly EconomyWebApiService _economyWebApi = new();
    private CancellationTokenSource? _economyCts;
    private bool _economyRunning;
    private bool _economyBusy;
    private string _economyStatus = string.Empty;
    private string _economySummary = string.Empty;

    public ObservableCollection<EconomyPriceChange> EconomyChanges { get; } = new();

    public bool EconomyRunning
    {
        get => _economyRunning;
        private set => SetProperty(ref _economyRunning, value);
    }

    public bool EconomyBusy
    {
        get => _economyBusy;
        private set
        {
            if (SetProperty(ref _economyBusy, value)) OnPropertyChanged(nameof(EconomyActionsEnabled));
        }
    }

    public bool EconomyActionsEnabled => !EconomyBusy;

    public string EconomyStatus
    {
        get => _economyStatus;
        private set => SetProperty(ref _economyStatus, value);
    }

    public string EconomySummary
    {
        get => _economySummary;
        private set => SetProperty(ref _economySummary, value);
    }

    public ICommand StartEconomyCommand { get; private set; } = null!;
    public ICommand StopEconomyCommand { get; private set; } = null!;
    public ICommand PreviewEconomyCommand { get; private set; } = null!;
    public ICommand UploadEconomyCommand { get; private set; } = null!;
    public ICommand OpenEconomyPreviewCommand { get; private set; } = null!;
    public ICommand PublishEconomyWebsiteCommand { get; private set; } = null!;

    private void InitializeEconomy()
    {
        StartEconomyCommand = new RelayCommand(_ => StartEconomy());
        StopEconomyCommand = new RelayCommand(_ => StopEconomy());
        PreviewEconomyCommand = new RelayCommand(async _ => await RunEconomyAsync(false));
        UploadEconomyCommand = new RelayCommand(async _ => await RunEconomyAsync(true));
        OpenEconomyPreviewCommand = new RelayCommand(_ => OpenEconomyPreview());
        PublishEconomyWebsiteCommand = new RelayCommand(async _ => await RunEconomyAsync(false, publishWebsite: true));
        EconomyStatus = Texts.IsGerman ? "Noch nicht gestartet." : "Not started yet.";
        EconomySummary = Texts.IsGerman
            ? "Noch keine Economy-Logs ausgewertet."
            : "No economy logs have been analyzed yet.";
    }

    private void StartEconomy(bool persistAutoStart = true)
    {
        if (EconomyRunning) return;
        EconomyManagerService.ParseSchedule(Settings.EconomyUploadTimes, Texts.IsGerman);
        _economyCts = new CancellationTokenSource();
        EconomyRunning = true;
        if (persistAutoStart)
        {
            Settings.AutoStartEconomy = true;
            SettingsStore.Save(Settings);
        }
        EconomyStatus = Texts.IsGerman ? "Economy-Monitoring laeuft." : "Economy monitoring is running.";
        _ = EconomyLoopAsync(_economyCts.Token);
    }

    private void StopEconomy(bool persistAutoStart = true)
    {
        _economyCts?.Cancel();
        _economyCts?.Dispose();
        _economyCts = null;
        EconomyRunning = false;
        if (persistAutoStart)
        {
            Settings.AutoStartEconomy = false;
            SettingsStore.Save(Settings);
        }
        EconomyStatus = Texts.IsGerman ? "Economy-Monitoring gestoppt." : "Economy monitoring stopped.";
    }

    private async Task EconomyLoopAsync(CancellationToken token)
    {
        var nextScan = DateTime.MinValue;
        try
        {
            while (!token.IsCancellationRequested)
            {
                var now = DateTime.Now;
                var upload = false;
                if (Settings.EconomyAutoUpload)
                {
                    var due = EconomyManagerService.GetDueScheduleKey(now, Settings.EconomyUploadTimes, Settings.EconomyLastUploadScheduleKey, Texts.IsGerman);
                    if (due is not null)
                    {
                        Settings.EconomyLastUploadScheduleKey = due;
                        SettingsStore.Save(Settings);
                        upload = true;
                    }
                }

                if (upload || now >= nextScan)
                {
                    await RunEconomyAsync(upload, token);
                    nextScan = DateTime.Now.AddMinutes(Math.Max(1, Settings.EconomyPollMinutes));
                }

                await Task.Delay(TimeSpan.FromSeconds(30), token);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            EconomyRunning = false;
            EconomyStatus = (Texts.IsGerman ? "Economy-Monitoring Fehler: " : "Economy monitoring error: ") + ex.Message;
            Log("Economy monitoring error: " + ex.Message);
            AppLogService.WriteException("EconomyMonitoring", ex);
        }
    }

    private async Task RunEconomyAsync(bool upload, CancellationToken token = default, bool publishWebsite = false)
    {
        if (EconomyBusy) return;
        EconomyBusy = true;
        EconomyStatus = Texts.IsGerman
            ? (upload ? "Logs werden ausgewertet und Override wird hochgeladen ..." : "Logs werden ausgewertet; Vorschau wird erstellt ...")
            : (upload ? "Analyzing logs and uploading the override ..." : "Analyzing logs and creating a preview ...");
        try
        {
            var service = new EconomyManagerService(Settings, Log, Texts.IsGerman);
            var result = await service.AnalyzeAsync(upload, token);
            var websitePublished = false;
            string? websiteWarning = null;
            if (publishWebsite && !Settings.EconomyWebApiEnabled)
                throw new InvalidOperationException(Texts.IsGerman ? "Aktiviere zuerst die Economy Web-API." : "Enable the Economy web API first.");
            if ((upload || publishWebsite) && Settings.EconomyWebApiEnabled)
            {
                try
                {
                    await _economyWebApi.PublishAsync(Settings, result.MarketItems, token);
                    websitePublished = true;
                    Log("Economy website updated: " + result.MarketItems.Count + " market items.");
                }
                catch (Exception webEx)
                {
                    websiteWarning = webEx.Message;
                    Log("Economy website update failed: " + webEx.Message);
                    AppLogService.WriteException("EconomyWebApi", webEx);
                }
            }
            void Apply()
            {
                EconomyChanges.Clear();
                foreach (var item in result.Changes) EconomyChanges.Add(item);
                var unknown = result.UnknownTraders.Count == 0
                    ? string.Empty
                    : (Texts.IsGerman ? " Nicht in der Override-Datei: " : " Not present in the override: ") + string.Join(", ", result.UnknownTraders) + ".";
                EconomySummary = Texts.IsGerman
                    ? string.Format(CultureInfo.CurrentCulture, "{0} Eintraege geprueft, {1} neue Trades, {2} betroffene Preise.{3}", result.OverrideItemCount, result.NewTransactionCount, result.Changes.Count, unknown)
                    : string.Format(CultureInfo.CurrentCulture, "{0} entries checked, {1} new trades, {2} affected prices.{3}", result.OverrideItemCount, result.NewTransactionCount, result.Changes.Count, unknown);
                var baseStatus = result.Uploaded
                    ? (Texts.IsGerman ? "Hochgeladen. Die Preise werden erst mit dem naechsten Serverneustart aktiv." : "Uploaded. Prices become active after the next server restart.")
                    : (Texts.IsGerman ? "Vorschau erstellt; auf dem Server wurde nichts geaendert." : "Preview created; nothing was changed on the server.");
                if (websitePublished) baseStatus += Texts.IsGerman ? " Homepage aktualisiert." : " Website updated.";
                if (websiteWarning is not null) baseStatus += (Texts.IsGerman ? " Homepage-Fehler: " : " Website error: ") + websiteWarning;
                EconomyStatus = baseStatus;
            }

            if (App.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess()) dispatcher.Invoke(Apply); else Apply();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            EconomyStatus = (Texts.IsGerman ? "Economy-Fehler: " : "Economy error: ") + ex.Message;
            Log("Economy error: " + ex.Message);
            AppLogService.WriteException("Economy", ex);
        }
        finally { EconomyBusy = false; }
    }

    private void OpenEconomyPreview()
    {
        if (!File.Exists(EconomyManagerService.PreviewFilePath))
        {
            EconomyStatus = Texts.IsGerman ? "Es wurde noch keine Vorschau erstellt." : "No preview has been created yet.";
            return;
        }
        Process.Start(new ProcessStartInfo(EconomyManagerService.PreviewFilePath) { UseShellExecute = true });
    }

    public void RefreshEconomyLanguage()
    {
        if (!EconomyRunning) EconomyStatus = Texts.IsGerman ? "Noch nicht gestartet." : "Not started yet.";
    }
}
