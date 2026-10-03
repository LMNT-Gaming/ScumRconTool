# Technische Dokumentation der Wetterberechnung

## Zweck und Abgrenzung

Das Red Raven RCON Tool berechnet oder steuert das Wetter in SCUM nicht selbst. Es liest den aktuellen, vom Spiel über die ggCON-HTTP-API gemeldeten Wetterzustand und bereitet ihn für die Discord-Serverstatusnachricht auf.

Die Abfrage ist damit **rein lesend**. In diesem Ablauf wird kein `#SetWeather`- oder anderer SCUM-Adminbefehl ausgeführt.

## Datenfluss

1. Das Tool ruft `GET /weather.json` an der konfigurierten ggCON-HTTP-API auf.
2. ggCON liefert die aktuellen Werte aus dem laufenden SCUM-Server als JSON.
3. Nur `nimbostratusCoverage` wird in den angezeigten Wetterwert umgerechnet.
4. Lufttemperatur, Wassertemperatur und Ingame-Uhrzeit werden separat formatiert.
5. Das Ergebnis wird in der bestehenden Discord-Serverstatusnachricht aktualisiert.

## Verbindung zur ggCON-API

Eine ausdrücklich konfigurierte ggCON-Basis-URL hat Vorrang. Fehlt sie, verwendet das Tool den SCUM-Host und den konfigurierten ggCON-HTTP-Port. Ist auch dieser nicht gesetzt, gilt zunächst `RCON-Port - 1` und abschließend Port `5376` als Fallback.

Das ggCON-Passwort wird im HTTP-Header `X-Password` übertragen. Falls kein eigenes ggCON-HTTP-Passwort hinterlegt ist, verwendet das Tool das konfigurierte RCON-Passwort. Der HTTP-Timeout beträgt 12 Sekunden.

## Wetterwert

Der vom Tool verwendete Wert ist:

```text
Wetterwert = clamp(nimbostratusCoverage, 0, 1)
Wetter in Prozent = Wetterwert × 100
```

`clamp` begrenzt den Wert auf den Bereich von `0.0` bis `1.0`. Fehlt der Wert, wird für die Berechnung `0.0` verwendet.

Beispiele:

| nimbostratusCoverage | Anzeige als Wert | Entspricht |
|---:|---:|---:|
| 0.00 | 0.00 | 0 % |
| 0.25 | 0.25 | 25 % |
| 0.50 | 0.50 | 50 % |
| 1.00 | 1.00 | 100 % |

### Warum ausschließlich `nimbostratusCoverage`?

Nach den beobachteten SCUM-/GGHost-Wetterdaten bildet `nimbostratusCoverage` die eigentliche Wetterintensität zuverlässig ab. Andere gelieferte Felder können auch bei einer Wetterintensität von 0 % noch erhöhte Werte enthalten. Das betrifft insbesondere `fogDensity` und `cirrostratusCoverage`.

Folgende API-Werte werden deshalb **nicht** in den Wetterwert eingerechnet:

- `rainIntensity`
- `snowIntensity`
- `fogDensity`
- `windSpeedKph`
- `lightningRate`
- `cirrostratusCoverage`
- `cumulonimbusCoverage`

Eine Maximalwertberechnung aus allen Feldern würde beispielsweise Nebel oder hohe Cirrostratus-Bewölkung fälschlich als starke allgemeine Wetterintensität anzeigen.

## Wettersymbol

Das Symbol richtet sich ausschließlich nach dem berechneten Wetterwert:

| Wetterwert | Symbol | Einordnung |
|---:|:---:|---|
| 0.00–0.09 | ☀️ | klar |
| 0.10–0.29 | 🌤️ | leicht bewölkt |
| 0.30–0.49 | ⛅ | bewölkt |
| 0.50–0.69 | ☁️ | stark bewölkt |
| 0.70–0.89 | 🌧️ | Regenwetter |
| 0.90–1.00 | ⛈️ | schweres Wetter/Gewitter |

Die Bezeichnungen dienen nur der verständlichen Darstellung im Discord-Status. Das Tool leitet daraus keine zusätzlichen Wettereffekte im Spiel ab.

## Temperatur und Ingame-Zeit

Luft- und Wassertemperatur kommen direkt aus `airTemperature` und `waterTemperature`. Das Tool rundet beide Werte kaufmännisch auf ganze Grad Celsius. Fehlende Werte werden als `--` angezeigt.

Die API liefert `timeOfDay` als Dezimalstunden. Die Darstellung erfolgt nach:

```text
Gesamtminuten = round(timeOfDay × 60)
Uhrzeit = Gesamtminuten modulo 1440
```

Beispiel: `13.5` wird zu `13:30` Uhr. Durch die Modulo-Behandlung bleiben auch Werte außerhalb eines normalen 24-Stunden-Bereichs darstellbar.

## Aktualisierung und Fehlerverhalten

Die Wetterabfrage läuft zusammen mit dem Discord-Serverstatus. Das konfigurierte Polling wird technisch auf mindestens 60 Sekunden begrenzt. Beim manuellen Aktualisieren wird dieselbe Berechnung verwendet.

Ist die ggCON-API nicht erreichbar, läuft in einen Timeout, liefert einen HTTP-Fehler oder `ok=false`, wird kein alter Wert neu berechnet. Die übrigen Serverstatusdaten können weiterhin aktualisiert werden; im Wetterfeld erscheint dann „Keine Wetterdaten verfügbar“. Der Fehler wird zusätzlich im Tool protokolliert.

## Beispiel der Discord-Ausgabe

```text
🌧️ Wetterwert 0.75
🌡️ Luft: 18°C | 🏊 Wasser: 16°C | ⌚ Ingame: 13:30
```

Der Wert `0.75` bedeutet dabei eine von SCUM/ggCON gemeldete `nimbostratusCoverage` von 75 % – nicht eine vom Tool selbst erzeugte Regenwahrscheinlichkeit.
