using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ScumRconTool.Services;

public sealed class ShopOrderApiService
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private readonly BotSettings _settings;

    public ShopOrderApiService(BotSettings settings) => _settings = settings;

    public async Task<ShopOrderReservation> ReserveAsync(string code, string steamId, CancellationToken ct)
    {
        var reply = await PostAsync(new { action = "reserve", code, steamId }, ct);
        if (reply.Order is null || string.IsNullOrWhiteSpace(reply.LeaseToken))
            throw new InvalidDataException("Shop API returned no reservation.");
        reply.Order.LeaseToken = reply.LeaseToken;
        return reply.Order;
    }

    public async Task<IReadOnlyList<ShopOrderSummary>> ListAsync(string steamId, CancellationToken ct) =>
        (await PostAsync(new { action = "list", steamId }, ct)).Orders ?? [];

    public Task ReleaseAsync(ShopOrderReservation order, string details, CancellationToken ct) =>
        TransitionAsync("release", order, details, ct);
    public Task CompleteAsync(ShopOrderReservation order, CancellationToken ct) =>
        TransitionAsync("complete", order, "Delivered by RedRaven.", ct);
    public Task ReviewAsync(ShopOrderReservation order, string details, CancellationToken ct) =>
        TransitionAsync("review", order, details, ct);

    private async Task TransitionAsync(string action, ShopOrderReservation order, string details, CancellationToken ct) =>
        _ = await PostAsync(new { action, code = order.Code, steamId = order.SteamId, leaseToken = order.LeaseToken, details }, ct);

    private async Task<ShopWorkerReply> PostAsync(object payload, CancellationToken ct)
    {
        var endpoint = (_settings.ShopWorkerApiEndpointUrl ?? string.Empty).Trim();
        var token = (_settings.ShopWorkerApiToken ?? string.Empty).Trim();
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new InvalidOperationException("Shop worker API endpoint is not configured.");
        if (token.Length < 24) throw new InvalidOperationException("Shop worker API token is missing or too short.");

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.PostAsJsonAsync(uri, payload, Json, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        ShopWorkerReply? reply = null;
        try { reply = JsonSerializer.Deserialize<ShopWorkerReply>(body, Json); } catch (JsonException) { }
        if (!response.IsSuccessStatusCode || reply?.Ok != true)
            throw new ShopWorkerException(reply?.Error ?? $"HTTP {(int)response.StatusCode}", response.StatusCode);
        return reply!;
    }
}

public sealed class ShopWorkerException(string message, System.Net.HttpStatusCode statusCode) : Exception(message)
{
    public System.Net.HttpStatusCode StatusCode { get; } = statusCode;
}

public sealed class ShopWorkerReply
{
    [JsonPropertyName("ok")] public bool Ok { get; set; }
    [JsonPropertyName("error")] public string Error { get; set; } = string.Empty;
    [JsonPropertyName("leaseToken")] public string LeaseToken { get; set; } = string.Empty;
    [JsonPropertyName("order")] public ShopOrderReservation? Order { get; set; }
    [JsonPropertyName("orders")] public List<ShopOrderSummary>? Orders { get; set; }
}

public sealed class ShopOrderReservation
{
    [JsonPropertyName("code")] public string Code { get; set; } = string.Empty;
    [JsonPropertyName("steamId")] public string SteamId { get; set; } = string.Empty;
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("price")] public int Price { get; set; }
    [JsonPropertyName("fulfillment")] public ShopOrderFulfillment Fulfillment { get; set; } = new();
    [JsonIgnore] public string LeaseToken { get; set; } = string.Empty;
}

public sealed class ShopOrderFulfillment
{
    [JsonPropertyName("type")] public string Type { get; set; } = string.Empty;
    [JsonPropertyName("entries")] public List<ShopOrderEntry> Entries { get; set; } = [];
}

public sealed class ShopOrderEntry
{
    [JsonPropertyName("code")] public string Code { get; set; } = string.Empty;
    [JsonPropertyName("quantity")] public int Quantity { get; set; }
}

public sealed class ShopOrderSummary
{
    [JsonPropertyName("code")] public string Code { get; set; } = string.Empty;
    [JsonPropertyName("pack_name")] public string PackName { get; set; } = string.Empty;
    [JsonPropertyName("price")] public int Price { get; set; }
}
