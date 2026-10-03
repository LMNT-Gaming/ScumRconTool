<?php
declare(strict_types=1);
require_once __DIR__ . '/../functions/order_shop_function.php';
$shopAdminNotice=''; $shopAdminError='';
if ($_SERVER['REQUEST_METHOD']==='POST' && str_starts_with((string)($_POST['action']??''),'shop_')) {
    try {
        order_shop_verify_csrf((string)($_POST['csrf']??''));
        if ($_POST['action']==='shop_save') { order_shop_save_pack($_POST); $shopAdminNotice='Pack gespeichert.'; }
        elseif ($_POST['action']==='shop_delete') { order_shop_delete_pack((int)($_POST['pack_id']??0)); $shopAdminNotice='Pack gelöscht.'; }
        elseif ($_POST['action']==='shop_resolve') { order_shop_admin_resolve((int)($_POST['order_id']??0),(string)($_POST['target_status']??'')); $shopAdminNotice='Prüffall abgeschlossen.'; }
    } catch(Throwable $e) { $shopAdminError=$e->getMessage(); }
}
$shopPacks=order_shop_packs(true); $shopOrders=order_shop_admin_orders(); $shopCats=order_shop_categories(); $shopCsrf=order_shop_csrf();
?>
<div class="admin-order-shop">
 <h1>Raven Market</h1><p class="muted">Konfiguriere kaufbare Packs. Items werden als <code>SpawnCode|Menge</code> eingetragen. Fahrzeugpacks enthalten genau einen Fahrzeugcode mit Menge 1.</p>
 <?php if($shopAdminNotice):?><div class="shop-alert success"><?=htmlspecialchars($shopAdminNotice)?></div><?php endif;?>
 <?php if($shopAdminError):?><div class="shop-alert error"><?=htmlspecialchars($shopAdminError)?></div><?php endif;?>
 <details class="shop-admin-card" open><summary>+ Neues Pack</summary><?php $pack=['id'=>0,'category'=>'mechanic','name'=>'','description'=>'','image_url'=>'','price'=>1000,'fulfillment_type'=>'items','enabled'=>1,'sort_order'=>0,'payload_json'=>'[]']; include __DIR__.'/admin_order_shop_form.php';?></details>
 <?php foreach($shopPacks as $pack):?><details class="shop-admin-card"><summary><?=htmlspecialchars($pack['name'])?> · <?=number_format((int)$pack['price'],0,',','.')?>$</summary><?php include __DIR__.'/admin_order_shop_form.php';?></details><?php endforeach;?>
 <section class="shop-admin-card"><h2>Letzte Bestellungen</h2><div class="admin-order-list">
 <?php foreach($shopOrders as $order):?><article><div><strong><?=htmlspecialchars($order['pack_name'])?></strong><small><?=htmlspecialchars($order['code'])?> · <?=htmlspecialchars($order['steam_id'])?> · <?=number_format((int)$order['price'],0,',','.')?>$</small><?php if($order['details']):?><p><?=htmlspecialchars($order['details'])?></p><?php endif;?></div><b class="status-<?=htmlspecialchars($order['status'])?>"><?=htmlspecialchars($order['status'])?></b><?php if(in_array($order['status'],['processing','review'],true)):?><form method="post" onsubmit="return confirm('Kontostand und Ausgabe wirklich manuell geprüft?')"><input type="hidden" name="action" value="shop_resolve"><input type="hidden" name="csrf" value="<?=htmlspecialchars($shopCsrf,ENT_QUOTES)?>"><input type="hidden" name="order_id" value="<?=(int)$order['id']?>"><button name="target_status" value="pending">Ohne Abbuchung erneut öffnen</button><button name="target_status" value="completed">Ausgeliefert</button><button class="danger" name="target_status" value="cancelled">Nach Erstattung stornieren</button></form><?php endif;?></article><?php endforeach;?>
 </div></section>
</div>
<div class="item-catalog-modal" id="shopItemCatalog" hidden role="dialog" aria-modal="true" aria-labelledby="shopItemCatalogTitle">
 <div class="item-catalog-dialog">
  <header><div><small>RAVEN MARKET</small><h2 id="shopItemCatalogTitle">Items auswählen</h2></div><button type="button" class="catalog-close" aria-label="Schließen">×</button></header>
  <div class="catalog-toolbar"><input type="search" id="catalogSearch" placeholder="Nice Name oder echter Spawn-Code"><select id="catalogCategory"><option value="">Alle Kategorien</option></select><span id="catalogCount">Katalog wird geladen …</span></div>
  <div class="catalog-grid" id="catalogGrid"></div>
  <footer><small>Es werden maximal 120 Treffer gleichzeitig angezeigt. Nutze die Suche für weitere Items.</small><div><button type="button" class="catalog-close secondary">Abbrechen</button><button type="button" id="catalogApply">Auswahl übernehmen</button></div></footer>
 </div>
</div>
<script src="assets/admin-order-shop.js?v=<?= (int) @filemtime(__DIR__ . '/../assets/admin-order-shop.js') ?>"></script>
