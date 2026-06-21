using System.Text.Json;

namespace ScumRconTool.Services;

public sealed class VoteCooldownStore
{
    private readonly string _path;
    private readonly Dictionary<string, DateTime> _lastVoteUtc = new(StringComparer.OrdinalIgnoreCase);

    public VoteCooldownStore(string fileName = "vote-cooldowns.json")
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScumRconTool", "State");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, fileName);
        Load();
    }

    public bool CanVote(string playerKey, TimeSpan cooldown, out TimeSpan remaining, out DateTime? lastVoteUtc)
    {
        remaining = TimeSpan.Zero;
        lastVoteUtc = null;

        playerKey = NormalizeKey(playerKey);
        if (string.IsNullOrWhiteSpace(playerKey) || cooldown <= TimeSpan.Zero)
        {
            return true;
        }

        var now = DateTime.UtcNow;
        if (_lastVoteUtc.TryGetValue(playerKey, out var lastVote))
        {
            lastVoteUtc = lastVote;
            var elapsed = now - lastVote;
            if (elapsed < cooldown)
            {
                remaining = cooldown - elapsed;
                return false;
            }
        }

        return true;
    }

    public void RecordVote(string playerKey)
    {
        playerKey = NormalizeKey(playerKey);
        if (string.IsNullOrWhiteSpace(playerKey))
        {
            return;
        }

        _lastVoteUtc[playerKey] = DateTime.UtcNow;
        Save();
    }

    public void Prune(TimeSpan keepFor)
    {
        if (keepFor <= TimeSpan.Zero) keepFor = TimeSpan.FromDays(7);

        var cutoff = DateTime.UtcNow - keepFor;
        var oldKeys = _lastVoteUtc
            .Where(x => x.Value < cutoff)
            .Select(x => x.Key)
            .ToList();

        foreach (var key in oldKeys)
        {
            _lastVoteUtc.Remove(key);
        }

        if (oldKeys.Count > 0)
        {
            Save();
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var json = File.ReadAllText(_path);
            var data = JsonSerializer.Deserialize<Dictionary<string, DateTime>>(json);
            if (data is null) return;

            foreach (var item in data)
            {
                if (string.IsNullOrWhiteSpace(item.Key)) continue;
                _lastVoteUtc[NormalizeKey(item.Key)] = DateTime.SpecifyKind(item.Value, DateTimeKind.Utc);
            }
        }
        catch
        {
            _lastVoteUtc.Clear();
        }
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var json = JsonSerializer.Serialize(_lastVoteUtc, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_path, json);
    }

    private static string NormalizeKey(string value) => (value ?? string.Empty).Trim().ToLowerInvariant();
}
