# Red Raven Rcon Tool

Windows-WPF-Verwaltung für SCUM-Server mit RCON, SFTP und Discord. Aktuelle Projektversion: **1.3.0**.

## Funktionen

- Dashboard, RCON-Konsole, Chat-Kommandos und Discord-Bridge mit aktualisierbarem Serverstatus.
- Skriptzone und Event-Verwaltung mit zentralen Lootpacks, Item-Katalog und Lootpack-Auswahl.
- Persönliche und Community-Herausforderungen, mehrere Ziele, Quiz, Belohnungen und Redeem-Codes.
- Wiederverwendbare Herausforderungen und automatische Rotation mit begrenzter Anzahl, Wiederholungspause und Abschlussfrist.
- SettingRandomizer mit gewichteten Werten, zeitlicher Planung, SFTP-Konfiguration und Discord-Ankündigungen.
- Wirtschaft, Fahrzeugversicherung und zugehörige Web-Schnittstellen.

Die frühere Kill-Feed-Funktion wurde entfernt.

## Build und Start

Voraussetzungen: Windows, ein .NET SDK mit Windows-Desktop-Unterstützung und zur Ausführung die .NET-8-Windows-Desktop-Runtime.

```powershell
dotnet restore ScumRconTool.Wpf.csproj
dotnet build ScumRconTool.Wpf.csproj -c Release --no-restore
dotnet run --project ScumRconTool.Wpf.csproj
```

Die Anwendung wird unter `bin/Release/net8.0-windows/` erstellt. Der mitgelieferte Item-Katalog `dbs14756250.sql` wird neben das Programm kopiert. Zugangsdaten werden in der Oberfläche eingerichtet; lokale Konfiguration und Laufzeitdaten gehören nicht ins Repository.

## Herausforderungen

**Erneut einplanen…** an einer Karte öffnet die Datumsauswahl und setzt den Fortschritt für den neuen Lauf automatisch zurück. **Automatische Planung…** öffnet die Vorlagenauswahl und Rotationseinstellungen. Details: [Herausforderungsplanung](docs/challenge-planning.md).

```powershell
dotnet run --project tests/ChallengePlanning/ChallengePlanning.Tests.csproj
```

Diese isolierten Tests prüfen Planung und Ablauf ohne einen laufenden Server oder echte Zähler zu verändern.

## Discord und Serverzugriff

Der Bot benötigt Zugriff auf die konfigurierten Kanäle sowie Rechte zum Senden, Einbetten, Bearbeiten und Löschen seiner Nachrichten. Für die Chat-Bridge muss der Message Content Intent aktiviert sein. RCON, SFTP und Kanal-IDs werden in den Einstellungen konfiguriert.

Der SettingRandomizer stellt die Serverkonfiguration bereit; SCUM aktiviert neue ServerSettings-Werte nach einem Serverneustart.

## Weitere Dokumentation

- [Chat-Kommandos](docs/chat-command-catalog.md)
- [Fahrzeugversicherung](docs/vehicle-insurance.md)
- [Challenge-Web-API](docs/CHALLENGE_USERCENTER_API_EXAMPLE.md)
- [Economy-Web-API](docs/ECONOMY_USERCENTER_API.md)
- [WPF und Debug-Logs](README_WPF_REWORK.md)

Web-Erweiterungen liegen unter `scum_usercenter`, `scum_kartographer` und den jeweiligen Deployment-Verzeichnissen. Sie werden separat eingerichtet; Beispiele für Umgebungsvariablen bleiben im Repository, echte `.env`-Dateien und Spielerdaten werden ausgeschlossen.
