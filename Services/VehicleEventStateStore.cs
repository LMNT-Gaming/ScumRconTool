using System.Text.Json;

namespace ScumRconTool.Services;

public sealed class VehicleEventStateStore
{
    private readonly string _path;
    private readonly Dictionary<string, VehicleEventState> _states = new(StringComparer.OrdinalIgnoreCase);

    public VehicleEventStateStore(string fileName = "vehicle-event-states.json")
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScumRconTool", "State");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, fileName);
        Load();
    }

    public bool ShouldSuppressDisappeared(VehicleDestructionLogEntry entry)
    {
        if (!entry.Action.Equals("Disappeared", StringComparison.OrdinalIgnoreCase)) return false;
        if (string.IsNullOrWhiteSpace(entry.VehicleId)) return false;
        if (!_states.TryGetValue(entry.VehicleId, out var state)) return false;
        if (state.TimestampUtc < DateTime.UtcNow.AddDays(-5)) return false;

        return state.Action.Equals("Destroyed", StringComparison.OrdinalIgnoreCase)
            || state.Action.Equals("VehicleInactiveTimerReached", StringComparison.OrdinalIgnoreCase)
            || state.Action.Equals("ForbiddenZoneTimerExpired", StringComparison.OrdinalIgnoreCase)
            || state.Action.Equals("Failed to spawn", StringComparison.OrdinalIgnoreCase);
    }

    public void Remember(VehicleDestructionLogEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.VehicleId)) return;
        var timestampUtc = entry.Timestamp?.ToUniversalTime() ?? DateTime.UtcNow;
        _states[entry.VehicleId] = new VehicleEventState
        {
            Action = entry.Action,
            TimestampUtc = timestampUtc
        };
    }

    public void Cleanup()
    {
        var cutoff = DateTime.UtcNow.AddDays(-5);
        foreach (var key in _states.Where(x => x.Value.TimestampUtc < cutoff).Select(x => x.Key).ToList())
        {
            _states.Remove(key);
        }
    }

    public void Save()
    {
        Cleanup();
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(_states, new JsonSerializerOptions { WriteIndented = true }));
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var data = JsonSerializer.Deserialize<Dictionary<string, VehicleEventState>>(File.ReadAllText(_path));
            if (data is null) return;
            foreach (var item in data)
            {
                _states[item.Key] = item.Value;
            }
            Cleanup();
        }
        catch
        {
            _states.Clear();
        }
    }

    private sealed class VehicleEventState
    {
        public string Action { get; set; } = string.Empty;
        public DateTime TimestampUtc { get; set; }
    }
}
