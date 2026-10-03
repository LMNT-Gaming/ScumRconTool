# SCUM Usercenter Rework

Dieser Ordner enthält die von dir eingespielte vollständige SCUM-Usercenter-Kopie mit dem neuen LMNT-Design.

## Zentrale Design-Dateien

- `includes/header.php` – App-Header, Navigation, Status und Seitenkontext
- `includes/footer.php` – mobiler Drawer und Bedienlogik
- `assets/modern.css` – zentrale Rot-Schwarz-Styles und alle responsiven Layoutkorrekturen
- `assets/style.css` – bestehende Modul-Basisstile
- `assets/gym.css` – bestehende Gym-spezifische Darstellung

## Layoutregeln

- Home, Squad, Map und Quests verwenden auf großen Displays drei ausreichend breite Spalten.
- Auf mittleren Desktops wird die dritte Spalte über die volle Breite nach unten gesetzt.
- Stats und Admin verwenden ein echtes Zweispaltenraster.
- Shop, Voucher und der darauf basierende Vote-Rewards-Bereich sind aus Navigation, Routen und aktiven Ladepfaden entfernt. Die alten Quelldateien bleiben nur als nicht erreichbare Rückfallkopie erhalten.
- Base und Gym nutzen die komplette verfügbare Inhaltsbreite.
- Online-Status bleibt grün; Offline- oder Fehlerstatus bleibt rot.

Map und Quests sind jetzt reguläre eingebettete Module. Ihre früheren zweiten HTML-Dokumente und doppelten Stylesheet-Imports wurden entfernt.

Der komplette Inhalt von `homepage-rework` ist für den späteren Umzug direkt ins Domain-Root vorbereitet.

## Challenges im Usercenter

Die neue Seite **Challenges** lädt ausschließlich ihren eigenen Inhalt per HTTP API nach. Sie aktualisiert sich bei sichtbarer Seite alle 30 Sekunden und nutzt ETags; die komplette Usercenter-Seite wird dabei nicht neu geladen.

Die Einrichtung des geschützten Push-Endpunkts, die `.env`-Werte und ein vollständiger HTTP/JSON-Beispielaufruf stehen in [`../docs/CHALLENGE_USERCENTER_API_EXAMPLE.md`](../docs/CHALLENGE_USERCENTER_API_EXAMPLE.md). Eine kopierbare Konfiguration liegt zusätzlich in `private/.env.challenge.example`.

Sicherheitsmodell:

- Das Tool sendet den vollständigen Snapshot mit einem geheimen Token an `api/challenge_push.php`.
- Der Token bleibt serverseitig und wird niemals an den Browser ausgegeben.
- `api/challenge_data.php` verlangt eine aktive Usercenter-Session und filtert persönliche Fortschritte anhand von `$_SESSION['steamid']`.
- Bei persönlichen Challenges sieht ein Spieler nur seine eigenen Zielstände. Bei Community-Challenges sieht er den Gesamtstand und seinen eigenen Beitrag.