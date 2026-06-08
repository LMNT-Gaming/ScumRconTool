using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text.Json;
using ScumRconTool.Models;

namespace ScumRconTool.Services;

public static class EventDefinitionStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static string DataDirectory => Path.Combine(AppContext.BaseDirectory, "Data");
    public static string ScriptDirectory => Path.Combine(DataDirectory, "Scripts");
    public static string LegacyFilePath => Path.Combine(DataDirectory, "scripts.json");
    public static string OlderLegacyFilePath => Path.Combine(DataDirectory, "events.json");

    // Backward-compatible alias used by older UI labels. New storage is Data/Scripts/*.json.
    public static string FilePath => LegacyFilePath;

    public static List<EventDefinition> Load()
    {
        EnsureDefaultFile();
        var files = GetScriptFiles();
        var result = new List<EventDefinition>();

        foreach (var file in files)
        {
            try
            {
                var json = File.ReadAllText(file);
                var definition = DeserializeSingle(json);
                definition.SourceFilePath = file;
                result.Add(definition);
            }
            catch
            {
                // Invalid files should not crash startup. They can be opened/fixed in the editor.
            }
        }

        return result.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static IReadOnlyList<string> GetScriptFiles()
    {
        Directory.CreateDirectory(ScriptDirectory);
        return Directory.GetFiles(ScriptDirectory, "*.json", SearchOption.TopDirectoryOnly)
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static void Save(List<EventDefinition> scripts)
    {
        Directory.CreateDirectory(ScriptDirectory);
        foreach (var script in scripts)
        {
            Save(script);
        }
    }

    public static string Save(EventDefinition script)
    {
        Directory.CreateDirectory(ScriptDirectory);
        var path = string.IsNullOrWhiteSpace(script.SourceFilePath)
            ? Path.Combine(ScriptDirectory, SanitizeFileName(string.IsNullOrWhiteSpace(script.Id) ? script.Name : script.Id) + ".json")
            : script.SourceFilePath!;

        script.SourceFilePath = path;
        File.WriteAllText(path, JsonSerializer.Serialize(script, Options));
        return path;
    }

    public static EventDefinition SaveRawJson(string json, string? existingPath)
    {
        var definition = DeserializeSingle(json);
        if (string.IsNullOrWhiteSpace(definition.Id))
        {
            definition.Id = SanitizeFileName(definition.Name);
        }

        definition.SourceFilePath = string.IsNullOrWhiteSpace(existingPath)
            ? Path.Combine(ScriptDirectory, SanitizeFileName(definition.Id) + ".json")
            : existingPath;

        Save(definition);
        return definition;
    }

    public static string FormatRawJson(string json)
    {
        var definition = DeserializeSingle(json);
        return JsonSerializer.Serialize(definition, Options);
    }

    public static EventDefinition DeserializeSingle(string json)
    {
        var trimmed = json.TrimStart();
        if (trimmed.StartsWith("["))
        {
            var list = JsonSerializer.Deserialize<List<EventDefinition>>(json, Options) ?? new List<EventDefinition>();
            if (list.Count != 1)
            {
                throw new InvalidOperationException("Dieser Editor speichert pro Datei genau ein Script. Bitte nur ein JSON-Objekt pro Datei verwenden.");
            }
            return list[0];
        }

        return JsonSerializer.Deserialize<EventDefinition>(json, Options)
               ?? throw new InvalidOperationException("Script konnte nicht gelesen werden.");
    }

    public static string GetRawJsonFor(EventDefinition definition)
    {
        if (!string.IsNullOrWhiteSpace(definition.SourceFilePath) && File.Exists(definition.SourceFilePath))
        {
            return File.ReadAllText(definition.SourceFilePath);
        }
        return JsonSerializer.Serialize(definition, Options);
    }

    public static void Delete(EventDefinition definition)
    {
        if (!string.IsNullOrWhiteSpace(definition.SourceFilePath) && File.Exists(definition.SourceFilePath))
        {
            File.Delete(definition.SourceFilePath);
        }
    }

    public static EventDefinition CreateTemplate()
    {
        var id = "new_script_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
        return new EventDefinition
        {
            Id = id,
            Name = "Neues Script",
            Enabled = false,
            Mode = "SilentZone",
            IncludeInRandomizer = false,
            RandomizerEveryMinutes = 360,
            InitiatorRepeatEveryMinutes = 0,
            MaxConcurrentRandomEvents = 1,
            ActivationZone = new EventZone
            {
                Name = "Aktivierzone",
                CenterX = 0,
                CenterY = 0,
                CenterZ = 0,
                Radius = 50000
            },
            InitiatorBlock = new ScriptBlock
            {
                Name = "InitiatorBlock",
                Enabled = true,
                Commands = new List<EventCommand>()
            },
            LiveBlock = new ScriptBlock
            {
                Name = "LiveBlock",
                Enabled = true,
                Commands = new List<EventCommand>
                {
                    new() { Name = "Eventzone started", Command = "#Broadcast Red Eventzone started", DelayMs = 50 }
                }
            },
            EmptyBlock = new ScriptBlock
            {
                Name = "EmptyBlock",
                Enabled = true,
                Commands = new List<EventCommand>()
            },
            CleanupWhenEmptySeconds = 300,
            CooldownMinutes = 60
        };
    }

    public static void EnsureDefaultFile()
    {
        Directory.CreateDirectory(ScriptDirectory);
        if (Directory.GetFiles(ScriptDirectory, "*.json", SearchOption.TopDirectoryOnly).Length > 0)
        {
            return;
        }

        if (File.Exists(LegacyFilePath))
        {
            MigrateLegacyArrayFile(LegacyFilePath);
            return;
        }

        if (File.Exists(OlderLegacyFilePath))
        {
            MigrateLegacyArrayFile(OlderLegacyFilePath);
            return;
        }

        Save(BuildRandomC4BunkerScript());
        Save(BuildRandomA3CarCrashScript());
    }

    private static void MigrateLegacyArrayFile(string path)
    {
        var json = File.ReadAllText(path);
        var list = JsonSerializer.Deserialize<List<EventDefinition>>(json, Options) ?? new List<EventDefinition>();
        foreach (var script in list)
        {
            script.SourceFilePath = Path.Combine(ScriptDirectory, SanitizeFileName(string.IsNullOrWhiteSpace(script.Id) ? script.Name : script.Id) + ".json");
            Save(script);
        }

        try
        {
            var backup = path + ".backup";
            if (!File.Exists(backup)) File.Copy(path, backup);
        }
        catch
        {
            // Backup is best effort.
        }
    }

    private static string SanitizeFileName(string value)
    {
        var cleaned = Regex.Replace(value.Trim().ToLowerInvariant(), "[^a-z0-9_\\-]+", "_");
        cleaned = Regex.Replace(cleaned, "_+", "_").Trim('_');
        return string.IsNullOrWhiteSpace(cleaned) ? "script" : cleaned;
    }

    private static EventDefinition BuildRandomC4BunkerScript() => new()
    {
        Id = "random_c4_wwii_bunker_npc_activity",
        Name = "Random - C4 WWII Bunker NPC Activity",
        Enabled = true,
        Mode = "RandomAnnouncedZone",
        IncludeInRandomizer = true,
        RandomizerEveryMinutes = 360,
        InitiatorRepeatEveryMinutes = 30,
        AnnouncementType = "Yellow",
        Announcement = "Npc activity in an C4 WWII Bunker",
        ActivationZone = new EventZone
        {
            Name = "C4 WWII Bunker Aktivierzone",
            CenterX = 577863.403,
            CenterY = 114773.696,
            CenterZ = 5944.446,
            Radius = 50000
        },
        InitiatorBlock = new ScriptBlock
        {
            Name = "InitiatorBlock - repeat announcement every 30 min",
            Commands = new List<EventCommand>
            {
                new() { Name = "C4 WWII Bunker Announcement", Command = "#Broadcast Yellow Npc activity in an C4 WWII Bunker", DelayMs = 50 }
            }
        },
        LiveBlock = new ScriptBlock
        {
            Name = "LiveBlock - C4 WWII Bunker guards and loot",
            Commands = new List<EventCommand>
            {
                new() { Name = "Eventzone started", Command = "#Broadcast Red Eventzone started", DelayMs = 50 },
                new() { Name = "Guard 1 - C4 bunker", Command = "#ExecAs {playerId} #SpawnArmedNPC BP_Guard_Lvl_5 1 Location \"[{X=577619.688 Y=115714.336 Z=5962.507|P=326.101379 Y=35.249073 R=0.000000}]\" DespawnLifetime 600", DelayMs = 800 },
                new() { Name = "Guard 2 - C4 bunker", Command = "#ExecAs {playerId} #SpawnArmedNPC BP_Guard_Lvl_5 1 Location \"[{X=577944.062 Y=115230.352 Z=5972.030|P=327.367035 Y=199.606567 R=0.000000}]\" DespawnLifetime 600", DelayMs = 800 },
                new() { Name = "Guard 3 - C4 bunker", Command = "#ExecAs {playerId} #SpawnArmedNPC BP_Guard_Lvl_5 1 Location \"[{X=577946.062 Y=115013.141 Z=5962.507|P=330.374634 Y=170.639771 R=0.000000}]\" DespawnLifetime 600", DelayMs = 800 },
                new() { Name = "Guard 4 - C4 bunker", Command = "#ExecAs {playerId} #SpawnArmedNPC BP_Guard_Lvl_5 1 Location \"[{X=577853.938 Y=113946.719 Z=5961.788|P=347.187531 Y=350.455017 R=0.000000}]\" DespawnLifetime 600", DelayMs = 800 },
                new() { Name = "Guard 5 - C4 bunker", Command = "#ExecAs {playerId} #SpawnArmedNPC BP_Guard_Lvl_5 1 Location \"[{X=578140.938 Y=114679.562 Z=5962.442|P=342.122498 Y=337.000732 R=0.000000}]\" DespawnLifetime 600", DelayMs = 800 },
                new() { Name = "Guard 6 - C4 bunker", Command = "#ExecAs {playerId} #SpawnArmedNPC BP_Guard_Lvl_5 1 Location \"[{X=577594.312 Y=114046.977 Z=5961.863|P=345.921082 Y=117.455917 R=0.000000}]\" DespawnLifetime 600", DelayMs = 800 },
                new() { Name = "Guard 7 - C4 bunker", Command = "#ExecAs {playerId} #SpawnArmedNPC BP_Guard_Lvl_5 1 Location \"[{X=577741.188 Y=114209.156 Z=5961.863|P=344.338135 Y=136.767029 R=0.000000}]\" DespawnLifetime 600", DelayMs = 800 },
                new() { Name = "Loot - Weapon SKS", Command = "#SpawnItem Weapon_SKS 1 Location \"[{X=577961.125 Y=115021.242 Z=5877.507|P=296.218018 Y=230.051529 R=0.000000}]\"", DelayMs = 50 },
                new() { Name = "Loot - SKS Clips", Command = "#SpawnItem Magazine_Clip_SKS 2 Location \"[{X=577969.312 Y=115101.781 Z=5877.507|P=316.004181 Y=302.388977 R=0.000000}]\"", DelayMs = 50 },
                new() { Name = "Loot - 7.62x39 Ammo Box", Command = "#SpawnItem Cal_7_62x39mm_Ammobox 2 Location \"[{X=577969.312 Y=115101.781 Z=5877.507|P=316.004181 Y=302.388977 R=0.000000}]\"", DelayMs = 50 },
                new() { Name = "Loot - Copper Coins", Command = "#SpawnItem Copper_Coins 1 Location \"[{X=577969.312 Y=115101.781 Z=5877.507|P=316.004181 Y=302.388977 R=0.000000}]\"", DelayMs = 50 }
            }
        },
        EmptyBlock = new ScriptBlock
        {
            Name = "EmptyBlock - C4 WWII Bunker cooldown",
            Commands = new List<EventCommand>
            {
                new() { Name = "C4 Bunker zone empty", Command = "#Broadcast Yellow C4 WWII Bunker eventzone is empty. Event cooldown started.", DelayMs = 50 }
            }
        },
        CleanupWhenEmptySeconds = 300,
        CooldownMinutes = 60
    };

    private static EventDefinition BuildRandomA3CarCrashScript() => new()
    {
        Id = "random_a3_npc_car_crash",
        Name = "Random - A3 NPC Car Crash",
        Enabled = true,
        Mode = "RandomAnnouncedZone",
        IncludeInRandomizer = true,
        RandomizerEveryMinutes = 360,
        InitiatorRepeatEveryMinutes = 30,
        AnnouncementType = "Yellow",
        Announcement = "Npc car crash in A3",
        ActivationZone = new EventZone
        {
            Name = "A3 Car Crash Aktivierzone",
            CenterX = 198138.895,
            CenterY = -348930.054,
            CenterZ = 14224.784,
            Radius = 50000
        },
        InitiatorBlock = new ScriptBlock
        {
            Name = "InitiatorBlock - repeat announcement every 30 min",
            Commands = new List<EventCommand>
            {
                new() { Name = "A3 Car Crash Announcement", Command = "#Broadcast Yellow Npc car crash in A3", DelayMs = 50 }
            }
        },
        LiveBlock = new ScriptBlock
        {
            Name = "LiveBlock - A3 car crash, guards and loot",
            Commands = new List<EventCommand>
            {
                new() { Name = "Eventzone started", Command = "#Broadcast Red Eventzone started", DelayMs = 50 },
                new() { Name = "Rager crash prop", Command = "#ExecAs {playerId} #SpawnVehicle BPC_Rager 1 Location \"[{X=197911.906 Y=-349413.969 Z=15509.562|P=90 Y=0 R=90}]\" Modifier minimalfunctional", DelayMs = 1000 },
                new() { Name = "Guard 1 - A3 crash", Command = "#ExecAs {playerId} #SpawnArmedNPC BP_Guard_Lvl_5 1 Location \"[{X=197837.422 Y=-348882.719 Z=14035.099|P=332.304779 Y=327.868164 R=0.000000}]\" DespawnLifetime 600", DelayMs = 800 },
                new() { Name = "Guard 2 - A3 crash", Command = "#ExecAs {playerId} #SpawnArmedNPC BP_Guard_Lvl_5 1 Location \"[{X=198232.266 Y=-349414.219 Z=13987.835|P=341.485626 Y=0.000824 R=0.000000}]\" DespawnLifetime 600", DelayMs = 800 },
                new() { Name = "Guard 3 - A3 crash", Command = "#ExecAs {playerId} #SpawnArmedNPC BP_Guard_Lvl_5 1 Location \"[{X=198997.328 Y=-348874.531 Z=13955.687|P=339.586090 Y=204.508347 R=0.000000}]\" DespawnLifetime 600", DelayMs = 800 },
                new() { Name = "Guard 4 - A3 crash", Command = "#ExecAs {playerId} #SpawnArmedNPC BP_Guard_Lvl_5 1 Location \"[{X=198096.406 Y=-348402.094 Z=14031.377|P=337.369904 Y=297.423126 R=0.000000}]\" DespawnLifetime 600", DelayMs = 800 },
                new() { Name = "Loot A3 spot 1 - Weapon SKS", Command = "#SpawnItem Weapon_SKS 1 Location \"[{X=198002.219 Y=-348777.250 Z=14019.290|P=329.455322 Y=236.482391 R=0.000000}]\"", DelayMs = 50 },
                new() { Name = "Loot A3 spot 2 - SKS Clips", Command = "#SpawnItem Magazine_Clip_SKS 2 Location \"[{X=197894.719 Y=-348745.594 Z=14034.636|P=338.161041 Y=250.728409 R=0.000000}]\"", DelayMs = 50 },
                new() { Name = "Loot A3 spot 2 - 7.62x39 Ammo Box", Command = "#SpawnItem Cal_7_62x39mm_Ammobox 2 Location \"[{X=197894.719 Y=-348745.594 Z=14034.636|P=338.161041 Y=250.728409 R=0.000000}]\"", DelayMs = 50 },
                new() { Name = "Loot A3 spot 2 - Copper Coins", Command = "#SpawnItem Copper_Coins 1 Location \"[{X=197894.719 Y=-348745.594 Z=14034.636|P=338.161041 Y=250.728409 R=0.000000}]\"", DelayMs = 50 }
            }
        },
        EmptyBlock = new ScriptBlock
        {
            Name = "EmptyBlock - A3 Car Crash cooldown",
            Commands = new List<EventCommand>
            {
                new() { Name = "A3 crash zone empty", Command = "#Broadcast Yellow A3 car crash eventzone is empty. Event cooldown started.", DelayMs = 50 }
            }
        },
        CleanupWhenEmptySeconds = 300,
        CooldownMinutes = 60
    };
}
