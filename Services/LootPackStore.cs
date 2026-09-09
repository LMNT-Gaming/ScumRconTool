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
            Category = NormalizeCategory(pack.Category, pack.Name, pack.Items),
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

    private static string NormalizeCategory(string? category, string? packName, IEnumerable<LootItem>? items)
    {
        var value = (category ?? string.Empty).Trim();
        if (value is "Weapons" or "Ammunition" or "Equipment" or "Consumables" or "Mixed") return value;

        var ids = (items ?? Array.Empty<LootItem>())
            .Select(item => (item.Item ?? string.Empty).Trim())
            .Where(id => id.Length > 0)
            .ToList();
        var name = (packName ?? string.Empty).Trim();

        try
        {
            if (new WeaponCompatibilityCatalog().ContainsWeapon(ids) || ids.Any(IsWeaponId)) return "Weapons";
        }
        catch
        {
            // Category inference is best effort; old packs remain usable.
        }

        if (ids.Count > 0 && ids.All(IsAmmunitionId)) return "Ammunition";
        if (ContainsAny(name, "Medic", "Medical", "Food", "Drink", "Verpflegung")) return "Consumables";
        if (ContainsAny(name, "Armor", "Armour", "Ghillie", "Lockpick", "Tool", "Kit", "Werkzeug")) return "Equipment";

        return "Mixed";
    }

    private static bool IsWeaponId(string id)
    {
        if (!(id.StartsWith("Weapon_", StringComparison.OrdinalIgnoreCase)
              || id.StartsWith("BP_Weapon_", StringComparison.OrdinalIgnoreCase)
              || id.StartsWith("BPC_Weapon_", StringComparison.OrdinalIgnoreCase))) return false;
        return !ContainsAny(id, "Scope", "Suppressor", "Rail", "Flashlight", "Cleaning_Kit", "Ghillie");
    }

    private static bool IsAmmunitionId(string id) =>
        id.StartsWith("Cal_", StringComparison.OrdinalIgnoreCase)
        || id.StartsWith("Magazine_", StringComparison.OrdinalIgnoreCase)
        || id.Contains("Ammobox", StringComparison.OrdinalIgnoreCase)
        || id.Contains("AmmoBox", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsAny(string value, params string[] needles) =>
        needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));
}
