using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Windows.Input;
using ScumRconTool.Services;
using ScumRconTool.Views;

namespace ScumRconTool.ViewModels;

public sealed partial class MainViewModel
{
    private CancellationTokenSource? _settingRandomizerCts;
    private SettingRandomizerCatalogEntry? _selectedSettingRandomizerCatalogEntry;
    private bool _settingRandomizerRunning;
    private string _settingRandomizerStatus = "Noch nicht gestartet.";
    private string _settingRandomizerPreview = "Noch keine Auswahl erzeugt.";
    private SettingRandomizerResult? _lastSettingRandomizerResult;
    private string? _settingRandomizerCatalogIniText;

    public ObservableCollection<SettingRandomizerRuleEditorViewModel> SettingRandomizerRules { get; } = new();
    public ObservableCollection<SettingRandomizerPackEditorViewModel> SettingRandomizerPacks { get; } = new();
    public ObservableCollection<SettingRandomizerCatalogEntry> SettingRandomizerCatalog { get; } = new();
    public ObservableCollection<ServerSettingsValidationIssue> ServerSettingsValidationIssues { get; } = new();
    private string _serverSettingsValidationSummary = string.Empty;
    public string ServerSettingsValidationSummary { get => _serverSettingsValidationSummary; private set => SetProperty(ref _serverSettingsValidationSummary, value); }

    public SettingRandomizerCatalogEntry? SelectedSettingRandomizerCatalogEntry
    {
        get => _selectedSettingRandomizerCatalogEntry;
        set => SetProperty(ref _selectedSettingRandomizerCatalogEntry, value);
    }

    public bool SettingRandomizerRunning
    {
        get => _settingRandomizerRunning;
        private set => SetProperty(ref _settingRandomizerRunning, value);
    }

    public string SettingRandomizerStatus
    {
        get => _settingRandomizerStatus;
        private set => SetProperty(ref _settingRandomizerStatus, value);
    }

    public string SettingRandomizerPreview
    {
        get => _settingRandomizerPreview;
        private set => SetProperty(ref _settingRandomizerPreview, value);
    }

    public ICommand StartSettingRandomizerCommand { get; private set; } = null!;
    public ICommand StopSettingRandomizerCommand { get; private set; } = null!;
    public ICommand ApplySettingRandomizerNowCommand { get; private set; } = null!;
    public ICommand PreviewSettingRandomizerCommand { get; private set; } = null!;
    public ICommand AddSettingRandomizerRuleCommand { get; private set; } = null!;
    public ICommand RemoveSettingRandomizerRuleCommand { get; private set; } = null!;
    public ICommand AddSettingRandomizerOptionCommand { get; private set; } = null!;
    public ICommand RemoveSettingRandomizerOptionCommand { get; private set; } = null!;
    public ICommand ResetSettingRandomizerCommand { get; private set; } = null!;
    public ICommand ValidateServerSettingsCommand { get; private set; } = null!;
    public ICommand AddSettingRandomizerPackCommand { get; private set; } = null!;
    public ICommand AddGasolineRandomizerPackCommand { get; private set; } = null!;
    public ICommand RemoveSettingRandomizerPackCommand { get; private set; } = null!;
    public ICommand AddSettingRandomizerPackOptionCommand { get; private set; } = null!;
    public ICommand AddSettingRandomizerPackSettingCommand { get; private set; } = null!;
    public ICommand RemoveSettingRandomizerPackOptionCommand { get; private set; } = null!;
    public ICommand AddSettingRandomizerPackValueCommand { get; private set; } = null!;
    public ICommand RemoveSettingRandomizerPackValueCommand { get; private set; } = null!;

    private void InitializeSettingRandomizer()
    {
        StartSettingRandomizerCommand = new RelayCommand(_ => StartSettingRandomizer());
        StopSettingRandomizerCommand = new RelayCommand(_ => StopSettingRandomizer());
        ApplySettingRandomizerNowCommand = new RelayCommand(async _ => await ApplySettingRandomizerAsync(upload: true));
        PreviewSettingRandomizerCommand = new RelayCommand(async _ => await ApplySettingRandomizerAsync(upload: false));
        AddSettingRandomizerRuleCommand = new RelayCommand(_ => AddSettingRandomizerRule());
        RemoveSettingRandomizerRuleCommand = new RelayCommand(rule => RemoveSettingRandomizerRule(rule as SettingRandomizerRuleEditorViewModel));
        AddSettingRandomizerOptionCommand = new RelayCommand(rule => AddSettingRandomizerOption(rule as SettingRandomizerRuleEditorViewModel));
        RemoveSettingRandomizerOptionCommand = new RelayCommand(option => RemoveSettingRandomizerOption(option as SettingRandomizerOptionEditorViewModel));
        ResetSettingRandomizerCommand = new RelayCommand(_ => ResetSettingRandomizer());
        ValidateServerSettingsCommand = new RelayCommand(async _ => await ValidateServerSettingsAsync());
        AddSettingRandomizerPackCommand = new RelayCommand(async _ => await AddSettingRandomizerPackAsync());
        AddGasolineRandomizerPackCommand = new RelayCommand(_ => AddGasolineRandomizerPack());
        RemoveSettingRandomizerPackCommand = new RelayCommand(pack => RemoveSettingRandomizerPack(pack as SettingRandomizerPackEditorViewModel));
        AddSettingRandomizerPackOptionCommand = new RelayCommand(pack => AddSettingRandomizerPackOption(pack as SettingRandomizerPackEditorViewModel));
        AddSettingRandomizerPackSettingCommand = new RelayCommand(async pack => await AddSettingRandomizerPackSettingAsync(pack as SettingRandomizerPackEditorViewModel));
        RemoveSettingRandomizerPackOptionCommand = new RelayCommand(option => RemoveSettingRandomizerPackOption(option as SettingRandomizerPackOptionEditorViewModel));
        AddSettingRandomizerPackValueCommand = new RelayCommand(option => (option as SettingRandomizerPackOptionEditorViewModel)?.Values.Add(new SettingRandomizerPackValueEditorViewModel()));
        RemoveSettingRandomizerPackValueCommand = new RelayCommand(value => RemoveSettingRandomizerPackValue(value as SettingRandomizerPackValueEditorViewModel));
        RefreshSettingRandomizerCatalog();
        LoadSettingRandomizerRulesFromSettings();
        LoadSettingRandomizerPacksFromSettings();
        SelectedSettingRandomizerCatalogEntry = SettingRandomizerCatalog.FirstOrDefault();
        SettingRandomizerStatus = T("RandomizerNotStarted");
        SettingRandomizerPreview = T("RandomizerNoSelection");
    }

