using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using System.Windows.Threading;
using ScumRconTool.Models;
using ScumRconTool.Services;

namespace ScumRconTool.ViewModels;

public sealed partial class MainViewModel
{
    private readonly DispatcherTimer _dashboardPlayerTimer = new() { Interval = TimeSpan.FromSeconds(10) };
    private bool _dashboardPlayerRefreshRunning;
    private bool _dashboardEnvironmentRefreshRunning;
    private bool _dashboardEnvironmentUpdating;
    private bool _dashboardTimeDirty;
    private bool _dashboardWeatherDirty;
    private DashboardPlayerViewModel? _selectedDashboardPlayer;
    private string _dashboardPlayerStatus = string.Empty;
    private string _dashboardEnvironmentStatus = string.Empty;
    private string _dashboardServerTime = "--:--";
    private double _dashboardWeatherPercent;
    private string _dashboardMessageText = string.Empty;
    private string _dashboardMessageStatus = string.Empty;

    public ObservableCollection<DashboardPlayerViewModel> DashboardPlayers { get; } = new();
    public ICommand RefreshDashboardPlayersCommand { get; private set; } = null!;
    public ICommand SetDashboardServerTimeCommand { get; private set; } = null!;
    public ICommand SetDashboardWeatherCommand { get; private set; } = null!;
    public ICommand SendDashboardGlobalChatCommand { get; private set; } = null!;
    public ICommand SendDashboardWhisperCommand { get; private set; } = null!;
    public ICommand SendDashboardAnnouncementCommand { get; private set; } = null!;

    public DashboardPlayerViewModel? SelectedDashboardPlayer
    {
        get => _selectedDashboardPlayer;
        set => SetProperty(ref _selectedDashboardPlayer, value);
    }

    public string DashboardPlayerStatus
    {
        get => _dashboardPlayerStatus;
        private set => SetProperty(ref _dashboardPlayerStatus, value);
    }

    public string DashboardEnvironmentStatus
    {
        get => _dashboardEnvironmentStatus;
        private set => SetProperty(ref _dashboardEnvironmentStatus, value);
    }

    public string DashboardServerTime
    {
        get => _dashboardServerTime;
        set
        {
            if (SetProperty(ref _dashboardServerTime, value ?? string.Empty) && !_dashboardEnvironmentUpdating)
                _dashboardTimeDirty = true;
        }
    }

    public double DashboardWeatherPercent
    {
        get => _dashboardWeatherPercent;
        set
        {
            if (SetProperty(ref _dashboardWeatherPercent, Math.Clamp(value, 0, 100)) && !_dashboardEnvironmentUpdating)
                _dashboardWeatherDirty = true;
        }
    }

    public string DashboardMessageText
    {
        get => _dashboardMessageText;
        set => SetProperty(ref _dashboardMessageText, value ?? string.Empty);
    }

    public string DashboardMessageStatus
    {
        get => _dashboardMessageStatus;
        private set => SetProperty(ref _dashboardMessageStatus, value);
    }

    private void InitializeDashboard()
    {
        DashboardPlayerStatus = Texts.IsGerman ? "Spieleransicht noch nicht geladen." : "Player view has not been loaded yet.";
        DashboardEnvironmentStatus = Texts.IsGerman ? "Wetter und Serverzeit werden beim Öffnen geladen." : "Weather and server time are loaded when the tab opens.";
        RefreshDashboardPlayersCommand = new RelayCommand(async _ => await RefreshDashboardAsync());
        SetDashboardServerTimeCommand = new RelayCommand(async _ => await SetDashboardServerTimeAsync());
        SetDashboardWeatherCommand = new RelayCommand(async _ => await SetDashboardWeatherAsync());
        SendDashboardGlobalChatCommand = new RelayCommand(async _ => await SendDashboardGlobalChatAsync());
        SendDashboardWhisperCommand = new RelayCommand(async _ => await SendDashboardWhisperAsync());
        SendDashboardAnnouncementCommand = new RelayCommand(async _ => await SendDashboardAnnouncementAsync());
        _dashboardPlayerTimer.Tick += DashboardPlayerTimerOnTick;
    }

    public void StartDashboardPlayerMonitor()
    {
        if (!_dashboardPlayerTimer.IsEnabled) _dashboardPlayerTimer.Start();
        DashboardPlayerTimerOnTick(this, EventArgs.Empty);
    }

    public void StopDashboardPlayerMonitor() => _dashboardPlayerTimer.Stop();

    private async void DashboardPlayerTimerOnTick(object? sender, EventArgs e)
    {
        try
        {
            await RefreshDashboardPlayersAsync(true);
            await RefreshDashboardEnvironmentAsync();
        }
        catch (Exception ex)
        {
            DashboardPlayerStatus = (Texts.IsGerman ? "Aktualisierung fehlgeschlagen: " : "Refresh failed: ") + ex.Message;
            AppLogService.WriteException("DashboardPlayers", ex);
        }
    }

