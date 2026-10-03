<?php

declare(strict_types=1);

// Admin-Schutz
if (empty($_SESSION['isAdmin'])) {
    http_response_code(403);
    exit('Kein Zugriff.');
}

require_once __DIR__ . '/../functions/db_function.php';
require_once __DIR__ . '/../functions/server_news_function.php';

$notice = '';
$error  = '';

$editId = isset($_GET['edit']) ? (int)$_GET['edit'] : 0;
$edit   = $editId ? news_get($editId) : null;

/** discord_json -> $cats laden */
$discordStruct = ['categories' => []];
if ($edit && !empty($edit['discord_json'])) {
    $tmp = json_decode((string)$edit['discord_json'], true);
    if (is_array($tmp)) $discordStruct = $tmp;
}
$cats = $discordStruct['categories'] ?? [];
if (!is_array($cats)) $cats = [];

// Aktionen
if ($_SERVER['REQUEST_METHOD'] === 'POST') {
    $action = (string)($_POST['action'] ?? '');

    if ($action === 'save') {
        $id    = (int)($_POST['id'] ?? 0);
        $title = (string)($_POST['title'] ?? '');
        $body  = (string)($_POST['body'] ?? '');
        $pub = true;          // immer Home
        $postDiscord = true;  // immer Discord


        $authorSteam = (string)($_SESSION['steamid'] ?? '');

        // Kategorien + Items aus POST
        $names  = $_POST['cat_name']  ?? [];
        $items  = $_POST['cat_items'] ?? [];
        $colors = $_POST['cat_color'] ?? [];

        $cats = [];
        if (is_array($names)) {
            foreach ($names as $i => $rawName) {
                $name = trim((string)$rawName);
                if ($name === '') continue;

                $rawItems = (is_array($items) && array_key_exists($i, $items)) ? (string)$items[$i] : '';
                $lines = preg_split("/\R/u", $rawItems);
                $clean = [];
                foreach ($lines as $ln) {
                    $ln = trim((string)$ln);
                    if ($ln === '') continue;
                    $clean[] = $ln;
                }

                $color = (is_array($colors) && array_key_exists($i, $colors)) ? (string)$colors[$i] : '#5865F2';
                $color = preg_match('/^#[0-9a-fA-F]{6}$/', $color) ? $color : '#5865F2';

                $cats[] = ['name' => $name, 'color' => $color, 'items' => $clean];
            }
        }


        $discordJson = json_encode(['categories' => $cats], JSON_UNESCAPED_UNICODE);

        // Speichern/Update Basis-Fields
        $res = news_save($id > 0 ? $id : null, $title, $body, $pub, $authorSteam);

        if (!$res['ok']) {
            $error = (string)$res['msg'];
        } else {
            $nid = (int)$res['id'];
            $notice = (string)$res['msg'];

            // discord_json in DB schreiben
            try {
                $pdo = db();
                $st = $pdo->prepare("UPDATE server_news SET discord_json = :j WHERE id = :id");
                $st->execute([':j' => $discordJson, ':id' => $nid]);
            } catch (Throwable $e) {
                $error = 'Konnte discord_json nicht speichern: ' . $e->getMessage();
            }

            // optional: direkt zu Discord posten
            if ($postDiscord && $error === '') {
                $d = news_post_to_discord($nid);
                if (!($d['ok'] ?? false)) $error = (string)($d['msg'] ?? 'Discord Fehler');
                else $notice .= ' ' . (string)($d['msg'] ?? '');
            }

            // reload edit
            $editId = $nid;
            $edit   = news_get($editId);

            // cats reload
            $discordStruct = ['categories' => []];
            if ($edit && !empty($edit['discord_json'])) {
                $tmp = json_decode((string)$edit['discord_json'], true);
                if (is_array($tmp)) $discordStruct = $tmp;
            }
            $cats = $discordStruct['categories'] ?? [];
            if (!is_array($cats)) $cats = [];
        }
    }

    if ($action === 'post_discord') {
        $id = (int)($_POST['id'] ?? 0);

        $d = news_post_to_discord($id);
        if (!($d['ok'] ?? false)) $error = (string)($d['msg'] ?? 'Discord Fehler');
        else $notice = (string)($d['msg'] ?? 'News zu Discord gepostet.');

        $editId = $id;
        $edit   = news_get($editId);

        // cats reload
        $discordStruct = ['categories' => []];
        if ($edit && !empty($edit['discord_json'])) {
            $tmp = json_decode((string)$edit['discord_json'], true);
            if (is_array($tmp)) $discordStruct = $tmp;
        }
        $cats = $discordStruct['categories'] ?? [];
        if (!is_array($cats)) $cats = [];
    }
    if ($action === 'delete') {
        $id = (int)($_POST['id'] ?? 0);
        if ($id > 0) {
            $res = news_delete($id);
            if ($res['ok']) $notice = $res['msg'];
            else $error = $res['msg'];
        }

        // zurück zur Liste
        $editId = 0;
        $edit = null;
    }
}

