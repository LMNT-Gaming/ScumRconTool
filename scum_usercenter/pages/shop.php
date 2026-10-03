<?php
//shop.php
if (empty($_SESSION['steamid'])) {
    http_response_code(401);
    exit('Bitte einloggen.');
}

require_once __DIR__ . '/../functions/shop_function.php';
require_once __DIR__ . '/../functions/shop_request_function.php';
require_once __DIR__ . '/../functions/shop_category_function.php';
require_once __DIR__ . '/../functions/env_function.php';
load_env(__DIR__ . '/../private/.env'); // nur hier einmal
// ===== Shop-Lock (nur anschauen erlaubt) =====
$shopLockedUntil = (string)(getenv('SHOP_LOCK_UNTIL') ?: ''); // z.B. "2025-12-24 18:00:00"
$shopLocked = false;

if ($shopLockedUntil !== '') {
    $shopLocked = time() < strtotime($shopLockedUntil);
}
$shopLocked = time() < strtotime($shopLockedUntil);

function shop_lock_message(string $until): string
{
    return 'Der Shop ist bis ' . date('d.m.Y H:i', strtotime($until)) . ' gesperrt. Du kannst weiterhin stöbern, aber aktuell keine Anträge/Käufe erstellen.';
}

// ===== PRG + Flash (damit Refresh keine POSTs wiederholt) =====
function flash_set(string $key, string $msg): void
{
    $_SESSION['flash'][$key] = $msg;
}
function flash_get(string $key): string
{
    $v = (string)($_SESSION['flash'][$key] ?? '');
    unset($_SESSION['flash'][$key]);
    return $v;
}
function redirect_shop(array $params = []): void
{
    $qs = http_build_query(array_merge(['page' => 'shop'], $params));
    header('Location: index.php?' . $qs, true, 303); // 303 -> GET nach POST
    exit;
}


function h($s)
{
    return htmlspecialchars((string)$s, ENT_QUOTES, 'UTF-8');
}

$steamid = (string)$_SESSION['steamid'];
$notice = flash_get('notice');
$error  = flash_get('error');


$action = $_POST['action'] ?? '';

/** 1) Antrag abbrechen (muss vor buy passieren) */
if ($action === 'cancel_request') {
    $reqId = (int)($_POST['request_id'] ?? 0);

    if ($reqId > 0) {
        $ok = shop_cancel_request($reqId, $steamid);
        if ($ok) flash_set('notice', 'Antrag wurde abgebrochen.');
        else     flash_set('error', 'Antrag konnte nicht abgebrochen werden (evtl. nicht mehr OFFEN).');
    } else {
        flash_set('error', 'Ungültige Request-ID.');
    }

    redirect_shop(['t' => time()]);
}

if ($action === 'delete_request') {
    $reqId = (int)($_POST['request_id'] ?? 0);

    if ($reqId > 0) {
        $ok = shop_user_delete_request($reqId, $steamid);
        if ($ok) flash_set('notice', 'Antrag wurde aus deiner Liste entfernt.');
        else     flash_set('error', 'Antrag kann nicht gelöscht werden (nur ABGEBROCHEN oder Approved).');
    } else {
        flash_set('error', 'Ungültige Request-ID.');
    }

    redirect_shop(['t' => time()]);
}


/** 2) Kauf/Antrag erstellen */
if ($action === 'buy' && $shopLocked) {
    flash_set('error', shop_lock_message($shopLockedUntil));
    redirect_shop(['t' => time()]);
}

