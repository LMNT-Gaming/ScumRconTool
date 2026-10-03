using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ScumRconTool.Services;

public sealed class VehicleInsuranceService
{
    private readonly IVehicleInsuranceApi _api;
    private readonly string _path;
    private readonly Action<string> _log;
    private readonly Func<DateTime> _now;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ConcurrentDictionary<string, Quote> _quotes = new();
    private VehicleInsuranceState _state;
    private VehicleInsuranceOptions _options;
    private bool _storageFault;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public event Action? Changed;
    public VehicleInsuranceOptions Options => _options.Copy();

    public VehicleInsuranceService(IVehicleInsuranceApi api, VehicleInsuranceOptions options, string path, Action<string> log, Func<DateTime>? now = null)
    {
        _api = api; _path = path; _log = log; _now = now ?? (() => DateTime.UtcNow); _options = options.Copy();
        // Never silently replace corrupt financial state with an empty database.
        _state = File.Exists(path) ? JsonSerializer.Deserialize<VehicleInsuranceState>(File.ReadAllText(path))
            ?? throw new InvalidDataException("Insurance state is empty.") : new();
        if (_state.Policies is null || _state.ProcessedCommands is null) throw new InvalidDataException("Invalid insurance state.");
    }

    public void Configure(VehicleInsuranceOptions options) { options.Validate(); _options = options.Copy(); _quotes.Clear(); }
    public void CancelQuote(string steamId) => _quotes.TryRemove(steamId, out _);
    public static bool IsInsuranceCommand(string command) => BuiltinChatCommandCatalog.IsInsurance(command);

