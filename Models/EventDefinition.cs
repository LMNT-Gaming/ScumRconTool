using System.Collections.Generic;
using System.Text.Json.Serialization;

using System.IO;
namespace ScumRconTool.Models;

public sealed class EventDefinition
{
    public string Id { get; set; } = "script";
    public string Name { get; set; } = "Script";
    public bool Enabled { get; set; } = true;

    // RandomAnnouncedZone/Random: Randomizer initiiert Script, dann wartet es auf Spieler in Zone.
    // SilentZone: Script ist dauerhaft scharf, wartet still auf Spieler in Zone.
    // Buyzone: Script kann per /buyevent gekauft werden und geht direkt live.
    // RandomActivated: prueft zufaellig belegte Zonen und startet mit Chance direkt.
    // DirectLive: Script wird bei Initiierung direkt live geschaltet.
    public string Mode { get; set; } = "RandomAnnouncedZone";
    public bool IncludeInRandomizer { get; set; } = true;
    public int RandomizerEveryMinutes { get; set; } = 360;

    // Wenn ein RandomAnnouncedZone-Script initiiert ist und noch nicht live wurde,
    // kann der InitiatorBlock in diesem Abstand erneut laufen. 0 = keine Wiederholung.
    public int InitiatorRepeatEveryMinutes { get; set; } = 0;

    // Global wirkendes Limit fuer RandomAnnouncedZone-Scripts.
    // 1 = immer nur ein Random-Event gleichzeitig in Initiated/Live/CleanupPending.
    // 0 = kein Limit. Das kleinste positive Limit aller aktivierten Random-Scripts gewinnt.
    public int MaxConcurrentRandomEvents { get; set; } = 1;

    // RandomActivated: Chance pro Faelligkeit, wenn aktuell mindestens ein Spieler
    // in der Aktivierzone steht. 100 = immer aktivieren.
    public int RandomActivationChancePercent { get; set; } = 25;

    // Optional: Scripts mit derselben Gruppe blockieren sich gegenseitig.
    // Beispiel: Sector-Z-Unterzonen koennen alle eventGroup="sector_z" nutzen,
    // damit nicht mehrere ueberlappende Zonen gleichzeitig live gehen.
    public string EventGroup { get; set; } = "";
    public int MaxConcurrentInGroup { get; set; } = 0;

    // Buyzone: Preis und optionaler Alias fuer /buyevent <name>.
    // BuyAlias leer = Name oder Id werden als Kaufname genutzt.
    public int BuyPrice { get; set; } = 0;
    public string BuyAlias { get; set; } = "";

    // Optionaler Trigger-Timer zwischen Aktivierung und Liveblock/Spawns/Loot.
    // Wird in Millisekunden gespeichert, damit kurze Event-Takte moeglich sind.
    public int ActivationDelayMs { get; set; } = 0;

    // Nachricht beim eigentlichen Trigger/Live-Start, getrennt von der Initiierung.
    public string TriggerServerMessageType { get; set; } = "Yellow";
    public string TriggerServerMessage { get; set; } = "";

    // Loot-Ausfuehrung im Script:
    // OneTotal/Single = genau ein Pack insgesamt.
    // OnePerLocation = je Lootpunkt genau ein Pack.
    public string LootPackSpawnMode { get; set; } = "OneTotal";

    // Namen aus der globalen Lootpack-Bibliothek Data/lootpacks.json.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? LootPackNames { get; set; }

    // Backward-compatible Felder aus Phase 2.
    public int AnnounceEveryMinutes { get; set; } = 360;
    public string AnnouncementType { get; set; } = "Yellow";
    public string Announcement { get; set; } = "Event wurde gesichtet.";

    public ScriptLocalVariables LocalVariables { get; set; } = new();

    public EventZone? ActivationZone { get; set; }
    public EventZone Zone { get; set; } = new();

    public ScriptBlock InitiatorBlock { get; set; } = new();

    // Laeuft direkt vor dem LiveBlock, z. B. um alte Lootreste an derselben Location zu entfernen.
    public ScriptBlock PreLiveCleanupBlock { get; set; } = new() { Name = "PreLiveCleanupBlock" };

    public ScriptBlock LiveBlock { get; set; } = new();

    // Vereinfachte Spawn-Bausteine fuer den Editor. Sie werden beim Live-Start
    // nach dem LiveBlock ausgefuehrt und koennen optional wiederholt laufen.
    public List<SpawnBlock> SpawnBlocks { get; set; } = new();

    // Legacy: frueher waren Lootpacks direkt im Script gespeichert.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<LootPack>? LootPacks { get; set; }

    // Legacy: frueher konnten komplette Loot-Commands im Script liegen.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<LootCommandPack>? LootCommandPacks { get; set; }

    public ScriptBlock EmptyBlock { get; set; } = new();

    // Optional: Laeuft nach EmptyBlock, wenn die Zone leer ist, z. B. Cleanup alter Items.
    public ScriptBlock CleanupBlock { get; set; } = new() { Name = "CleanupBlock" };