if ($action === 'buy') {
    $itemId   = (int)($_POST['item_id'] ?? 0);
    $currency = strtoupper((string)($_POST['currency'] ?? ''));

    $xRaw = $_POST['coord_x'] ?? '';
    $yRaw = $_POST['coord_y'] ?? '';

    // Item laden
    $item = shop_get_item($itemId);
    if (!$item) {
        $error = 'Item nicht gefunden.';
    } else {
        // Preis bestimmen (auch für VOUCHER!)
        $prices = $item['prices'] ?? [];
        if (!isset($prices[$currency])) {
            $error = 'Ungültiges Zahlmittel.';
        } else {
            $price = (int)$prices[$currency];

            // Koordinaten prüfen (falls nötig)
            $needsCoords = (int)($item['requires_coordinates'] ?? 0) === 1;
            $x = null;
            $y = null;

            if ($needsCoords) {
                if ($xRaw === '' || $yRaw === '' || !is_numeric($xRaw) || !is_numeric($yRaw)) {
                    $error = 'Koordinaten sind Pflicht. Bitte Position auf der Karte wählen.';
                } else {
                    $x = (int)$xRaw;
                    $y = (int)$yRaw;

                    if ($x < -900000 || $x > 620000 || $y < -900000 || $y > 620000) {
                        $error = 'Ungültige Koordinaten. Bitte erneut wählen.';
                    }
                }
            }

            if ($error === '') {
                if ($currency === 'VOUCHER') {
                    // Direkt genehmigen + abbuchen + ggf. inventory gutschreiben
                    $res = shop_buy_with_voucher_instant($steamid, $itemId, $price, $x, $y);

                    if (!empty($res['ok'])) $notice = (string)$res['msg'];
                    else $error = (string)($res['msg'] ?? 'Kauf fehlgeschlagen.');
                } else {
                    // Normal: Antrag erstellen
                    shop_create_request($steamid, $itemId, $currency, $price, $x, $y);
                    $notice = 'Antrag erstellt. Ein Admin wird das ingame bearbeiten.';
                }
            }
        }
    }
    if ($notice !== '') flash_set('notice', $notice);
    if ($error  !== '') flash_set('error',  $error);
    redirect_shop(['t' => time()]);
}



$items = shop_list_items(); // liefert prices_text + prices[] (JOIN-Version)
$myRequests = shop_list_requests_for_user($steamid, 20);
?>
<header>
    <style>
        .map-inner {
            position: relative;
            width: 100%;
        }

        #mapPickerImage {
            display: block;
            width: 100%;
            height: auto;
        }

        #mapPickerCanvas.map-overlay {
            position: absolute;
            left: 0;
            top: 0;
            width: 100%;
            height: 100%;
            z-index: 5;
            pointer-events: auto;
        }

        /* optional: wenn irgendwo pointer-events none gesetzt ist */
        .map-overlay {
            pointer-events: auto !important;
        }

        /* ===== Shop Toolbar ===== */
        .shop-toolbar {
            display: flex;
            gap: 10px;
            align-items: center;
            flex-wrap: wrap;
            margin-top: 10px;
        }

        .shop-filter {
            display: flex;
            gap: 8px;
            flex-wrap: wrap;
        }

        .shop-filter .subtab {
            padding: 7px 10px;
            font-size: 11px;
        }

        .shop-filter .subtab.active {
            background: rgba(255, 255, 255, 0.95);
            color: #000;
        }

        .shop-search {
            height: 34px;
            border-radius: 12px;
            border: 1px solid rgba(255, 255, 255, 0.12);
            background: rgba(0, 0, 0, 0.55);
            color: #fff;
            padding: 0 10px;
            min-width: 220px;
            flex: 1 1 260px;
        }

        /* ===== Shop Cards: einheitlich ===== */
        .shop-card {
            display: flex;
            /* wichtig */
            flex-direction: column;
            /* wichtig */
            min-height: 290px;
            /* Basishöhe (anpassen wenn du willst) */
        }

        .shop-card-top {
            flex: 1 1 auto;
            /* Top darf wachsen */
            align-content: start;
        }

        /* Bild immer gleich */
        .shop-img {
            height: 110px;
            /* einheitlicher */
        }

        /* Meta Layout stabilisieren */
        .shop-meta {
            display: flex;
            flex-direction: column;
            min-width: 0;
        }

        /* Beschreibung: Platz für ~200 Zeichen */
        .shop-desc {
            margin-top: 6px;
            line-height: 1.35;

            /* 7 Zeilen sind i.d.R. ~180–240 Zeichen je nach Wortlänge */
            display: -webkit-box;
            -webkit-line-clamp: 7;
            -webkit-box-orient: vertical;
            overflow: hidden;

            min-height: calc(7 * 1.35em);
            /* sorgt für gleich hohe Top-Bereiche */
        }




        /* Bottom immer unten */
        .shop-card-bottom {
            margin-top: auto;
            /* klebt unten */
        }

        /* Actions immer gleiche Stelle */
        .shop-actions {
            margin-top: 12px;
            display: flex;
            justify-content: flex-end;
            gap: 8px;
        }

        /* Optional: "Position wählen" Button + Preview sauber stapeln */
        .shop-form .subtab {
            width: 100%;
        }

        .shop-form #mapPickerConfirm.subtab {
            width: auto;
        }

        @media (max-width: 420px) {
            .shop-grid {
                grid-template-columns: 1fr;
            }
        }

        .shop-catrow {
            display: flex;
            align-items: center;
            gap: 8px;
            margin-top: 10px;
            /* sauber unter der Beschreibung */
            padding-top: 8px;
            border-top: 1px solid rgba(255, 255, 255, 0.08);
        }

        .shop-catlabel {
            font-size: 11px;
            font-weight: 900;
            letter-spacing: 1.1px;
            text-transform: uppercase;
            opacity: .75;
            white-space: nowrap;
        }

        /* Card Header (Kategorie) */
        .shop-card-head {
            padding: 9px 12px;
            border-bottom: 1px solid rgba(255, 255, 255, 0.08);
            background: rgba(0, 0, 0, 0.35);
            display: flex;
            align-items: center;
            justify-content: space-between;
        }

        /* Titel im Header */
        .shop-cat-title {
            font-weight: 900;
            letter-spacing: 1.2px;
            text-transform: uppercase;
            font-size: 12px;
        }

        /* Standard-Farbe (Fallback) */
        .shop-card {
            --cat-accent: rgba(255, 255, 255, 0.35);
        }

        .shop-card-head {
            border-left: 4px solid var(--cat-accent);
        }

        .main-card {
            margin-top: 6px;
        }

        button:disabled,
        .subtab:disabled {
            opacity: .45;
            cursor: not-allowed;
            filter: grayscale(35%);
        }
    </style>