    private void RefreshSettingRandomizerCatalog(string? iniText = null)
    {
        if (!string.IsNullOrWhiteSpace(iniText)) _settingRandomizerCatalogIniText = iniText;
        if (string.IsNullOrWhiteSpace(_settingRandomizerCatalogIniText) && File.Exists(SettingRandomizerService.LastUploadedCachePath))
        {
            try { _settingRandomizerCatalogIniText = File.ReadAllText(SettingRandomizerService.LastUploadedCachePath); }
            catch (Exception ex) { Log("SettingRandomizer: lokaler INI-Katalog konnte nicht gelesen werden: " + ex.Message); }
        }

        var selectedKey = SelectedSettingRandomizerCatalogEntry?.Key;
        var entries = string.IsNullOrWhiteSpace(_settingRandomizerCatalogIniText)
            ? SettingRandomizerConfiguration.GetCatalog(Texts.IsGerman)
            : SettingRandomizerConfiguration.BuildCatalogFromIni(_settingRandomizerCatalogIniText, Texts.IsGerman);
        SettingRandomizerCatalog.Clear();
        foreach (var entry in entries) SettingRandomizerCatalog.Add(entry);
        SelectedSettingRandomizerCatalogEntry = SettingRandomizerCatalog.FirstOrDefault(x => x.Key.Equals(selectedKey, StringComparison.OrdinalIgnoreCase))
            ?? SettingRandomizerCatalog.FirstOrDefault();
    }

    private async Task RefreshSettingRandomizerCatalogFromServerAsync()
    {
        try
        {
            SettingRandomizerStatus = Texts.IsGerman
                ? "Aktuelle ServerSettings.ini wird für den Einstellungskatalog gelesen ..."
                : "Loading the current ServerSettings.ini for the settings catalog ...";
            var service = new SettingRandomizerService(Settings, Log, Texts.IsGerman);
            var text = await service.DownloadCurrentAsync();
            RefreshSettingRandomizerCatalog(text);
            SettingRandomizerStatus = Texts.IsGerman
                ? $"{SettingRandomizerCatalog.Count} aktuelle Servereinstellungen geladen."
                : $"Loaded {SettingRandomizerCatalog.Count} current server settings.";
        }
        catch (Exception ex)
        {
            Log("SettingRandomizer: aktueller INI-Katalog konnte nicht geladen werden: " + ex.Message);
            SettingRandomizerStatus = Texts.IsGerman
                ? "Aktuelle INI konnte nicht gelesen werden; vorhandener Katalog wird verwendet."
                : "Could not read the current INI; using the existing catalog.";
        }
    }

    private void LoadSettingRandomizerRulesFromSettings()
    {
        SettingRandomizerRules.Clear();
        try
        {
            foreach (var rule in SettingRandomizerConfiguration.Parse(Settings.SettingRandomizerRulesJson))
            {
                SettingRandomizerRules.Add(SettingRandomizerRuleEditorViewModel.FromRule(rule, Texts.IsGerman));
            }
        }
        catch (Exception ex)
        {
            Log("SettingRandomizer: Regeln konnten nicht geladen werden, Standard wird verwendet: " + ex.Message);
            foreach (var rule in SettingRandomizerConfiguration.Parse(SettingRandomizerConfiguration.BuildDefaultJson()))
            {
                SettingRandomizerRules.Add(SettingRandomizerRuleEditorViewModel.FromRule(rule, Texts.IsGerman));
            }
        }
    }

    private void SyncSettingRandomizerRulesToSettings()
    {
        Settings.SettingRandomizerRulesJson = "[]";
        Settings.SettingRandomizerPacksJson = SettingRandomizerConfiguration.SerializePacks(
            SettingRandomizerPacks.Select(x => x.ToPack()));
    }

    private void LoadSettingRandomizerPacksFromSettings()
    {
        SettingRandomizerRules.Clear();
        SettingRandomizerPacks.Clear();
        try
        {
            var packs = SettingRandomizerConfiguration.ParsePacks(Settings.SettingRandomizerPacksJson);
            var legacyRules = SettingRandomizerConfiguration.Parse(Settings.SettingRandomizerRulesJson);
            foreach (var rule in legacyRules)
            {
                var alreadyIncluded = packs.Any(pack => pack.Options.SelectMany(option => option.Values).Any(value =>
                    value.Section.Equals(rule.Section, StringComparison.OrdinalIgnoreCase) &&
                    value.Key.Equals(rule.Key, StringComparison.OrdinalIgnoreCase)));
                if (!alreadyIncluded) packs.Add(SettingRandomizerConfiguration.ConvertRuleToPack(rule));
            }

            foreach (var pack in packs)
                SettingRandomizerPacks.Add(SettingRandomizerPackEditorViewModel.FromPack(pack, Texts.IsGerman));

            if (legacyRules.Count > 0)
            {
                Settings.SettingRandomizerRulesJson = "[]";
                Settings.SettingRandomizerPacksJson = SettingRandomizerConfiguration.SerializePacks(packs);
                SettingsStore.Save(Settings);
                Log($"SettingRandomizer: {legacyRules.Count} bisherige Einzelregel(n) wurden in Würfelsets migriert.");
            }
        }
        catch (Exception ex)
        {
            Log("SettingRandomizer: Würfelsets konnten nicht geladen werden, Standard wird verwendet: " + ex.Message);
            foreach (var pack in SettingRandomizerConfiguration.ParsePacks(SettingRandomizerConfiguration.BuildDefaultPacksJson()))
                SettingRandomizerPacks.Add(SettingRandomizerPackEditorViewModel.FromPack(pack, Texts.IsGerman));
        }
    }

