<?php
// Admin-Schutz
if (empty($_SESSION['isAdmin'])) {
  http_response_code(403);
  exit('Kein Zugriff.');
}

$adminTab = $_GET['tab'] ?? 'overview';
$allowedTabs = ['overview', 'news', 'vehicles'];


if (!in_array($adminTab, $allowedTabs, true)) {
  $adminTab = 'overview';
}

require_once __DIR__ . '/../functions/scum_db.php';

require_once __DIR__ . '/../functions/scum_vehicle_function.php';
require_once __DIR__ . '/../functions/scum_user_function.php'; // für "gehört mir?"

/**
 * Helper: prüft ob eine Spalte in einer SQLite-Tabelle existiert
 */
function sqlite_has_column(SQLite3 $db, string $table, string $column): bool
{
  $res = $db->query("PRAGMA table_info(" . SQLite3::escapeString($table) . ")");
  if (!$res) return false;
  while ($row = $res->fetchArray(SQLITE3_ASSOC)) {
    if (isset($row['name']) && $row['name'] === $column) return true;
  }
  return false;
}

/**
 * Helper: prüft ob eine SQLite-Tabelle existiert
 */
function sqlite_has_table(SQLite3 $db, string $table): bool
{
  $stmt = $db->prepare("SELECT name FROM sqlite_master WHERE type='table' AND name = :t LIMIT 1");
  $stmt->bindValue(':t', $table, SQLITE3_TEXT);
  $r = $stmt->execute();
  $row = $r ? $r->fetchArray(SQLITE3_ASSOC) : false;
  return is_array($row) && ($row['name'] ?? '') === $table;
}

$scumDb = getScumDb();

// 1) User-Anzahl aus SCUM.db
$userCount = 0;
$r = $scumDb->querySingle("SELECT COUNT(id) AS c FROM user_profile", true);
if (is_array($r) && isset($r['c'])) $userCount = (int)$r['c'];

// 3) Userliste: wir versuchen "steam id" + "name" + "last seen" dynamisch zu finden
//    (weil SCUM.db je nach Version andere Spaltennamen haben kann)
$nameCol = sqlite_has_column($scumDb, 'user_profile', 'name') ? 'name' : (sqlite_has_column($scumDb, 'user_profile', 'nickname') ? 'nickname' : null);

// SteamId Spalten (häufige Varianten)
$steamCol = null;
foreach (['steam_id', 'steamid', 'steamId', 'platform_user_id', 'user_id'] as $c) {
  if (sqlite_has_column($scumDb, 'user_profile', $c)) {
    $steamCol = $c;
    break;
  }
}

// LastSeen Spalten (häufige Varianten)
$lastSeenCol = null;
foreach (['last_seen', 'last_login', 'last_login_time', 'last_online', 'last_played', 'updated_at', 'last_logout'] as $c) {
  if (sqlite_has_column($scumDb, 'user_profile', $c)) {
    $lastSeenCol = $c;
    break;
  }
}

// Squad: versuchen wir nur, wenn die Tabellen existieren
$hasSquad = sqlite_has_table($scumDb, 'squad') && (sqlite_has_table($scumDb, 'squad_member') || sqlite_has_table($scumDb, 'squad_members'));

$squadMemberTable = null;
if ($hasSquad) {
  $squadMemberTable = sqlite_has_table($scumDb, 'squad_member') ? 'squad_member' : 'squad_members';
}

// Query bauen
$selectSteam = $steamCol ? "up.$steamCol AS steamid" : "CAST(up.id AS TEXT) AS steamid";
$selectName  = $nameCol  ? "up.$nameCol AS pname" : "('User #' || up.id) AS pname";
$selectLast  = $lastSeenCol ? "up.$lastSeenCol AS last_seen" : "NULL AS last_seen";
// --- Kuna/Gold robust: Tabellen & Spalten automatisch erkennen ---
$hasCurrencies = sqlite_has_table($scumDb, 'bank_account_registry_currencies');