</header>
<div class="main-card">
    <div class="center-head">
        <div class="userblock">
            <h1>Shop</h1>
        </div>
    </div>
    <div class="contentspace">
        <?php if ($notice): ?>
            <div class="scum-slot" style="margin-top:12px; border-color: rgba(255,255,255,0.25);">
                ✅ <?= h($notice) ?>
            </div>
        <?php endif; ?>

        <?php if ($error): ?>
            <div class="scum-slot" style="margin-top:12px; border-color: rgba(255,80,80,0.65);">
                ❌ <?= h($error) ?>
            </div>
        <?php endif; ?>
        <?php if ($shopLocked): ?>
            <div class="scum-slot" style="margin-top:12px; border-color: rgba(255,180,0,0.55);">
                🔒 <?= h(shop_lock_message($shopLockedUntil)) ?>
            </div>
        <?php endif; ?>
        <?php
        function statusLabel(string $s): string
        {
            return match ($s) {
                'pending'   => 'OFFEN',
                'approved'  => 'APPROVED',
                'delivered' => 'DELIVERED',
                'rejected'  => 'REJECTED',
                'cancelled' => 'ABGEBROCHEN',
                default     => strtoupper($s),
            };
        }
        ?>

        <h2 style="margin-top:18px;">Meine Anträge</h2>

        <div class="admin-table">
            <div class="admin-table-head" style="grid-template-columns: 70px 1fr 0.9fr 0.8fr 0.8fr;">
                <span>ID</span><span>Item</span><span>Zahlung</span><span>Status</span><span>Koords</span>
            </div>

            <?php if (empty($myRequests)): ?>
                <div class="admin-table-row muted" style="grid-template-columns: 1fr;">
                    <span>Keine Anträge vorhanden.</span>
                </div>
            <?php endif; ?>

            <?php foreach ($myRequests as $r): ?>
                <?php
                $pay = strtoupper((string)$r['currency']) . ': ' . (int)$r['price'];
                $coords = '—';
                if (!empty($r['coord_x']) || !empty($r['coord_y'])) {
                    $coords = 'X: ' . (int)$r['coord_x'] . ' / Y: ' . (int)$r['coord_y'];
                }
                $st = (string)$r['status'];
                $note = trim((string)($r['admin_note'] ?? ''));
                ?>

                <div class="admin-table-row" style="grid-template-columns: 70px 1fr 0.9fr 0.8fr 0.8fr;">
                    <span><?= (int)$r['id'] ?></span>
                    <span>
                        <?= h($r['item_name']) ?>
                        <?php if (!empty($r['requires_coordinates'])): ?>
                            <span class="muted" style="margin-left:6px;">📍</span>
                        <?php endif; ?>
                    </span>
                    <span><?= h($pay) ?></span>
                    <span>
                        <span class="status-pill status-<?= h($st) ?>">
                            <?= h(statusLabel($st)) ?>
                        </span>

                        <?php if ($st === 'pending'): ?>
                            <form method="post" style="display:inline;">
                                <input type="hidden" name="action" value="cancel_request">
                                <input type="hidden" name="request_id" value="<?= (int)$r['id'] ?>">
                                <button class="subtab danger" type="submit"
                                    onclick="return confirm('Antrag wirklich abbrechen?');">
                                    Abbrechen
                                </button>
                            </form>
                        <?php endif; ?>
                        <?php if ($st === 'cancelled' || $st === 'approved' || $st === 'rejected'): ?>
                            <form method="post" style="display:inline;">
                                <input type="hidden" name="action" value="delete_request">
                                <input type="hidden" name="request_id" value="<?= (int)$r['id'] ?>">
                                <button class="subtab danger" type="submit"
                                    onclick="return confirm('Antrag wirklich löschen (aus deiner Liste entfernen)?');">
                                    Löschen
                                </button>
                            </form>
                        <?php endif; ?>
                    </span>
                    <span><?= h($coords) ?></span>
                </div>

                <?php if ($note !== ''): ?>
                    <div class="req-note">
                        <div class="req-note-title">Admin Note</div>
                        <div class="req-note-text"><?= nl2br(h($note)) ?></div>
                    </div>
                <?php endif; ?>

            <?php endforeach; ?>
        </div>
        <p class="muted">Wähle ein Item und ein Zahlmittel. Basiszonen benötigen Koordinaten.</p>
        <p class="muted">Gutscheine können über Servervotes erzeugt werden, beim kauf wird direkt abgezogen.</p>

        <div class="shop-toolbar">
            <div class="shop-filter" id="shopCategoryFilter">
                <!-- Buttons werden per JS erzeugt -->
            </div>
            <input class="shop-search" id="shopSearch" type="text" placeholder="Suche (Name/Beschreibung) …">
        </div>
        <div class="shop-grid">
            <?php foreach ($items as $it): ?>
                <?php
                $prices      = $it['prices'] ?? [];
                $needsCoords = !empty($it['requires_coordinates']);
                $img         = !empty($it['image']) ? ('assets/img/shop/' . $it['image']) : '';

                $cat     = trim((string)($it['category_name'] ?? ''));
                $catKey  = trim((string)($it['category_slug'] ?? ''));
                if ($catKey === '') $catKey = 'uncategorized';

                $catColor = trim((string)($it['category_color'] ?? ''));
                if ($catColor === '') $catColor = 'rgba(255,255,255,0.35)';

                $searchBlob = strtolower(($it['name'] ?? '') . ' ' . ($it['description'] ?? ''));
                ?>

                <div class="shop-card"
                    data-category="<?= h($catKey) ?>"
                    data-cat="<?= h($catKey) ?>"
                    data-search="<?= h($searchBlob) ?>"
                    style="--cat-accent: <?= h($catColor) ?>;">

                    <div class="shop-card-head">
                        <span class="shop-cat-title"><?= $cat !== '' ? h($cat) : 'Ohne Kategorie' ?></span>
                    </div>

                    <div class="shop-card-top">
                        <div class="shop-img">
                            <?php if ($img): ?>
                                <img src="<?= h($img) ?>" alt="">
                            <?php else: ?>
                                <div class="shop-img-placeholder">NO IMAGE</div>
                            <?php endif; ?>
                        </div>

                        <div class="shop-meta">
                            <div class="shop-title"><?= h($it['name']) ?></div>
                            <div class="shop-desc muted"><?= nl2br(h($it['description'] ?? '')) ?></div>
                        </div>
                    </div>

                    <div class="shop-card-bottom">
                        <form method="post" class="shop-form" data-needs-coords="<?= $needsCoords ? '1' : '0' ?>">
                            <input type="hidden" name="action" value="buy">
                            <input type="hidden" name="item_id" value="<?= (int)$it['id'] ?>">

                            <div class="shop-prices">
                                <?php foreach (['SCUM_DOLLAR' => 'SCUM-DOLLAR', 'GOLD' => 'GOLD', 'VOUCHER' => 'GUTSCHEIN'] as $cur => $label): ?>
                                    <?php if (!empty($prices[$cur])): ?>
                                        <label class="pay-pill">
                                            <input type="radio" name="currency" value="<?= h($cur) ?>" required <?= $shopLocked ? 'disabled' : '' ?>>
                                            <span><?= h($label) ?>: <?= (int)$prices[$cur] ?></span>
                                        </label>
                                    <?php endif; ?>
                                <?php endforeach; ?>
                            </div>

                            <input type="hidden" name="coord_x" id="coord_x_<?= (int)$it['id'] ?>" value="">
                            <input type="hidden" name="coord_y" id="coord_y_<?= (int)$it['id'] ?>" value="">

                            <?php if ($needsCoords): ?>
                                <button type="button" class="subtab" onclick="openMapPickerForItem(<?= (int)$it['id'] ?>)" <?= $shopLocked ? 'disabled' : '' ?>>
                                    📍 Position auf Karte wählen
                                </button>
                                <div class="muted" id="coord_preview_<?= (int)$it['id'] ?>" style="font-size:11px;">
                                    Keine Position gewählt
                                </div>
                            <?php endif; ?>

                            <div class="shop-actions">
                                <button class="subtab active" type="submit" <?= $shopLocked ? 'disabled title="Shop aktuell gesperrt"' : '' ?>>
                                    Kaufen (Antrag)
                                </button>
                            </div>
                        </form>
                    </div>

                </div>
            <?php endforeach; ?>

        </div>
    </div>
