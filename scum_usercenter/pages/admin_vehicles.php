<?php

declare(strict_types=1);

// Admin-Schutz (falls jemand die Datei direkt aufruft)
if (empty($_SESSION['isAdmin'])) {
    http_response_code(403);
    exit('Kein Zugriff.');
}

require_once __DIR__ . '/../functions/scum_vehicle_function.php';
require_once __DIR__ . '/../functions/scum_user_function.php';

$steamId = (string)($_SESSION['steamid'] ?? '');
$me = $steamId !== '' ? scum_get_user_profile_by_steamid($steamId) : null;
$myUpid = (int)($me['id'] ?? 0);

$vehOnlyEmpty = !empty($_GET['only_empty']);
$vehQ = (string)($_GET['q'] ?? '');

$vehicles = scum_admin_list_all_vehicles($vehOnlyEmpty, $vehQ);
$vehType = (string)($_GET['veh_type'] ?? '');
$vehType = trim($vehType);

if ($vehType !== '') {
    $vehicles = array_values(array_filter($vehicles, function ($v) use ($vehType) {
        return (string)($v['vehicle_name'] ?? '') === $vehType;
    }));
}
?>

<h1>Admin – Fahrzeuge</h1>

<form method="get" style="display:flex; gap:10px; align-items:center; margin: 12px 0;">
    <input type="hidden" name="page" value="admin">
    <input type="hidden" name="tab" value="vehicles">
    <?php
    // Dropdown-Liste serverseitig aus $vehicles bauen (nur aktuelle Trefferliste)
    $vehNames = [];
    foreach ($vehicles as $v) {
        $n = trim((string)($v['vehicle_name'] ?? ''));
        if ($n !== '') $vehNames[$n] = true;
    }
    $vehNames = array_keys($vehNames);
    natcasesort($vehNames);

    $vehType = (string)($_GET['veh_type'] ?? '');
    ?>
    <?php
    $vehType = trim((string)($_GET['veh_type'] ?? ''));

    // WICHTIG: Counts immer gesamt (ohne Filter)
    $typeCounts = scum_admin_vehicle_type_counts(); // [name => count]
    $totalAll = array_sum($typeCounts);
    ?>
    <select class="scum-input" name="veh_type" id="vehTypeSelect" style="min-width:260px;">
        <option value="">Alle Fahrzeuge (<?= (int)$totalAll ?>)</option>
        <?php foreach ($typeCounts as $name => $cnt): ?>
            <option value="<?= htmlspecialchars($name, ENT_QUOTES) ?>" <?= $vehType === $name ? 'selected' : '' ?>>
                <?= htmlspecialchars($name) ?> — <?= (int)$cnt ?>
            </option>
        <?php endforeach; ?>
    </select>

    <input
        class="scum-input"
        type="text"
        name="q"
        value="<?= htmlspecialchars($vehQ, ENT_QUOTES) ?>"
        placeholder="Suche: Fahrzeug, Entity-ID, Owner..."
        style="min-width:320px;">

    <label class="scum-checkbox" style="display:flex; gap:8px; align-items:center;">
        <input type="checkbox" name="only_empty" value="1" <?= $vehOnlyEmpty ? 'checked' : '' ?>>
        Nur leere Fahrzeuge
    </label>

    <button class="scum-btn" type="submit">Filtern</button>
    <a class="scum-btn scum-btn-ghost" href="index.php?page=admin&tab=vehicles">Reset</a>

    <div style="margin-left:auto; opacity:.8;">
        Treffer: <b><?= (int)count($vehicles) ?></b>
    </div>
</form>

