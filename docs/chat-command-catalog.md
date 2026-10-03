# Zentraler Chatbefehlskatalog

Unter **Chat Commands** stehen eingebaute Pflichtbefehle und eigene editierbare Regeln
in derselben Liste. Pflichtbefehle sind nicht löschbar, umbenennbar oder deaktivierbar.
Sie werden aus `Services/BuiltinChatCommandCatalog.cs` aufgebaut, nicht als scheinbare
RCON-Regeln in der Settings-JSON gespeichert. Sie bleiben dadurch auch bei leeren Regeln,
beim Neuladen und nach „Beispiele einfügen“ vorhanden.

Funktionen behalten ihre eigenen Einstellungen und Sicherheitsprüfungen. Insbesondere
wird die Versicherung dadurch nicht automatisch aktiviert. Der gemeinsame Chatmonitor
muss laufen. Vorhandene eigene Regeln werden nicht migriert oder entfernt.
„Beispiele einfügen“ ersetzt wie bisher die eigenen Regeln, nicht die Pflichtbefehle.
Die Sprachumschaltung übersetzt die Beschreibungen unmittelbar.

| Befehl | Aliase / Hinweis |
| --- | --- |
| `/versicherung ID [1\|2\|4]` | `/insurance` |
| `/versicherungen` | `/insurances` |
| `/getrefund V-ID` | Eigener freigegebener Versicherungsfall |
| `/ja` | `/yes` für Versicherung; `ja` zusätzlich für kostenpflichtige Votes |
| `/nein` | `/no`, offenes Versicherungsangebot abbrechen |
| `/claimall` | Eigene offenen Challenge-/Quiz-Rewards |
| `/reward-CODE` | Dynamischer privater Code, keine fremden Codes im Katalog |
| `/quizN ANTWORT` | z. B. `/quiz1 Berlin`, Nummern aus Herausforderungen |
| `/mechs` | Letzter lokal gespeicherter Randomizer-Stand |
| `/buyevent [NAME]` | Kaufbare Events aus Skriptzone |
| `/wc` | `/challenge`, `/challenges`; eigene passende Regel hat wie bisher Vorrang |
| `/CODE` | Hinweis auf konfigurierte RedeemCodes, z. B. `/starter` |

`/help`, `/events`, `/discord`, `/vote day` und `/vote weather` sind editierbare
Beispielregeln und bleiben dies. Weitere eigene Befehle stehen ebenfalls in dieser Liste.
Das `/CODE`-Muster ist Dokumentation und fängt keine unbekannten Eingaben ab;
nur tatsächlich konfigurierte RedeemCodes werden eingelöst. Ihre Aktionen und
Nutzungslimits werden weiterhin im bestehenden Menü RedeemCodes gepflegt.

Der Katalog wird auch zur Befehlserkennung verwendet. Zahlungs-, Cooldown-, Eigentümer-
und Wiederholungsschutz bleiben in den bestehenden Fachfunktionen. Die Reihenfolge
der Handler und die bisherigen Vorrangregeln wurden nicht geändert.

Offline-Tests: `dotnet run --project tests/VehicleInsurance/VehicleInsurance.Tests.csproj`
prüft Katalogabdeckung, Aliase, Mustergrenzen und die bestehenden Versicherungsfälle.