$list = news_list(30, false);

$titleVal = $edit
    ? (string)$edit['title']
    : date('d.m.Y');

$bodyVal  = $edit ? (string)$edit['body'] : '';
$pubVal   = $edit ? ((int)$edit['is_published'] === 1) : false;
?>

<h1>Admin – Servernews</h1>

<?php if ($notice !== ''): ?>
    <div class="scum-slot" style="border-color: rgba(80,255,109,.35); color:#d7ffe3; margin-bottom:10px;">
        <?= htmlspecialchars($notice) ?>
    </div>
<?php endif; ?>

<?php if ($error !== ''): ?>
    <div class="scum-slot" style="border-color: rgba(255,80,80,.35); color:#ffd7d7; margin-bottom:10px;">
        <?= htmlspecialchars($error) ?>
    </div>
<?php endif; ?>

<div class="admin-cards" style="margin-bottom:14px;">
    <div class="admin-card">
        <div class="admin-card-title">News</div>
        <div class="admin-card-value"><?= number_format(count($list), 0, ',', '.') ?></div>
        <div class="admin-card-sub">gesamt</div>
    </div>

    <div class="admin-card">
        <div class="admin-card-title">Veröffentlicht</div>
        <div class="admin-card-value">
            <?php
            $pubCount = 0;
            foreach ($list as $n) if ((int)($n['is_published'] ?? 0) === 1) $pubCount++;
            echo number_format($pubCount, 0, ',', '.');
            ?>
        </div>
        <div class="admin-card-sub">sichtbar auf Home</div>
    </div>
</div>