<div class="admin-table">
    <div class="admin-table-head sortable" style="grid-template-columns: 1.3fr 1fr 1.3fr .6fr .7fr .9fr;">
        <span data-col="vehicle">Fahrzeug</span>
        <span data-col="entity">Entity-ID</span>
        <span data-col="owner">Owner</span>
        <span data-col="empty">Leer?</span>
        <span data-col="mine">Meins?</span>
        <span data-col="last">Last Access</span>
    </div>


    <?php if (empty($vehicles)): ?>
        <div class="admin-table-row muted" style="grid-template-columns: 1.3fr 1fr 1.3fr .6fr .7fr .9fr;">
            <span>—</span><span>Keine Fahrzeuge gefunden</span><span>—</span><span>—</span><span>—</span><span>—</span>
        </div>
    <?php else: ?>
        <?php foreach ($vehicles as $v): ?>
            <?php
            $isEmpty = !empty($v['is_empty']);
            $ownerUpid = (int)($v['owner_user_profile_id'] ?? 0);
            $isMine = ($myUpid > 0 && $ownerUpid > 0 && $ownerUpid === $myUpid);
            ?>
            <div class="admin-table-row"
                data-vehicle="<?= htmlspecialchars(strtolower($v['vehicle_name'])) ?>"
                data-entity="<?= htmlspecialchars($v['vehicle_entity_id']) ?>"
                data-owner="<?= htmlspecialchars(strtolower($v['owner_name'] ?? '')) ?>"
                data-empty="<?= $isEmpty ? 1 : 0 ?>"
                data-mine="<?= $isMine ? 1 : 0 ?>"
                data-last="<?= htmlspecialchars($v['last_access']) ?>"
                style="grid-template-columns: 1.3fr 1fr 1.3fr .6fr .7fr .9fr;">
                <span><?= htmlspecialchars((string)$v['vehicle_name']) ?></span>

                <span
                    class="copy-id"
                    data-copy="<?= htmlspecialchars('#DestroyVehicle ' . (string)$v['vehicle_entity_id'], ENT_QUOTES) ?>"
                    title="Klicken zum Kopieren"
                    style="
    cursor:pointer;
    user-select:none;
    font-family: ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, 'Liberation Mono', 'Courier New', monospace;
  ">
                    <?= htmlspecialchars((string)$v['vehicle_entity_id']) ?>
                </span>


                <span>
                    <?php if ($isEmpty): ?>
                        <span class="muted">—</span>
                    <?php else: ?>
                        <?= htmlspecialchars((string)($v['owner_name'] ?? 'Unbekannt')) ?>
                        <span class="muted">(UPID <?= (int)$ownerUpid ?>)</span>
                    <?php endif; ?>
                </span>

                <span><?= $isEmpty ? 'JA' : 'NEIN' ?></span>
                <span><?= $isMine ? 'JA' : '—' ?></span>

                <span class="muted"><?= htmlspecialchars((string)$v['last_access']) ?></span>
            </div>
        <?php endforeach; ?>
    <?php endif; ?>
</div>

<div class="scum-slot muted" style="margin-top:10px;">
    „Leer“ = im Vehicle-XML wurde keine owningUserProfileId gefunden.
</div>
<script>
    (function() {
        function flash(el, ok) {
            const old = el.textContent;
            el.textContent = ok ? '✓ kopiert' : '✕ Fehler';
            el.style.opacity = '0.85';
            setTimeout(() => {
                el.textContent = old;
                el.style.opacity = '';
            }, 900);
        }

        async function copyText(text) {
            // Modern (HTTPS / localhost)
            if (navigator.clipboard && window.isSecureContext) {
                await navigator.clipboard.writeText(text);
                return true;
            }

            // Fallback (HTTP)
            const ta = document.createElement('textarea');
            ta.value = text;
            ta.setAttribute('readonly', '');
            ta.style.position = 'fixed';
            ta.style.left = '-9999px';
            ta.style.top = '-9999px';
            document.body.appendChild(ta);
            ta.select();
            try {
                const ok = document.execCommand('copy');
                document.body.removeChild(ta);
                return ok;
            } catch (e) {
                document.body.removeChild(ta);
                return false;
            }
        }

        document.addEventListener('click', async (ev) => {
            const el = ev.target.closest('.copy-id');
            if (!el) return;

            const id = el.getAttribute('data-copy') || '';
            if (!id) return;

            const ok = await copyText(id);
            flash(el, ok);
        });
    })();
</script>
<script>
    (function() {
        const head = document.querySelector('.admin-table-head.sortable');
        if (!head) return;

        let currentCol = null;
        let currentDir = 1; // 1 = ASC, -1 = DESC

        const rowsContainer = head.parentElement;
        const rows = () => Array.from(rowsContainer.querySelectorAll('.admin-table-row'));

        function parseValue(row, col) {
            const v = row.dataset[col] ?? '';

            // Zahlen
            if (col === 'entity' || col === 'empty' || col === 'mine') {
                return Number(v) || 0;
            }

            // Datum: dd.mm.YYYY HH:ii
            if (col === 'last') {
                const m = v.match(/(\d{2})\.(\d{2})\.(\d{4})\s+(\d{2}):(\d{2})/);
                if (!m) return 0;
                return new Date(`${m[3]}-${m[2]}-${m[1]}T${m[4]}:${m[5]}:00`).getTime();
            }

            // Text
            return v.toString();
        }

        head.querySelectorAll('span[data-col]').forEach(span => {
            span.style.cursor = 'pointer';

            span.addEventListener('click', () => {
                const col = span.dataset.col;

                // Richtung umschalten
                if (currentCol === col) {
                    currentDir *= -1;
                } else {
                    currentCol = col;
                    currentDir = 1;
                }

                // Pfeile resetten
                head.querySelectorAll('span').forEach(s => {
                    s.textContent = s.textContent.replace(/[▲▼]\s*$/, '');
                });

                span.textContent += currentDir === 1 ? ' ▲' : ' ▼';

                const sorted = rows().sort((a, b) => {
                    const av = parseValue(a, col);
                    const bv = parseValue(b, col);

                    if (av < bv) return -1 * currentDir;
                    if (av > bv) return 1 * currentDir;
                    return 0;
                });

                sorted.forEach(r => rowsContainer.appendChild(r));
            });
        });
    })();
</script>
<script>
    document.addEventListener('change', (e) => {
        if (e.target && e.target.id === 'vehTypeSelect') {
            e.target.form.submit();
        }
    });
</script>