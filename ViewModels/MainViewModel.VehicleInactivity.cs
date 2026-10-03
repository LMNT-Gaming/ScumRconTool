using ScumRconTool.Services;

namespace ScumRconTool.ViewModels;

public sealed partial class MainViewModel
{
    private IReadOnlyDictionary<string, string>? _vehicleTypeIcons;
    private DateTime _vehicleTypeIconsUtc;

    private void StartVehicleInactivityWarnings()
    {
        _vehicleInactivityWarnings?.Dispose();
        _vehicleInactivityWarnings = null;
        if (!Settings.VehicleInactivityWarningEnabled) return;
        _vehicleInactivityWarnings = new VehicleInactivityWarningService(Settings, async (warning, token) =>
        {
            if (_discord is null || !_discord.IsReady) throw new InvalidOperationException("Discord-Hauptbot ist noch nicht bereit.");
            string? iconUrl = null;
            try { iconUrl = await ResolveVehicleIconUrlAsync(warning.VehicleName, token); }
            catch (Exception ex) { Log("Fahrzeugbild konnte nicht geladen werden: " + ex.Message); }
            await _discord.SendVehicleInactivityWarningAsync(Settings.VehicleInactivityWarningDiscordChannelId, warning, iconUrl, token);
        }, Log);
        _vehicleInactivityWarnings.Start();
        Log($"Fahrzeug-Inaktivitätswarnung aktiv: {Settings.VehicleInactivityWarningHours}h vorher, Scan alle {Math.Clamp(Settings.VehicleInactivityScanMinutes, 15, 1440)} Minuten.");
    }

    private async Task<string?> ResolveVehicleIconUrlAsync(string vehicleName, CancellationToken token)
    {
        if (_vehicleTypeIcons is null || DateTime.UtcNow - _vehicleTypeIconsUtc > TimeSpan.FromHours(12))
        {
            var catalog = await new GgconHttpApiService(Settings).GetInsuranceVehicleTypeCatalogAsync(token);
            _vehicleTypeIcons = catalog.Where(x => !string.IsNullOrWhiteSpace(x.IconName))
                .ToDictionary(x => VehicleInsurancePricing.NormalizeClass(x.VehicleClass), x => x.IconName, StringComparer.OrdinalIgnoreCase);
            _vehicleTypeIconsUtc = DateTime.UtcNow;
        }
        if (!_vehicleTypeIcons.TryGetValue(VehicleInsurancePricing.NormalizeClass(vehicleName), out var icon) ||
            !System.Text.RegularExpressions.Regex.IsMatch(icon, "^[A-Za-z0-9_]+$")) return null;
        return "https://icons.gghost.games/icons/" + icon + ".webp";
    }
}