    private void AddSettingRandomizerRule()
    {
        var entry = SelectedSettingRandomizerCatalogEntry;
        if (entry is null) return;
        if (SettingRandomizerRules.Any(x => x.Section.Equals(entry.Section, StringComparison.OrdinalIgnoreCase) &&
                                            x.Key.Equals(entry.Key, StringComparison.OrdinalIgnoreCase)))
        {
            SettingRandomizerStatus = T("RandomizerAlreadyInPlan");
            return;
        }

        var editor = SettingRandomizerRuleEditorViewModel.FromRule(entry.CreateRule(enabled: true), Texts.IsGerman);
        SettingRandomizerRules.Add(editor);
        SettingRandomizerStatus = Tf("RandomizerAdded", entry.DisplayName);
    }

    private void RemoveSettingRandomizerRule(SettingRandomizerRuleEditorViewModel? rule)
    {
        if (rule is null) return;
        SettingRandomizerRules.Remove(rule);
    }

    private void AddSettingRandomizerOption(SettingRandomizerRuleEditorViewModel? rule)
    {
        rule?.AddOption(new SettingRandomizerOptionEditorViewModel
        {
            Value = T("RandomizerNewValue"),
            ChancePercent = 0,
            DiscordAnnouncement = string.Empty
        });
    }

    private void RemoveSettingRandomizerOption(SettingRandomizerOptionEditorViewModel? option)
    {
        if (option is null) return;
        foreach (var rule in SettingRandomizerRules)
        {
            if (rule.Options.Contains(option))
            {
                rule.RemoveOption(option);
                break;
            }
        }
    }

    private async Task AddSettingRandomizerPackAsync()
    {
        await RefreshSettingRandomizerCatalogFromServerAsync();
        var creation = SettingRandomizerPackDialog.Create(SettingRandomizerCatalog, Texts.IsGerman);
        if (creation is null) return;
        var existingPack = SettingRandomizerPacks.FirstOrDefault(x => x.ContainsKey(creation.Key));
        if (existingPack is not null)
        {
            SettingRandomizerStatus = Texts.IsGerman
                ? $"'{creation.DisplayName}' wird bereits vom Set '{existingPack.Name}' gesteuert."
                : $"'{creation.DisplayName}' is already controlled by set '{existingPack.Name}'.";
            return;
        }

        var pack = new SettingRandomizerPackEditorViewModel { Name = creation.SetName, Enabled = false };
        var suggestedValues = GetSuggestedValues(creation);
        for (var index = 1; index <= creation.VariantCount; index++)
        {
            var suggestedValue = suggestedValues[(index - 1) % suggestedValues.Count];
            var option = new SettingRandomizerPackOptionEditorViewModel
            {
                Name = (Texts.IsGerman ? $"Variante {index}" : $"Variant {index}") + $" – {suggestedValue}",
                StatusText = string.Empty
            };
            var value = CreatePackValue(creation);
            value.Value = suggestedValue;
            option.Values.Add(value);
            pack.AddOption(option);
        }
        pack.RedistributeChances();
        SettingRandomizerPacks.Add(pack);
        SettingRandomizerStatus = Texts.IsGerman
            ? $"Set '{creation.SetName}' wurde erstellt. Es wird unabhängig von den anderen aktiven Sets gewürfelt."
            : $"Set '{creation.SetName}' was created. It is rolled independently of the other active sets.";
    }

    private async Task AddSettingRandomizerPackSettingAsync(SettingRandomizerPackEditorViewModel? pack)
    {
        if (pack is null) return;
        await RefreshSettingRandomizerCatalogFromServerAsync();
        var selection = SettingRandomizerPackDialog.PickSetting(SettingRandomizerCatalog, Texts.IsGerman);
        if (selection is null) return;
        var owningPack = SettingRandomizerPacks.FirstOrDefault(x => x.ContainsKey(selection.Key));
        if (owningPack is not null)
        {
            SettingRandomizerStatus = Texts.IsGerman
                ? $"'{selection.DisplayName}' wird bereits vom Set '{owningPack.Name}' gesteuert."
                : $"'{selection.DisplayName}' is already controlled by set '{owningPack.Name}'.";
            return;
        }

        var suggestedValues = GetSuggestedValues(selection);
        for (var index = 0; index < pack.Options.Count; index++)
        {
            var value = CreatePackValue(selection);
            value.Value = suggestedValues[index % suggestedValues.Count];
            pack.Options[index].Values.Add(value);
        }
        SettingRandomizerStatus = Texts.IsGerman
            ? $"'{selection.DisplayName}' wurde zu allen Varianten von '{pack.Name}' hinzugefügt."
            : $"'{selection.DisplayName}' was added to every variant of '{pack.Name}'.";
    }

