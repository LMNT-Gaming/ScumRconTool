# Fahrzeugversicherung / Vehicle insurance

## Einschalten

Im neuen Hauptmenü **Fahrzeugversicherung → Einstellungen** zuerst **Fahrzeugtypen laden**,
Preise prüfen, Versicherung aktivieren und speichern. Die Funktion ist standardmäßig aus.
Die vorhandenen ggCON-HTTP-Zugangsdaten werden verwendet. Der Chatmonitor startet mit der
Versicherung; es ist keine zusätzliche dauerhafte RCON-Verbindung erforderlich.

- Standardpreis: 10.000 $ je Woche; je Fahrzeugtyp überschreibbar oder deaktivierbar.
- Laufzeiten: 1, 2 oder 4 Wochen, ohne automatische Verlängerung.
- Rabatt: standardmäßig 5 % für zwei und 10 % für vier Wochen, frei einstellbar.
- Rechnung: Wochenpreis × Wochen × (1 − Rabatt / 100), auf ganze Dollar gerundet.
- Während ein Fahrzeug versichert ist, kann kein zweiter Vertrag dafür gekauft werden.
  Nach Ablauf kann der Eigentümer es erneut versichern.
- Neue, eindeutig zuordenbare Typen verwenden den Standardpreis. „Fahrzeugtypen laden“
  ergänzt die Preisverwaltung, ohne vorhandene Preise zu überschreiben.

## Spielerbefehle

| Deutsch / Englisch | Wirkung |
| --- | --- |
| `/versicherung 6382416` / `/insurance 6382416` | Fahrzeug prüfen und alle drei Preise privat anzeigen |
| `/versicherung 6382416 2` / `/insurance 6382416 2` | Angebot für zwei Wochen anfordern |
| `/ja` / `/yes` | Innerhalb von 60 Sekunden bestätigen und bezahlen |
| `/nein` / `/no` | Angebot abbrechen |
| `/versicherungen` / `/insurances` | Eigene Verträge und Vertrags-IDs anzeigen |
| `/getrefund V-012345ABCDEF` | Freigegebenes Ersatzfahrzeug anfordern |

Es wird immer die Steam-ID des Chatabsenders verwendet. Die Vertrags-ID statt des
Fahrzeugnamens verhindert Verwechslungen bei mehreren gleichen Fahrzeugen. Angebote
und Antworten werden nicht öffentlich gesendet. Ein neues kostenpflichtiges Vote-Angebot
verwirft das Versicherungsangebot und umgekehrt; `/ja` bestätigt nicht beides.

## Voraussetzungen und Grenzen

Die ggCON-Version muss `/vehicles.json` inklusive `count`, `ownershipResolved` und
`ownerSteamId`, `/vehicle-types.json`, `/logs` mit Quelle `vehicle_destruction`,
Spielerkonto/Chat sowie `/spawn-vehicle` unterstützen. Eigentümer müssen eindeutig
aufgelöst werden; ein fremdes oder ungeklärtes Fahrzeug kann nicht versichert werden.
Fahrzeugklassen werden gegen den Live-Katalog geprüft; `BPC_Barba` und `Barba_ES`
werden beispielsweise einander zugeordnet. Bei mehreren passenden Typen wird abgebrochen.

Der Monitor prüft alle 15 Sekunden. Ersatz wird nur freigegeben, wenn:

1. Ein `Destroyed`-Eintrag zu Fahrzeug-ID, Eigentümer und Fahrzeugmodell passt.
2. Die Zerstörung innerhalb der bezahlten Laufzeit liegt.

Ein unbrauchbares Wrack darf dabei weiterhin in `/vehicles.json` stehen und sogar
`rendered: true` haben. Ausschlaggebend ist der eindeutig zugeordnete `Destroyed`-Eintrag,
nicht das Verschwinden aus der Fahrzeugliste. Bereits gespeicherte Fälle mit Status
`DestroyedPendingCheck` und gültigem Zerstörungszeitpunkt werden beim nächsten Monitorlauf
automatisch freigegeben, ohne Admin-Quittierung oder erneutes Abspielen des Logs.
Auch nach Abholung kann dieselbe zerstörte Fahrzeug-ID nicht neu versichert werden.

