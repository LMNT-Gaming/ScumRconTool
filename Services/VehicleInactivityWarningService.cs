using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace ScumRconTool.Services;

public sealed record VehicleInactivityWarning(string VehicleId, string VehicleName, string OwnerName, DateTime LastAccessUtc, DateTime DeletionUtc);

public sealed class VehicleInactivityWarningService : IDisposable
{
    private static readonly Regex OwnerIdRegex = new("(?:_owningUserProfileId|owningUserProfileId)=\\\"(?<id>\\d+)\\\"", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private readonly BotSettings settings;
    private readonly SftpLogService sftp;
    private readonly Func<VehicleInactivityWarning, CancellationToken, Task> publish;
    private readonly Action<string> log;
    private readonly string statePath = Path.Combine(EventDefinitionStore.DataDirectory, "State", "vehicle-inactivity-warnings.json");
    private readonly CancellationTokenSource cts = new();
    private Task? loop;

    public VehicleInactivityWarningService(BotSettings settings, Func<VehicleInactivityWarning, CancellationToken, Task> publish, Action<string> log)
    { this.settings = settings; this.publish = publish; this.log = log; sftp = new SftpLogService(settings); }

    public void Start() => loop ??= Task.Run(() => LoopAsync(cts.Token));

    private async Task LoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var retrySoon = false;
            try { await ScanAsync(token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (Exception ex) { retrySoon = true; log("Fahrzeug-Inaktivitätswarnung: " + ex.Message); AppLogService.WriteException("VehicleInactivityWarning", ex); }
            var waitMinutes = retrySoon ? 5 : Math.Clamp(settings.VehicleInactivityScanMinutes, 15, 1440);
            try { await Task.Delay(TimeSpan.FromMinutes(waitMinutes), token); }
            catch (OperationCanceledException) { break; }
        }
    }

    internal async Task ScanAsync(CancellationToken token)
    {
        if (!settings.VehicleInactivityWarningEnabled || settings.VehicleInactivityWarningDiscordChannelId == 0) return;
        var root = Path.Combine(EventDefinitionStore.DataDirectory, "VehicleInactivity");
        var ini = await sftp.DownloadFileAsync(settings.SettingRandomizerRemoteFilePath, Path.Combine("VehicleInactivity", "Ini"), token);
        var db = await sftp.DownloadFileAsync(settings.WeeklyTaskDbRemoteFilePath, Path.Combine("VehicleInactivity", "Db"), token);
        if (string.IsNullOrWhiteSpace(ini) || string.IsNullOrWhiteSpace(db) || !File.Exists(ini) || !File.Exists(db)) return;
        try
        {
            var inactivity = ReadInactivity(File.ReadAllLines(ini));
            var warnBefore = TimeSpan.FromHours(Math.Clamp(settings.VehicleInactivityWarningHours, 1, 168));
            var now = DateTime.UtcNow;
            var sent = LoadState();
            foreach (var vehicle in ReadVehicles(db, inactivity).Where(v => v.DeletionUtc > now && v.DeletionUtc - now <= warnBefore))
            {
                var key = vehicle.VehicleId + "|" + vehicle.LastAccessUtc.Ticks.ToString(CultureInfo.InvariantCulture);
                if (sent.Contains(key)) continue;
                await publish(vehicle, token);
                sent.Add(key);
                SaveState(sent);
            }
        }
        finally
        {
            LocalRetentionService.TryDeleteFile(ini); LocalRetentionService.TryDeleteFile(db);
            LocalRetentionService.CleanupDirectory(root);
        }
    }

    internal static TimeSpan ReadInactivity(IEnumerable<string> lines)
    {
        const string key = "scum.MaximumTimeOfVehicleInactivity=";
        var value = lines.Select(x => x.Trim()).FirstOrDefault(x => x.StartsWith(key, StringComparison.OrdinalIgnoreCase))?[key.Length..].Trim();
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException("MaximumTimeOfVehicleInactivity fehlt in ServerSettings.ini.");
        var parts = value.Split(':');
        if (parts.Length != 3 || !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var hours)
            || !int.TryParse(parts[1], out var minutes) || !int.TryParse(parts[2], out var seconds)
            || hours < 1 || minutes is < 0 or > 59 || seconds is < 0 or > 59)
            throw new InvalidOperationException("MaximumTimeOfVehicleInactivity ist ungültig: " + value);
        return TimeSpan.FromHours(hours) + TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds);
    }

    private static IReadOnlyList<VehicleInactivityWarning> ReadVehicles(string dbPath, TimeSpan inactivity)
    {
        var output = new Dictionary<string, VehicleInactivityWarning>(StringComparer.Ordinal);
        var cs = new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadOnly }.ToString();
        using var connection = new SqliteConnection(cs); connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = @"SELECT vs.vehicle_entity_id, vs.vehicle_asset_id, vs.vehicle_last_access_time, ie.xml
