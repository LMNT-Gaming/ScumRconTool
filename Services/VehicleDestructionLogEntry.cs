namespace ScumRconTool.Services;

public sealed class VehicleDestructionLogEntry
{
    public DateTime? Timestamp { get; init; }
    public string Action { get; init; } = string.Empty;
    public string VehicleClass { get; init; } = string.Empty;
    public string VehicleName => VehicleIconName;
    public string VehicleIconName => VehicleDestructionLogParser.BuildVehicleIconName(VehicleClass);
    public string VehicleId { get; init; } = string.Empty;
    public string OwnerRaw { get; init; } = string.Empty;
    public string OwnerSteamId { get; init; } = string.Empty;
    public string OwnerName { get; init; } = string.Empty;
    public int? OwnerProfileId { get; init; }
    public double? X { get; init; }
    public double? Y { get; init; }
    public double? Z { get; init; }
    public string SourceFile { get; init; } = string.Empty;
    public string RawLine { get; init; } = string.Empty;

    public bool HasOwner => !string.IsNullOrWhiteSpace(OwnerSteamId) || !string.IsNullOrWhiteSpace(OwnerName);
    public string IconUrl => $"https://icons.gghost.games/icons/ICO_{VehicleIconName}.webp";

    public string Key => string.Join('|', Timestamp?.ToString("O") ?? string.Empty, Action, VehicleClass, VehicleId, OwnerRaw, X?.ToString("R") ?? string.Empty, Y?.ToString("R") ?? string.Empty, Z?.ToString("R") ?? string.Empty);
}