    private IReadOnlyList<string> GetSuggestedValues(SettingRandomizerPackDialogResult selection)
    {
        var catalogEntry = SettingRandomizerCatalog.FirstOrDefault(x =>
            x.Section.Equals(selection.Section, StringComparison.OrdinalIgnoreCase) &&
            x.Key.Equals(selection.Key, StringComparison.OrdinalIgnoreCase));
        return new[] { selection.Value }
            .Concat(catalogEntry?.DefaultOptions.Select(x => x.Value) ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
    private static SettingRandomizerPackValueEditorViewModel CreatePackValue(SettingRandomizerPackDialogResult selection) => new()
    {
        Section = selection.Section,
        Key = selection.Key,
        DisplayName = selection.DisplayName,
        Value = selection.Value
    };

    private void AddSettingRandomizerPackOption(SettingRandomizerPackEditorViewModel? pack)
    {
        if (pack is null) return;
        var option = new SettingRandomizerPackOptionEditorViewModel
        {
            Name = Texts.IsGerman ? $"Variante {pack.Options.Count + 1}" : $"Variant {pack.Options.Count + 1}",
            StatusText = string.Empty
        };
        foreach (var value in pack.Options.FirstOrDefault()?.Values ?? [])
            option.Values.Add(SettingRandomizerPackValueEditorViewModel.FromValue(value.ToValue()));
        pack.AddOption(option);
        pack.RedistributeChances();
    }

    private void AddGasolineRandomizerPack()
    {
        if (SettingRandomizerPacks.Any(x => x.ContainsKey("scum.GasolinePricePerUnitMultiplier")))
        {
            SettingRandomizerStatus = T("RandomizerGasolinePackExists");
            return;
        }
        SettingRandomizerPacks.Add(SettingRandomizerPackEditorViewModel.FromPack(SettingRandomizerConfiguration.BuildGasolinePack(), Texts.IsGerman));
        SettingRandomizerStatus = T("RandomizerGasolinePackAdded");
    }

    private void RemoveSettingRandomizerPack(SettingRandomizerPackEditorViewModel? pack)
    {
        if (pack is not null) SettingRandomizerPacks.Remove(pack);
    }

    private void RemoveSettingRandomizerPackOption(SettingRandomizerPackOptionEditorViewModel? option)
    {
        if (option is null) return;
        foreach (var pack in SettingRandomizerPacks)
        {
            if (!pack.Options.Contains(option)) continue;
            if (pack.Options.Count <= 1)
            {
                SettingRandomizerStatus = Texts.IsGerman
                    ? "Ein Set benötigt mindestens eine Variante."
                    : "A set requires at least one variant.";
                return;
            }
            pack.RemoveOption(option);
            pack.RedistributeChances();
            return;
        }
    }

    private void RemoveSettingRandomizerPackValue(SettingRandomizerPackValueEditorViewModel? value)
    {
        if (value is null) return;
        foreach (var pack in SettingRandomizerPacks)
        {
            if (!pack.Options.SelectMany(x => x.Values).Contains(value)) continue;
            foreach (var option in pack.Options)
            {
                foreach (var match in option.Values.Where(x =>
                             x.Section.Equals(value.Section, StringComparison.OrdinalIgnoreCase) &&
                             x.Key.Equals(value.Key, StringComparison.OrdinalIgnoreCase)).ToList())
                    option.Values.Remove(match);
            }
            return;
        }
    }

    private void ResetSettingRandomizer()
    {
        Settings.SettingRandomizerRulesJson = "[]";
        Settings.SettingRandomizerPacksJson = SettingRandomizerConfiguration.BuildDefaultPacksJson();
        LoadSettingRandomizerPacksFromSettings();
        SettingRandomizerStatus = T("RandomizerPlanRestored");
    }
    private void StartSettingRandomizer(bool persistAutoStart = true)
    {
        SyncSettingRandomizerRulesToSettings();
        SettingRandomizerService.ValidateRules(SettingRandomizerRules.Select(x => x.ToRule()), SettingRandomizerPacks.Select(x => x.ToPack()), Texts.IsGerman);
        var schedule = SettingRandomizerService.ParseSchedule(Settings.SettingRandomizerScheduleTimes, Texts.IsGerman);

        _settingRandomizerCts?.Cancel();
        _settingRandomizerCts?.Dispose();
        _settingRandomizerCts = new CancellationTokenSource();
        if (persistAutoStart)
        {
            Settings.AutoStartSettingRandomizer = true;
            SettingsStore.Save(Settings);
        }

        SettingRandomizerRunning = true;
        SettingRandomizerStatus = Tf("RandomizerScheduleNext", FormatNextSettingRandomizerRun(schedule, DateTime.Now));
        _ = RunSettingRandomizerLoopAsync(_settingRandomizerCts.Token);
        Log("SettingRandomizer gestartet. Zeiten: " + Settings.SettingRandomizerScheduleTimes);
        _ = EnsureSettingRandomizerChatCommandsAsync();
    }

    private void StopSettingRandomizer(bool persistAutoStart = true)
    {
        _settingRandomizerCts?.Cancel();
        _settingRandomizerCts?.Dispose();
        _settingRandomizerCts = null;
        if (persistAutoStart)
        {
            Settings.AutoStartSettingRandomizer = false;
            SettingsStore.Save(Settings);
        }
        SettingRandomizerRunning = false;
        SettingRandomizerStatus = T("RandomizerScheduleStopped");
    }

    private async Task RunSettingRandomizerLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var dueKey = SettingRandomizerService.GetDueScheduleKey(
                    DateTime.Now,
                    Settings.SettingRandomizerScheduleTimes,
                    Settings.SettingRandomizerLastScheduleKey,
                    Texts.IsGerman);

                if (dueKey is not null)
                {
                    await ApplySettingRandomizerAsync(upload: true, cancellationToken);
                    Settings.SettingRandomizerLastScheduleKey = dueKey;
                    SettingsStore.Save(Settings);
                }

                await Task.Delay(TimeSpan.FromSeconds(15), cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            AppLogService.WriteException("SettingRandomizerLoop", ex);
            Log("SettingRandomizer Zeitplan-Fehler: " + ex.Message);
            SetSettingRandomizerState(false, Tf("RandomizerScheduleError", ex.Message));
        }
    }

    private async Task EnsureSettingRandomizerChatCommandsAsync()
    {
        if (_chatCommands?.IsRunning == true) return;
        try
        {
            await StartChatCommandsAsync(persistAutoStart: false, ensureDefaultRules: false);
        }
        catch (Exception ex)
        {
            Log("SettingRandomizer: Chatbefehle für /mechs konnten nicht gestartet werden: " + ex.Message);
            AppLogService.WriteException("SettingRandomizer.ChatCommands", ex);
        }
    }

    private async Task<bool> HandleMechsChatCommandAsync(ChatLogMessage message, CancellationToken cancellationToken)
    {
        var text = (message.Message ?? string.Empty).Trim();
        if (!BuiltinChatCommandCatalog.Is(BuiltinChatCommandId.Mechs, text)) return false;
        if (string.IsNullOrWhiteSpace(message.SteamId)) return true;

        string response;
        try
        {
            var value = SettingRandomizerService.ReadLastUploadedValue("World", "scum.DisableSentrySpawning");

            if (!bool.TryParse(value, out var spawningDisabled))
            {
                response = T("MechsStatusUnknown");
            }
            else
            {
                response = spawningDisabled ? T("MechsStatusOff") : T("MechsStatusOn");
            }
        }
        catch (Exception ex)
        {
            response = T("MechsStatusError");
            Log("/mechs konnte den lokal gespeicherten ServerSettings-Stand nicht lesen: " + ex.Message);
            AppLogService.WriteException("SettingRandomizer.MechsCommand", ex);
        }

        await SendWeeklyRewardNoticeAsync(message.SteamId, response, cancellationToken);
        return true;
    }

    private async Task ApplySettingRandomizerAsync(bool upload, CancellationToken cancellationToken = default)
    {
        SyncSettingRandomizerRulesToSettings();
        var rules = SettingRandomizerRules.Select(x => x.ToRule()).ToList();
        var service = new SettingRandomizerService(Settings, Log, Texts.IsGerman);
        SetSettingRandomizerStatus(upload ? T("RandomizerApplying") : T("RandomizerGeneratingPreview"));
        var packs = SettingRandomizerPacks.Select(x => x.ToPack()).ToList();
        var result = await service.ApplyAsync(rules, packs, upload, cancellationToken);
        _lastSettingRandomizerResult = result;
        SettingRandomizerPreview = result.GetSummary(Texts.IsGerman);
        RefreshSettingRandomizerCatalog(result.UpdatedText);
        UpdateServerSettingsValidation(result.UpdatedText);
        RefreshPuppetSpawnPreviews();

        if (upload)
        {
            SettingsStore.Save(Settings);
            SetSettingRandomizerStatus(T("RandomizerUploadSuccess"));
        }
        else
        {
            SetSettingRandomizerStatus(T("RandomizerPreviewComplete"));
        }

        Log("SettingRandomizer " + (upload ? (Texts.IsGerman ? "angewendet: " : "applied: ") : "Test: ") + result.GetSummary(Texts.IsGerman));
    }

    private async Task ValidateServerSettingsAsync()
    {
        try
        {
            ServerSettingsValidationSummary = Texts.IsGerman
                ? "ServerSettings.ini wird geladen und geprüft ..."
                : "Loading and validating ServerSettings.ini ...";
            var service = new SettingRandomizerService(Settings, Log, Texts.IsGerman);
            var text = await service.DownloadCurrentAsync();
            RefreshSettingRandomizerCatalog(text);
            UpdateServerSettingsValidation(text);
            RefreshPuppetSpawnPreviews();
        }
        catch (Exception ex)
        {
            ServerSettingsValidationSummary = Texts.IsGerman
                ? "Prüfung fehlgeschlagen: " + ex.Message
                : "Validation failed: " + ex.Message;
            AppLogService.WriteException("SettingRandomizer.ValidateServerSettings", ex);
        }
    }

    private void UpdateServerSettingsValidation(string text)
    {
        var issues = ServerSettingsValidationService.Validate(text, Texts.IsGerman).ToList();
        var availableTargets = ServerSettingsValidationService.ParseEntries(text)
            .Select(x => x.Section.Trim() + "/" + x.Key.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var configuredTargets = SettingRandomizerPacks.SelectMany(pack => pack.Options.SelectMany(option => option.Values)
                .Where(value => !string.IsNullOrWhiteSpace(value.Section) && !string.IsNullOrWhiteSpace(value.Key))
                .Select(value => new
                {
                    Id = value.Section.Trim() + "/" + value.Key.Trim(),
                    value.Section,
                    value.Key,
                    PackName = string.IsNullOrWhiteSpace(pack.Name) ? (Texts.IsGerman ? "Unbenanntes Set" : "Unnamed set") : pack.Name
                })
                .GroupBy(value => value.Id, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First()))
            .ToList();

        foreach (var conflict in configuredTargets.GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase).Where(x => x.Count() > 1))
        {
            var owners = string.Join(", ", conflict.Select(x => x.PackName).Distinct(StringComparer.OrdinalIgnoreCase));
            issues.Add(new ServerSettingsValidationIssue(
                "Danger",
                Texts.IsGerman ? "Würfelsets bearbeiten denselben Wert" : "Dice sets edit the same value",
                Texts.IsGerman
                    ? $"{conflict.Key} wird von mehreren Sets bearbeitet: {owners}."
                    : $"{conflict.Key} is edited by multiple sets: {owners}.",
                [conflict.Key]));
        }

        foreach (var missing in configuredTargets.Where(x => !availableTargets.Contains(x.Id)))
        {
            issues.Add(new ServerSettingsValidationIssue(
                "Danger",
                Texts.IsGerman ? "Servereinstellung nicht mehr vorhanden" : "Server setting no longer exists",
                Texts.IsGerman
                    ? $"{missing.Key} aus '{missing.PackName}' ist in der aktuellen INI nicht mehr enthalten."
                    : $"{missing.Key} from '{missing.PackName}' is no longer present in the current INI.",
                [missing.Id]));
        }
        ServerSettingsValidationIssues.Clear();
        foreach (var issue in issues) ServerSettingsValidationIssues.Add(issue);
        var danger = issues.Count(x => x.Severity.Equals("Danger", StringComparison.OrdinalIgnoreCase));
        var warnings = issues.Count(x => x.Severity.Equals("Warning", StringComparison.OrdinalIgnoreCase));
        ServerSettingsValidationSummary = issues.Count == 0
            ? (Texts.IsGerman ? "Keine Auffälligkeiten gefunden." : "No issues found.")
            : (Texts.IsGerman
                ? $"{issues.Count} Hinweis(e): {danger} kritisch, {warnings} Warnung(en)."
                : $"{issues.Count} issue(s): {danger} critical, {warnings} warning(s).");
    }

    private void RefreshPuppetSpawnPreviews()
    {
        foreach (var option in SettingRandomizerPacks.SelectMany(x => x.Options)) option.RefreshPuppetSpawnPreview();
    }
    private IReadOnlyList<string> GetCurrentSettingRandomizerStatusTexts()
    {
        try
        {
            var texts = new List<string>();
            foreach (var rule in SettingRandomizerConfiguration.Parse(Settings.SettingRandomizerRulesJson).Where(x => x.Enabled))
            {
                var currentValue = SettingRandomizerService.ReadLastUploadedValue(rule.Section, rule.Key);
                if (string.IsNullOrWhiteSpace(currentValue)) continue;
                var option = rule.Options.FirstOrDefault(x => SettingRandomizerValuesEqual(x.Value, currentValue));
                if (option is null || string.IsNullOrWhiteSpace(option.DiscordAnnouncement)) continue;
                var statusText = SettingRandomizerConfiguration.GetLocalizedAnnouncement(
                    rule.Key,
                    option.Value,
                    option.DiscordAnnouncement,
                    Texts.IsGerman).Trim();
                if (statusText.Length > 0) texts.Add(statusText);
            }
            foreach (var storedPack in SettingRandomizerConfiguration.ParsePacks(Settings.SettingRandomizerPacksJson).Where(x => x.Enabled))
            {
                var pack = SettingRandomizerPackEditorViewModel.FromPack(storedPack, Texts.IsGerman).ToPack();
                var currentOption = pack.Options.FirstOrDefault(option => option.Values.Count > 0 && option.Values.All(value =>
                {
                    var currentValue = SettingRandomizerService.ReadLastUploadedValue(value.Section, value.Key);
                    return !string.IsNullOrWhiteSpace(currentValue) && SettingRandomizerValuesEqual(value.Value, currentValue);
                }));

                var isGasolinePack = pack.Options.SelectMany(option => option.Values).Any(value =>
                    value.Key.Equals("scum.GasolinePricePerUnitMultiplier", StringComparison.OrdinalIgnoreCase));
                if (currentOption is null && isGasolinePack)
                {
                    // Das Benzinset umfasst acht gekoppelte Werte. Der Preis ist je Variante eindeutig und
                    // bleibt deshalb auch dann ein stabiler Erkennungswert, wenn ein Nebenwert manuell abweicht.
                    var currentPrice = SettingRandomizerService.ReadLastUploadedValue("Features", "scum.GasolinePricePerUnitMultiplier");
                    currentOption = pack.Options.FirstOrDefault(option => option.Values.Any(value =>
                        value.Key.Equals("scum.GasolinePricePerUnitMultiplier", StringComparison.OrdinalIgnoreCase) &&
                        !string.IsNullOrWhiteSpace(currentPrice) &&
                        SettingRandomizerValuesEqual(value.Value, currentPrice)));
                }

                if (currentOption is not null && !string.IsNullOrWhiteSpace(currentOption.StatusText))
                {
                    var statusText = currentOption.StatusText.Trim();
                    texts.Add(isGasolinePack ? "⛽ " + statusText : statusText);
                }
            }
            return texts.Distinct(StringComparer.Ordinal).ToList();
        }
        catch (Exception ex)
        {
            Log("SettingRandomizer: Lokaler ServerSettings-Stand konnte nicht gelesen werden: " + ex.Message);
            return Array.Empty<string>();
        }
    }

    private static bool SettingRandomizerValuesEqual(string? configured, string? current)
    {
        var left = (configured ?? string.Empty).Trim();
        var right = (current ?? string.Empty).Trim();
        if (left.Equals(right, StringComparison.OrdinalIgnoreCase)) return true;
        return decimal.TryParse(left, NumberStyles.Float, CultureInfo.InvariantCulture, out var leftNumber) &&
               decimal.TryParse(right, NumberStyles.Float, CultureInfo.InvariantCulture, out var rightNumber) &&
               leftNumber == rightNumber;
    }
    private void SetSettingRandomizerStatus(string value)
    {
        if (App.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
            dispatcher.Invoke(() => SettingRandomizerStatus = value);
        else
            SettingRandomizerStatus = value;
    }

    private void SetSettingRandomizerState(bool running, string status)
    {
        if (App.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(() =>
            {
                SettingRandomizerRunning = running;
                SettingRandomizerStatus = status;
            });
        }
        else
        {
            SettingRandomizerRunning = running;
            SettingRandomizerStatus = status;
        }
    }

    public void RefreshSettingRandomizerLanguage()
    {
        RefreshSettingRandomizerCatalog();
        foreach (var rule in SettingRandomizerRules) rule.SetLanguage(Texts.IsGerman);
        foreach (var pack in SettingRandomizerPacks) pack.SetLanguage(Texts.IsGerman);
        SettingRandomizerPreview = _lastSettingRandomizerResult?.GetSummary(Texts.IsGerman) ?? T("RandomizerNoSelection");
        if (_lastSettingRandomizerResult is not null) UpdateServerSettingsValidation(_lastSettingRandomizerResult.UpdatedText);
        RefreshPuppetSpawnPreviews();
        if (SettingRandomizerRunning)
        {
            var schedule = SettingRandomizerService.ParseSchedule(Settings.SettingRandomizerScheduleTimes, Texts.IsGerman);
            SettingRandomizerStatus = Tf("RandomizerScheduleNext", FormatNextSettingRandomizerRun(schedule, DateTime.Now));
        }
        else
        {
            SettingRandomizerStatus = T("RandomizerNotStarted");
        }
    }

    private static string FormatNextSettingRandomizerRun(IReadOnlyList<TimeSpan> times, DateTime now)
    {
        var next = times.Select(x => now.Date.Add(x)).FirstOrDefault(x => x > now);
        if (next == default) next = now.Date.AddDays(1).Add(times[0]);
        return next.ToString("dd.MM.yyyy HH:mm", CultureInfo.CurrentCulture);
    }
}

public sealed class SettingRandomizerPackEditorViewModel : ObservableObject
{
    private bool _enabled;
    private string _name = string.Empty;
    private bool _isGerman = true;
    public bool Enabled { get => _enabled; set => SetProperty(ref _enabled, value); }
    public string Name { get => _name; set => SetProperty(ref _name, value); }
    public ObservableCollection<SettingRandomizerPackOptionEditorViewModel> Options { get; } = new();
    public double TotalChance => Options.Sum(x => x.ChancePercent);
    public string ChanceSummary => _isGerman ? $"Summe: {TotalChance:0.##} %" : $"Total: {TotalChance:0.##}%";
    public bool ChanceIsValid => Math.Abs(TotalChance - 100d) <= 0.01d;

    public void AddOption(SettingRandomizerPackOptionEditorViewModel option)
    {
        option.PropertyChanged += OptionOnPropertyChanged;
        Options.Add(option);
        RaiseChanceProperties();
    }

    public bool RemoveOption(SettingRandomizerPackOptionEditorViewModel option)
    {
        option.PropertyChanged -= OptionOnPropertyChanged;
        var removed = Options.Remove(option);
        RaiseChanceProperties();
        return removed;
    }

    public bool ContainsKey(string key) => Options.SelectMany(x => x.Values).Any(x => x.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

    public void RedistributeChances()
    {
        if (Options.Count == 0) return;
        var chance = 100d / Options.Count;
        for (var index = 0; index < Options.Count; index++)
            Options[index].ChancePercent = index == Options.Count - 1 ? 100d - (chance * index) : chance;
        RaiseChanceProperties();
    }

    private void OptionOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingRandomizerPackOptionEditorViewModel.ChancePercent)) RaiseChanceProperties();
    }

