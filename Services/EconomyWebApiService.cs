using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ScumRconTool.Services;

public sealed class EconomyWebApiService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task PublishAsync(
        BotSettings settings,
        IReadOnlyList<EconomyMarketItem> items,
        CancellationToken cancellationToken = default)
    {
        if (!settings.EconomyWebApiEnabled) return;

        var endpoint = (settings.EconomyWebApiEndpointUrl ?? string.Empty).Trim();
        var token = (settings.EconomyWebApiToken ?? string.Empty).Trim();
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var endpointUri) ||
            endpointUri.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException("Economy Web API endpoint is invalid.");
        }

        if (token.Length < 16)
            throw new InvalidOperationException("Economy Web API token is missing or shorter than 16 characters.");

        var payload = new EconomyWebSnapshot
        {
            GeneratedAtUtc = DateTime.UtcNow,
            ServerName = settings.DiscordServerName ?? string.Empty,
            Items = items.Select(x => new EconomyWebItem
            {
                Code = x.Item,
                TraderCount = x.TraderCount,
                Transactions = x.Transactions,
                BasePurchasePrice = x.BasePurchasePrice,
                CurrentPurchasePrice = x.CurrentPurchasePrice,
                BaseSellPrice = x.BaseSellPrice,
                CurrentSellPrice = x.CurrentSellPrice,
                ChangePercent = x.ChangePercent
            }).ToList()
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, endpointUri);
        request.Headers.Add("X-Economy-Token", token);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");

        using var response = await Http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Economy Web API returned HTTP {(int)response.StatusCode}: {Trim(body, 300)}");
        }
    }

    private static string Trim(string value, int length)
    {
        var text = string.IsNullOrWhiteSpace(value) ? "empty response" : value.Trim();
        return text.Length <= length ? text : text[..length] + " ...";
    }
}

public sealed class EconomyWebSnapshot
{
    public DateTime GeneratedAtUtc { get; set; }
    public string ServerName { get; set; } = string.Empty;
    public List<EconomyWebItem> Items { get; set; } = new();
}

public sealed class EconomyWebItem
{
    public string Code { get; set; } = string.Empty;
    public int TraderCount { get; set; }
    public long Transactions { get; set; }
    public decimal BasePurchasePrice { get; set; }
    public decimal CurrentPurchasePrice { get; set; }
    public decimal BaseSellPrice { get; set; }
    public decimal CurrentSellPrice { get; set; }
    public double ChangePercent { get; set; }
}
