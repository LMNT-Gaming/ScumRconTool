# SCUM RCON Tool

.NET 8 WinForms Tool fuer ggCON/RCON und Script-Events.

## Phase 5

Neu:

- Scripts liegen jetzt einzeln unter `Data/Scripts/*.json`.
- Der Tab `Skripte` hat einen saubereren Editor fuer das ausgewaehlte Script.
- Buttons: Neu, Duplizieren, Loeschen, Speichern, Pruefen, Formatieren, Ordner.
- Alte `Data/scripts.json` oder `Data/events.json` Dateien werden automatisch migriert, falls `Data/Scripts` leer ist.
- Pro Datei gilt: genau ein JSON-Objekt = genau ein Script.

## Script-Status

- `Stopped`: nicht aktiv
- `Initiated`: initiiert/angekuendigt, wartet auf Aktivierzone
- `Live`: LiveBlock wurde gestartet
- `CleanupPending`: Zone leer, Cleanup-Timer laeuft
- `Cooldown`: Cooldown aktiv

## Modi

- `RandomAnnouncedZone`: Randomizer initiiert das Script, LiveBlock startet bei Spieler in Aktivierzone.
- `SilentZone`: Script ist dauerhaft scharf, LiveBlock startet bei Spieler in Aktivierzone.
- `DirectLive`: LiveBlock startet direkt nach InitiatorBlock.

## Hinweise

Engine stoppen, bevor Script-Dateien gespeichert, angelegt, dupliziert oder geloescht werden.
NPC/Vehicle-Spawns koennen weiterhin `#ExecAs {playerId}` nutzen. Item-Spawns sollten nicht per `#ExecAs` laufen.


Phase 6 - LootPacks und Cleanup
--------------------------------
- Scripts koennen jetzt `lootPacks` enthalten. Beim Live-Start wird genau ein aktiviertes Pack zufaellig ausgewaehlt.
- Ein LootPack spawnt seine Items ohne `#ExecAs`, damit kein Spieler-Executor fuer Item-Spawns genutzt wird.
- `preLiveCleanupBlock` laeuft direkt vor dem LiveBlock. Damit koennen alte Items am selben Event-Ort geloescht werden, bevor neuer Loot gespawnt wird.
- `cleanupBlock` laeuft nach dem EmptyBlock, wenn die Zone leer war und Cooldown startet.
- Beispiel-Cleanup: `#DestroyAllItemsWithinRadius all 20 Location "[{X=... Y=... Z=...|P=... Y=... R=...}]"`

## Phase 7: Randomizer-Limit

Random-Scripts koennen jetzt ueber `maxConcurrentRandomEvents` begrenzt werden.

Beispiel:

```json
"mode": "RandomAnnouncedZone",
"includeInRandomizer": true,
"maxConcurrentRandomEvents": 1
```

`1` bedeutet: Es darf immer nur ein Random-Script gleichzeitig in `Initiated`, `Live` oder `CleanupPending` sein. Erst wenn dieses Script in `Cooldown` oder `Stopped` geht, darf der Randomizer ein weiteres starten.

`0` bedeutet: kein Limit. Wenn mehrere Random-Scripts unterschiedliche positive Limits haben, verwendet die Engine das kleinste positive Limit als globales Limit.

## Kill-Log SFTP -> Ingame Announce

Neu hinzugefuegt wurde der Tab **Kill Logs**.

Ablauf:
1. SSFTP Daten, Remote-Logordner und lokalen Zielordner eintragen.
2. RCON verbinden.
3. **Kill-Logs laden + neue announce** druecken oder **Auto-Killfeed starten** verwenden.
4. Neue Kills werden per RCON als `#announce Blue ...` in den Ingamechat geschrieben.

Das Announce-Template unterstuetzt diese Platzhalter:
- `{killer}`
- `{victim}`
- `{weapon}`
- `{distance}`
- `{time}`
- `{raw}`

Beim ersten Lauf werden vorhandene Kill-Zeilen nur gemerkt und nicht announced, damit alte Logs nicht in den Chat gespammt werden.

### SCUM Kill-Log Details

- SCUM-Kill-Logs werden als UTF-16 Little Endian gelesen. Wenn eine BOM vorhanden ist, wird sie erkannt; ohne BOM wird bewusst UTF-16 LE als Default verwendet.
- Es wird kein fixer Dateiname wie `kill.log` erwartet. Im Tab **Kill Logs** gibt es das Feld **Dateimuster**.
- Standard-Dateimuster: `kill*.log`
- Mehrere Muster sind moeglich, getrennt mit `;`, `,` oder `|`, z. B. `kill*.log;Kills*.log`.
- Der Remote-Wert bleibt der Ordner, in dem die SCUM-Logdateien liegen. Die Datei-Auswahl erfolgt danach anhand des Dateimusters.


## Kill-Logs per SFTP

Die Kill-Logs werden per SFTP/SSH geladen, nicht per klassischem FTP. Standard-Port ist 22.
Der Remote-Wert ist der Ordner auf dem Server, in dem die SCUM-Logdateien liegen; die Datei-Auswahl erfolgt ueber das Muster, z. B. `kill*.log`.
SCUM-Logs werden beim Auslesen als UTF-16 Little Endian behandelt.

## Discord Bot Status

Im Tab **Discord** kann ein Discord-Bot gestartet werden, der seine Aktivitaet regelmaessig mit der aktuellen Spielerzahl aktualisiert.

Ablauf:

1. RCON verbinden.
2. Discord Bot Token eintragen.
3. Max Spieler auf `20` lassen oder anpassen.
4. Status-Template einstellen, z. B. `SCUM {players}/{max} Spieler online`.
5. **Bot-Status starten** klicken.

Der Bot fragt per RCON `#ListPlayersJson` ab, parst daraus die Spieleranzahl und setzt dann die Discord-Aktivitaet des Bots.

Platzhalter im Status-Template:

- `{players}` aktuelle Spielerzahl
- `{max}` maximale Spielerzahl, Standard `20`
- `{updated}` Uhrzeit der letzten Aktualisierung

Hinweis: Fuer den reinen Bot-Status sind keine besonderen Server-Rechte noetig. Der Bot muss aber mit seinem Token gueltig angemeldet werden koennen.

## Discord Bot Status

Im Tab **Discord** kann ein Discord-Bot gestartet werden, der seine Aktivitaet regelmaessig mit der aktuellen Spielerzahl aktualisiert.

Ablauf:

1. RCON verbinden.
2. Discord Bot Token eintragen.
3. Max Spieler auf `20` lassen oder anpassen.
4. Status-Template einstellen, z. B. `SCUM {players}/{max} Spieler online`.
5. **Bot-Status starten** klicken.

Der Bot fragt per RCON `#ListPlayersJson` ab, parst daraus die Spieleranzahl und setzt dann die Discord-Aktivitaet des Bots.

Platzhalter im Status-Template:

- `{players}` aktuelle Spielerzahl
- `{max}` maximale Spielerzahl, Standard `20`
- `{updated}` Uhrzeit der letzten Aktualisierung

Hinweis: Fuer den reinen Bot-Status sind keine besonderen Server-Rechte noetig. Der Bot muss aber mit seinem Token gueltig angemeldet werden koennen.