    private void RaiseChanceProperties()
    {
        OnPropertyChanged(nameof(TotalChance));
        OnPropertyChanged(nameof(ChanceSummary));
        OnPropertyChanged(nameof(ChanceIsValid));
    }

    public void SetLanguage(bool isGerman)
    {
        _isGerman = isGerman;
        var gasoline = Name.Equals("Benzinversorgung", StringComparison.OrdinalIgnoreCase) || Name.Equals("Gasoline supply", StringComparison.OrdinalIgnoreCase);
        if (gasoline) Name = isGerman ? "Benzinversorgung" : "Gasoline supply";
        foreach (var option in Options)
        {
            option.SetLanguage(isGerman);
            if (!gasoline) continue;
            var id = option.Name.ToLowerInvariant();
            if (id is "knapp" or "scarce")
            {
                option.Name = isGerman ? "Knapp" : "Scarce";
                option.StatusText = isGerman ? "Benzin ist knapp und teuer." : "Gasoline is scarce and expensive.";
            }
            else if (id is "normal")
            {
                option.StatusText = isGerman ? "Die Benzinversorgung ist stabil." : "The gasoline supply is stable.";
            }
            else if (id is "überfluss" or "ueberfluss" or "abundant")
            {
                option.Name = isGerman ? "Überfluss" : "Abundant";
                option.StatusText = isGerman ? "Die Tankstellen sind besonders gut versorgt." : "Gas stations are particularly well supplied.";
            }
        }
        OnPropertyChanged(nameof(ChanceSummary));
    }

