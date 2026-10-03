<?php
if (empty($_SESSION['isAdmin'])) { http_response_code(403); exit('Kein Zugriff.'); }

require_once __DIR__ . '/../functions/shop_request_function.php';
require_once __DIR__ . '/../functions/scum_user_function.php';

function h($s){ return htmlspecialchars((string)$s, ENT_QUOTES, 'UTF-8'); }

// 1) Status VOR ALLEM setzen
$status = (string)($_GET['status'] ?? 'pending');
$allowed = ['pending','approved','rejected']; // delivered entfernt
if (!in_array($status, $allowed, true)) $status = 'pending';

// 2) POST handling (vor Output)
if (($_POST['action'] ?? '') === 'set_status') {
    $id = (int)($_POST['id'] ?? 0);
    $newStatus = (string)($_POST['status'] ?? '');
    $note = trim((string)($_POST['admin_note'] ?? ''));

    shop_update_request_status($id, $newStatus, (string)$_SESSION['steamid'], $note);

    header('Location: index.php?page=admin&tab=requests&status=' . urlencode($status));
    exit;
}

// 3) Rows laden
$rows = shop_list_requests($status);

// 4) SteamIDs -> Namen (Batch)
$steamIds = [];
foreach ($rows as $r) $steamIds[] = (string)$r['steamid'];
$steamIds = array_values(array_unique($steamIds));
$nameMap = scum_get_names_by_steamids($steamIds);
?>

<h1>Admin – Anträge</h1>

<div style="display:flex; gap:8px; flex-wrap:wrap; margin: 10px 0 14px;">
  <a class="subtab <?= $status==='pending'?'active':'' ?>"  href="index.php?page=admin&tab=requests&status=pending">Offen</a>
  <a class="subtab <?= $status==='approved'?'active':'' ?>" href="index.php?page=admin&tab=requests&status=approved">Approved</a>
  <a class="subtab <?= $status==='rejected'?'active':'' ?>" href="index.php?page=admin&tab=requests&status=rejected">Rejected</a>
</div>

<div class="admin-table">
  <div class="admin-table-head" style="grid-template-columns: 70px 1.2fr 1fr 0.8fr 0.8fr 160px;">
    <span>ID</span><span>User</span><span>Item</span><span>Zahlung</span><span>Koords</span><span>Aktion</span>
  </div>

  <?php if (!$rows): ?>
    <div class="admin-table-row muted" style="grid-template-columns: 1fr;">
      <span>Keine Einträge.</span>
    </div>
  <?php endif; ?>

  <?php foreach ($rows as $r): ?>
    <?php
      $coords = '—';
      if (!empty($r['coord_x']) || !empty($r['coord_y'])) {
        $coords = 'X: ' . (int)$r['coord_x'] . ' / Y: ' . (int)$r['coord_y'];
      }
      $pay = strtoupper((string)$r['currency']) . ': ' . (int)$r['price'];

      $sid = (string)$r['steamid'];
      $pname = $nameMap[$sid] ?? 'Unbekannt';
    ?>

    <div class="admin-table-row" style="grid-template-columns: 70px 1.2fr 1fr 0.8fr 0.8fr 160px; align-items:center;">
      <span><?= (int)$r['id'] ?></span>

      <span>
        <strong><?= h($pname) ?></strong><br>
        <span class="muted" style="font-size:11px;"><?= h($sid) ?></span>
      </span>

      <span>
        <?= h($r['item_name']) ?>
        <?php if (!empty($r['requires_coordinates'])): ?>
          <span class="muted" style="margin-left:6px;">📍</span>
        <?php endif; ?>
      </span>

      <span><?= h($pay) ?></span>
      <span><?= h($coords) ?></span>

      <span style="display:flex; gap:6px; justify-content:flex-end;">
        <button class="subtab" type="button" onclick="openReq(<?= (int)$r['id'] ?>)">Bearbeiten</button>
      </span>
    </div>

    <div id="req-<?= (int)$r['id'] ?>" class="req-editor" style="display:none;">
      <form method="post" class="req-editor-inner">
        <input type="hidden" name="action" value="set_status">
        <input type="hidden" name="id" value="<?= (int)$r['id'] ?>">

        <div class="req-grid">
          <div>
            <div class="req-label">User</div>
            <div class="req-value">
              <strong><?= h($pname) ?></strong><br>
              <span class="muted" style="font-size:11px;"><?= h($sid) ?></span>
            </div>
          </div>
          <div>
            <div class="req-label">Item</div>
            <div class="req-value"><?= h($r['item_name']) ?></div>
          </div>
          <div>
            <div class="req-label">Zahlung</div>
            <div class="req-value"><?= h($pay) ?></div>
          </div>
          <div>
            <div class="req-label">Koordinaten</div>
            <div class="req-value"><?= h($coords) ?></div>
          </div>
        </div>

        <div style="margin-top:10px;">
          <div class="req-label">Admin Notiz</div>
          <textarea name="admin_note" rows="2" style="width:100%;"><?= h($r['admin_note'] ?? '') ?></textarea>
        </div>

        <div class="req-actions">
          <button class="subtab" name="status" value="pending"  type="submit">Pending</button>
          <button class="subtab active" name="status" value="approved" type="submit">Approve</button>
          <button class="subtab" name="status" value="rejected" type="submit">Reject</button>
          <button class="subtab" type="button" onclick="closeReq(<?= (int)$r['id'] ?>)">Schließen</button>
        </div>
      </form>
    </div>

  <?php endforeach; ?>
</div>

<script>
function openReq(id){
  const el = document.getElementById("req-" + id);
  if (el) el.style.display = "block";
}
function closeReq(id){
  const el = document.getElementById("req-" + id);
  if (el) el.style.display = "none";
}
</script>