Ein verschwundenes/unbeladenes Fahrzeug allein reicht ausdrücklich nicht.
`Disappeared` und andere Aktionen werden nicht als Versicherungsfall gewertet.
SCUM-Chat- und Zerstörungslogs werden als UTC gelesen, unabhängig von der lokalen
Windows-Zeitzone. Server- und Rechneruhr müssen synchronisiert sein. Der Schutz vor
alten Chatbefehlen bleibt aktiv und protokolliert abgelehnte Zeitstempel zur Diagnose.

**Log-Grenze:** ggCON hält nur einen begrenzten Logpuffer vor. Bei längerem Ausfall
können Einträge fehlen; dann erfolgt keine automatische Ersatzfreigabe. Admins müssen
den Fall anhand archivierter Logs separat prüfen. Ein `Destroyed`-Eintrag verrät nicht
zuverlässig, ob die Ursache ein Adminbefehl war. Wipes/Adminlöschungen deshalb nicht als
reguläre Versicherungsfälle behandeln; Versicherung vorher deaktivieren und Verträge prüfen.

**Ersatzumfang:** ein Grundmodell am online befindlichen Spieler, kein gesichertes
Inventar, Tuning, Treibstoff oder sonstiger vorheriger Zustand. Nach bestätigtem
Spawnauftrag ist der Vertrag geschlossen. Die API liefert keine neue Fahrzeug-ID;
das neue Fahrzeug muss separat versichert werden. Eine positive HTTP-Antwort bestätigt
den Auftrag, nicht die tatsächlich sichtbare Entstehung des Fahrzeugs. Bei Problemen
müssen die Serverlogs geprüft werden; es wird nicht automatisch nochmals gespawnt.

## Schutz vor Doppelbuchungen

Vor Abbuchung beziehungsweise Spawn wird der Zwischenstatus dauerhaft gespeichert.
Bei Timeout, unklarer Antwort oder Absturz bleiben `PaymentReview`/`SpawnReview` gesperrt.
Es gibt keine automatische Wiederholung einer möglicherweise bereits ausgeführten Aktion.

Unter **Übersicht → Verlauf / Transaktion prüfen** kann der Admin nach Prüfung der
ggCON-/Serverlogs „erfolgreich“ oder „nicht ausgeführt“ bestätigen. Diese Buttons ändern
ausschließlich den Vertrag; sie buchen kein Geld und erzeugen kein Fahrzeug.
„Nicht ausgeführt“ darf nur gewählt werden, wenn sicher nichts abgebucht/gespawnt wurde.

Das Vertragsjournal liegt serverbezogen unter:
`%LOCALAPPDATA%\ScumRconTool\State\vehicle-insurance-<Serverhash>.json`.
Es enthält auch abgeschlossene Verträge und den Logcursor. Regelmäßig sichern und
nicht löschen, um einen vermeintlich hängenden Vorgang erneut auszulösen. Ein beschädigtes
Journal sperrt die Versicherung statt alle Verträge zu vergessen. Beim Wechsel der
Serververbindung ist RedRaven neu zu starten. Nur eine RedRaven-Instanz pro Server betreiben.
Kontobewegungen anderer Tools können nicht atomar mit dieser Versicherung gesperrt werden.

## Usercenter einrichten

