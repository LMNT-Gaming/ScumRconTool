# Red Raven Rcon Tool

Dieser Ordner ist ein erster WPF-Umbau des vorhandenen WinForms-Projekts.

## Was neu ist

- Neues WPF-Projekt `ScumRconTool.Wpf.csproj`
- Schwarz/rotes Theme in `Themes/DarkRedTheme.xaml`
- Dashboard, RCON-Konsole, Discord-Bridge, Script-Editor und Settings als neue Oberfläche
- Wiederverwendung der vorhandenen Services, Models und Script-Dateien
- Discord-Bridge-Service mit Gateway Intents fuer Guild Messages und Message Content
- Discord Chatlog Embeds fuer Ingame-Chatlogs
- Discord -> Ingamechat ueber sicheren `#Broadcast`-Wrapper
- Command-Filter fuer Discord-Nachrichten: blockiert Nachrichten, die wie Commands beginnen (`#`, `/`, `!`, `.`, `;`, `$`)
- AvalonEdit JSON-Editor mit Zeilennummern, Formatieren, Validieren, Speichern und Duplizieren

## Wichtige ggCON/RCON Hinweise

Die ggCON-Doku beschreibt RCON als Valve Source RCON kompatibel. RCON-Befehle werden als `SERVERDATA_EXECCOMMAND` gesendet und Antworten kommen als JSON zurueck. Fuer Ingame-Nachrichten wird hier standardmaessig `#Broadcast [type] <text>` verwendet.

## Discord Voraussetzungen

Im Discord Developer Portal muss beim Bot in der Regel aktiviert werden:

- Server Members Intent ist nicht erforderlich fuer diese Bridge
- Message Content Intent ist erforderlich, damit der Bot Nachrichteninhalte lesen kann

Der Bot braucht im Ziel-Channel mindestens:

- View Channel
- Read Message History
- Send Messages
- Embed Links

## Start

Auf Windows mit installiertem .NET 8 SDK:

```powershell
dotnet restore .\ScumRconTool.Wpf.csproj
dotnet build .\ScumRconTool.Wpf.csproj
dotnet run --project .\ScumRconTool.Wpf.csproj
```

## Hinweis

In dieser Umgebung konnte nicht kompiliert werden, weil `dotnet` nicht installiert ist. Die Dateien sind als Windows-WPF-Projekt vorbereitet.

## Debug Logs

Die WPF-App schreibt interne Debug-Logs nach:

```text
<OutputDirectory>\logs\scum-rcon-tool-yyyy-MM-dd.log
```

In der Oberfläche gibt es den Tab **Debug Logs**. Dort sieht man live:

- RCON-Verbindung und gesendete Befehle
- gekürzte RCON-Antworten
- Discord-Bridge-Start und Chatlog-Forwarding
- Script-Engine Start/Stop/Scan
- Fehler aus Commands und unbehandelte UI-/Task-Exceptions

Die Datei kann über **Log-Ordner öffnen** direkt im Explorer geöffnet werden.