    private async Task RefreshDashboardAsync()
    {
        await RefreshDashboardPlayersAsync(true);
        await RefreshDashboardEnvironmentAsync();
    }

    private async Task RefreshDashboardEnvironmentAsync()
    {
        if (_dashboardEnvironmentRefreshRunning) return;
        _dashboardEnvironmentRefreshRunning = true;
        try
        {
            var weather = await new GgconHttpApiService(Settings).GetWeatherAsync();
            if (weather is null) return;

            var currentTime = weather.FormatIngameTime();
            var currentWeather = weather.GetWeatherScore() * 100d;
            _dashboardEnvironmentUpdating = true;
            try
            {
                if (!_dashboardTimeDirty) DashboardServerTime = currentTime;
                if (!_dashboardWeatherDirty) DashboardWeatherPercent = currentWeather;
            }
            finally { _dashboardEnvironmentUpdating = false; }

            DashboardEnvironmentStatus = Texts.IsGerman
                ? $"Aktuell: {currentTime} Uhr · Wetter {currentWeather:0}%"
                : $"Current: {currentTime} · weather {currentWeather:0}%";
        }
        catch (Exception ex)
        {
            DashboardEnvironmentStatus = (Texts.IsGerman ? "Wetter/Zeit konnten nicht geladen werden: " : "Weather/time could not be loaded: ") + ex.Message;
            AppLogService.WriteException("DashboardEnvironment", ex);
        }
        finally { _dashboardEnvironmentRefreshRunning = false; }
    }

