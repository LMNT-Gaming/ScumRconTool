using ScumRconTool.Services;
using System.Text.Json;

var passed = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception("FAILED: " + name); Console.WriteLine("PASS " + name); passed++; }
var options = new VehicleInsuranceOptions { Enabled = true };
Check(BuiltinChatCommandCatalog.All.Select(x => x.Id).Distinct().Count() == Enum.GetValues<BuiltinChatCommandId>().Length, "every built-in has a required catalog entry");
Check(BuiltinChatCommandCatalog.All.All(x => !string.IsNullOrWhiteSpace(x.DescriptionDe) && !string.IsNullOrWhiteSpace(x.DescriptionEn)), "required commands documented in German and English");
foreach (var (text, id) in new (string, BuiltinChatCommandId)[] {
    ("/versicherung 4000008 2",BuiltinChatCommandId.InsuranceQuote), ("/INSURANCE 123",BuiltinChatCommandId.InsuranceQuote),
    ("/versicherungen",BuiltinChatCommandId.InsuranceList), ("/insurances",BuiltinChatCommandId.InsuranceList),
    ("/getrefund V-123",BuiltinChatCommandId.InsuranceRefund), ("/ja",BuiltinChatCommandId.Confirm), ("/yes",BuiltinChatCommandId.Confirm),
    ("/nein",BuiltinChatCommandId.Cancel), ("/no",BuiltinChatCommandId.Cancel), ("/claimall",BuiltinChatCommandId.ClaimAll),
    ("/reward-ABCDEF",BuiltinChatCommandId.Reward), ("/quiz12 Berlin",BuiltinChatCommandId.Quiz), ("/mechs",BuiltinChatCommandId.Mechs),
    ("/buyevent Event with spaces",BuiltinChatCommandId.BuyEvent), ("/wc",BuiltinChatCommandId.Challenges),
    ("/challenge",BuiltinChatCommandId.Challenges), ("/challenges",BuiltinChatCommandId.Challenges) })
    Check(BuiltinChatCommandCatalog.Resolve(text) == id, "shared dispatch: " + text);
foreach (var unknown in new[] { "/insurancesomething", "/mechs now", "/buyeventual", "/quizN answer", "/quiz1extra", "/custom", "/starter" })
    Check(BuiltinChatCommandCatalog.Resolve(unknown) is null, "custom/invalid command not captured: " + unknown);
