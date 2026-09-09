using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ScumRconTool.Services;

public sealed class VehicleInsuranceWebApiService
{
    private readonly HttpClient _http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(15) };
    private string _lastHash = "";
    public async Task PublishAsync(VehicleInsuranceOptions options, List<VehicleInsurancePolicy> policies, bool force, CancellationToken ct)
    {
        if (!options.WebEnabled) return;
        options.Validate();
        // Publish only user-center fields. Internal history and credentials never leave the tool.
        var contracts = policies.Select(p => new { p.Id, p.SteamId, p.PlayerName, p.VehicleId, p.VehicleName, p.SpawnClass,
            p.Price, p.Weeks, p.StartUtc, p.EndUtc, p.DestroyedUtc, p.ClaimedUtc, status = p.Status.ToString() }).ToList();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(options.WebEndpoint + "|" + options.WebToken + "|" + JsonSerializer.Serialize(contracts))));
        if (!force && hash == _lastHash) return;
        using var request = new HttpRequestMessage(HttpMethod.Post, options.WebEndpoint);
        request.Headers.Add("X-Insurance-Token", options.WebToken.Trim());
        request.Content = JsonContent.Create(new { schemaVersion = 1, generatedAtUtc = DateTime.UtcNow, policies = contracts });
        using var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        if (!body.RootElement.TryGetProperty("ok", out var ok) || ok.ValueKind != JsonValueKind.True) throw new InvalidOperationException("Insurance website did not acknowledge snapshot.");
        _lastHash = hash;
    }
}
