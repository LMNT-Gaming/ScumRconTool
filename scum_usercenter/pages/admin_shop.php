<?php
//admin_shop.php
if (empty($_SESSION['isAdmin'])) {
    http_response_code(403);
    exit('Kein Zugriff.');
}

require_once __DIR__ . '/../functions/shop_function.php';
require_once __DIR__ . '/../functions/image_function.php';
require_once __DIR__ . '/../functions/shop_category_function.php';

$shopImages = getShopImages();
$action = $_POST['action'] ?? '';
$noticeCat = '';
$errorCat  = '';

// Kategorie anlegen
if ($action === 'cat_create') {
    $res = shop_category_create($_POST);
    if (!empty($res['ok'])) $noticeCat = (string)$res['msg'];
    else $errorCat = (string)($res['msg'] ?? 'Fehler beim Erstellen.');
}
if ($action === 'cat_update') {
    $id = (int)($_POST['cat_id'] ?? 0);
    $res = shop_category_update($id, $_POST);
    if (!empty($res['ok'])) $noticeCat = (string)$res['msg'];
    else $errorCat = (string)($res['msg'] ?? 'Fehler beim Speichern.');
}

// Kategorie löschen
if ($action === 'cat_delete') {
    $res = shop_category_delete((int)($_POST['cat_id'] ?? 0));
    if (!empty($res['ok'])) $noticeCat = (string)$res['msg'];
    else $errorCat = (string)($res['msg'] ?? 'Fehler beim Löschen.');
}

$editId = isset($_GET['edit']) ? (int)$_GET['edit'] : 0;


if ($action === 'create') {
    shop_create_item($_POST);
    header('Location: index.php?page=admin&tab=shop');
    exit;
}

if ($action === 'update' && !empty($_POST['id'])) {
    shop_update_item((int)$_POST['id'], $_POST);
    header('Location: index.php?page=admin&tab=shop');
    exit;
}

if (isset($_GET['delete'])) {
    shop_delete_item((int)$_GET['delete']);
    header('Location: index.php?page=admin&tab=shop');
    exit;
}

$items = shop_list_items();
$categories = shop_categories_list();
$editItem = $editId ? shop_get_item($editId) : null;

$selectedImage = $editItem['image'] ?? '';
$prices = $editItem['prices'] ?? [];
$priceScum = $prices['SCUM_DOLLAR'] ?? '';
$priceGold = $prices['GOLD'] ?? '';
$priceVoucher = $prices['VOUCHER'] ?? '';

function h($s)
{
    return htmlspecialchars((string)$s, ENT_QUOTES, 'UTF-8');
}
?>
<h1>Admin – Shopsettings</h1>

<style>
    .admin-shop-layout {
        display: grid;
        grid-template-columns: 1fr 360px;
        gap: 12px;
        align-items: start;
    }

    @media (max-width: 1100px) {
        .admin-shop-layout {
            grid-template-columns: 1fr;
        }
    }

    .admin-shop-side {
        position: sticky;
        top: 14px;
    }

    .cat-input {
        width: 100%;
        background: rgba(0, 0, 0, 0.55);
        border: 1px solid rgba(255, 255, 255, 0.12);
        color: white;
        padding: 8px 10px;
        font: inherit;
    }

    .cat-row {
        display: grid;
        grid-template-columns: 1fr;
        gap: 6px;
    }

    .cat-inline {
        display: grid;
        grid-template-columns: 1fr 44px;
        gap: 8px;
        align-items: center;
    }

    .cat-color {
        width: 44px;
        height: 34px;
        border-radius: 10px;
        border: 1px solid rgba(255, 255, 255, 0.18);
        background: rgba(0, 0, 0, 0.35);
        padding: 0;
        cursor: pointer;
    }
</style>