    public SettingRandomizerPack ToPack() => new()
    {
        Enabled = Enabled,
        Name = Name?.Trim() ?? string.Empty,
        Options = Options.Select(x => x.ToOption()).ToList()
    };

    public static SettingRandomizerPackEditorViewModel FromPack(SettingRandomizerPack pack, bool isGerman)
    {
        var editor = new SettingRandomizerPackEditorViewModel { Enabled = pack.Enabled, Name = pack.Name, _isGerman = isGerman };
        foreach (var option in pack.Options) editor.AddOption(SettingRandomizerPackOptionEditorViewModel.FromOption(option));
        editor.SetLanguage(isGerman);
        return editor;
    }
}

public sealed class SettingRandomizerPackOptionEditorViewModel : ObservableObject
{
    private string _name = string.Empty;
    private double _chancePercent;
    private string _statusText = string.Empty;
    private bool _isGerman = true;

    public SettingRandomizerPackOptionEditorViewModel()
    {
        Values.CollectionChanged += ValuesOnCollectionChanged;
    }

    public string Name { get => _name; set => SetProperty(ref _name, value); }
    public double ChancePercent { get => _chancePercent; set => SetProperty(ref _chancePercent, value); }
    public string StatusText { get => _statusText; set => SetProperty(ref _statusText, value); }
    public ObservableCollection<SettingRandomizerPackValueEditorViewModel> Values { get; } = new();
    public bool HasPuppetSpawnSettings => Values.Any(x => PuppetSpawnPreviewService.IsRelevant(x.Key));
    public string PuppetSpawnPreview => PuppetSpawnPreviewService.Build(Values.Select(x => x.ToValue()), _isGerman);

