using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ScumRconTool.Models;

public sealed class EventDefinition
{
    public string Id { get; set; } = "script";
    public string Name { get; set; } = "Script";
    public bool Enabled { get; set; } = true;

    // RandomAnnouncedZone: Randomizer initiiert Script, dann wartet es auf Spieler in Zone.
    // SilentZone: Script ist dauerhaft scharf, wartet still auf Spieler in Zone.
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

    // Backward-compatible Felder aus Phase 2.
    public int AnnounceEveryMinutes { get; set; } = 360;
    public string AnnouncementType { get; set; } = "Yellow";
    public string Announcement { get; set; } = "Event wurde gesichtet.";

    public EventZone? ActivationZone { get; set; }
    public EventZone Zone { get; set; } = new();

    public ScriptBlock InitiatorBlock { get; set; } = new();

    // Laeuft direkt vor dem LiveBlock, z. B. um alte Lootreste an derselben Location zu entfernen.
    public ScriptBlock PreLiveCleanupBlock { get; set; } = new() { Name = "PreLiveCleanupBlock" };

    public ScriptBlock LiveBlock { get; set; } = new();

    // Optional: pro Live-Start wird genau ein LootPack zufaellig gewaehlt und als einzelne #SpawnItem-Commands gespawnt.
    public List<LootPack> LootPacks { get; set; } = new();

    // Optional: pro Live-Start wird genau ein kompletter Loot-Command zufaellig gewaehlt.
    // Gedacht fuer Befehle wie #SpawnInventoryFullOf ..., bei denen der Inhalt komplett im Command steckt.
    public List<LootCommandPack> LootCommandPacks { get; set; } = new();

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
                    DelayMs = 250
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

public sealed class EventCommand
{
    public string Name { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public string Command { get; set; } = "";
    public int Repeat { get; set; } = 1;
    public int DelayMs { get; set; } = 250;
}

public sealed class LootPack
{
    public string Name { get; set; } = "LootPack";
    public bool Enabled { get; set; } = true;

    // Gewicht fuer die Zufallsauswahl. 1 = normal, 2 = doppelt so wahrscheinlich.
    public int Weight { get; set; } = 1;

    // SCUM Location-String ohne extra Quotes, z. B.:
    // [{X=577961.125 Y=115021.242 Z=5877.507|P=296.218018 Y=230.051529 R=0.000000}]
    public string Location { get; set; } = "";

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

    public int DelayMs { get; set; } = 250;
}

public sealed class LootItem
{
    public string Item { get; set; } = "";
    public int Quantity { get; set; } = 1;
    public int DelayMs { get; set; } = 250;
}

public enum EventRuntimeState
{
    Stopped,
    Initiated,
    Live,
    CleanupPending,
    Cooldown
}
