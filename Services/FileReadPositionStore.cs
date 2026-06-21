using System.Text.Json;

namespace ScumRconTool.Services;

public sealed class FileReadPositionStore
{
    private readonly string _path;
    private readonly Dictionary<string, long> _positions = new(StringComparer.OrdinalIgnoreCase);

    public FileReadPositionStore(string fileName)
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScumRconTool", "State");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, fileName);
        Load();
    }

    public long GetPosition(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return 0;
        return _positions.TryGetValue(key, out var value) ? Math.Max(0, value) : 0;
    }

    public void SetPosition(string key, long position)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        _positions[key] = Math.Max(0, position);
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var json = JsonSerializer.Serialize(_positions, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_path, json);
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var json = File.ReadAllText(_path);
            var data = JsonSerializer.Deserialize<Dictionary<string, long>>(json);
            if (data is null) return;
            foreach (var item in data)
            {
                _positions[item.Key] = Math.Max(0, item.Value);
            }
        }
        catch
        {
            _positions.Clear();
        }
    }
}