    public void SetLanguage(bool isGerman)
    {
        _isGerman = isGerman;
        RefreshPuppetSpawnPreview();
    }

    public void RefreshPuppetSpawnPreview()
    {
        OnPropertyChanged(nameof(HasPuppetSpawnSettings));
        OnPropertyChanged(nameof(PuppetSpawnPreview));
    }

    private void ValuesOnCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (SettingRandomizerPackValueEditorViewModel value in e.OldItems) value.PropertyChanged -= ValueOnPropertyChanged;
        if (e.NewItems is not null)
            foreach (SettingRandomizerPackValueEditorViewModel value in e.NewItems) value.PropertyChanged += ValueOnPropertyChanged;
        RefreshPuppetSpawnPreview();
    }

    private void ValueOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SettingRandomizerPackValueEditorViewModel.Key) or nameof(SettingRandomizerPackValueEditorViewModel.Value))
            RefreshPuppetSpawnPreview();
    }

    public SettingRandomizerPackOption ToOption() => new()
    {
        Name = Name?.Trim() ?? string.Empty,
        ChancePercent = ChancePercent,
        StatusText = StatusText?.Trim() ?? string.Empty,
        Values = Values.Select(x => x.ToValue()).ToList()
    };

    public static SettingRandomizerPackOptionEditorViewModel FromOption(SettingRandomizerPackOption option)
    {
        var editor = new SettingRandomizerPackOptionEditorViewModel { Name = option.Name, ChancePercent = option.ChancePercent, StatusText = option.StatusText };
        foreach (var value in option.Values) editor.Values.Add(SettingRandomizerPackValueEditorViewModel.FromValue(value));
        return editor;
    }
}
public sealed class SettingRandomizerPackValueEditorViewModel : ObservableObject
{
    private string _section = "Features";
    private string _key = string.Empty;
    private string _displayName = string.Empty;
    private string _value = string.Empty;
    public string Section { get => _section; set => SetProperty(ref _section, value); }
    public string Key { get => _key; set => SetProperty(ref _key, value); }
    public string DisplayName { get => _displayName; set => SetProperty(ref _displayName, value); }
    public string Value { get => _value; set => SetProperty(ref _value, value); }