FROM vehicle_spawner vs JOIN vehicle_entity ve ON vs.vehicle_entity_id=ve.entity_id
JOIN item_entity ie ON ve.item_container_entity_id=ie.entity_id
WHERE vs.vehicle_last_access_time IS NOT NULL AND vs.vehicle_last_access_time > 0";
        using var reader = command.ExecuteReader();
        var rows = new List<(string Id,string Asset,long Last,IReadOnlyList<long> Owners)>(); var ownerIds = new HashSet<long>();
        while (reader.Read())
        {
            var candidates = OwnerIdRegex.Matches(reader.IsDBNull(3) ? "" : reader.GetString(3))
                .Select(match => long.TryParse(match.Groups["id"].Value, out var id) ? id : 0)
                .Where(id => id > 0).Distinct().ToList();
            if (candidates.Count == 0) continue;
            var row = (Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture) ?? "", reader.IsDBNull(1) ? "" : reader.GetString(1), Convert.ToInt64(reader.GetValue(2), CultureInfo.InvariantCulture), (IReadOnlyList<long>)candidates);
            if (row.Item1.Length == 0) continue; rows.Add(row); foreach (var owner in candidates) ownerIds.Add(owner);
        }
        reader.Close();
        var names = new Dictionary<long,string>();
        if (ownerIds.Count > 0)
        {
            using var owners = connection.CreateCommand();
            owners.CommandText = "SELECT id,name FROM user_profile WHERE id IN (" + string.Join(',', ownerIds) + ")";
            using var rr = owners.ExecuteReader(); while (rr.Read()) names[rr.GetInt64(0)] = rr.IsDBNull(1) ? "Unbekannt" : rr.GetString(1);
        }
        foreach (var row in rows)
        {
            var last = DateTimeOffset.FromUnixTimeSeconds(row.Last).UtcDateTime;
            var name = row.Asset.Replace("Vehicle:BPC_", "", StringComparison.OrdinalIgnoreCase);
            var ownerName = row.Owners.Select(id => names.GetValueOrDefault(id, "")).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            if (string.IsNullOrWhiteSpace(ownerName)) continue;
            output[row.Id] = new(row.Id, string.IsNullOrWhiteSpace(name) ? "Unbekannt" : name, ownerName, last, last + inactivity);
        }
        return output.Values.ToList();
    }

    private HashSet<string> LoadState()
    {
        try { return File.Exists(statePath) ? JsonSerializer.Deserialize<HashSet<string>>(File.ReadAllText(statePath)) ?? [] : []; }
        catch { return []; }
    }
    private void SaveState(HashSet<string> state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(statePath)!);
        var relevant = state.TakeLast(10000).ToHashSet(StringComparer.Ordinal);
        File.WriteAllText(statePath, JsonSerializer.Serialize(relevant));
    }
    public void Dispose() { cts.Cancel(); cts.Dispose(); }
}
