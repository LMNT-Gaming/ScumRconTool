# Challenge Usercenter API

Das Red Raven Recon Tool überträgt den letzten Challenge-Stand per HTTPS an das Usercenter. Der Push-Endpunkt speichert die Daten serverseitig; der Browser erhält den geheimen Token niemals.

## Einrichtung

1. Den Ordner `scum_usercenter` auf den Webserver kopieren.
2. Einen langen zufälligen Token erzeugen und in `scum_usercenter/private/.env` eintragen:

```env
CHALLENGE_PUSH_TOKEN=hier-einen-langen-zufaelligen-token-eintragen
# Optional:
CHALLENGE_DATA_DIR=/root/scum/private/challenge_data
```

3. Im Tool unter **Challenges** aktivieren:

```text
Endpoint: https://example.org/scum_usercenter/api/challenge_push.php
Token:    derselbe Wert wie CHALLENGE_PUSH_TOKEN
```

4. Einstellungen speichern und **Scannen + Discord** ausführen. Der Status im Tool bestätigt die Übertragung.

## HTTP-Beispiel

```http
POST /scum_usercenter/api/challenge_push.php HTTP/1.1
Content-Type: application/json
Authorization: Bearer <CHALLENGE_PUSH_TOKEN>
X-Challenge-Token: <CHALLENGE_PUSH_TOKEN>

{
  "generatedAtUtc": "2026-08-15T12:00:00Z",
  "challenges": [{
    "id": "weekly-kills",
    "title": "Hunter",
    "goalScope": "PerPlayer",
    "goalLogic": "All",
    "startUtc": "2026-08-15T00:00:00Z",
    "endUtc": "2026-08-22T00:00:00Z",
    "reward": "2500 Geld · 10 Fame",
    "goals": [{
      "key": "kills.puppets",
      "name": "Puppets killed",
      "target": 100,
      "players": [{
        "steamId": "76561198000000000",
        "playerName": "Example",
        "squadName": "Ravens",
        "progress": 42,
        "target": 100,
        "percent": 42,
        "isCompleted": false
      }]
    }]
  }]
}
```

`api/challenge_data.php` ist nur mit bestehender Usercenter-Session erreichbar. Die Steam-ID aus der Session bestimmt serverseitig den Spieler. Bei persönlichen Challenges werden keine Stände anderer Spieler ausgegeben. Bei Community-Challenges werden nur der gemeinsame Gesamtstand und der eigene Beitrag geliefert.

Das Frontend fragt den Endpunkt alle 30 Sekunden nur bei sichtbarer Seite ab, verwendet ETags und ersetzt ausschließlich das Challenge-Modul.