<div class="admin-shop-layout">

    <!-- ✅ LINKS: dein bisheriger Inhalt -->
    <div class="admin-shop-main">

        <div class="admin-cards" style="grid-template-columns: 1fr;">
            <div class="admin-card">
                <div class="admin-card-title"><?= $editItem ? 'Item bearbeiten' : 'Neues Item anlegen' ?></div>

                <form method="post" style="margin-top:10px; display:grid; gap:10px;">
                    <input type="hidden" name="action" value="<?= $editItem ? 'update' : 'create' ?>">
                    <?php if ($editItem): ?>
                        <input type="hidden" name="id" value="<?= (int)$editItem['id'] ?>">
                    <?php endif; ?>

                    <div class="kv-row" style="grid-template-columns: 30% 70%;">
                        <span>Name</span>
                        <span style="text-align:left;">
                            <input name="name" required value="<?= h($editItem['name'] ?? '') ?>" style="width:100%;">
                        </span>
                    </div>

                    <!-- ⚠️ SPÄTER ersetzen durch category_id Dropdown -->
                    <div class="kv-row" style="grid-template-columns: 30% 70%;">
                        <span>Kategorie</span>
                        <span style="text-align:left;">
                            <?php $curCatId = (int)($editItem['category_id'] ?? 0); ?>

                            <select name="category_id" style="width:100%;">
                                <option value="">— keine —</option>
                                <?php foreach ($categories as $c): ?>
                                    <option value="<?= (int)$c['id'] ?>" <?= $curCatId === (int)$c['id'] ? 'selected' : '' ?>>
                                        <?= h($c['name']) ?>
                                    </option>
                                <?php endforeach; ?>
                            </select>

                        </span>
                    </div>

                    <div class="kv-row" style="grid-template-columns: 30% 70%;">
                        <span>Zahlmittel (ODER)</span>
                        <span style="text-align:left;">
                            <div style="display:grid; gap:8px;">
                                <div style="display:grid; grid-template-columns: 140px 1fr; gap:10px; align-items:center;">
                                    <strong>SCUM$</strong>
                                    <input name="price_scum_dollar" type="number" min="0" value="<?= h($priceScum) ?>" placeholder="z.B. 10000">
                                </div>

                                <div style="display:grid; grid-template-columns: 140px 1fr; gap:10px; align-items:center;">
                                    <strong>GOLD</strong>
                                    <input name="price_gold" type="number" min="0" value="<?= h($priceGold) ?>" placeholder="z.B. 25">
                                </div>

                                <div style="display:grid; grid-template-columns: 140px 1fr; gap:10px; align-items:center;">
                                    <strong>GUTSCHEIN</strong>
                                    <input name="price_voucher" type="number" min="0" value="<?= h($priceVoucher) ?>" placeholder="z.B. 1">
                                </div>

                                <div class="muted" style="font-size:11px;">
                                    Leer lassen = Zahlmittel nicht verfügbar. Mindestens ein Feld ausfüllen.
                                </div>
                            </div>
                        </span>
                    </div>

                    <div class="kv-row" style="grid-template-columns: 30% 70%;">
                        <span>Bild</span>
                        <span style="text-align:left;">
                            <input type="hidden" name="image" id="selectedImage" value="<?= h($selectedImage) ?>">

                            <div class="image-grid">
                                <?php foreach ($shopImages as $img):
                                    $active = ($img['file'] === $selectedImage) ? 'active' : '';
                                ?>
                                    <div class="image-tile <?= $active ?>" data-file="<?= h($img['file']) ?>">
                                        <img src="<?= h($img['path']) ?>" alt="">
                                        <div class="image-label"><?= h($img['file']) ?></div>
                                    </div>
                                <?php endforeach; ?>
                            </div>
                        </span>
                    </div>

                    <div class="kv-row" style="grid-template-columns: 30% 70%;">
                        <span>Beschreibung</span>
                        <span style="text-align:left;">
                            <textarea name="description" rows="3" style="width:100%;"><?= h($editItem['description'] ?? '') ?></textarea>
                        </span>
                    </div>

                    <label class="admin-row">
                        <input type="checkbox" name="is_inventory_item" value="1" <?= !empty($editItem) ? ((int)$editItem['is_inventory_item'] === 1 ? 'checked' : '') : 'checked' ?>>
                        <span>Ist Inventar-Item (wird dem User gutgeschrieben)</span>
                    </label>

                    <label class="admin-row">
                        <input type="checkbox" name="requires_coordinates" value="1"
                            <?= !empty($editItem) && (int)$editItem['requires_coordinates'] === 1 ? 'checked' : '' ?>>
                        <span>Benötigt Karten-Koordinaten (z. B. Basiszone)</span>
                    </label>

                    <label class="admin-row">
                        <input type="checkbox" name="is_active" value="1" <?= !empty($editItem) ? ((int)$editItem['is_active'] === 1 ? 'checked' : '') : 'checked' ?>>
                        <span>Aktiv</span>
                    </label>

                    <div style="display:flex; gap:10px; justify-content:flex-end;">
                        <?php if ($editItem): ?>
                            <a class="subtab" href="index.php?page=admin&tab=shop">Abbrechen</a>
                        <?php endif; ?>
                        <button class="subtab active" type="submit"><?= $editItem ? 'Speichern' : 'Hinzufügen' ?></button>
                    </div>
                </form>
            </div>
        </div>
    </div>

   <div class="admin-card">
  <div class="admin-card-title">Kategorien</div>

  <?php if (!empty($noticeCat)): ?>
    <div class="scum-slot" style="margin-top:10px;">
      ✅ <?= h($noticeCat) ?>
    </div>
  <?php endif; ?>

  <?php if (!empty($errorCat)): ?>
    <div class="scum-slot" style="margin-top:10px; border-color: rgba(255,80,80,.6);">
      ❌ <?= h($errorCat) ?>
    </div>
  <?php endif; ?>

  <!-- ✅ Editor (Create/Update) -->
  <form method="post" id="catEditorForm" style="margin-top:10px; display:grid; gap:8px;">
    <input type="hidden" name="action" id="catAction" value="cat_create">
    <input type="hidden" name="cat_id" id="catId" value="">

    <input class="cat-input" name="name" id="catName" required placeholder="Name (z. B. Waffen)">
    <input class="cat-input" name="slug" id="catSlug" required placeholder="Slug (auto)">

    <div class="cat-inline">
      <input class="cat-input" type="text" name="color" id="catColorText" value="#ffffff" placeholder="#RRGGBB">
      <input class="cat-color" type="color" id="catColorPicker" value="#ffffff" title="Farbe wählen">
    </div>

    <input class="cat-input" name="sort_order" id="catSort" type="number" value="0" placeholder="Sortierung">

    <div style="display:flex; gap:8px; justify-content:flex-end;">
      <button class="subtab" type="button" id="catResetBtn">Neu</button>
      <button class="subtab active" type="submit" id="catSaveBtn">Kategorie anlegen</button>
    </div>
  </form>

  <!-- ✅ Kompakte Liste -->
  <div style="margin-top:12px; display:grid; gap:6px;">
    <?php if (empty($categories)): ?>
      <div class="muted" style="font-size:12px;">Keine Kategorien vorhanden.</div>
    <?php endif; ?>

    <?php foreach ($categories as $c): ?>
      <div class="scum-slot" style="height:auto; padding:10px;">
        <div style="display:flex; align-items:center; gap:8px;">
          <span style="width:10px;height:18px;background:<?= h($c['color']) ?>; border:1px solid rgba(255,255,255,.25);"></span>

          <div style="min-width:0;">
            <div style="font-weight:900; letter-spacing:.6px;"><?= h($c['name']) ?></div>
            <div class="muted" style="font-size:11px;">slug: <?= h($c['slug']) ?> · sort: <?= (int)$c['sort_order'] ?></div>
          </div>

          <div style="margin-left:auto; display:flex; gap:6px;">
            <button
              type="button"
              class="subtab"
              data-cat-edit
              data-id="<?= (int)$c['id'] ?>"
              data-name="<?= h($c['name']) ?>"
              data-slug="<?= h($c['slug']) ?>"
              data-color="<?= h($c['color']) ?>"
              data-sort="<?= (int)$c['sort_order'] ?>"
            >Edit</button>

            <form method="post" style="display:inline;">
              <input type="hidden" name="action" value="cat_delete">
              <input type="hidden" name="cat_id" value="<?= (int)$c['id'] ?>">
              <button class="subtab danger" type="submit"
                onclick="return confirm('Kategorie löschen? Nur möglich, wenn keine Items zugeordnet sind.');">
                Del
              </button>
            </form>
          </div>
        </div>
      </div>
    <?php endforeach; ?>
  </div>