// Balance-Spalte finden
$balCol = null;
if ($hasCurrencies) {
  foreach (['balance', 'amount', 'value', 'currency_amount'] as $c) {
    if (sqlite_has_column($scumDb, 'bank_account_registry_currencies', $c)) {
      $balCol = $c;
      break;
    }
  }
  if ($balCol === null) $balCol = 'balance'; // fallback (wird dann ggf. SQL-Fehler zeigen -> dann wissen wir es)
}

// Account-Mapping-Tabelle finden (falls vorhanden)
$acctTable = null;
foreach (['bank_account_registry', 'bank_account', 'bank_accounts'] as $t) {
  if (sqlite_has_table($scumDb, $t)) {
    $acctTable = $t;
    break;
  }
}

// Wenn es eine Account-Tabelle gibt: finde "id" und "user_profile_id"
$acctIdCol = null;
$acctUserCol = null;

if ($acctTable) {
  foreach (['id', 'bank_account_id', 'account_id'] as $c) {
    if (sqlite_has_column($scumDb, $acctTable, $c)) {
      $acctIdCol = $c;
      break;
    }
  }
  foreach (['user_profile_id', 'user_profile', 'profile_id', 'owner_profile_id'] as $c) {
    if (sqlite_has_column($scumDb, $acctTable, $c)) {
      $acctUserCol = $c;
      break;
    }
  }
}

// Join-Strategie:
// A) Wenn Account-Tabelle sauber erkannt -> currencies.bank_account_id -> account.id -> account.user_profile_id -> user_profile.id
// B) Sonst fallback -> currencies.bank_account_id == user_profile.id
$currencyJoinSql = '';
$selectCurrencies = "0 AS kuna, 0 AS gold";

if ($hasCurrencies) {
  $selectCurrencies = "
        COALESCE(k.$balCol, 0) AS kuna,
        COALESCE(g.$balCol, 0) AS gold
    ";

  if ($acctTable && $acctIdCol && $acctUserCol) {
    $currencyJoinSql = "
        LEFT JOIN $acctTable ba ON ba.$acctUserCol = up.id
        LEFT JOIN bank_account_registry_currencies k ON k.bank_account_id = ba.$acctIdCol AND k.currency_type = 1
        LEFT JOIN bank_account_registry_currencies g ON g.bank_account_id = ba.$acctIdCol AND g.currency_type = 2
        ";
  } else {
    $currencyJoinSql = "
        LEFT JOIN bank_account_registry_currencies k ON k.bank_account_id = up.id AND k.currency_type = 1
        LEFT JOIN bank_account_registry_currencies g ON g.bank_account_id = up.id AND g.currency_type = 2
        ";
  }
}

$sqlUsers = "
SELECT
  up.user_id AS steamid,
  up.name AS pname,
  up.last_login_time AS last_seen,
  up.fame_points AS famepoints,
  " . ($hasSquad ? "s.name AS squad_name," : "NULL AS squad_name,") . "
  COALESCE(k.account_balance, 0) AS kuna,
  COALESCE(g.account_balance, 0) AS gold
FROM user_profile up
" . ($hasSquad ? "
LEFT JOIN $squadMemberTable sm ON sm.user_profile_id = up.id
LEFT JOIN squad s ON s.id = sm.squad_id
" : "") . "
LEFT JOIN bank_account_registry_currencies k
  ON k.bank_account_id = up.id AND k.currency_type = 1
LEFT JOIN bank_account_registry_currencies g
  ON g.bank_account_id = up.id AND g.currency_type = 2
ORDER BY up.fame_points DESC
LIMIT 25
";


$users = [];
$resUsers = $scumDb->query($sqlUsers);
if ($resUsers) {
  while ($row = $resUsers->fetchArray(SQLITE3_ASSOC)) {
    $users[] = $row;
  }
}
?>