    public SettingRandomizerPackValue ToValue() => new()
    {
        Section = Section?.Trim() ?? string.Empty,
        Key = Key?.Trim() ?? string.Empty,
        DisplayName = DisplayName?.Trim() ?? string.Empty,
        Value = Value?.Trim() ?? string.Empty
    };

    public static SettingRandomizerPackValueEditorViewModel FromValue(SettingRandomizerPackValue value) => new()
    {
        Section = value.Section,
        Key = value.Key,
        DisplayName = value.DisplayName,
        Value = value.Value
    };
}
public sealed class SettingRandomizerRuleEditorViewModel : ObservableObject
{
    private bool _enabled;
    private string _section = "World";
    private string _key = string.Empty;
    private string _displayName = string.Empty;
    private bool _isGerman = true;

    public bool Enabled { get => _enabled; set => SetProperty(ref _enabled, value); }
    public string Section { get => _section; set => SetProperty(ref _section, value); }
    public string Key { get => _key; set => SetProperty(ref _key, value); }
    public string DisplayName { get => _displayName; set => SetProperty(ref _displayName, value); }
    public ObservableCollection<SettingRandomizerOptionEditorViewModel> Options { get; } = new();
    public double TotalChance => Options.Sum(x => x.ChancePercent);
    public string ChanceSummary => _isGerman ? $"Summe: {TotalChance:0.##} %" : $"Total: {TotalChance:0.##}%";
    public bool ChanceIsValid => Math.Abs(TotalChance - 100d) <= 0.01d;

    public void AddOption(SettingRandomizerOptionEditorViewModel option)
    {
        option.PropertyChanged += OptionOnPropertyChanged;
        Options.Add(option);
        RaiseChanceProperties();
    }

    public void RemoveOption(SettingRandomizerOptionEditorViewModel option)
    {
        option.PropertyChanged -= OptionOnPropertyChanged;
        Options.Remove(option);
        RaiseChanceProperties();
    }

    private void OptionOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingRandomizerOptionEditorViewModel.ChancePercent)) RaiseChanceProperties();
    }

    private void RaiseChanceProperties()
    {
        OnPropertyChanged(nameof(TotalChance));
        OnPropertyChanged(nameof(ChanceSummary));
        OnPropertyChanged(nameof(ChanceIsValid));
    }

    public void SetLanguage(bool isGerman)
    {
        _isGerman = isGerman;
        DisplayName = SettingRandomizerConfiguration.GetLocalizedDisplayName(Key, DisplayName, isGerman);
        foreach (var option in Options)
        {
            option.DiscordAnnouncement = SettingRandomizerConfiguration.GetLocalizedAnnouncement(Key, option.Value, option.DiscordAnnouncement, isGerman);
        }
        OnPropertyChanged(nameof(ChanceSummary));
    }

    public SettingRandomizerRule ToRule() => new()
    {
        Enabled = Enabled,
        Section = Section?.Trim() ?? string.Empty,
        Key = Key?.Trim() ?? string.Empty,
        DisplayName = DisplayName?.Trim() ?? string.Empty,
        Options = Options.Select(x => x.ToOption()).ToList()
    };

    public static SettingRandomizerRuleEditorViewModel FromRule(SettingRandomizerRule rule, bool isGerman)
    {
        var editor = new SettingRandomizerRuleEditorViewModel
        {
            Enabled = rule.Enabled,
            Section = rule.Section,
            Key = rule.Key,
            DisplayName = rule.DisplayName,
            _isGerman = isGerman
        };
        foreach (var option in rule.Options) editor.AddOption(SettingRandomizerOptionEditorViewModel.FromOption(option));
        editor.SetLanguage(isGerman);
        return editor;
    }
}

public sealed class SettingRandomizerOptionEditorViewModel : ObservableObject
{
    private string _value = string.Empty;
    private double _chancePercent;
    private string _discordAnnouncement = string.Empty;

    public string Value { get => _value; set => SetProperty(ref _value, value); }
    public double ChancePercent { get => _chancePercent; set => SetProperty(ref _chancePercent, value); }
    public string DiscordAnnouncement { get => _discordAnnouncement; set => SetProperty(ref _discordAnnouncement, value); }

    public SettingRandomizerOption ToOption() => new()
    {
        Value = Value?.Trim() ?? string.Empty,
        ChancePercent = ChancePercent,
        DiscordAnnouncement = DiscordAnnouncement?.Trim() ?? string.Empty
    };

    public static SettingRandomizerOptionEditorViewModel FromOption(SettingRandomizerOption option) => new()
    {
        Value = option.Value,
        ChancePercent = option.ChancePercent,
        DiscordAnnouncement = option.DiscordAnnouncement
    };
}