    // Backward-compatible Felder aus Phase 2. Werden weiterhin geladen.
    public List<EventCommand> OnEnterCommands { get; set; } = new();
    public List<EventCommand> OnEmptyCommands { get; set; } = new();

    public int CleanupWhenEmptySeconds { get; set; } = 300;
    public int CooldownMinutes { get; set; } = 60;

    [JsonIgnore]
    public string? SourceFilePath { get; set; }

    [JsonIgnore]
    public EventZone EffectiveZone => ActivationZone ?? Zone;

    public List<EventCommand> GetInitiatorCommands()
    {
        if (InitiatorBlock.Commands.Count > 0) return InitiatorBlock.Commands;
        if (!string.IsNullOrWhiteSpace(Announcement))
        {
            return new List<EventCommand>
            {
                new()
                {
                    Name = "Announcement",
                    Enabled = true,
                    Command = $"#Broadcast {AnnouncementType} {Announcement}",
                    Repeat = 1,
                    DelayMs = 50
                }
            };
        }
        return new List<EventCommand>();
    }

    public List<EventCommand> GetLiveCommands()
    {
        if (LiveBlock.Commands.Count > 0) return LiveBlock.Commands;
        return OnEnterCommands;
    }

    public List<EventCommand> GetEmptyCommands()
    {
        if (EmptyBlock.Commands.Count > 0) return EmptyBlock.Commands;
        return OnEmptyCommands;
    }
}

public sealed class ScriptBlock
{
    public string Name { get; set; } = "Block";
    public bool Enabled { get; set; } = true;
    public List<EventCommand> Commands { get; set; } = new();
}

public sealed class EventZone
{
    public string Name { get; set; } = "Zone";
    public double CenterX { get; set; }
    public double CenterY { get; set; }
    public double CenterZ { get; set; }
    public double Radius { get; set; } = 75000;

    public WorldPosition Center => new() { X = CenterX, Y = CenterY, Z = CenterZ };
}

public sealed class ScriptLocalVariables
{
    public string InitiatorMessage { get; set; } = "";
    public List<ScriptLocationVariable> LootSpawnLocations { get; set; } = new();
    public List<ScriptLocationVariable> NpcSpawnLocations { get; set; } = new();
}

public sealed class ScriptLocationVariable
{
    public string Name { get; set; } = "position";
    public string Location { get; set; } = "";
}

public sealed class EventCommand
{
    public string Name { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public string Command { get; set; } = "";
    public int Repeat { get; set; } = 1;
    public int DelayMs { get; set; } = 50;
}

public sealed class SpawnBlock
{
    public string Name { get; set; } = "Spawn";
    public bool Enabled { get; set; } = true;

    // Zombie, ArmedNPC, Vehicle, Item, Custom oder CargoDrop.
    public string Type { get; set; } = "ArmedNPC";
    public string Asset { get; set; } = "BP_Guard_Lvl_1";
    public int Quantity { get; set; } = 1;
    public string Location { get; set; } = "";

    // Freier Zusatz, z. B. "Modifier minimalfunctional".
    public string Extra { get; set; } = "";
    public int DespawnLifetimeSeconds { get; set; } = 0;

    // StartDelaySeconds verzoegert den ersten Spawn. RepeatEverySeconds ist
    // der Abstand zwischen Wiederholungen, falls Repeat > 1.
    public int StartDelaySeconds { get; set; } = 0;
    public int StartDelayMs { get; set; } = 0;
    public int Repeat { get; set; } = 1;
    public int RepeatEverySeconds { get; set; } = 0;
    public int RepeatEveryMs { get; set; } = 0;
    public int DelayMs { get; set; } = 250;
    public bool UseTriggerPlayer { get; set; } = true;
}

public sealed class LootPack
{
    public string Name { get; set; } = "LootPack";
    public bool Enabled { get; set; } = true;

    // Gewicht fuer die Zufallsauswahl. 1 = normal, 2 = doppelt so wahrscheinlich.
    public int Weight { get; set; } = 1;

    // Legacy-Kompatibilitaet: neue Lootpacks sind nur Item-Sammlungen.
    // Spawnorte liegen in LocalVariables.LootSpawnLocations.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Location { get; set; }

    public List<LootItem> Items { get; set; } = new();
}

public sealed class LootCommandPack
{
    public string Name { get; set; } = "LootCommandPack";
    public bool Enabled { get; set; } = true;

    // Gewicht fuer die Zufallsauswahl. 1 = normal, 2 = doppelt so wahrscheinlich.
    public int Weight { get; set; } = 1;

    // Optionaler kompletter SCUM Location-String, falls der Command keine eigene Location enthaelt.
    public string Location { get; set; } = "";

    // Kompletter Command, z. B.:
    // #SpawnInventoryFullOf Improved_Wooden_Chest 1 Weapon_SKS 1 ... Location "[{X=...}]"
    public string Command { get; set; } = "";

    public int DelayMs { get; set; } = 50;
}

public sealed class LootItem
{
    public string Item { get; set; } = "";
    public int Quantity { get; set; } = 1;
    public int DelayMs { get; set; } = 50;
}

public enum EventRuntimeState
{
    Stopped,
    Initiated,
    Live,
    CleanupPending,
    Cooldown
}
