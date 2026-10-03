<?php
declare(strict_types=1);
require_once __DIR__.'/../functions/heavy_industries_function.php';
$hiNotice=''; $hiError='';
if($_SERVER['REQUEST_METHOD']==='POST' && ($_POST['action']??'')==='save_heavy_industries'){
 try{heavy_industries_verify_csrf((string)($_POST['csrf']??''));heavy_industries_save_config($_POST);$hiNotice='Preise und Verfügbarkeit gespeichert.';}catch(Throwable $e){$hiError=$e->getMessage();}
}
$hiCatalog=heavy_industries_catalog();$hiConfig=heavy_industries_config();$hiCsrf=heavy_industries_csrf();
?>
<h1>Heavy Industries</h1>
<p class="muted">Preise in Scummies festlegen. Bei einer Spieleranfrage wird nur eine Discord-Nachricht erzeugt; es erfolgt keine Abbuchung und kein ggHaul-Aufruf.</p>
<?php if($hiNotice!==''):?><div class="scum-slot"><?=htmlspecialchars($hiNotice)?></div><?php endif;?>
<?php if($hiError!==''):?><div class="scum-slot"><?=htmlspecialchars($hiError)?></div><?php endif;?>
<form method="post"><input type="hidden" name="action" value="save_heavy_industries"><input type="hidden" name="csrf" value="<?=htmlspecialchars($hiCsrf,ENT_QUOTES)?>">
 <div class="admin-cards">
 <?php foreach($hiCatalog as $key=>$item):$entry=$hiConfig[$key];?><div class="admin-card"><div class="admin-card-title"><?=htmlspecialchars($item['name'])?></div><label><input type="checkbox" name="enabled[<?=htmlspecialchars($key,ENT_QUOTES)?>]" value="1" <?=!empty($entry['enabled'])?'checked':''?>> Verfügbar</label><label class="muted" style="display:block;margin-top:8px">Preis in Scummies</label><input type="number" min="0" max="100000000" step="1" name="price[<?=htmlspecialchars($key,ENT_QUOTES)?>]" value="<?=(int)$entry['price']?>"></div><?php endforeach;?>
 </div><button class="subtab active" type="submit" style="border:0;cursor:pointer;margin-top:12px">Speichern</button>
</form>
