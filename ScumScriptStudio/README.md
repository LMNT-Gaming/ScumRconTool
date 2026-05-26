# Scum Script Studio

Separates WPF-Editorprojekt fuer SCUM Event-Skripte.

## Start

```powershell
dotnet restore
dotnet build
dotnet run
```

Oder in Visual Studio `ScumScriptStudio.csproj` oeffnen.

## Funktionen

- Script-Ordner laden
- JSON-Skripte links durchsuchen
- Basisdaten und Aktivierungszone bearbeiten
- Command-Bloecke bearbeiten
- Raw JSON direkt bearbeiten
- Validation/Warnings
- `zone` -> `activationZone` konvertieren
- `lootPacks` in Kisten-Commands via `#SpawnInventoryFullOf Improved_Wooden_Chest` umwandeln



## Simple Mode

Der Simple Mode ist fuer schnelles Erstellen gedacht:
- Event-ID/Name setzen
- NPC-Spawns als Location-Zeilen eintragen
- Loot-Spots als Location-Zeilen eintragen
- Lootpacks mit Items pflegen
- "Event automatisch bauen" erzeugt:
  - activationZone aus NPC- und Loot-Locations
  - preLiveCleanupBlock aus Loot-Spots
  - cleanupBlock aus Loot-Spots
  - lootCommandPacks als Kisten pro Loot-Spot
  - liveBlock mit NPC-Spawns


## Spawn-Typ im Simple Mode

NPC-Spawns haben jetzt eine Spalte `Spawn`:
- `ArmedNPC` erzeugt `#SpawnArmedNPC <NpcType> <Count> Location "..."`
- `Puppet` erzeugt `#SpawnRandomZombie <Count> Location "..." DespawnLifetime 600`

Bei `Puppet` wird das Feld `NPC Type` ignoriert.


Puppet/Zombie-Spawns nutzen jetzt ebenfalls `DespawnLifetime 600`.


## Lootpacks ohne Kistenmodus

Der Kistenmodus wurde komplett entfernt, weil `#SpawnInventoryFullOf` keine zuverlässige Location-Unterstützung hat.

ScriptStudio erzeugt jetzt wieder normale `lootPacks`:

```json
"lootPacks": [
  {
    "name": "Loot Spot 1 - AKM Pack",
    "enabled": true,
    "weight": 40,
    "location": "[{X=... Y=... Z=...|P=... Y=... R=...}]",
    "items": [
      { "item": "Weapon_AKM", "quantity": 1, "delayMs": 250 }
    ]
  }
]
```

Simple Mode erzeugt automatisch:
- `activationZone`
- `preLiveCleanupBlock`
- `cleanupBlock`
- `liveBlock`
- normale `lootPacks`

Alte `lootCommandPacks` können beim Laden noch gelesen und in normale Lootpacks zurückgewandelt werden.