</div>
<div id="mapPickerModal" class="map-modal hidden">
    <div class="map-modal-inner">
        <div class="map-modal-head">
            <span>Position auf SCUM Karte wählen</span>
            <button type="button" class="subtab" onclick="closeMapPicker()">✕</button>
        </div>

        <div class="map-inner">
            <img id="mapPickerImage" src="assets/img/scum_map.jpg" alt="SCUM Map">
            <canvas id="mapPickerCanvas" class="map-overlay"></canvas>
        </div>

        <div class="map-modal-footer">
            <div id="mapPickerPreview" class="muted">X: — / Y: —</div>
            <button class="subtab active" id="mapPickerConfirm" type="button" disabled>Übernehmen</button>
        </div>
    </div>
</div>

<script>
    document.addEventListener("DOMContentLoaded", () => {
        document.querySelectorAll(".shop-form").forEach(form => {
            form.addEventListener("submit", (e) => {
                const needs = form.dataset.needsCoords === "1";
                if (!needs) return;

                const x = form.querySelector('input[name="coord_x"]')?.value?.trim();
                const y = form.querySelector('input[name="coord_y"]')?.value?.trim();

                if (!x || !y) {
                    e.preventDefault();
                    alert("Bitte Position auf der Karte wählen (Koordinaten sind Pflicht).");
                }
            });
        });
    });
