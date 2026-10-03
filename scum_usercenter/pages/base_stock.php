<?php
// /pages/base_stock.php
declare(strict_types=1);

if (session_status() !== PHP_SESSION_ACTIVE) session_start();
if (empty($_SESSION['steamid'])) {
    http_response_code(401);
    exit('Bitte einloggen.');
}

require_once __DIR__ . '/../functions/base_stock_function.php';

function h(string $s): string
{
    return htmlspecialchars($s, ENT_QUOTES);
}
function short_asset_name(string $asset): string
{
    if ($asset === '') return '';
    $pos = strrpos($asset, '.');
    return $pos !== false ? substr($asset, $pos + 1) : $asset;
}

function item_display_name(string $itemClass): string
{
    // Sprachsuffix entfernen (_ES, _DE, ...)
    return preg_replace('/_[A-Z]{2}$/', '', $itemClass);
}

// später: du mapst item_class -> bildpfad
function item_image_url(string $itemClass): string
{
    // _ES am Ende entfernen
    if (str_ends_with($itemClass, '_ES')) {
        $itemClass = substr($itemClass, 0, -3);
    }

    return 'https://www.lmnt-gaming.net/images/items/' . rawurlencode($itemClass) . '.png';
}
$baseX = null;
$baseY = null;

try {
    $db = getScumDb(); // falls du so eine Funktion hast, sonst: new SQLite3($scumDbPath, SQLITE3_OPEN_READONLY);

    // Beispiel: Base für den Squad (bitte ggf. WHERE anpassen: squad_id / owner_profile_id / whatever du benutzt)
    $stmt = $db->prepare("
        SELECT location_x, location_y
        FROM base
        WHERE squad_id = :sid
        ORDER BY id DESC
        LIMIT 1
    ");
    $stmt->bindValue(':sid', (int)$squadId, SQLITE3_INTEGER);
    $res = $stmt->execute();
    $row = $res ? $res->fetchArray(SQLITE3_ASSOC) : false;

    if ($row) {
        $baseX = (float)$row['location_x'];
        $baseY = (float)$row['location_y'];
    }
} catch (Throwable $e) {
    // optional: error_log($e->getMessage());
}


$steamId = (string)$_SESSION['steamid'];
$containers = base_stock_load_for_steamid($steamId);
$baseLoc = base_stock_get_base_location_for_steamid($steamId);
?>
<style>
    /* ===== Base Stock / Lager ===== */
    .base-stock-grid {
        display: grid;
        gap: 12px;
        grid-template-columns: repeat(auto-fill, minmax(360px, 1fr));
    }

    .base-stock-card {
        padding: 12px;
    }

    .base-stock-header {
        display: flex;
        justify-content: space-between;
        align-items: flex-start;
        gap: 10px;
    }

    .base-stock-title {
        font-weight: 800;
        font-size: 16px;
        line-height: 1.2;
    }

    .base-stock-sub {
        opacity: .8;
        font-size: 8px;
        margin-top: 4px;
    }

    .base-stock-items {
        margin-top: 10px;
        display: grid;
        grid-template-columns: repeat(auto-fill, 96px);
        gap: 10px;
    }

    .item-tile {
        width: 96px;
        height: 96px;
        position: relative;
        display: flex;
        align-items: center;
        justify-content: center;
        overflow: hidden;
        border-radius: 10px;

        /* wenn du schon scum-slot borders hast, kannst du das auch weglassen */
        border: 1px solid rgba(255, 255, 255, .12);
        background: rgba(0, 0, 0, .22);
    }

    .item-tile img {
        width: 100%;
        height: 100%;
        object-fit: contain;
        image-rendering: pixelated;
    }

    .item-fallback {
        width: 100%;
        height: 100%;
        display: none;
        align-items: center;
        justify-content: center;
        padding: 8px;
        font-size: 11px;
        opacity: .85;
        text-align: center;
    }

    .item-count {
        position: absolute;
        right: 6px;
        bottom: 6px;
        padding: 2px 6px;
        border-radius: 999px;
        font-size: 12px;
        font-weight: 800;
        background: rgba(0, 0, 0, .6);
        border: 1px solid rgba(255, 255, 255, .14);
    }
</style>
<main class="content layout-3col">
    <div class="center">
        <section class="panel base-stock-page" id="baseStockPage">
            <div class="panel-topbar">
                <div class="panel-topbar-title">BASIS-BESTÄNDE</div>
            </div>
            <section class="base-location-card" aria-labelledby="base-location-title">
                <div class="base-map-frame">
                    <img id="baseMiniMapImg" src="assets/img/scum_map.jpg" alt="SCUM-Karte mit markiertem Basis-Standort">
                    <canvas id="baseMiniMapCanvas" width="512" height="512"></canvas>
                </div>
                <div class="base-location-copy">
                    <span class="base-location-kicker">SQUAD / MAP DATA</span>
                    <h2 id="base-location-title">Basis-Standort</h2>
                    <?php if (!$baseLoc): ?>
                        <p>Aktuell wurden keine Base-Koordinaten gefunden.</p>
                    <?php else: ?>
                        <p>Der zuletzt ermittelte Standort deiner Squad-Basis.</p>
                        <dl class="base-coordinates">
                            <div><dt>X-Koordinate</dt><dd><?= h((string) $baseLoc['x']) ?></dd></div>
                            <div><dt>Y-Koordinate</dt><dd><?= h((string) $baseLoc['y']) ?></dd></div>
                        </dl>
                    <?php endif; ?>
                </div>
            </section>

            <div class="panel-body">

                <?php if (!$containers): ?>
                    <div class="scum-slot">Keine Basis-Container gefunden (oder du bist in keinem Squad).</div>
                <?php else: ?>
                    <div class="base-stock-toolbar">
                        <input
                            id="baseStockSearch"
                            class="base-stock-search"
                            type="text"
                            placeholder="Suche nach Schrank, Asset oder Item…"
                            autocomplete="off" />
                        <button id="baseStockClear" class="base-stock-clear" type="button">X</button>
                        <div id="baseStockCount" class="base-stock-count"></div>
                    </div>

                    <div class="base-stock-grid">

                        <?php foreach ($containers as $c): ?>
                            <?php
                            $title = $c['container_name'] !== '' ? $c['container_name'] : ('Container #' . (int)$c['element_id']);
                            $assetFull = (string)$c['asset'];
                            $asset = short_asset_name($assetFull);

                            $items = $c['items'] ?? [];
                            $totalItems = 0;
                            foreach ($items as $it) $totalItems += (int)$it['count'];
                            ?>

                            <?php
                            // Suchtext für Container: Name + Asset
                            $containerSearch = mb_strtolower($title . ' ' . $asset);
                            ?>
                            <div class="scum-slot base-stock-card base-stock-container"
                                data-search="<?= h($containerSearch) ?>">

                                <div class="base-stock-header">
                                    <div>
                                        <div class="base-stock-title"><?= h($title) ?></div>
                                        <div class="base-stock-sub">
                                            <div class="base-stock-sub">
                                                <?= h($asset) ?>
                                            </div>
                                        </div>
                                    </div>

                                    <div class="base-stock-sub" style="text-align:right;">
                                        Items: <?= $totalItems ?>
                                    </div>
                                </div>

                                <?php if (!$items): ?>
                                    <div class="base-stock-sub" style="margin-top:10px;">(leer)</div>
                                <?php else: ?>
                                    <div class="base-stock-items">
                                        <?php foreach ($items as $it): ?>
                                            <?php
                                            $cls = (string)$it['class'];
                                            $cnt = (int)$it['count'];

                                            $displayName = item_display_name($cls);          // _ES weg (für Anzeige)
                                            $img = item_image_url($cls);                     // _ES weg (für Bildpfad)
                                            $fallbackText = str_replace('_', ' ', $displayName);
                                            ?>

                                            <?php
                                            $itemSearch = mb_strtolower($cls . ' ' . $displayName);
                                            ?>
                                            <div class="item-tile"
                                                title="<?= h($cls) ?>"
                                                data-item="<?= h($itemSearch) ?>">
                                                <img
                                                    src="<?= h($img) ?>"
                                                    alt="<?= h($displayName) ?>"
                                                    onerror="this.style.display='none'; this.nextElementSibling.style.display='flex';" />

                                                <!-- Fallback nur wenn Bild fehlt -->
                                                <div class="item-fallback" style="display:none; line-height:1.2; word-break:break-word;">
                                                    <?= h($fallbackText) ?>
                                                </div>

                                                <?php if ($cnt > 1): ?>
                                                    <div class="item-count">x<?= $cnt ?></div>
                                                <?php endif; ?>
                                            </div>
                                        <?php endforeach; ?>

                                    </div>
                                <?php endif; ?>

                            </div>

                        <?php endforeach; ?>

                    </div>

                <?php endif; ?>

        </section>
    </div>
</main>
<script>
    (function() {
        const input = document.getElementById('baseStockSearch');
        const clearBtn = document.getElementById('baseStockClear');
        const countEl = document.getElementById('baseStockCount');

        const containers = Array.from(document.querySelectorAll('.base-stock-container'));

        function norm(s) {
            return (s || '').toString().toLowerCase().trim();
        }

        function applyFilter() {
            const q = norm(input.value);

            let shown = 0;

            for (const card of containers) {
                const containerText = norm(card.dataset.search);

                // items in diesem container
                const itemEls = Array.from(card.querySelectorAll('.item-tile'));
                const itemsText = itemEls.map(el => norm(el.dataset.item)).join(' ');

                const match = (q === '') || containerText.includes(q) || itemsText.includes(q);

                card.style.display = match ? '' : 'none';
                if (match) shown++;
            }

            countEl.textContent = q === '' ? `${shown} Container` : `${shown} Treffer`;
        }

        input.addEventListener('input', applyFilter);

        clearBtn.addEventListener('click', () => {
            input.value = '';
            input.focus();
            applyFilter();
        });

        // Enter verhindert evtl. Form-Submit (falls du später mal in Form packst)
        input.addEventListener('keydown', (e) => {
            if (e.key === 'Enter') e.preventDefault();
        });

        // initial
        applyFilter();
    })();
</script>
<script>
(function(){
  const BASE = <?= $baseLoc ? json_encode($baseLoc, JSON_UNESCAPED_UNICODE|JSON_UNESCAPED_SLASHES) : 'null' ?>;
  if (!BASE) return;

  // ===== 1:1 aus map.php (nur Koordinatenberechnung) =====
  const worldLeftX   = 615000;
  const worldRightX  = -898000;
  const worldTopY    = 590000;
  const worldBottomY = -895000;

  function worldToPixelX(x, mapWidth) {
    return ((x - worldLeftX) / (worldRightX - worldLeftX)) * mapWidth;
  }
  function worldToPixelY(y, mapHeight) {
    return ((y - worldTopY) / (worldBottomY - worldTopY)) * mapHeight;
  }

  const canvas = document.getElementById('baseMiniMapCanvas');
  if (!canvas) return;
  const ctx = canvas.getContext('2d');
  const w = canvas.width, h = canvas.height;

  let px = worldToPixelX(Number(BASE.x), w);
  let py = worldToPixelY(Number(BASE.y), h);

  // Debug: falls außerhalb, clampen (damit du wenigstens was siehst)
  const out = (px < 0 || px > w || py < 0 || py > h);
  const pxRaw = px, pyRaw = py;

  px = Math.max(0, Math.min(w, px));
  py = Math.max(0, Math.min(h, py));

  ctx.clearRect(0,0,w,h);

  // Marker
  ctx.beginPath();
  ctx.arc(px, py, 10, 0, Math.PI * 2);
  ctx.strokeStyle = 'rgba(255,255,255,0.95)';
  ctx.lineWidth = 2;
  ctx.stroke();

  ctx.beginPath();
  ctx.arc(px, py, 5, 0, Math.PI * 2);
  ctx.fillStyle = 'rgba(239,68,68,0.95)';
  ctx.fill();
  ctx.strokeStyle = 'rgba(0,0,0,0.55)';
  ctx.lineWidth = 2;
  ctx.stroke();

  // Debug-Text im Canvas (oben links)
  ctx.font = '12px consolas, system-ui, sans-serif';
  ctx.fillStyle = 'rgba(255,255,255,0.9)';
  ctx.fillText(`Base world: X=${BASE.x} Y=${BASE.y}`, 10, 18);
  ctx.fillText(`Pixel: x=${pxRaw.toFixed(1)} y=${pyRaw.toFixed(1)}${out ? ' (OUTSIDE)' : ''}`, 10, 34);

  // Wenn OUTSIDE: zusätzlich eine Warnbox
  if (out) {
    ctx.fillStyle = 'rgba(239,68,68,0.85)';
    ctx.fillText(`⚠ Bounds passen nicht / Koords außerhalb`, 10, 50);
  }
})();
</script>
