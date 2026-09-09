using System.Net.Http.Json;
using System.Text.Json;

namespace ScumRconTool.Services;

public sealed partial class GgconHttpApiService
{
    public async Task<InsuranceVehicleList> GetInsuranceVehiclesAsync(CancellationToken ct)
    {
        using var client = CreateClient();
        using var response = await client.GetAsync(Combine(RequireBaseUrl(), "/vehicles.json"), ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("count", out _) || !doc.RootElement.TryGetProperty("vehicles", out _))
            throw new InvalidDataException("Vehicle snapshot fields missing.");
        var result = JsonSerializer.Deserialize<InsuranceVehicleList>(body, JsonOptions) ?? throw new InvalidDataException("No vehicle snapshot.");
        VehicleInsuranceService.ValidateList(result);
        return result;
    }

    public async Task<List<string>> GetInsuranceVehicleTypesAsync(CancellationToken ct)
        => (await GetInsuranceVehicleTypeCatalogAsync(ct)).Select(x => x.VehicleClass).ToList();

    public async Task<List<GgconVehicleTypeInfo>> GetInsuranceVehicleTypeCatalogAsync(CancellationToken ct)
    {
        using var client = CreateClient();
        using var response = await client.GetAsync(Combine(RequireBaseUrl(), "/vehicle-types.json"), ct);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        if (doc.RootElement.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.False) throw new InvalidDataException("Vehicle catalog rejected.");
        return doc.RootElement.GetProperty("items").EnumerateArray()
            .Select(x => new GgconVehicleTypeInfo(
                x.GetProperty("i").GetString() ?? "",
                x.TryGetProperty("ico", out var icon) ? icon.GetString() ?? "" : ""))
            .Where(x => !string.IsNullOrWhiteSpace(x.VehicleClass))
            .DistinctBy(x => x.VehicleClass, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task InsuranceMutationAsync(string path, object payload, CancellationToken ct)
    {
        using var client = CreateClient();
        using var response = await client.PostAsJsonAsync(Combine(RequireBaseUrl(), path), payload, JsonOptions, ct);
        response.EnsureSuccessStatusCode();
        var result = JsonSerializer.Deserialize<GgconCommandResponse>(await response.Content.ReadAsStringAsync(ct), JsonOptions);
        if (result is null || !result.Ok || result.Accepted == false || result.Dispatched == false)
            throw new InvalidDataException("Insurance mutation was not positively acknowledged; manual review required.");
    }
}

public sealed record GgconVehicleTypeInfo(string VehicleClass, string IconName);

public sealed class GgconVehicleInsuranceApi : IVehicleInsuranceApi
{
    private readonly BotSettings _settings;
    private readonly string _serverKey;
    public static string ServerKey(BotSettings settings) => $"{settings.Host.Trim()}|{settings.GgconHttpBaseUrl.Trim().TrimEnd('/')}|{settings.GgconHttpPort}|{settings.Port}";
    public GgconVehicleInsuranceApi(BotSettings settings) { _settings = settings; _serverKey = ServerKey(settings); }
    private GgconHttpApiService Api()
    {
        if (ServerKey(_settings) != _serverKey) throw new InvalidOperationException("Server changed: restart RedRaven before using insurance.");
        return new GgconHttpApiService(_settings);
    }
    public Task<InsuranceVehicleList> VehiclesAsync(CancellationToken ct) => Api().GetInsuranceVehiclesAsync(ct);
    public Task<List<string>> TypesAsync(CancellationToken ct) => Api().GetInsuranceVehicleTypesAsync(ct);
    public async Task<double?> BalanceAsync(string steamId, CancellationToken ct) => (await Api().GetPlayerAccountAsync(steamId, ct)).AccountBalance;
    public Task DebitAsync(string steamId, int amount, CancellationToken ct) => Api().InsuranceMutationAsync("/players/" + Uri.EscapeDataString(steamId) + "/currency", new { action = "change", amount = -amount }, ct);
    public async Task SpawnAsync(string steamId, string vehicleClass, CancellationToken ct)
    {
        var api = Api();
        if (!(await api.GetOnlinePlayersAsync(ct)).Any(x => x.UserId == steamId)) throw new InvalidOperationException("Player must be online.");
        await api.InsuranceMutationAsync("/spawn-vehicle", new { steamId, vehicle = vehicleClass }, ct);
    }
    public Task MessageAsync(string steamId, string text, CancellationToken ct) => Api().SendMessageAsync(text.Length > 180 ? text[..180] : text, "ServerMessage", steamId, ct);
    public async Task<InsuranceDestructionBatch> DestructionsAsync(long? since, CancellationToken ct)
    {
        var result = await Api().GetLogsAsync(since ?? 0, "vehicle_destruction", ct);
        var entries = result.Lines.Where(x => x.Source.Equals("vehicle_destruction", StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.T).Select(x => VehicleDestructionLogParser.ParseLine(x.Line, "ggCON HTTP"))
            .Where(x => x is not null).Cast<VehicleDestructionLogEntry>().ToList();
        return new(result.Next, entries);
    }
}