</div>



</div>


<h2 style="margin-top:18px;">Shop Items</h2>

<div class="admin-table">
    <div class="admin-table-head" style="grid-template-columns: 60px 1.2fr 0.9fr 1fr 0.6fr 140px;">
    <span>ID</span><span>Name</span><span>Kategorie</span><span>Preise (ODER)</span><span>Aktiv</span><span>Aktionen</span>
</div>

    <?php foreach ($items as $it): ?>
        <div class="admin-table-row" style="grid-template-columns: 60px 1.2fr 0.9fr 1fr 0.6fr 140px;">
    <span><?= (int)$it['id'] ?></span>
    <span><?= h($it['name']) ?></span>

    <span>
        <?php if (!empty($it['category_name'])): ?>
            <span style="display:inline-flex; align-items:center; gap:8px;">
                <span style="width:10px;height:16px;background:<?= h($it['category_color'] ?? '#ffffff') ?>; border:1px solid rgba(255,255,255,.25);"></span>
                <?= h($it['category_name']) ?>
            </span>
        <?php else: ?>
            <span class="muted">—</span>
        <?php endif; ?>
    </span>

    <span><?= h($it['prices_text'] ?? '—') ?></span>
    <span><?= ((int)$it['is_active'] === 1) ? 'Ja' : 'Nein' ?></span>
    <span style="display:flex; gap:6px;">
        <a class="subtab" href="index.php?page=admin&tab=shop&edit=<?= (int)$it['id'] ?>">Edit</a>
        <a class="subtab" href="index.php?page=admin&tab=shop&delete=<?= (int)$it['id'] ?>" onclick="return confirm('Wirklich löschen?');">Del</a>
    </span>
