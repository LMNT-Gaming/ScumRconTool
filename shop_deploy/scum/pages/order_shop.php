<?php
declare(strict_types=1);
require_once __DIR__ . '/../functions/order_shop_function.php';
$notice = ''; $error = ''; $created = null;
if ($_SERVER['REQUEST_METHOD'] === 'POST' && ($_POST['action'] ?? '') === 'create_order') {
    try {
        order_shop_verify_csrf((string)($_POST['csrf'] ?? ''));
        $created = order_shop_create_order((string)($_SESSION['steamid'] ?? ''), (int)($_POST['pack_id'] ?? 0));
        $notice = 'Bestellung vorbereitet. Gib den Code jetzt im Ingame-Chat ein.';
    } catch (Throwable $e) { $error = $e->getMessage(); }
}
$categories = order_shop_categories(); $packs = order_shop_packs(); $orders = order_shop_orders_for_player((string)$_SESSION['steamid']); $csrf = order_shop_csrf();
?>
<main class="order-shop">
  <?php if ($notice): ?><div class="shop-alert success"><?=htmlspecialchars($notice)?></div><?php endif; ?>
  <?php if ($error): ?><div class="shop-alert error"><?=htmlspecialchars($error)?></div><?php endif; ?>
  <?php if ($created): ?>
    <section class="order-ticket">
      <span>DEIN BESTELLCODE</span><strong>/bestellung <?=htmlspecialchars($created['code'])?></strong>
      <p><?=htmlspecialchars($created['name'])?> · <?=number_format((int)$created['price'],0,',','.')?> Scummies · gültig <?=htmlspecialchars($created['expires'])?></p>
      <small>Auf der Webseite wurde nur dein Guthaben geprüft. Abbuchung und Ausgabe erfolgen erst durch diesen Befehl im Spiel.</small>
    </section>
  <?php endif; ?>
  <section class="shop-intro"><div><span>RAVEN MARKET</span><h2>Versorgung. Direkt zu dir.</h2><p>Wähle ein Pack, erzeuge deinen persönlichen Code und löse ihn im SCUM-Chat ein. Du musst dabei online sein.</p></div></section>
  <div class="subshop-tabs" role="tablist">
    <button class="active" data-shop-tab="all">Alle</button>
    <?php foreach ($categories as $key=>$category): ?><button data-shop-tab="<?=htmlspecialchars($key)?>"><?=htmlspecialchars($category['name'])?></button><?php endforeach; ?>
    <a href="index.php?page=heavy_industries">Heavy Industries <small>Terminware</small></a>
  </div>
  <section class="shop-pack-grid">
    <?php if (!$packs): ?><div class="shop-empty">Noch keine direkt lieferbaren Packs eingerichtet.</div><?php endif; ?>
    <?php foreach ($packs as $pack): $cat=$categories[$pack['category']]??['name'=>$pack['category'],'subtitle'=>'']; ?>
      <article class="order-pack" data-category="<?=htmlspecialchars($pack['category'])?>">
        <div class="pack-image"><?php if($pack['image_url']):?><img src="<?=htmlspecialchars($pack['image_url'])?>" alt="" loading="lazy"><?php else:?><span>RR</span><?php endif;?></div>
        <div class="pack-body"><small><?=htmlspecialchars($cat['name'])?></small><h3><?=htmlspecialchars($pack['name'])?></h3><p><?=nl2br(htmlspecialchars($pack['description']))?></p></div>
        <div class="pack-buy"><strong><?=number_format((int)$pack['price'],0,',','.')?> <em>Scummies</em></strong>
          <form method="post"><input type="hidden" name="action" value="create_order"><input type="hidden" name="csrf" value="<?=htmlspecialchars($csrf,ENT_QUOTES)?>"><input type="hidden" name="pack_id" value="<?=(int)$pack['id']?>"><button type="submit">Bestellcode erzeugen</button></form>
        </div>
      </article>
    <?php endforeach; ?>
  </section>
  <?php if ($orders): ?><section class="my-orders"><h2>Meine letzten Bestellungen</h2><div class="order-list">
    <?php foreach($orders as $order):?><div><strong><?=htmlspecialchars($order['pack_name'])?></strong><code>/bestellung <?=htmlspecialchars($order['code'])?></code><span class="status-<?=htmlspecialchars($order['status'])?>"><?=htmlspecialchars($order['status'])?></span></div><?php endforeach;?>
  </div></section><?php endif;?>
</main>
<script>document.querySelectorAll('[data-shop-tab]').forEach(b=>b.addEventListener('click',()=>{document.querySelectorAll('[data-shop-tab]').forEach(x=>x.classList.toggle('active',x===b));document.querySelectorAll('.order-pack').forEach(c=>c.hidden=b.dataset.shopTab!=='all'&&c.dataset.category!==b.dataset.shopTab)}));</script>

