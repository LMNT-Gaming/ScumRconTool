using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.IO;

namespace ScumRconTool.Services;

public sealed class GenericSeenStore
{
    private readonly string _path;
    private readonly HashSet<string> _seen = new(StringComparer.OrdinalIgnoreCase);

    public GenericSeenStore(string fileName)
    {
        _path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ScumRconTool", fileName);
        Load();
    }

    public bool Add(string value) => _seen.Add(Hash(value));
    public bool Contains(string value) => _seen.Contains(Hash(value));

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
