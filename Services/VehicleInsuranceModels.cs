using System.Text.Json;
using System.Text.Json.Serialization;

namespace ScumRconTool.Services;

public sealed class VehicleInsuranceOptions
{
    public bool Enabled { get; set; }
    public int DefaultWeeklyPrice { get; set; } = 10000;
    public decimal TwoWeekDiscountPercent { get; set; } = 5;
    public decimal FourWeekDiscountPercent { get; set; } = 10;
    public List<VehicleInsurancePrice> Prices { get; set; } = new();
    public bool WebEnabled { get; set; }
    public string WebEndpoint { get; set; } = "";
    public string WebToken { get; set; } = "";

    public VehicleInsuranceOptions Copy() => JsonSerializer.Deserialize<VehicleInsuranceOptions>(JsonSerializer.Serialize(this))!;
    public void Validate()
    {
        if (DefaultWeeklyPrice < 1 || DefaultWeeklyPrice > 100000000 || TwoWeekDiscountPercent is < 0 or >= 100 || FourWeekDiscountPercent is < 0 or >= 100)
            throw new InvalidOperationException("Insurance: prices must be 1–100,000,000; discounts must be 0–99.99%.");
        if (Prices.Any(x => string.IsNullOrWhiteSpace(x.VehicleClass) || x.WeeklyPrice is < 1 or > 100000000) ||
            Prices.GroupBy(x => VehicleInsurancePricing.NormalizeClass(x.VehicleClass)).Any(x => x.Count() > 1))
            throw new InvalidOperationException("Insurance: each vehicle type needs one unique, valid price.");
        if (WebEnabled && (!Uri.TryCreate(WebEndpoint, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !string.IsNullOrEmpty(uri.UserInfo) || WebToken.Trim().Length < 16))
            throw new InvalidOperationException("Insurance website: HTTPS endpoint and a token of at least 16 characters required.");
    }
}

public sealed class VehicleInsurancePrice
{
    public string VehicleClass { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public int WeeklyPrice { get; set; } = 10000;
}

public static class VehicleInsurancePricing
{
    public static string NormalizeClass(string value)
    {
        value = (value ?? "").Trim().ToUpperInvariant();
        foreach (var prefix in new[] { "BPC_", "BP_" })
            if (value.StartsWith(prefix, StringComparison.Ordinal)) { value = value[prefix.Length..]; break; }
        if (value.EndsWith("_C", StringComparison.Ordinal)) value = value[..^2];
        if (value.EndsWith("_ES", StringComparison.Ordinal)) value = value[..^3];
        return value;
    }

    public static int Calculate(VehicleInsuranceOptions options, string vehicleClass, int weeks)
    {
        options.Validate();
        if (weeks is not (1 or 2 or 4)) throw new ArgumentOutOfRangeException(nameof(weeks));
        var price = options.Prices.SingleOrDefault(x => NormalizeClass(x.VehicleClass) == NormalizeClass(vehicleClass));
        if (price?.Enabled == false) throw new InvalidOperationException("This vehicle type is not insurable.");
        var discount = weeks == 2 ? options.TwoWeekDiscountPercent : weeks == 4 ? options.FourWeekDiscountPercent : 0;
        return Math.Max(1, checked((int)decimal.Round((price?.WeeklyPrice ?? options.DefaultWeeklyPrice) * weeks * (1 - discount / 100), 0, MidpointRounding.AwayFromZero)));
    }
}

public sealed class InsurableVehicle
{
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public long Id { get; set; }
    public string Class { get; set; } = "";
    public string Name { get; set; } = "";
    public string? OwnerSteamId { get; set; }
}

public sealed class InsuranceVehicleList
{
    public bool Ok { get; set; }
    public bool OwnershipResolved { get; set; }
    public int Count { get; set; }
    public List<InsurableVehicle> Vehicles { get; set; } = new();
}

// Intermediate states are deliberately durable: an uncertain request is NEVER automatically repeated.
public enum InsuranceStatus { PaymentReview, Active, DestroyedPendingCheck, Claimable, SpawnReview, Claimed, Cancelled }

public sealed class VehicleInsurancePolicy
{
    public string Id { get; set; } = "V-" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
    public string SteamId { get; set; } = "";
    public string PlayerName { get; set; } = "";
    public long VehicleId { get; set; }
    public string VehicleName { get; set; } = "";
    public string SpawnClass { get; set; } = "";
    public int Weeks { get; set; }
    public int Price { get; set; }
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }
    public DateTime? DestroyedUtc { get; set; }
    public DateTime? ClaimedUtc { get; set; }
    public InsuranceStatus Status { get; set; }
    public List<string> History { get; set; } = new();
    [JsonIgnore] public string StatusText => Status == InsuranceStatus.Active && EndUtc <= DateTime.UtcNow ? "Expired / Abgelaufen" : Status.ToString();
    [JsonIgnore] public string Summary => $"{Id} · {VehicleName} (#{VehicleId}) · {PlayerName} · {StatusText} · {EndUtc.ToLocalTime():g}";
}

public sealed class VehicleInsuranceState
{
    public List<VehicleInsurancePolicy> Policies { get; set; } = new();
    public List<string> ProcessedCommands { get; set; } = new();
    public long? LogCursor { get; set; }
}

public interface IVehicleInsuranceApi
{
    Task<InsuranceVehicleList> VehiclesAsync(CancellationToken ct);
    Task<List<string>> TypesAsync(CancellationToken ct);
    Task<double?> BalanceAsync(string steamId, CancellationToken ct);
    Task DebitAsync(string steamId, int amount, CancellationToken ct);
    Task SpawnAsync(string steamId, string vehicleClass, CancellationToken ct);
    Task MessageAsync(string steamId, string text, CancellationToken ct);
    Task<InsuranceDestructionBatch> DestructionsAsync(long? since, CancellationToken ct);
}

public sealed record InsuranceDestructionBatch(long? Next, List<VehicleDestructionLogEntry> Entries);
