SCUM Kartographer - Standalone

Installation:
1. Den Ordner "scum_kartographer" in den Webroot hochladen.
2. Aufrufen: /scum_kartographer/index.php

Wichtig:
- Keine Session, kein Steam-Login, keine Admin-Pruefung.
- Die Karte liegt unter assets/scum_map.jpg.
- Die World-Bounds sind im oberen PHP-Block der index.php gesetzt.
- Gemerkte Punkte werden nur lokal im Browser gespeichert.

Hinweis Speicherung:
Marker/Punkte werden ausschliesslich lokal im Browser des jeweiligen Nutzers per localStorage gespeichert. Es gibt keine Server-Datei, keine Datenbank und keine globale geteilte Marker-Liste.