</div>
    <?php endforeach; ?>
</div>

<script>
    document.addEventListener("DOMContentLoaded", () => {
        document.querySelectorAll(".image-tile").forEach(tile => {
            tile.addEventListener("click", () => {
                document.querySelectorAll(".image-tile").forEach(e => e.classList.remove("active"));
                tile.classList.add("active");
                const hidden = document.getElementById("selectedImage");
                if (hidden) hidden.value = tile.dataset.file || "";
            });
        });
    });
</script>
<script>
  function slugifyName(s) {
    s = (s || "").toString().trim().toLowerCase();
    // umlaute grob
    s = s.replace(/ä/g,'ae').replace(/ö/g,'oe').replace(/ü/g,'ue').replace(/ß/g,'ss');
    // alles was nicht a-z0-9 zu "-"
    s = s.replace(/[^a-z0-9]+/g, '-');
    s = s.replace(/-+/g, '-').replace(/^-|-$/g, '');
    return s;
  }

  function normalizeHex(s){
    s = (s || "").trim();
    if (/^#([0-9a-f]{6})$/i.test(s)) return s;
    return null;
  }

  document.addEventListener("DOMContentLoaded", () => {
    const form   = document.getElementById("catEditorForm");
    const action = document.getElementById("catAction");
    const idEl   = document.getElementById("catId");
    const nameEl = document.getElementById("catName");
    const slugEl = document.getElementById("catSlug");
    const sortEl = document.getElementById("catSort");

    const colorText = document.getElementById("catColorText");
    const colorPick = document.getElementById("catColorPicker");

    const resetBtn = document.getElementById("catResetBtn");
    const saveBtn  = document.getElementById("catSaveBtn");

    let slugManuallyEdited = false;

    function setModeCreate() {
      action.value = "cat_create";
      idEl.value = "";
      saveBtn.textContent = "Kategorie anlegen";
      slugManuallyEdited = false;
      form.reset();
      // default values
      colorText.value = "#ffffff";
      colorPick.value = "#ffffff";
      sortEl.value = "0";
    }

    function setModeEdit(cat) {
      action.value = "cat_update";
      idEl.value = cat.id;
      nameEl.value = cat.name;
      slugEl.value = cat.slug;
      colorText.value = cat.color || "#ffffff";
      sortEl.value = cat.sort || 0;

      const hx = normalizeHex(colorText.value);
      if (hx) colorPick.value = hx;

      saveBtn.textContent = "Kategorie speichern";
      slugManuallyEdited = true; // weil wir einen existierenden slug laden
    }

    // Slug automatisch aus Name (nur solange user den slug nicht manuell ändert)
    nameEl.addEventListener("input", () => {
      if (slugManuallyEdited) return;
      slugEl.value = slugifyName(nameEl.value);
    });

    slugEl.addEventListener("input", () => {
      slugManuallyEdited = true;
      slugEl.value = slugifyName(slugEl.value);
    });

    // Color sync
    if (colorText && colorPick) {
      const hx = normalizeHex(colorText.value);
      if (hx) colorPick.value = hx;

      colorPick.addEventListener("input", () => {
        colorText.value = colorPick.value;
      });
      colorText.addEventListener("input", () => {
        const hx2 = normalizeHex(colorText.value);
        if (hx2) colorPick.value = hx2;
      });
    }

    // Edit buttons -> Editor füllen
    document.querySelectorAll("[data-cat-edit]").forEach(btn => {
      btn.addEventListener("click", () => {
        setModeEdit({
          id: btn.dataset.id || "",
          name: btn.dataset.name || "",
          slug: btn.dataset.slug || "",
          color: btn.dataset.color || "#ffffff",
          sort: btn.dataset.sort || 0
        });
        // optional: Editor sichtbar in viewport holen
        form.scrollIntoView({ behavior: "smooth", block: "start" });
      });
    });

    // Reset -> Create Mode
    resetBtn?.addEventListener("click", () => setModeCreate());

    // init
    setModeCreate();
  });
</script>

