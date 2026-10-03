<?php
declare(strict_types=1);
header('X-Content-Type-Options: nosniff');
header('Referrer-Policy: no-referrer');
$pageTitle = 'Datenschutz – Red Raven Serverliste';
require dirname(__DIR__) . '/includes/header.php';
?>
<link rel="stylesheet" href="assets/server-browser.css">
<section class="card rr-privacy">
  <span class="badge">Freiwilliges Opt-in</span>
  <h1>Datenschutzhinweise zur LMNT Serverliste</h1>
  <p>Die Teilnahme ist freiwillig. Nach ausdrücklicher Zustimmung übermittelt das Red Raven RCON Tool den konfigurierten Servernamen, die öffentliche IP-Adresse des SCUM-Servers, die Tool-Version, eine zufällige Installations-ID, ein Sicherheitstoken und den Zeitpunkt der Meldung.</p>
  <p>Öffentlich angezeigt werden ausschließlich Servername, SCUM-Server-IP, Tool-Version und letzter Meldezeitpunkt. Das Sicherheitstoken wird niemals angezeigt und serverseitig nur als Hash gespeichert.</p>
  <p>Nicht übertragen werden Passwörter, Discord-Token, Logs, Chatdaten, Spieler-IDs oder Inhalte der Scripts.</p>
  <p>Die Zustimmung kann jederzeit in den Tool-Einstellungen widerrufen werden. Das Tool fordert anschließend die Löschung des Eintrags an. Nicht mehr aktualisierte Einträge werden spätestens nach 90 Tagen automatisch gelöscht.</p>
  <p><strong>Open Source:</strong> Bei Fragen oder Zweifeln kann der vollständige Quellcode unter <a href="https://github.com/LMNT-Gaming/ScumRconTool" target="_blank" rel="noopener noreferrer">LMNT-Gaming/ScumRconTool</a> eingesehen werden.</p>
  <p>Angaben zum Betreiber und Kontakt findest du im <a href="../impressum.php">Impressum der LMNT-Gaming-Webseite</a>.</p>

  <hr class="sep">
  <h2>Privacy information (English)</h2>
  <p>Participation is voluntary. After explicit consent, Red Raven RCON Tool sends the configured server name, public SCUM server IP address, tool version, a random installation ID, a security token, and the report time.</p>
  <p>The public directory displays only the server name, SCUM server IP, tool version, and latest report time. The security token is never displayed and is stored by the server only as a hash.</p>
  <p>Passwords, Discord tokens, logs, chat data, player IDs, and script contents are not transmitted.</p>
  <p>Consent can be withdrawn at any time in the tool settings. The tool will then request deletion of the listing. Entries that are no longer updated are automatically deleted after no more than 90 days.</p>
  <p><strong>Open source:</strong> If you have any questions or doubts, review the complete source code at <a href="https://github.com/LMNT-Gaming/ScumRconTool" target="_blank" rel="noopener noreferrer">LMNT-Gaming/ScumRconTool</a>.</p>
  <p>Operator and contact details are available in the <a href="../impressum.php">LMNT-Gaming website imprint</a>.</p>

  <div class="rr-actions"><a class="btn" href="index.php">Zurück zur Serverliste</a></div>
</section>
<?php require dirname(__DIR__) . '/includes/footer.php'; ?>