using System.Text.Json;
using System.Text.Json.Serialization;

namespace ScumRconTool.Services;

public sealed class WeaponCompatibilityCatalog
{
    private static IReadOnlyList<WeaponCompatibilityEntry>? _cache;

    public IReadOnlyList<WeaponCompatibilityEntry> Load()
    {
        if (_cache is not null) return _cache;

        try
        {
            var json = ReadCatalogJson();
            if (string.IsNullOrWhiteSpace(json)) return _cache = Array.Empty<WeaponCompatibilityEntry>();
            var source = JsonSerializer.Deserialize<WeaponCompatibilitySource>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new WeaponCompatibilitySource();

            _cache = source.Weapons
                .Where(weapon => !string.IsNullOrWhiteSpace(weapon.Id))
                .Select(weapon => new WeaponCompatibilityEntry(
                    weapon.Name,
                    weapon.Id,
                    weapon.Caliber,
                    weapon.MagazineName,
                    ToSpawnableItemId(weapon.MagazineId),
                    ToSpawnableItemId(weapon.AmmoBoxId),
                    weapon.OptionIndexes
                        .Where(index => index >= 0 && index < source.Options.Count)
                        .Select(index => source.Options[index])
                        .Where(option => !string.IsNullOrWhiteSpace(option.Id))
                        .Select(option => new WeaponAccessoryEntry(
                            option.Group,
                            ToSpawnableItemId(option.RequiredRailId),
                            option.Name,
                            ToSpawnableItemId(option.Id)))
                        .ToList()))
                .ToList();
        }
        catch
        {
            _cache = Array.Empty<WeaponCompatibilityEntry>();
        }

        return _cache;
    }

    public IReadOnlyList<WeaponCompatibilityEntry> FindWeapons(IEnumerable<string> itemIds)
    {
        var ids = itemIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(NormalizeWeaponId)
            .Where(id => id.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Load().Where(weapon => ids.Contains(NormalizeWeaponId(weapon.Id))).ToList();
    }

    public bool ContainsWeapon(IEnumerable<string> itemIds) => FindWeapons(itemIds).Count > 0;

    private static string ToSpawnableItemId(string? technicalId)
    {
        var value = (technicalId ?? string.Empty).Trim();
        if (value.StartsWith("BPC_", StringComparison.OrdinalIgnoreCase)) return value[4..];
        if (value.StartsWith("BP_", StringComparison.OrdinalIgnoreCase)) return value[3..];
        return value;
    }

    private static string NormalizeWeaponId(string? itemId)
    {
        var value = (itemId ?? string.Empty).Trim();
        if (value.StartsWith("BPC_", StringComparison.OrdinalIgnoreCase)) value = value[4..];
        else if (value.StartsWith("BP_", StringComparison.OrdinalIgnoreCase)) value = value[3..];

        if (value.StartsWith("Weapon_", StringComparison.OrdinalIgnoreCase)) value = value[7..];
        foreach (var suffix in new[] { "_Black", "_Green", "_Rail", "_V2" })
        {
            if (value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                value = value[..^suffix.Length];
                break;
            }
        }

        return new string(value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
    }

    private static string? FindCatalogPath()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Data", "weapon_compatibility.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "Data", "weapon_compatibility.json")
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static string? ReadCatalogJson()
    {
        var path = FindCatalogPath();
        if (path is not null) return File.ReadAllText(path);

        using var stream = typeof(WeaponCompatibilityCatalog).Assembly
            .GetManifestResourceStream("ScumRconTool.Data.weapon_compatibility.json");
        if (stream is null) return null;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private sealed class WeaponCompatibilitySource
    {
        [JsonPropertyName("options")]
        public List<WeaponCompatibilityOptionSource> Options { get; set; } = new();

        [JsonPropertyName("weapons")]
        public List<WeaponCompatibilityWeaponSource> Weapons { get; set; } = new();
    }

    private sealed class WeaponCompatibilityOptionSource
    {
        [JsonPropertyName("g")]
        public string Group { get; set; } = string.Empty;

        [JsonPropertyName("r")]
        public string RequiredRailId { get; set; } = string.Empty;

        [JsonPropertyName("n")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;
    }

    private sealed class WeaponCompatibilityWeaponSource
    {
        [JsonPropertyName("n")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("c")]
        public string Caliber { get; set; } = string.Empty;

        [JsonPropertyName("mn")]
        public string MagazineName { get; set; } = string.Empty;

        [JsonPropertyName("m")]
        public string MagazineId { get; set; } = string.Empty;

        [JsonPropertyName("a")]
        public string AmmoBoxId { get; set; } = string.Empty;

        [JsonPropertyName("o")]
        public List<int> OptionIndexes { get; set; } = new();
    }
}

public sealed record WeaponCompatibilityEntry(
    string Name,
    string Id,
    string Caliber,
    string MagazineName,
    string MagazineId,
    string AmmoBoxId,
    IReadOnlyList<WeaponAccessoryEntry> Options);

public sealed record WeaponAccessoryEntry(string Group, string RequiredRailId, string Name, string Id);
