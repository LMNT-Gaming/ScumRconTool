using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ScumRconTool.Models;

namespace ScumRconTool.Services;

public static class LootPackStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static string FilePath => Path.Combine(EventDefinitionStore.DataDirectory, "lootpacks.json");

    public static List<LootPack> Load()
    {
        Directory.CreateDirectory(EventDefinitionStore.DataDirectory);
        if (!File.Exists(FilePath))
        {
            return new List<LootPack>();
        }

        try
        {
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<List<LootPack>>(json, Options)?
                .Where(pack => pack is not null)
                .Select(Normalize)
                .ToList() ?? new List<LootPack>();
        }
        catch
        {
            return new List<LootPack>();
        }
    }

    public static void Save(IEnumerable<LootPack> packs)
    {
        Directory.CreateDirectory(EventDefinitionStore.DataDirectory);
        var clean = packs
            .Where(pack => pack is not null)
            .Select(Normalize)
            .Where(pack => !string.IsNullOrWhiteSpace(pack.Name))
            .ToList();
        File.WriteAllText(FilePath, JsonSerializer.Serialize(clean, Options));
    }

    public static bool MergeMissing(ICollection<LootPack> target, IEnumerable<LootPack> source)
    {
        var changed = false;
        foreach (var pack in source)
        {
            if (string.IsNullOrWhiteSpace(pack.Name))
            {
                continue;
            }

            if (target.Any(existing => string.Equals(existing.Name, pack.Name, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            target.Add(Normalize(pack));
            changed = true;
        }

        return changed;
    }

    private static LootPack Normalize(LootPack pack)
    {
        return new LootPack
        {
            Name = string.IsNullOrWhiteSpace(pack.Name) ? "LootPack" : pack.Name.Trim(),
            Enabled = pack.Enabled,
            Weight = Math.Max(1, pack.Weight),
            Location = null,
            Items = (pack.Items ?? new List<LootItem>())
                .Where(item => !string.IsNullOrWhiteSpace(item.Item))
                .Select(item => new LootItem
                {
                    Item = item.Item.Trim(),
                    Quantity = Math.Max(1, item.Quantity),
                    DelayMs = Math.Max(0, item.DelayMs)
                })
                .ToList()
        };
    }
}
