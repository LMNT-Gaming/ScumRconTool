# Herausforderungsplanung

## Erneut einplanen

An jeder zugeklappten Karte zeigt **Erneut einplanen…** eine Datum- und Uhrzeitauswahl. Ziele, Loot und Laufzeit bleiben bestehen. Speichern aktiviert die Herausforderung und startet den automatischen Dienst. Der neue Lauf erhält automatisch einen neuen Zähler-Nullpunkt beim ersten aktiven Datenbankscan. Bestehende Redeem-Codes bleiben gültig.

Die Karte zeigt direkt **Persönlich**, **Community** oder **Quiz** und die Laufzeit. Der bisherige Reset-Knopf bleibt für einen gezielten Neustart verfügbar.

## Automatische Rotation

Über **Automatische Planung…** werden Vorlagen ausgewählt und die Rotation aktiviert. Grundeinstellungen:

- Maximal 3 gleichzeitig eingeplante Herausforderungen.
- 24 Stunden Pause nach Laufende vor einer erneuten Verwendung derselben Vorlage.
- Abgeschlossene Community-Herausforderungen noch 10 Minuten anzeigen.

Manuell aktive oder künftig reservierte Herausforderungen zählen zum Limit. Vorhandene manuelle Planungen werden bei einer Verringerung des Limits nicht abgebrochen; die Rotation füllt erst wieder freie Plätze auf.

Die Rotation bevorzugt am längsten nicht eingeplante Vorlagen. Bei gleichem Stand entscheidet der Zufall. Wenn alle Vorlagen in ihrer Wiederholungspause sind, bleiben Plätze vorübergehend frei. Für tägliche Abwechslung sollten mehr Vorlagen ausgewählt werden als gleichzeitig laufen dürfen. Quiz wird weiterhin manuell eingeplant.

## Laufende Aufgaben und Abschluss

Der Dienst prüft Ablauf und Abschlussfrist minütlich, solange das Tool läuft. Datenbankfortschritt wird nach dem eingestellten Scanintervall eingelesen; ein Abschluss kann erst beim Scan erkannt werden. Die Abschlussfrist beginnt nach Verarbeitung der Belohnungen.

Community-Herausforderungen werden nach der Abschlussfrist deaktiviert. Persönliche Herausforderungen laufen bis zum Zeitende, damit weitere Spieler ihr eigenes Ziel erreichen können. Die Konfiguration bleibt als Vorlage erhalten.

Beim nächsten Discord-Abgleich werden nur eindeutig markierte, nicht mehr aktive Challenge-Nachrichten des Bots entfernt. Erklärungstexte und andere Nachrichten bleiben bestehen. Die Übersicht geplanter Herausforderungen wird weiterhin aktualisiert.

## Betrieb und Prüfungen

Die Automatik benötigt das geöffnete Tool und den laufenden Herausforderungsdienst. Während das Tool ausgeschaltet ist, werden keine Aufgaben gewechselt. Die Option zum Überspringen von Datenbankdownloads ohne Online-Spieler gilt weiterhin; dadurch kann sich der erste Zähler-Nullpunkt bis zum nächsten erlaubten Scan verschieben.

```powershell
dotnet run --project tests/ChallengePlanning/ChallengePlanning.Tests.csproj
```

Die Tests decken Slots einschließlich manueller Reservierungen, Vorlagenpriorität, Wiederholungspause, Community-Abschlussfrist, persönliche Laufzeiten und erneute Einplanung ab.
