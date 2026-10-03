<?php
declare(strict_types=1);
if (empty($_SESSION['steamid'])) { http_response_code(401); exit('Bitte einloggen.'); }
?>
<link rel="stylesheet" href="assets/insurance.css?v=<?= (int)filemtime(__DIR__.'/../assets/insurance.css') ?>">
<script src="assets/insurance.js?v=<?= (int)filemtime(__DIR__.'/../assets/insurance.js') ?>" defer></script>
<main class="content insurance-page">
 <section class="insurance-toolbar">
  <p>Abschluss ingame: <code>/versicherung FAHRZEUGID 1</code> – wahlweise 1, 2 oder 4 Wochen. Bestätigen mit <code>/ja</code>.</p>
  <button type="button" data-insurance-refresh>Jetzt aktualisieren</button>
 </section>
 <h2>Meine Fahrzeuge <span data-insurance-vehicle-count></span></h2>
 <p class="insurance-note">Deine verschlossenen Fahrzeuge aus dem Datenstand der Homepage, wie in der Übersicht. Eigentümer und Preis werden beim Abschluss ingame nochmals aktuell geprüft.</p>
 <p data-insurance-vehicles-status role="status">Fahrzeuge werden geladen …</p>
 <div class="insurance-grid" data-insurance-vehicles aria-live="polite"></div>
 <h2 class="insurance-section-title">Meine Versicherungsverträge</h2>
 <p data-insurance-status role="status">Verträge werden geladen …</p>
 <div class="insurance-grid" data-insurance-list aria-live="polite"></div>
 <p class="insurance-note">Versichert ist ein Ersatz-Grundmodell, nicht Inventar oder Umbauten. Keine automatische Verlängerung. Ein Ersatz beendet die Versicherung; versichere das neue Fahrzeug anschließend mit seiner neuen Fahrzeug-ID.</p>
</main>
