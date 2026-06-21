using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.IO;

namespace ScumRconTool.Services;

public sealed class KillLogSeenStore
{
    private readonly string _path;
    private readonly HashSet<string> _seen = new(StringComparer.OrdinalIgnoreCase);

    public KillLogSeenStore()
    {
        _path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ScumRconTool", "killlog-seen.json");
        Load();
    }

    public bool IsEmpty => _seen.Count == 0;

    public bool Add(KillLogEntry entry) => _seen.Add(Hash(entry.Key));

    public bool Contains(KillLogEntry entry) => _seen.Contains(Hash(entry.Key));

    public void Save()
    {
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(_path, JsonSerializer.Serialize(_seen.OrderBy(x => x).ToArray(), new JsonSerializerOptions { WriteIndented = true }));
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var values = JsonSerializer.Deserialize<string[]>(File.ReadAllText(_path));
            if (values is null) return;
            foreach (var value in values) _seen.Add(value);
        }
        catch
        {
            _seen.Clear();
        }
    }

    private static string Hash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes);
    }
}