    private async Task RefreshDashboardPlayersAsync(bool force)
    {
        if (_dashboardPlayerRefreshRunning) return;
        _dashboardPlayerRefreshRunning = true;
        try
        {
            if (force) ClearPlayerCache();
            var selectedId = SelectedDashboardPlayer?.UserId;
            var players = await FetchPlayersAsync();
            var mapped = players.Where(p => p.Location is not null)
                .OrderBy(p => p.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .Select(p => new DashboardPlayerViewModel(p)).ToList();

            DashboardPlayers.Clear();
            foreach (var player in mapped) DashboardPlayers.Add(player);
            SelectedDashboardPlayer = DashboardPlayers.FirstOrDefault(p =>
                string.Equals(p.UserId, selectedId, StringComparison.OrdinalIgnoreCase))
                ?? DashboardPlayers.FirstOrDefault();

            var withoutPosition = Math.Max(0, players.Count - mapped.Count);
            DashboardPlayerStatus = withoutPosition == 0
                ? (Texts.IsGerman
                    ? $"{players.Count} Spieler · aktualisiert {DateTime.Now:HH:mm:ss} · automatisch alle 10 Sekunden"
                    : $"{players.Count} players · updated {DateTime.Now:HH:mm:ss} · automatically every 10 seconds")
                : (Texts.IsGerman
                    ? $"{mapped.Count}/{players.Count} Spieler mit Position · aktualisiert {DateTime.Now:HH:mm:ss}"
                    : $"{mapped.Count}/{players.Count} players with a position · updated {DateTime.Now:HH:mm:ss}");
        }
        finally { _dashboardPlayerRefreshRunning = false; }
    }

    private async Task SetDashboardServerTimeAsync()
    {
        if (!TryParseDashboardTime(DashboardServerTime, out var hour, out var normalized))
            throw new InvalidOperationException(Texts.IsGerman
                ? "Bitte die Serverzeit als HH:mm zwischen 00:00 und 23:59 eingeben."
                : "Enter server time as HH:mm between 00:00 and 23:59.");

        await new GgconHttpApiService(Settings).SetServerTimeAsync(hour);
        Log($"Dashboard: Serverzeit per ggCON HTTP auf {normalized} gesetzt.");

        _dashboardEnvironmentUpdating = true;
        DashboardServerTime = normalized;
        _dashboardEnvironmentUpdating = false;
        _dashboardTimeDirty = false;
        DashboardEnvironmentStatus = Texts.IsGerman
            ? $"Serverzeit auf {normalized} Uhr gesetzt."
            : $"Server time set to {normalized}.";
    }

    private async Task SetDashboardWeatherAsync()
    {
        await new GgconHttpApiService(Settings).SetServerWeatherAsync(DashboardWeatherPercent / 100d);
        Log($"Dashboard: Wetter per ggCON HTTP auf {DashboardWeatherPercent:0}% gesetzt.");
        _dashboardWeatherDirty = false;
        DashboardEnvironmentStatus = Texts.IsGerman
            ? $"Wetter auf {DashboardWeatherPercent:0}% gesetzt."
            : $"Weather set to {DashboardWeatherPercent:0}%.";
    }

    private async Task SendDashboardGlobalChatAsync()
    {
        try
        {
            var text = RequireDashboardMessage();
            await new GgconHttpApiService(Settings).SendMessageAsync(text, "ServerMessage");
            CompleteDashboardMessage(
                Texts.IsGerman ? "Globaler Sidechat wurde gesendet." : "Global side chat message sent.",
                "Dashboard: Globaler Sidechat per ggCON HTTP gesendet.");
        }
        catch (Exception ex)
        {
            FailDashboardMessage(ex);
        }
    }

    private async Task SendDashboardWhisperAsync()
    {
        try
        {
            var text = RequireDashboardMessage();
            var player = SelectedDashboardPlayer ?? throw new InvalidOperationException(Texts.IsGerman
                ? "Bitte zuerst einen Spieler in der Liste auswählen."
                : "Select a player from the list first.");
            await new GgconHttpApiService(Settings).SendMessageAsync(text, "Cyan", player.UserId);
            CompleteDashboardMessage(
                Texts.IsGerman ? $"Whisper an {player.DisplayName} wurde gesendet." : $"Whisper sent to {player.DisplayName}.",
                $"Dashboard: Whisper per ggCON HTTP an {player.DisplayName}/{player.UserId} gesendet.");
        }
        catch (Exception ex)
        {
            FailDashboardMessage(ex);
        }
    }

    private async Task SendDashboardAnnouncementAsync()
    {
        try
        {
            var text = RequireDashboardMessage();
            await new GgconHttpApiService(Settings).SendWarningAsync(text);
            CompleteDashboardMessage(
                Texts.IsGerman ? "Announcement wurde gesendet." : "Announcement sent.",
                "Dashboard: Announcement per ggCON HTTP gesendet.");
        }
        catch (Exception ex)
        {
            FailDashboardMessage(ex);
        }
    }

    private string RequireDashboardMessage()
    {
        var text = (DashboardMessageText ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException(Texts.IsGerman
                ? "Bitte zuerst eine Nachricht eingeben."
                : "Enter a message first.");
        }

        return text;
    }

    private void CompleteDashboardMessage(string status, string logEntry)
    {
        DashboardMessageStatus = status;
        DashboardMessageText = string.Empty;
        Log(logEntry);
    }

    private void FailDashboardMessage(Exception ex)
    {
        DashboardMessageStatus = (Texts.IsGerman ? "Senden fehlgeschlagen: " : "Send failed: ") + ex.Message;
        AppLogService.WriteException("DashboardMessage", ex);
    }

    private static bool TryParseDashboardTime(string? value, out double decimalHour, out string normalized)
    {
        decimalHour = 0;
        normalized = string.Empty;
        var formats = new[] { @"hh\:mm", @"h\:mm" };
        if (!TimeSpan.TryParseExact((value ?? string.Empty).Trim(), formats, CultureInfo.InvariantCulture, out var time) ||
            time < TimeSpan.Zero || time >= TimeSpan.FromHours(24))
            return false;

        decimalHour = time.TotalHours;
        normalized = $"{time.Hours:00}:{time.Minutes:00}";
        return true;
    }


    private void DisposeDashboard()
    {
        _dashboardPlayerTimer.Stop();
        _dashboardPlayerTimer.Tick -= DashboardPlayerTimerOnTick;
    }
}

public sealed class DashboardPlayerViewModel
{
    private const double MapWidth = 1516d, MapHeight = 1518d;
    private const double WorldLeftX = 618000d, WorldRightX = -898000d;
    private const double WorldTopY = 618000d, WorldBottomY = -900000d;

    public DashboardPlayerViewModel(ScumPlayer player)
    {
        DisplayName = player.DisplayName;
        SteamName = player.SteamName ?? string.Empty;
        UserId = player.UserId ?? string.Empty;
        Fame = player.Fame;
        AccountBalance = player.AccountBalance;
        Health = player.Health;
        Ping = player.Ping;
        X = player.Location?.X ?? 0;
        Y = player.Location?.Y ?? 0;
        Z = player.Location?.Z ?? 0;
        MapX = ((X - WorldLeftX) / (WorldRightX - WorldLeftX)) * MapWidth;
        MapY = ((Y - WorldTopY) / (WorldBottomY - WorldTopY)) * MapHeight;
    }

    public string DisplayName { get; }
    public string SteamName { get; }
    public string UserId { get; }
    public double Fame { get; }
    public double AccountBalance { get; }
    public double? Health { get; }
    public int? Ping { get; }
    public double X { get; }
    public double Y { get; }
    public double Z { get; }
    public double MapX { get; }
    public double MapY { get; }
    public string Coordinates => string.Create(CultureInfo.InvariantCulture, $"X {X:0} / Y {Y:0} / Z {Z:0}");
    public string SecondaryLine => string.IsNullOrWhiteSpace(SteamName) || SteamName == DisplayName
        ? UserId : SteamName + " · " + UserId;
    public string HealthText => Health.HasValue ? $"{Health:0}%" : "–";
    public string PingText => Ping.HasValue ? $"{Ping} ms" : "–";
}
