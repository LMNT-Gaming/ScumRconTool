# LMNT SCUM Serverliste auf Strato installieren

1. Den kompletten Inhalt dieses Ordners nach `redravenserver/` im Hauptverzeichnis der LMNT-Homepage hochladen.
   Vorher `config.example.php` nach `config.php` kopieren und die beiden Geheimwerte durch unabhängig erzeugte Zufallswerte ersetzen. Die echte `config.php` bleibt lokal.
2. Dem Ordner `data/` Schreibrechte fuer PHP geben. Bei Strato genuegt normalerweise `755`; falls PHP nicht schreiben kann, `775` testen.
3. Im Browser `https://lmnt-gaming.net/redravenserver/` oeffnen.
4. Mit einem aktuellen Tool-Build zustimmen und nach wenigen Sekunden die Liste neu laden.
5. `privacy.php` vor der Veroeffentlichung um Betreiber-, Kontakt- und weitere Pflichtangaben ergaenzen.

Die API liegt unter `api/heartbeat.php`. Es wird keine Datenbank benoetigt. Eintraege werden in `data/servers.json` gespeichert; der Ordner ist durch `.htaccess` vor Webzugriff geschuetzt.

Die Seiten verwenden den bestehenden LMNT-Homepage-Rahmen aus ../includes/header.php und ../includes/footer.php sowie dessen Theme. Der Ordner edravenserver/ muss deshalb direkt neben includes/ und ssets/ liegen.

Jede Installation besitzt ein eigenes zufaelliges Token. Die API speichert nur dessen HMAC-Hash. Neue Installationen sind pro Absender-IP auf zehn Registrierungen in 24 Stunden begrenzt; die Absender-IP selbst wird nur als gesalzener Rate-Limit-Hash gespeichert. Vor einem echten Release sollten `rate_limit_salt` und `token_pepper` in `config.php` durch zwei neue lange Zufallswerte ersetzt und niemals in ein oeffentliches Repository eingecheckt werden.
