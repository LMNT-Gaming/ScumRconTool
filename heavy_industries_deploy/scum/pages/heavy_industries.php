<?php
declare(strict_types=1);
require_once __DIR__.'/../functions/heavy_industries_function.php';
$catalog=heavy_industries_catalog(); $config=heavy_industries_config(); $notice=''; $error='';
if ($_SERVER['REQUEST_METHOD']==='POST' && ($_POST['action']??'')==='request') {
    try { heavy_industries_verify_csrf((string)($_POST['csrf']??'')); $key=(string)($_POST['product']??''); heavy_industries_send_request((string)$_SESSION['steamid'],$key,(int)($_POST['price']??0)); $notice='Deine Terminanfrage wurde an das Adminteam gesendet.'; }
    catch(Throwable $e){ $error=$e->getMessage(); }
}
$csrf=heavy_industries_csrf();
?>
<main class="heavy-industries">
 <section class="hi-notice"><strong>Platzierung nur nach Terminvereinbarung</strong><p>Diese stationären Geräte werden ausschließlich gemeinsam mit einem Admin über ggHaul platziert. Die Anfrage reserviert und bezahlt noch nichts. Preis, Termin und endgültige Freigabe werden vorher bestätigt.</p></section>
 <?php if($notice!==''):?><div class="hi-feedback success" role="status"><?=htmlspecialchars($notice)?></div><?php endif;?>
 <?php if($error!==''):?><div class="hi-feedback error" role="alert"><?=htmlspecialchars($error)?></div><?php endif;?>
 <section class="hi-grid" aria-label="Heavy Industries Geräte">
 <?php foreach($catalog as $key=>$item): $entry=$config[$key]; ?>
  <article class="hi-card <?=empty($entry['enabled'])?'disabled':''?>">
   <div class="hi-image"><img src="assets/heavy-industries/<?=htmlspecialchars($item['image'])?>" alt="<?=htmlspecialchars($item['name'])?>" loading="lazy"></div>
   <div class="hi-copy"><span class="hi-code"><?=htmlspecialchars($item['type'])?></span><h2><?=htmlspecialchars($item['name'])?></h2><p>Stationäres Industriegerät für deine Basis. Platzierung und Ausrichtung erfolgen beim vereinbarten Termin.</p></div>
   <div class="hi-buy"><strong><?=((int)$entry['price']>0)?number_format((int)$entry['price'],0,',','.').' Scummies':'Preis folgt'?></strong>
    <form method="post"><input type="hidden" name="action" value="request"><input type="hidden" name="csrf" value="<?=htmlspecialchars($csrf,ENT_QUOTES)?>"><input type="hidden" name="product" value="<?=htmlspecialchars($key,ENT_QUOTES)?>"><input type="hidden" name="price" value="<?=(int)$entry['price']?>"><button type="submit" <?=empty($entry['enabled'])||((int)$entry['price']<=0)?'disabled':''?>>Termin anfragen</button></form>
   </div>
  </article>
 <?php endforeach;?>
 </section>
</main>