    public async Task<List<VehicleInsurancePolicy>> SnapshotAsync()
    {
        await _gate.WaitAsync();
        try { return JsonSerializer.Deserialize<List<VehicleInsurancePolicy>>(JsonSerializer.Serialize(_state.Policies))!; }
        finally { _gate.Release(); }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporary = _path + ".tmp";
            // Flush before atomic replacement so a crash does not produce a half-written policy ledger.
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            { JsonSerializer.Serialize(stream, _state, Json); stream.Flush(true); }
            File.Move(temporary, _path, true);
        }
        catch { _storageFault = true; throw; }
        Changed?.Invoke();
    }

    private void Note(VehicleInsurancePolicy policy, string text)
    {
        policy.History.Add($"{_now():O} {text}");
        _log($"Insurance {policy.Id}: {text}");
    }

    public async Task<bool> HandleAsync(ChatLogMessage message, bool german, CancellationToken ct)
    {
        var parts = message.Message.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return false;
        var command = parts[0].ToLowerInvariant();
        var commandId = BuiltinChatCommandCatalog.Resolve(command);
        bool confirmation = commandId is BuiltinChatCommandId.Confirm or BuiltinChatCommandId.Cancel;
        if (!IsInsuranceCommand(command) && !(confirmation && _quotes.ContainsKey(message.SteamId))) return false;
        if (message.SteamId.Length != 17 || !message.SteamId.All(char.IsDigit)) return true;
        await _gate.WaitAsync(ct);
        try
        {
            string L(string de, string en) => german ? de : en;
            async Task Reply(string text) => await _api.MessageAsync(message.SteamId, text, ct);
            if (!_options.Enabled) { CancelQuote(message.SteamId); await Reply(L("Versicherung ist deaktiviert.", "Insurance is disabled.")); return true; }
            if (_storageFault) { await Reply(L("Versicherung wegen Speicherfehler gesperrt. Bitte Admin kontaktieren.", "Insurance locked after storage failure. Contact an admin.")); return true; }
            // Do not execute historical financial commands from chat backfill after a restart.
            if (!message.LoggedAtUtc.HasValue || message.LoggedAtUtc < _now().AddMinutes(-2) || message.LoggedAtUtc > _now().AddSeconds(30))
            {
                _log($"Insurance: {command} ignored by replay protection. Log UTC: {message.LoggedAtUtc:O}; now UTC: {_now():O}.");
                return true;
            }
            var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(message.SteamId + "|" + message.LoggedAtUtc.Value.ToString("O") + "|" + message.Message)));
            if (_state.ProcessedCommands.Contains(key)) return true;
            _state.ProcessedCommands.Add(key);
            if (_state.ProcessedCommands.Count > 10000) _state.ProcessedCommands.RemoveRange(0, _state.ProcessedCommands.Count - 10000);
            Save();

            if (commandId == BuiltinChatCommandId.InsuranceList)
            {
                var mine = _state.Policies.Where(x => x.SteamId == message.SteamId).OrderByDescending(x => x.StartUtc).Take(15).ToList();
                if (mine.Count == 0) await Reply(L("Keine Versicherungen vorhanden.", "No insurance policies."));
                foreach (var p in mine) await Reply($"{p.Id} | {p.VehicleName} #{p.VehicleId} | {p.StatusText} | {p.EndUtc.ToLocalTime():g}");
                return true;
            }
            if (commandId == BuiltinChatCommandId.Cancel) { CancelQuote(message.SteamId); await Reply(L("Angebot abgebrochen.", "Quote cancelled.")); return true; }
            if (commandId == BuiltinChatCommandId.InsuranceQuote)
            {
                CancelQuote(message.SteamId);
                if (parts.Length is < 2 or > 3 || !long.TryParse(parts[1], out var vehicleId) || vehicleId <= 0)
                { await Reply("/versicherung FAHRZEUG-ID [1|2|4] · /insurance VEHICLE-ID [1|2|4]"); return true; }
                var vehicle = await OwnedVehicleAsync(message.SteamId, vehicleId, ct);
                var types = await _api.TypesAsync(ct);
                var candidates = types.Where(x => VehicleInsurancePricing.NormalizeClass(x) == VehicleInsurancePricing.NormalizeClass(vehicle.Class)).ToList();
                if (candidates.Count != 1) { await Reply(L("Spawnmodell nicht eindeutig. Admin muss den Fahrzeugtyp prüfen.", "Spawn model ambiguous. Ask an admin to check the vehicle type.")); return true; }
                if (parts.Length == 2)
                {
                    foreach (var w in new[] { 1, 2, 4 }) await Reply($"{vehicle.Name} #{vehicle.Id} | {w} {(german ? "Wochen" : "weeks")}: {VehicleInsurancePricing.Calculate(_options, candidates[0], w)}$ | /versicherung {vehicle.Id} {w}");
                    return true;
                }
                if (!int.TryParse(parts[2], out var weeks) || weeks is not (1 or 2 or 4)) { await Reply(L("Laufzeit: 1, 2 oder 4 Wochen.", "Duration: 1, 2 or 4 weeks.")); return true; }
                var cost = VehicleInsurancePricing.Calculate(_options, candidates[0], weeks);
                _quotes[message.SteamId] = new Quote(vehicleId, vehicle.Name, candidates[0], weeks, cost, _now());
                await Reply($"{vehicle.Name} #{vehicle.Id}: {cost}$ / {weeks} {(german ? "Wochen" : "weeks")}. /ja (60s) · /nein");
                await Reply(L("Ein Ersatz-Grundmodell bei Zerstörung. Kein Inventar/Tuning. Keine automatische Verlängerung.", "One replacement base model on destruction. No inventory/upgrades. No automatic renewal."));
                return true;
            }
            if (commandId == BuiltinChatCommandId.Confirm)
            {
                if (!_quotes.TryRemove(message.SteamId, out var quote) || quote.CreatedUtc.AddSeconds(60) <= _now() || message.LoggedAtUtc < quote.CreatedUtc.AddSeconds(-1))
                { await Reply(L("Angebot abgelaufen. Bitte neu anfragen.", "Quote expired. Please request a new quote.")); return true; }
                var currentVehicle = await OwnedVehicleAsync(message.SteamId, quote.VehicleId, ct);
                if (VehicleInsurancePricing.NormalizeClass(currentVehicle.Class) != VehicleInsurancePricing.NormalizeClass(quote.SpawnClass))
                    throw new InvalidOperationException("Vehicle model changed after the quote. Request a new quote.");
                var balance = await _api.BalanceAsync(message.SteamId, ct);
                if (!balance.HasValue || !double.IsFinite(balance.Value) || balance < quote.Price)
                { await Reply(L("Kontostand unbekannt oder Guthaben zu niedrig.", "Balance unavailable or insufficient funds.")); return true; }
                var start = _now();
                var policy = new VehicleInsurancePolicy { SteamId = message.SteamId, PlayerName = message.PlayerName, VehicleId = quote.VehicleId,
                    VehicleName = quote.Name, SpawnClass = quote.SpawnClass, Weeks = quote.Weeks, Price = quote.Price,
                    StartUtc = start, EndUtc = start.AddDays(quote.Weeks * 7), Status = InsuranceStatus.PaymentReview };
                _state.Policies.Add(policy);
                Note(policy, "Debit pending; do not retry without reconciliation."); Save();
                // If this request or its response is lost, PaymentReview survives and blocks repeat payments.
                await _api.DebitAsync(message.SteamId, quote.Price, ct);
                policy.Status = InsuranceStatus.Active; Note(policy, "Payment acknowledged; policy active."); Save();
                await Reply($"{policy.Id} | {policy.VehicleName} | {policy.Price}$ | {(german ? "Versichert bis" : "Covered until")} {policy.EndUtc.ToLocalTime():g}");
                return true;
            }
            if (commandId == BuiltinChatCommandId.InsuranceRefund)
            {
                if (parts.Length != 2) { await Reply("/getrefund V-ID · /versicherungen"); return true; }
                var p = _state.Policies.SingleOrDefault(x => x.SteamId == message.SteamId && x.Id.Equals(parts[1], StringComparison.OrdinalIgnoreCase));
                if (p?.Status != InsuranceStatus.Claimable) { await Reply(L("Kein freigegebener Ersatz für diesen Vertrag.", "No approved replacement for this policy.")); return true; }
                if (!HasCoveredDestruction(p)) throw new InvalidOperationException("Verified destruction evidence is missing; admin review required.");
                // A destroyed wreck can remain rendered and listed by ggCON. Its presence
                // must not override the already verified Destroyed event.
                if (!(await _api.TypesAsync(ct)).Contains(p.SpawnClass, StringComparer.OrdinalIgnoreCase)) throw new InvalidOperationException("Spawn model no longer available.");
                p.Status = InsuranceStatus.SpawnReview; Note(p, "Replacement dispatch pending; do not retry without reconciliation."); Save();
                await _api.SpawnAsync(message.SteamId, p.SpawnClass, ct);
                p.Status = InsuranceStatus.Claimed; p.ClaimedUtc = _now(); Note(p, "Replacement command acknowledged; contract closed."); Save();
                await Reply(L("Ersatz angefordert. Vertrag geschlossen; neues Fahrzeug separat versichern.", "Replacement requested. Policy closed; insure the new vehicle separately."));
                return true;
            }
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _log("Insurance: " + ex.Message);
            try { await _api.MessageAsync(message.SteamId, german ? "Versicherung konnte den Vorgang nicht abschließen. Bei Zahlung/Spawn bitte Admin prüfen lassen; nichts wird automatisch wiederholt." : "Insurance could not finish. Ask an admin to check payment/spawn; nothing is retried automatically.", ct); }
            catch (Exception notice) { _log("Insurance notice: " + notice.Message); }
            return true;
        }
        finally { _gate.Release(); }
    }

    private async Task<InsurableVehicle> OwnedVehicleAsync(string steamId, long id, CancellationToken ct)
    {
        // Do not sell a new policy for the same wreck after the replacement was claimed.
        if (_state.Policies.Any(p => p.VehicleId == id && p.DestroyedUtc.HasValue))
            throw new InvalidOperationException("This vehicle is recorded as destroyed. Insure the replacement's new vehicle ID.");
        var list = await _api.VehiclesAsync(ct); ValidateList(list);
        var vehicle = list.Vehicles.SingleOrDefault(x => x.Id == id);
        if (!list.OwnershipResolved || vehicle is null || vehicle.OwnerSteamId != steamId) throw new InvalidOperationException("Vehicle ownership could not be verified.");
        if (_state.Policies.Any(p => p.VehicleId == id && (p.Status is InsuranceStatus.PaymentReview or InsuranceStatus.DestroyedPendingCheck or InsuranceStatus.Claimable or InsuranceStatus.SpawnReview ||
            p.Status == InsuranceStatus.Active && p.EndUtc > _now())))
            throw new InvalidOperationException("Vehicle already covered or awaiting reconciliation.");
        return vehicle;
    }

    public static void ValidateList(InsuranceVehicleList list)
    {
        if (!list.Ok || list.Vehicles is null || list.Count != list.Vehicles.Count || list.Vehicles.Any(v => v.Id <= 0) || list.Vehicles.Select(v => v.Id).Distinct().Count() != list.Count)
            throw new InvalidDataException("Incomplete vehicle snapshot; no insurance action allowed.");
    }

    public async Task PollAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (!_options.Enabled || _storageFault) return;
            ReleaseVerifiedDestructions(); // Also recover previously saved pending cases without replaying old logs.
            var batch = await _api.DestructionsAsync(_state.LogCursor, ct);
            foreach (var entry in batch.Entries)
            {
                if (!entry.Action.Equals("Destroyed", StringComparison.OrdinalIgnoreCase) || !entry.Timestamp.HasValue || !long.TryParse(entry.VehicleId, out var id)) continue;
                var time = entry.Timestamp.Value.ToUniversalTime();
                foreach (var policy in _state.Policies.Where(p => p.VehicleId == id && p.Status == InsuranceStatus.Active && p.StartUtc <= time && p.EndUtc > time &&
                    p.SteamId == entry.OwnerSteamId && VehicleInsurancePricing.NormalizeClass(p.SpawnClass) == VehicleInsurancePricing.NormalizeClass(entry.VehicleClass)))
                {
                    policy.Status = InsuranceStatus.DestroyedPendingCheck; policy.DestroyedUtc = time;
                    Note(policy, "Destroyed event matched by ID, owner, model and coverage period.");
                }
            }
            _state.LogCursor = batch.Next ?? _state.LogCursor;
            Save(); // Persist matched event evidence before releasing a claim.
            ReleaseVerifiedDestructions();
        }
        finally { _gate.Release(); }
    }

    private static bool HasCoveredDestruction(VehicleInsurancePolicy policy) =>
        policy.DestroyedUtc.HasValue && policy.StartUtc <= policy.DestroyedUtc.Value && policy.DestroyedUtc.Value < policy.EndUtc;

    private void ReleaseVerifiedDestructions()
    {
        // DestroyedPendingCheck is only written after matching ID, owner, model and coverage.
        // No absence/rendered check: both an intact car and a non-drivable wreck can be listed.
        var pending = _state.Policies.Where(p => p.Status == InsuranceStatus.DestroyedPendingCheck && HasCoveredDestruction(p)).ToList();
        if (pending.Count == 0) return;
        foreach (var policy in pending)
        {
            policy.Status = InsuranceStatus.Claimable;
            Note(policy, "Verified Destroyed event: replacement available; a remaining wreck does not block the claim.");
        }
        Save();
    }

    // Administrative reconciliation changes the ledger only. Never issues money or another spawn.
    public async Task ResolveAsync(string id, bool succeeded)
    {
        await _gate.WaitAsync();
        try
        {
            if (_storageFault) throw new IOException("Restart after repairing insurance storage first.");
            var p = _state.Policies.Single(x => x.Id == id);
            p.Status = p.Status switch
            {
                InsuranceStatus.PaymentReview => succeeded ? InsuranceStatus.Active : InsuranceStatus.Cancelled,
                InsuranceStatus.SpawnReview => succeeded ? InsuranceStatus.Claimed : InsuranceStatus.Claimable,
                _ => throw new InvalidOperationException("This policy does not need transaction reconciliation.")
            };
            if (p.Status == InsuranceStatus.Claimed) p.ClaimedUtc = _now();
            Note(p, succeeded ? "Admin verified transaction succeeded." : "Admin verified transaction did NOT happen."); Save();
        }
        finally { _gate.Release(); }
    }

    private sealed record Quote(long VehicleId, string Name, string SpawnClass, int Weeks, int Price, DateTime CreatedUtc);
}