<div class="main-card" style="margin-bottom:14px;">
    <div class="center-head">
        <h2><?= $edit ? 'News bearbeiten' : 'Neue News' ?></h2>
    </div>

    <div class="center-body">
        <form method="post">
            <input type="hidden" name="action" value="save">
            <input type="hidden" name="id" value="<?= (int)($edit['id'] ?? 0) ?>">

            <div class="kv" style="margin-bottom:10px;">
                <div class="kv-row">
                    <span>Titel (Home)</span>
                    <span style="flex:1;">
                        <input name="title" value="<?= htmlspecialchars($titleVal) ?>" maxlength="120"
                            class="news-input">
                    </span>
                </div>
            </div>

            <div class="kv" style="margin-bottom:10px;">
                <div class="kv-row">
                    <span>Home Text (optional)</span>
                    <span style="flex:1;">
                        <textarea name="body" rows="4" class="news-textarea"><?= htmlspecialchars($bodyVal) ?></textarea>
                    </span>
                </div>
            </div>

            <div class="section-title">Discord News Builder</div>

            <div id="catsWrap" class="news-builder">
                <?php foreach ($cats as $c): ?>
                    <?php
                    $cname = (string)($c['name'] ?? '');
                    $items = $c['items'] ?? [];
                    if (!is_array($items)) $items = [];
                    $itemsText = implode("\n", array_map('strval', $items));
                    ?>
                    <div class="scum-slot" data-cat style="margin-bottom:10px;">
                        <div style="display:flex; gap:10px; align-items:center;">
                            <div style="font-weight:800;">Kategorie</div>
                            <?php $ccolor = (string)($c['color'] ?? '#5865F2'); ?>
                            <input type="color" style="width: 33px;" name="cat_color[]" value="<?= htmlspecialchars($ccolor) ?>" class="news-color">
                            <input class="news-input" name="cat_name[]" value="<?= htmlspecialchars($cname) ?>" placeholder="z.B. UPDATE, Serverconfig, Events...">
                            <button type="button" class="news-x" onclick="removeCat(this)">✕</button>
                        </div>

                        <div class="muted" style="margin-top:8px;">Bulletpoints (eine Zeile = ein Punkt)</div>
                        <textarea class="news-textarea" name="cat_items[]" rows="5"><?= htmlspecialchars($itemsText) ?></textarea>
                    </div>
                <?php endforeach; ?>
            </div>

            <div style="display:flex; gap:10px; flex-wrap:wrap; margin-top:10px;">
                <button type="button" class="nav-btn" style="border:0; cursor:pointer;" onclick="addCat()">+ Kategorie hinzufügen</button>
                <div class="scum-slot muted" style="margin-top:10px;">
                    Speichern = automatisch auf Home veröffentlichen + zu Discord posten.
                </div>
                <button class="subtab active" type="submit" style="border:0; cursor:pointer;">Speichern</button>

                <?php if ($edit): ?>
                    <a class="nav-btn" style="text-decoration:none;" href="index.php?page=admin&tab=news">Neu</a>
                <?php endif; ?>
            </div>

            <?php if ($edit): ?>
                <div class="scum-slot muted" style="margin-top:10px;">
                    ID: <?= (int)$edit['id'] ?> • erstellt: <?= htmlspecialchars((string)$edit['created_at']) ?>
                    • Discord: <?= !empty($edit['discord_posted_at']) ? htmlspecialchars((string)$edit['discord_posted_at']) : '—' ?>
                </div>
            <?php endif; ?>
        </form>

        <?php if ($edit && empty($edit['discord_posted_at'])): ?>
            <form method="post" style="margin-top:10px;">
                <input type="hidden" name="action" value="post_discord">
                <input type="hidden" name="id" value="<?= (int)$edit['id'] ?>">
                <button class="nav-btn" type="submit" style="border:0; cursor:pointer;">Jetzt zu Discord posten</button>
            </form>
        <?php endif; ?>
    </div>


    <h2>Letzte News</h2>
    <div class="admin-table">
        <div class="admin-table-head">
            <span>ID</span><span>Titel</span><span>Datum</span><span>Aktion</span>
        </div>

        <?php foreach ($list as $n): ?>
            <?php
            $id = (int)$n['id'];
            $isPub = (int)($n['is_published'] ?? 0) === 1;
            $disc = !empty($n['discord_posted_at']);
            ?>
            <div class="admin-table-row" style="grid-template-columns: 80px 1fr 180px 220px;">
                <span><?= $id ?></span>
                <span><?= htmlspecialchars((string)$n['title']) ?></span>
                <span class="muted"><?= htmlspecialchars((string)$n['created_at']) ?></span>
                <span style="display:flex; gap:8px; justify-content:flex-end; align-items:center;">
                    <a class="nav-btn" style="text-decoration:none;" href="index.php?page=admin&tab=news&edit=<?= $id ?>">Edit</a>

                    <form method="post" style="margin:0;" onsubmit="return confirm('News wirklich löschen?');">
                        <input type="hidden" name="action" value="delete">
                        <input type="hidden" name="id" value="<?= $id ?>">
                        <button class="nav-btn" type="submit" style="border:0; cursor:pointer; background:rgba(120,0,0,.6);">
                            Löschen
                        </button>
                    </form>
                </span>
            </div>

        <?php endforeach; ?>
    </div>
</div>
<script>
    function addCat() {
        const wrap = document.getElementById('catsWrap');
        const div = document.createElement('div');
        div.className = 'scum-slot';
        div.setAttribute('data-cat', '');
        div.style.marginBottom = '10px';
        div.innerHTML = `
  <div class="news-cat-head">
    <div class="news-cat-label">Kategorie</div>
    <input type="color" style="width: 33px;" name="cat_color[]" value="#5865F2" class="news-color">
    <input class="news-input" name="cat_name[]" value="" placeholder="z.B. UPDATE, Serverconfig, Events...">
    <button type="button" class="news-x" onclick="removeCat(this)">✕</button>
  </div>
  <div class="muted" style="margin-top:8px;">Bulletpoints (eine Zeile = ein Punkt)</div>
  <textarea class="news-textarea" name="cat_items[]" rows="5"></textarea>
`;

        wrap.appendChild(div);
    }

    function removeCat(btn) {
        const box = btn.closest('[data-cat]');
        if (box) box.remove();
    }
</script>