</script>
<script>
    // bestehendes coords-wrap show/hide
    document.addEventListener("DOMContentLoaded", () => {
        document.querySelectorAll(".shop-form").forEach(form => {
            const needs = form.dataset.needsCoords === "1";
            const coords = form.querySelector(".coords-wrap");
            if (needs && coords) coords.style.display = "grid";
        });
    });

    // ===== Map Picker =====
    const worldLeftX = 615000;
    const worldRightX = -898000;
    const worldTopY = 590000;
    const worldBottomY = -895000;

    const modal = document.getElementById('mapPickerModal');
    const img = document.getElementById('mapPickerImage');
    const canvas = document.getElementById('mapPickerCanvas');
    const ctx = canvas.getContext('2d');
    const preview = document.getElementById('mapPickerPreview');
    const confirmBtn = document.getElementById('mapPickerConfirm');

    let currentItemId = null;
    let selectedX = null;
    let selectedY = null;

    function pixelToWorldX(px, mapWidth) {
        return worldLeftX + (px / mapWidth) * (worldRightX - worldLeftX);
    }

    function pixelToWorldY(py, mapHeight) {
        return worldTopY + (py / mapHeight) * (worldBottomY - worldTopY);
    }

    function resizeCanvasToImage() {
        const rect = img.getBoundingClientRect();
        if (rect.width < 2 || rect.height < 2) return; // noch nicht gelayoutet

        // Canvas-Pixelgröße (für sauberes Zeichnen)
        canvas.width = Math.round(rect.width);
        canvas.height = Math.round(rect.height);

        ctx.clearRect(0, 0, canvas.width, canvas.height);
    }


    function drawMarker(px, py) {
        ctx.clearRect(0, 0, canvas.width, canvas.height);
        ctx.beginPath();
        ctx.arc(px, py, 8, 0, Math.PI * 2);
        ctx.strokeStyle = 'rgba(255,255,255,0.95)';
        ctx.lineWidth = 2;
        ctx.stroke();
        ctx.beginPath();
        ctx.arc(px, py, 2, 0, Math.PI * 2);
        ctx.fillStyle = 'rgba(255,255,255,0.95)';
        ctx.fill();
    }

    function openMapPickerForItem(itemId) {
        currentItemId = itemId;
        selectedX = null;
        selectedY = null;
        preview.textContent = 'X: — / Y: —';
        confirmBtn.disabled = true;

        modal.classList.remove('hidden');

        const ready = () => {
            resizeCanvasToImage();
        };

        if (!img.complete) img.addEventListener('load', ready, {
            once: true
        });
        else ready();
    }

    function closeMapPicker() {
        modal.classList.add('hidden');
        currentItemId = null;
        ctx.clearRect(0, 0, canvas.width, canvas.height);
    }

    window.openMapPickerForItem = openMapPickerForItem;
    window.closeMapPicker = closeMapPicker;

    canvas.addEventListener('click', (e) => {
        const rect = canvas.getBoundingClientRect();
        const px = (e.clientX - rect.left) * (canvas.width / rect.width);
        const py = (e.clientY - rect.top) * (canvas.height / rect.height);

        selectedX = Math.round(pixelToWorldX(px, canvas.width));
        selectedY = Math.round(pixelToWorldY(py, canvas.height));

        preview.textContent = `X: ${selectedX} / Y: ${selectedY}`;
        confirmBtn.disabled = false;

        drawMarker(px, py);
    });

    confirmBtn.addEventListener('click', () => {
        if (currentItemId === null || selectedX === null) return;

        const xEl = document.getElementById(`coord_x_${currentItemId}`);
        const yEl = document.getElementById(`coord_y_${currentItemId}`);
        if (xEl) xEl.value = selectedX;
        if (yEl) yEl.value = selectedY;

        const p = document.getElementById(`coord_preview_${currentItemId}`);
        if (p) p.textContent = `Gewählt: X ${selectedX} / Y ${selectedY}`;

        closeMapPicker();
    });

    window.addEventListener('resize', () => {
        if (!modal.classList.contains('hidden')) resizeCanvasToImage();
    });
