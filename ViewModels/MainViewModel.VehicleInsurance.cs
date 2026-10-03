using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Input;
using ScumRconTool.Services;

namespace ScumRconTool.ViewModels;

public sealed partial class MainViewModel
{
    private VehicleInsuranceService? _insuranceService;
    private GgconVehicleInsuranceApi? _insuranceApi;
    private readonly VehicleInsuranceWebApiService _insuranceWeb = new();
    private readonly SemaphoreSlim _insurancePollGate = new(1, 1);
    private CancellationTokenSource? _insuranceCts;
    private Task? _insuranceLoop;
    private string _insuranceStatus = "";
    public string InsuranceStatus { get => _insuranceStatus; private set => SetProperty(ref _insuranceStatus, value); }
    public ObservableCollection<VehicleInsurancePrice> InsurancePrices { get; } = new();
    public ObservableCollection<VehicleInsurancePolicy> InsurancePolicies { get; } = new();
    public ICommand SaveInsuranceSettingsCommand { get; private set; } = null!;
    public ICommand LoadInsuranceTypesCommand { get; private set; } = null!;
    public ICommand RefreshInsuranceCommand { get; private set; } = null!;
    public ICommand PublishInsuranceCommand { get; private set; } = null!;
    public ICommand InsuranceTransactionSucceededCommand { get; private set; } = null!;
    public ICommand InsuranceTransactionFailedCommand { get; private set; } = null!;