Die Website ist eine reine persönliche Vertragsansicht, keine zweite Zahlungsstelle.
Unter „Meine Fahrzeuge“ stehen die eigenen verschlossenen Fahrzeuge aus derselben
Homepage-Datenbank wie in der Usercenter-Übersicht, einschließlich Fahrzeug-ID und
letztem Zugriff. Der Versicherungsstatus wird mit den übertragenen eigenen Verträgen
abgeglichen. „Preisanfrage kopieren“ kopiert `/versicherung FAHRZEUGID` für den Ingame-Chat;
auf der Website wird nichts gekauft. Squad-/Fremdfahrzeuge werden nicht mitgeliefert.
Der Fahrzeug-Datenstand kann älter sein als der Serverzustand; RedRaven prüft Eigentümer
und Preis beim Abschluss erneut live. Bei einem Datenbankausfall bleiben Verträge lesbar
und die Fahrzeugliste wird als nicht aktuell gekennzeichnet. Keine zusätzlichen
ggCON-/FTP-Abfragen: Es wird nur die bereits auf der Homepage vorhandene SCUM.db gelesen.
RedRaven sendet Änderungen per HTTPS-POST; der Browser lädt nur die eigenen Verträge
alle 15 Sekunden nach, ohne die Seite neu zu laden. In unsichtbaren Tabs pausiert die
Abfrage. Der Zeitstempel zeigt die letzte tatsächliche Übertragung, nicht eine Livegarantie.

1. In `scum/private/.env` einen eigenen zufälligen Token mit mindestens 32 Zeichen setzen:

   ```dotenv
   INSURANCE_PUSH_TOKEN=HIER_EINEN_NEUEN_ZUFAELLIGEN_TOKEN_EINTRAGEN
   # Optional: bestehendes, für PHP beschreibbares Verzeichnis außerhalb des Webroots
   # INSURANCE_DATA_DIR=/absoluter/privater/pfad/insurance
   ```

2. Im Tool Webübertragung einschalten; Endpoint:
   `https://lmnt-gaming.net/scum/api/insurance_push.php`
   und denselben Token eintragen, speichern und **Jetzt übertragen** anklicken.
   Nicht das ggCON-Kennwort als Website-Token verwenden.
3. Im Usercenter **Versicherung** auswählen:
   `https://lmnt-gaming.net/scum/index.php?page=insurance`.

Die Website prüft den Header `X-Insurance-Token`, übernimmt nur erlaubte Felder und
filtert den Abruf anhand der angemeldeten Steam-ID aus der PHP-Sitzung. Fremde
Verträge, interne Historie und API-Kennwörter werden nicht im Browser bereitgestellt.
Token nicht in JavaScript oder URLs hinterlegen. HTTPS ist Pflicht.

Standardablage: `scum/private/insurance_data/snapshot.json`. Die mitgelieferte
`.htaccess` sperrt den direkten Abruf unter Apache. Bei anderem Webserver entsprechenden
Zugriffsschutz einrichten oder `INSURANCE_DATA_DIR` außerhalb des Webroots verwenden.
Vor Einsatz prüfen, dass ein direkter HTTP-Aufruf der Datendatei **403/404** ergibt.

### Zu übertragende Dateien

Diese lokalen Dateien wurden für die vorhandene PHP-Homepage vorbereitet; kein automatischer
Upload auf den öffentlichen Webserver und keine Änderung deiner `.env` erfolgt:

- `scum/index.php`
- `scum/includes/header.php`
- `scum/pages/insurance.php`
- `scum/functions/insurance_function.php`
- `scum/api/insurance_push.php`
- `scum/api/insurance_data.php`
- `scum/assets/insurance.css`
- `scum/assets/insurance.js`
- `scum/private/insurance_data/.htaccess`

Die vorhandene `scum/functions/env_function.php` wird weiterverwendet. PHP 8.1 oder neuer.
Die Stagingkopie dieser neun Dateien liegt im Projekt unter `deploy/vehicle-insurance/scum`.
Nur für einen Server pro Endpoint/Token gedacht; mehrere Server benötigen getrennte Ablagen.

## Tests

`dotnet run --project tests/VehicleInsurance/VehicleInsurance.Tests.csproj`
prüft Preise, Eigentümer, abgelaufene Angebote, Chat-Replays, unvollständige Fahrzeuglisten,
Zerstörung und die Sperren bei unklarer Zahlung/Spawn vollständig offline mit Fake-API.

`php tests/VehicleInsuranceWeb/test.php` prüft die PHP-Endpunkte an einem lokalen
Testserver mit synthetischen Verträgen und Test-Token, ohne ggCON oder Live-Homepage.

Produktive Zahlungen und Spawns wurden bei der Implementierung nicht ausgeführt.