</script>
<script>
    document.addEventListener("DOMContentLoaded", () => {
        const cards = Array.from(document.querySelectorAll(".shop-card"));
        const filterHost = document.getElementById("shopCategoryFilter");
        const searchInput = document.getElementById("shopSearch");

        if (!filterHost || cards.length === 0) return;

        // Kategorien aus DOM sammeln
        const cats = new Map(); // key -> label
        cards.forEach(c => {
            const key = (c.dataset.category || "uncategorized").trim();
            if (!cats.has(key)) {
                const label = key === "uncategorized" ? "Ohne Kategorie" : key.toUpperCase();
                cats.set(key, label);
            }
        });

        // Filter Buttons rendern
        const makeBtn = (key, label) => {
            const b = document.createElement("button");
            b.type = "button";
            b.className = "subtab";
            b.dataset.filter = key;
            b.textContent = label;
            return b;
        };

        filterHost.appendChild(makeBtn("all", "Alle"));
        Array.from(cats.entries())
            .sort((a, b) => a[0].localeCompare(b[0]))
            .forEach(([k, label]) => filterHost.appendChild(makeBtn(k, label)));

        let activeCat = "all";
        let q = "";

        function apply() {
            const qNorm = q.trim().toLowerCase();

            cards.forEach(c => {
                const catOk = (activeCat === "all") || ((c.dataset.category || "") === activeCat);
                const hay = (c.dataset.search || "");
                const qOk = !qNorm || hay.includes(qNorm);

                c.style.display = (catOk && qOk) ? "" : "none";
            });
        }

        // Button Klicks
        filterHost.addEventListener("click", (e) => {
            const btn = e.target.closest("button[data-filter]");
            if (!btn) return;

            activeCat = btn.dataset.filter;

            filterHost.querySelectorAll("button.subtab").forEach(b => b.classList.remove("active"));
            btn.classList.add("active");

            apply();
        });

        // Default: "Alle" aktiv
        const first = filterHost.querySelector('button[data-filter="all"]');
        if (first) first.classList.add("active");

        // Suche
        if (searchInput) {
            searchInput.addEventListener("input", () => {
                q = searchInput.value || "";
                apply();
            });
        }

        apply();
    });
</script>