Check(VehicleInsurancePricing.Calculate(options, "Barba_ES", 1) == 10000, "one week");
Check(VehicleInsurancePricing.Calculate(options, "Barba_ES", 2) == 19000, "two weeks 5 percent");
Check(VehicleInsurancePricing.Calculate(options, "Barba_ES", 4) == 36000, "four weeks 10 percent");
options.Prices.Add(new() { VehicleClass = "BPC_Barba", WeeklyPrice = 20000 });
Check(VehicleInsurancePricing.Calculate(options, "Barba_ES", 4) == 72000, "per-model price normalization");
options.Prices[0].Enabled = false;
try { VehicleInsurancePricing.Calculate(options, "Barba_ES", 1); throw new Exception("FAILED disabled model"); } catch (InvalidOperationException) { passed++; }
options.Prices[0].Enabled = true;
var root = Path.Combine(Path.GetTempPath(), "redraven-insurance-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var now = DateTime.UtcNow;
var api = new FakeApi();
var service = new VehicleInsuranceService(api, options, Path.Combine(root, "policy.json"), _ => {}, () => now);
ChatLogMessage Msg(string text, string steam = FakeApi.Steam) { now = now.AddSeconds(2); return new() { Message = text, SteamId = steam, PlayerName = "Test", LoggedAtUtc = now }; }
await service.HandleAsync(Msg("/versicherung 123 2", "76561198000000002"), true, default);
await service.HandleAsync(Msg("/ja", "76561198000000002"), true, default);
Check(api.Debits == 0, "wrong owner blocked");
await service.HandleAsync(Msg("/versicherung 123 2"), true, default);
var yes = Msg("/ja");
await Task.WhenAll(service.HandleAsync(yes, true, default), service.HandleAsync(yes, true, default));
Check(api.Debits == 1, "duplicate confirmation charged once");
var policy = (await service.SnapshotAsync()).Single();
Check(policy.Price == 38000 && policy.Status == InsuranceStatus.Active, "price locked and active");
await service.HandleAsync(Msg("/versicherung 123 1"), true, default);
await service.HandleAsync(Msg("/ja"), true, default);
Check(api.Debits == 1, "active policy cannot be purchased twice");
now = now.AddHours(1);
VehicleDestructionLogEntry Entry(string action, string steam = FakeApi.Steam) => VehicleDestructionLogParser.ParseLine($"{now:yyyy.MM.dd-HH.mm.ss}: [{action}] Barba_ES. VehicleId: 123. Owner: {steam} (1, Test). Location: X=1 Y=2 Z=3", "test")!;
api.Entries = new() { Entry("Disappeared") }; await service.PollAsync(default);
Check((await service.SnapshotAsync()).Single().Status == InsuranceStatus.Active, "disappearance not a covered destruction");
api.Entries = new() { Entry("Destroyed", "76561198000000002") }; await service.PollAsync(default);
Check((await service.SnapshotAsync()).Single().Status == InsuranceStatus.Active, "wrong destruction owner blocked");
api.Entries = new() { VehicleDestructionLogParser.ParseLine(Entry("Destroyed").RawLine.Replace("Barba_ES", "Laika_ES"), "test")! }; await service.PollAsync(default);
Check((await service.SnapshotAsync()).Single().Status == InsuranceStatus.Active, "wrong destruction model blocked");
api.Entries = new() { new() { Action = "Destroyed", Timestamp = policy.StartUtc.AddSeconds(-1), VehicleId = "123", VehicleClass = "Barba_ES", OwnerSteamId = FakeApi.Steam } }; await service.PollAsync(default);
Check((await service.SnapshotAsync()).Single().Status == InsuranceStatus.Active, "destruction outside coverage blocked");
api.Entries = new() { Entry("Destroyed") }; await service.PollAsync(default);
Check((await service.SnapshotAsync()).Single().Status == InsuranceStatus.Claimable && api.Vehicles.Count == 1, "verified destruction qualifies even while wreck remains listed");
await service.PollAsync(default);
Check((await service.SnapshotAsync()).Count == 1, "repeated destruction event does not duplicate claim");
api.FailSpawn = true;
await service.HandleAsync(Msg("/getrefund " + policy.Id), true, default);
Check((await service.SnapshotAsync()).Single().Status == InsuranceStatus.SpawnReview, "uncertain spawn locked");
service = new VehicleInsuranceService(api, options, Path.Combine(root, "policy.json"), _ => {}, () => now);
await service.HandleAsync(Msg("/getrefund " + policy.Id), true, default);
Check(api.Spawns == 1, "restart never repeats uncertain spawn");
await service.ResolveAsync(policy.Id, false); api.FailSpawn = false;
await service.HandleAsync(Msg("/getrefund " + policy.Id), true, default);
await service.HandleAsync(Msg("/getrefund " + policy.Id), true, default);
Check(api.Spawns == 2 && (await service.SnapshotAsync()).Single().Status == InsuranceStatus.Claimed, "confirmed failed dispatch can retry once; claimed stays closed");
await service.HandleAsync(Msg("/versicherung 123 1"), true, default);
await service.HandleAsync(Msg("/ja"), true, default);
Check(api.Debits == 1, "claimed wreck cannot be insured again");
api = new FakeApi { FailDebit = true };
var paymentPath = Path.Combine(root, "payment.json");
service = new VehicleInsuranceService(api, options, paymentPath, _ => {}, () => now);
await service.HandleAsync(Msg("/versicherung 123 1"), true, default);
await service.HandleAsync(Msg("/ja"), true, default);
service = new VehicleInsuranceService(api, options, paymentPath, _ => {}, () => now);
await service.HandleAsync(Msg("/versicherung 123 1"), true, default); await service.HandleAsync(Msg("/ja"), true, default);
Check(api.Debits == 1 && (await service.SnapshotAsync()).Single().Status == InsuranceStatus.PaymentReview, "uncertain debit survives restart and blocks repeat purchase");
api = new FakeApi(); service = new VehicleInsuranceService(api, options, Path.Combine(root, "expiry.json"), _ => {}, () => now);
await service.HandleAsync(Msg("/versicherung 123 1"), true, default); now = now.AddSeconds(61); await service.HandleAsync(Msg("/ja"), true, default);
Check(api.Debits == 0, "quote timeout");
await service.HandleAsync(new() { SteamId = FakeApi.Steam, Message = "/versicherung 123 1", LoggedAtUtc = now.AddHours(-1) }, true, default);
await service.HandleAsync(Msg("/ja"), true, default); Check(api.Debits == 0, "old chat backfill rejected");
await service.HandleAsync(Msg("/versicherung 123 1"), true, default); service.CancelQuote(FakeApi.Steam);
Check(!await service.HandleAsync(Msg("/ja"), true, default), "cancelled quote hands confirmation back to vote handler");
await service.HandleAsync(Msg("/versicherung 123 1"), true, default);
api.Vehicles[0].Class = "Laika_ES";
await service.HandleAsync(Msg("/ja"), true, default);
Check(api.Debits == 0, "model changed since quote blocks payment");
try { VehicleInsuranceService.ValidateList(new() { Ok = true, Count = 1, Vehicles = new() }); throw new Exception("FAILED snapshot"); } catch (InvalidDataException) { passed++; }
// Regression: actual SCUM log format, with synthetic identity and a fixed UTC clock.
now = new DateTime(2026, 9, 3, 16, 16, 18, DateTimeKind.Utc);
api = new FakeApi(); api.Vehicles[0].Id = 4000008;
service = new VehicleInsuranceService(api, options, Path.Combine(root, "utc-chat.json"), _ => {}, () => now);
ChatLogMessage Raw(string command) => AutomationLogParser.ParseChatLine($"{now:yyyy.MM.dd-HH.mm.ss}: '{FakeApi.Steam}:Test(0)' 'Global: {command}'")!;
var actual = Raw("/versicherung 4000008");
Check(actual.LoggedAtUtc == now && actual.LoggedAtUtc.Value.Kind == DateTimeKind.Utc, "real chat timestamp stays UTC in local timezone");
await service.HandleAsync(actual, true, default);
Check(api.Messages.Count == 3 && api.Messages.Any(x => x.Contains("4000008")), "real-format insurance query produces all three offers");
await service.HandleAsync(Raw("/versicherungen"), true, default);
Check(api.Messages.Last() == "Keine Versicherungen vorhanden.", "real-format policies query replies");
await service.HandleAsync(Raw("/versicherung 4000008 1"), true, default);
now = now.AddSeconds(2); await service.HandleAsync(Raw("/ja"), true, default);
Check(api.Debits == 1, "real-format fresh confirmation is accepted");
now = now.AddSeconds(10);
api.Entries = new() { VehicleDestructionLogParser.ParseLine($"{now:yyyy.MM.dd-HH.mm.ss}: [Destroyed] Barba_ES. VehicleId: 4000008. Owner: {FakeApi.Steam} (1, Test). Location: X=1 Y=2 Z=3", "test")! };
Check(api.Entries[0].Timestamp == now && api.Entries[0].Timestamp!.Value.Kind == DateTimeKind.Utc, "destruction log timestamp stays UTC");
api.Vehicles.Clear(); await service.PollAsync(default);
Check((await service.SnapshotAsync()).Single().Status == InsuranceStatus.Claimable, "destruction just after purchase remains inside coverage");
foreach (var utc in new[] { new DateTime(2026,1,3,16,0,0,DateTimeKind.Utc), new DateTime(2026,10,25,1,30,0,DateTimeKind.Utc) })
{
 now = utc;
 Check(Raw("/versicherungen").LoggedAtUtc == utc, "UTC parsing independent of winter/DST transition");
}
var legacyPath = Path.Combine(root, "pending-wreck.json");
var legacy = new VehicleInsurancePolicy { SteamId = FakeApi.Steam, VehicleId = 123, VehicleName = "Barba", SpawnClass = "BPC_Barba",
    StartUtc = now.AddDays(-1), EndUtc = now.AddDays(6), DestroyedUtc = now.AddMinutes(-5), Status = InsuranceStatus.DestroyedPendingCheck };
var missingEvidence = new VehicleInsurancePolicy { VehicleId = 456, StartUtc = now.AddDays(-1), EndUtc = now.AddDays(6), Status = InsuranceStatus.DestroyedPendingCheck };
File.WriteAllText(legacyPath, JsonSerializer.Serialize(new VehicleInsuranceState { Policies = new() { legacy, missingEvidence } }));
api = new FakeApi(); // Old event is no longer in the log buffer; wreck is still listed.
service = new VehicleInsuranceService(api, options, legacyPath, _ => {}, () => now);
await service.PollAsync(default);
var migrated = await service.SnapshotAsync();
Check(migrated.Single(p => p.Id == legacy.Id).Status == InsuranceStatus.Claimable, "saved pending wreck released without replaying old event");
Check(migrated.Single(p => p.Id == missingEvidence.Id).Status == InsuranceStatus.DestroyedPendingCheck, "pending case without evidence is not released");
Check(api.Debits == 0 && api.Spawns == 0, "pending-case recovery does not charge or spawn");
Console.WriteLine($"{passed} checks passed. Offline only. Test state: {root}");

public sealed class FakeApi : IVehicleInsuranceApi
{
 public const string Steam = "76561198000000001";
 public List<InsurableVehicle> Vehicles = new() { new() { Id = 123, Class = "Barba_ES", Name = "Barba", OwnerSteamId = Steam } };
 public List<VehicleDestructionLogEntry> Entries = new();
 public int Debits, Spawns;
 public List<string> Messages = new();
 public bool FailDebit, FailSpawn;
 public Task<InsuranceVehicleList> VehiclesAsync(CancellationToken ct) => Task.FromResult(new InsuranceVehicleList { Ok = true, OwnershipResolved = true, Count = Vehicles.Count, Vehicles = Vehicles.ToList() });
 public Task<List<string>> TypesAsync(CancellationToken ct) => Task.FromResult(new List<string> { "BPC_Barba" });
 public Task<double?> BalanceAsync(string id, CancellationToken ct) => Task.FromResult<double?>(1000000);
 public Task DebitAsync(string id, int amount, CancellationToken ct) { Debits++; if (FailDebit) throw new TimeoutException(); return Task.CompletedTask; }
 public Task SpawnAsync(string id, string model, CancellationToken ct) { Spawns++; if (FailSpawn) throw new TimeoutException(); return Task.CompletedTask; }
 public Task MessageAsync(string id, string text, CancellationToken ct) { Messages.Add(text); return Task.CompletedTask; }
 public Task<InsuranceDestructionBatch> DestructionsAsync(long? since, CancellationToken ct) => Task.FromResult(new InsuranceDestructionBatch(1, Entries));
}
