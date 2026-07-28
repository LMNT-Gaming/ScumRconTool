using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Input;
using ScumRconTool.Services;

namespace ScumRconTool.ViewModels;

public sealed partial class MainViewModel
{
    private CancellationTokenSource? _settingRandomizerCts;
    private SettingRandomizerCatalogEntry? _selectedSettingRandomizerCatalogEntry;
    private bool _settingRandomizerRunning;
    private string _settingRandomizerStatus = "Noch nicht gestartet.";
    private string _settingRandomizerPreview = "Noch keine Auswahl erzeugt.";

    public ObservableCollection<SettingRandomizerRuleEditorViewModel> SettingRandomizerRules { get; } = new();
    public IReadOnlyList<SettingRandomizerCatalogEntry> SettingRandomizerCatalog { get; } = SettingRandomizerConfiguration.Catalog;

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
        LoadSettingRandomizerRulesFromSettings();
        SelectedSettingRandomizerCatalogEntry = SettingRandomizerCatalog.FirstOrDefault();
    }

    private void LoadSettingRandomizerRulesFromSettings()
    {
        SettingRandomizerRules.Clear();
        try
        {
            foreach (var rule in SettingRandomizerConfiguration.Parse(Settings.SettingRandomizerRulesJson))
            {
                SettingRandomizerRules.Add(SettingRandomizerRuleEditorViewModel.FromRule(rule));
            }
        }
        catch (Exception ex)
        {
            Log("SettingRandomizer: Regeln konnten nicht geladen werden, Standard wird verwendet: " + ex.Message);
            foreach (var rule in SettingRandomizerConfiguration.Parse(SettingRandomizerConfiguration.BuildDefaultJson()))
            {
                SettingRandomizerRules.Add(SettingRandomizerRuleEditorViewModel.FromRule(rule));
            }
        }
    }

    private void SyncSettingRandomizerRulesToSettings()
    {
        Settings.SettingRandomizerRulesJson = SettingRandomizerConfiguration.Serialize(
            SettingRandomizerRules.Select(x => x.ToRule()));
    }

    private void AddSettingRandomizerRule()
    {
        var entry = SelectedSettingRandomizerCatalogEntry;
        if (entry is null) return;
        if (SettingRandomizerRules.Any(x => x.Section.Equals(entry.Section, StringComparison.OrdinalIgnoreCase) &&
                                            x.Key.Equals(entry.Key, StringComparison.OrdinalIgnoreCase)))
        {
            SettingRandomizerStatus = "Diese Einstellung ist bereits im Plan enthalten.";
            return;
        }

        var editor = SettingRandomizerRuleEditorViewModel.FromRule(entry.CreateRule(enabled: true));
        SettingRandomizerRules.Add(editor);
        SettingRandomizerStatus = $"'{entry.DisplayName}' wurde hinzugef?gt.";
    }

    private void RemoveSettingRandomizerRule(SettingRandomizerRuleEditorViewModel? rule)
    {
        if (rule is null) return;
        SettingRandomizerRules.Remove(rule);
    }

    private static void AddSettingRandomizerOption(SettingRandomizerRuleEditorViewModel? rule)
    {
        rule?.AddOption(new SettingRandomizerOptionEditorViewModel
        {
            Value = "Neuer Wert",
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

    private void ResetSettingRandomizer()
    {
        Settings.SettingRandomizerRulesJson = SettingRandomizerConfiguration.BuildDefaultJson();
        LoadSettingRandomizerRulesFromSettings();
        SettingRandomizerStatus = "Beispielplan wiederhergestellt. Nur 'Sentries deaktivieren' ist aktiv.";
    }

    private void StartSettingRandomizer(bool persistAutoStart = true)
    {
        SyncSettingRandomizerRulesToSettings();
        SettingRandomizerService.ValidateRules(SettingRandomizerRules.Select(x => x.ToRule()));
        var schedule = SettingRandomizerService.ParseSchedule(Settings.SettingRandomizerScheduleTimes);

        _settingRandomizerCts?.Cancel();
        _settingRandomizerCts?.Dispose();
        _settingRandomizerCts = new CancellationTokenSource();
        if (persistAutoStart)
        {
            Settings.AutoStartSettingRandomizer = true;
            SettingsStore.Save(Settings);
        }

        SettingRandomizerRunning = true;
        SettingRandomizerStatus = "Zeitplan aktiv. N?chster Upload: " + FormatNextSettingRandomizerRun(schedule, DateTime.Now);
        _ = RunSettingRandomizerLoopAsync(_settingRandomizerCts.Token);
        Log("SettingRandomizer gestartet. Zeiten: " + Settings.SettingRandomizerScheduleTimes);
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
        SettingRandomizerStatus = "Zeitplan gestoppt.";
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
                    Settings.SettingRandomizerLastScheduleKey);

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
            SetSettingRandomizerState(false, "Zeitplan-Fehler: " + ex.Message);
        }
    }

    private async Task ApplySettingRandomizerAsync(bool upload, CancellationToken cancellationToken = default)
    {
        SyncSettingRandomizerRulesToSettings();
        var rules = SettingRandomizerRules.Select(x => x.ToRule()).ToList();
        var service = new SettingRandomizerService(Settings, Log);
        SetSettingRandomizerStatus(upload ? "ServerSettings.ini wird geladen und aktualisiert ..." : "Testauswahl wird erzeugt ...");
        var result = await service.ApplyAsync(rules, upload, cancellationToken);
        SettingRandomizerPreview = result.Summary;

        if (upload)
        {
            SettingsStore.Save(Settings);
            SetSettingRandomizerStatus("Upload erfolgreich. Die Werte werden mit dem n?chsten Serverneustart aktiv.");
            await AnnounceSettingRandomizerResultAsync(result);
        }
        else
        {
            SetSettingRandomizerStatus("Testauswahl fertig. Es wurde nichts hochgeladen.");
        }

        Log("SettingRandomizer " + (upload ? "angewendet: " : "Test: ") + result.Summary);
    }

    private async Task AnnounceSettingRandomizerResultAsync(SettingRandomizerResult result)
    {
        if (!Settings.SettingRandomizerDiscordAnnouncementEnabled || string.IsNullOrWhiteSpace(result.DiscordText)) return;
        try
        {
            if (_discord is null || !_discord.IsReady) await StartDiscordAsync();
            if (_discord is null || !_discord.IsReady) throw new InvalidOperationException("Discord Bot ist noch nicht bereit.");
            await _discord.SendTextMessageAsync(Settings.SettingRandomizerDiscordChannelId, result.DiscordText);
            Log("SettingRandomizer: Discord-Ank?ndigung gesendet.");
        }
        catch (Exception ex)
        {
            Log("SettingRandomizer: Config wurde hochgeladen, Discord-Ank?ndigung ist fehlgeschlagen: " + ex.Message);
            AppLogService.WriteException("SettingRandomizerDiscord", ex);
            SetSettingRandomizerStatus("Upload erfolgreich; Discord-Ank?ndigung fehlgeschlagen: " + ex.Message);
        }
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

    private static string FormatNextSettingRandomizerRun(IReadOnlyList<TimeSpan> times, DateTime now)
    {
        var next = times.Select(x => now.Date.Add(x)).FirstOrDefault(x => x > now);
        if (next == default) next = now.Date.AddDays(1).Add(times[0]);
        return next.ToString("dd.MM.yyyy HH:mm", CultureInfo.CurrentCulture);
    }
}

public sealed class SettingRandomizerRuleEditorViewModel : ObservableObject
{
    private bool _enabled;
    private string _section = "World";
    private string _key = string.Empty;
    private string _displayName = string.Empty;

    public bool Enabled { get => _enabled; set => SetProperty(ref _enabled, value); }
    public string Section { get => _section; set => SetProperty(ref _section, value); }
    public string Key { get => _key; set => SetProperty(ref _key, value); }
    public string DisplayName { get => _displayName; set => SetProperty(ref _displayName, value); }
    public ObservableCollection<SettingRandomizerOptionEditorViewModel> Options { get; } = new();
    public double TotalChance => Options.Sum(x => x.ChancePercent);
    public string ChanceSummary => $"Summe: {TotalChance:0.##} %";
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

    public SettingRandomizerRule ToRule() => new()
    {
        Enabled = Enabled,
        Section = Section?.Trim() ?? string.Empty,
        Key = Key?.Trim() ?? string.Empty,
        DisplayName = DisplayName?.Trim() ?? string.Empty,
        Options = Options.Select(x => x.ToOption()).ToList()
    };

    public static SettingRandomizerRuleEditorViewModel FromRule(SettingRandomizerRule rule)
    {
        var editor = new SettingRandomizerRuleEditorViewModel
        {
            Enabled = rule.Enabled,
            Section = rule.Section,
            Key = rule.Key,
            DisplayName = rule.DisplayName
        };
        foreach (var option in rule.Options) editor.AddOption(SettingRandomizerOptionEditorViewModel.FromOption(option));
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