<main class="content layout-3col">

  <!-- LEFT: optional (kannst du auch leer lassen) -->
  <aside class="side left">
    <section class="panel panel-left">
      <div class="panel-topbar">
        <div class="panel-topbar-title">ADMINCENTER</div>
      </div>

      <div class="panel-body">
        <div class="admin-nav">
          <a class="subtab <?= $adminTab === 'news' ? 'active' : '' ?>" href="index.php?page=admin&tab=news">Servernews</a>
          <a class="subtab <?= $adminTab === 'overview' ? 'active' : '' ?>" href="index.php?page=admin&tab=overview">Übersicht</a>

          <a class="subtab <?= $adminTab === 'vehicles' ? 'active' : '' ?>" href="index.php?page=admin&tab=vehicles">Fahrzeuge</a>
        </div>

        <div class="scum-slot muted" style="margin-top:10px;">
          SCUM-Daten und Serververwaltung
        </div>
      </div>
    </section>
  </aside>

  <!-- CENTER: Admin Content -->
  <section class="center">
    <div class="main-card">

      <div class="center-head">
        <div class="userblock">
          <div class="userlabel">Admin</div>
          <div class="username"><?= htmlspecialchars($_SESSION['steamid'] ?? '') ?></div>
        </div>

        <div class="moneyblock">
          <div class="userlabel">Bereich</div>
          <div class="money"><?= strtoupper($adminTab) ?></div>
        </div>
      </div>

      <div class="center-body">

        <?php if ($adminTab === 'overview'): ?>
          <h1>Admin – Übersicht</h1>

          <div class="admin-cards">
            <div class="admin-card">
              <div class="admin-card-title">User</div>
              <div class="admin-card-value"><?= number_format($userCount, 0, ',', '.') ?></div>
              <div class="admin-card-sub">aus SCUM.db</div>
            </div>

            <div class="admin-card">
              <div class="admin-card-title">Status</div>
              <div class="admin-card-value">OK</div>
              <div class="admin-card-sub">Übersicht live</div>
            </div>
          </div>

          <h2 style="margin-top:18px;">Userübersicht</h2>

          <div class="admin-table">
            <div class="admin-table-head">
              <span>SteamID</span><span>Name</span><span>Squad</span>
              <span>Fame</span><span>Kuna</span><span>Gold</span>
              <span>Last Seen</span>
            </div>


            <?php if (empty($users)): ?>
              <div class="admin-table-row muted">
                <span>—</span><span>Keine Daten</span><span>—</span><span>—</span>
              </div>
            <?php else: ?>
              <?php foreach ($users as $u): ?>
                <?php
                $sid = (string)($u['steamid'] ?? '');

                ?>
                <div class="admin-table-row">
                  <span><?= htmlspecialchars($sid) ?></span>
                  <span><?= htmlspecialchars((string)($u['pname'] ?? '')) ?></span>
                  <span><?= htmlspecialchars((string)($u['squad_name'] ?? '—')) ?></span>

                  <span><?= htmlspecialchars((string)($u['famepoints'] ?? '—')) ?></span>
                  <span><?= number_format((float)($u['kuna'] ?? 0), 0, ',', '.') ?></span>
                  <span><?= number_format((float)($u['gold'] ?? 0), 0, ',', '.') ?></span>

                  <span class="muted">
                    <?php
                    $ls = $u['last_seen'] ?? null;
                    echo htmlspecialchars($ls === null || $ls === '' ? '—' : (string)$ls);
                    ?>
                  </span>
                </div>

              <?php endforeach; ?>
            <?php endif; ?>
          </div>
        <?php elseif ($adminTab === 'news'): ?>
          <?php require __DIR__ . '/admin_news.php'; ?>
        <?php elseif ($adminTab === 'vehicles'): ?>
          <?php require __DIR__ . '/admin_vehicles.php'; ?>
        <?php endif; ?>



      </div>
    </div>
  </section>
</main>