    private void InitializeVehicleInsurance()
    {
        Settings.VehicleInsurance ??= new();
        foreach (var p in Settings.VehicleInsurance.Prices) InsurancePrices.Add(p);
        SaveInsuranceSettingsCommand = new RelayCommand(async _ => { SyncVehicleInsuranceSettings(); SettingsStore.Save(Settings); await ApplyVehicleInsuranceSettingsAsync(); StartVehicleInactivityWarnings(); });
        LoadInsuranceTypesCommand = new RelayCommand(async _ =>
        {
            var api = _insuranceApi ?? throw new InvalidOperationException(InsuranceStatus);
            foreach (var type in (await api.TypesAsync(CancellationToken.None)).OrderBy(x => x))
                if (!InsurancePrices.Any(p => VehicleInsurancePricing.NormalizeClass(p.VehicleClass) == VehicleInsurancePricing.NormalizeClass(type)))
                    InsurancePrices.Add(new VehicleInsurancePrice { VehicleClass = type, WeeklyPrice = Settings.VehicleInsurance.DefaultWeeklyPrice });
            InsuranceStatus = Texts.IsGerman ? "Fahrzeugtypen geladen. Preise prüfen und speichern." : "Vehicle types loaded. Check prices and save.";
        });
        RefreshInsuranceCommand = new RelayCommand(async _ => await ScanInsuranceAsync(false, CancellationToken.None));
        PublishInsuranceCommand = new RelayCommand(async _ => await ScanInsuranceAsync(true, CancellationToken.None));
        InsuranceTransactionSucceededCommand = new RelayCommand(async p => await ResolveInsuranceAsync(p as VehicleInsurancePolicy, true));
        InsuranceTransactionFailedCommand = new RelayCommand(async p => await ResolveInsuranceAsync(p as VehicleInsurancePolicy, false));
        try
        {
            _insuranceApi = new GgconVehicleInsuranceApi(Settings);
            var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(GgconVehicleInsuranceApi.ServerKey(Settings))))[..20];
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScumRconTool", "State", "vehicle-insurance-" + key + ".json");
            _insuranceService = new VehicleInsuranceService(_insuranceApi, Settings.VehicleInsurance, path, Log);
            InsuranceStatus = Settings.VehicleInsurance.Enabled ? "Insurance: ready / bereit" : "Insurance: disabled / deaktiviert";
        }
        catch (Exception ex) { InsuranceStatus = "Insurance LOCKED: " + ex.Message; AppLogService.WriteException("Insurance.Load", ex); }
    }

    private void SyncVehicleInsuranceSettings()
    {
        Settings.VehicleInsurance.Prices = InsurancePrices.ToList();
        Settings.VehicleInsurance.Validate();
    }

    private async Task ApplyVehicleInsuranceSettingsAsync()
    {
        if (_insuranceService is null)
        {
            if (!Settings.VehicleInsurance.Enabled) return;
            throw new InvalidOperationException(InsuranceStatus);
        }
        _insuranceService.Configure(Settings.VehicleInsurance);
        if (_insuranceCts is not null)
        {
            _insuranceCts.Cancel();
            if (_insuranceLoop is not null) await _insuranceLoop;
            _insuranceCts.Dispose(); _insuranceCts = null;
        }
        if (Settings.VehicleInsurance.Enabled)
        {
            if (_chatCommands?.IsRunning != true) await StartChatCommandsAsync(false, false);
            _insuranceCts = new CancellationTokenSource();
            var token = _insuranceCts.Token;
            _insuranceLoop = Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    try { await ScanInsuranceAsync(false, token); }
                    catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
                    catch (Exception ex)
                    {
                        Log("Insurance monitor: " + ex.Message);
                        await Application.Current.Dispatcher.InvokeAsync(() => InsuranceStatus = "Insurance: " + ex.Message);
                    }
                    try { await Task.Delay(TimeSpan.FromSeconds(15), token); }
                    catch (OperationCanceledException) { break; }
                }
            });
        }
        InsuranceStatus = Settings.VehicleInsurance.Enabled ? "Insurance: active / aktiv" : "Insurance: disabled / deaktiviert";
        await RefreshInsuranceRowsAsync();
    }

    private async Task RefreshInsuranceRowsAsync()
    {
        if (_insuranceService is null) return;
        var policies = await _insuranceService.SnapshotAsync();
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            InsurancePolicies.Clear();
            foreach (var p in policies.OrderByDescending(p => p.StartUtc)) InsurancePolicies.Add(p);
        });
    }

    private async Task ScanInsuranceAsync(bool forcePublish, CancellationToken ct)
    {
        if (_insuranceService is null) throw new InvalidOperationException(InsuranceStatus);
        await _insurancePollGate.WaitAsync(ct);
        try
        {
            await _insuranceService.PollAsync(ct);
            await RefreshInsuranceRowsAsync();
            await _insuranceWeb.PublishAsync(_insuranceService.Options, await _insuranceService.SnapshotAsync(), forcePublish, ct);
            await Application.Current.Dispatcher.InvokeAsync(() => InsuranceStatus = $"Insurance: {DateTime.Now:T} · {InsurancePolicies.Count} contracts / Verträge");
        }
        finally { _insurancePollGate.Release(); }
    }

    private async Task<bool> HandleVehicleInsuranceChatAsync(ChatLogMessage message, CancellationToken ct)
    {
        if (_insuranceService is null) return false;
        return await _insuranceService.HandleAsync(message, Texts.IsGerman, ct);
    }

    private async Task ResolveInsuranceAsync(VehicleInsurancePolicy? policy, bool succeeded)
    {
        if (policy is null || _insuranceService is null) return;
        if (policy.Status is not (Services.InsuranceStatus.PaymentReview or Services.InsuranceStatus.SpawnReview))
            throw new InvalidOperationException(Texts.IsGerman ? "Nur offene Zahlungs-/Spawnprüfungen können hier bestätigt werden." : "Only pending payment/spawn reviews can be resolved here.");
        var text = Texts.IsGerman
            ? $"{policy.Id}: Hast du in den ggCON-/Serverlogs geprüft, dass die Transaktion {(succeeded ? "erfolgreich war" : "NICHT ausgeführt wurde")}? Dies ändert nur den Vertragsstatus. Es wird kein Geld gebucht und kein Fahrzeug erzeugt."
            : $"{policy.Id}: Did you verify in the ggCON/server logs that the transaction {(succeeded ? "succeeded" : "did NOT happen")}? Only the policy status changes. No money or vehicle is issued.";
        if (MessageBox.Show(text, "Insurance", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        await _insuranceService.ResolveAsync(policy.Id, succeeded);
        await RefreshInsuranceRowsAsync();
    }
}
