<?php
declare(strict_types=1);

require_once __DIR__ . '/../config/settings.php';

function heavy_industries_catalog(): array {
    return [
        'refrigerator' => ['name'=>'Kühlschrank','type'=>'BP_Refrigerator','image'=>'BP_Refrigerator.png'],
        'lathe' => ['name'=>'Drehbank','type'=>'Lathe_Machine','image'=>'Lathe_Machine.png'],
        'drill_press_01' => ['name'=>'Industriebohrmaschine','type'=>'Drill_Press_01','image'=>'Drill_Press_01.png'],
        'drill_press_02' => ['name'=>'Standbohrmaschine','type'=>'Drill_Press_02','image'=>'Drill_Press_02.png'],
        'gas_stove' => ['name'=>'Gasherd mit Backofen','type'=>'Gas_Stove_Oven','image'=>'Gas_Stove_Oven.png'],
    ];
}

function heavy_industries_config_path(): string { return __DIR__ . '/../private/heavy_industries.json'; }
function heavy_industries_config(): array {
    $out=[]; foreach (heavy_industries_catalog() as $key=>$item) $out[$key]=['price'=>0,'enabled'=>false];
    $path=heavy_industries_config_path();
    if (!is_file($path)) return $out;
    $raw=json_decode((string)file_get_contents($path),true); if (!is_array($raw)) return $out;
    foreach ($out as $key=>$value) if (isset($raw[$key]) && is_array($raw[$key])) $out[$key]=[
        'price'=>max(0,min(100000000,(int)($raw[$key]['price']??0))),
        'enabled'=>(bool)($raw[$key]['enabled']??false),
    ];
    return $out;
}

function heavy_industries_save_config(array $post): void {
    $out=[]; foreach (heavy_industries_catalog() as $key=>$item) $out[$key]=[
        'price'=>max(0,min(100000000,(int)($post['price'][$key]??0))),
        'enabled'=>isset($post['enabled'][$key]),
    ];
    $path=heavy_industries_config_path(); $dir=dirname($path);
    if (!is_dir($dir) && !mkdir($dir,0750,true) && !is_dir($dir)) throw new RuntimeException('Privater Konfigurationsordner konnte nicht erstellt werden.');
    $tmp=$path.'.tmp.'.bin2hex(random_bytes(4));
    if (file_put_contents($tmp,json_encode($out,JSON_PRETTY_PRINT|JSON_UNESCAPED_UNICODE),LOCK_EX)===false || !rename($tmp,$path)) { @unlink($tmp); throw new RuntimeException('Preise konnten nicht gespeichert werden.'); }
}

function heavy_industries_csrf(): string {
    if (empty($_SESSION['heavy_industries_csrf'])) $_SESSION['heavy_industries_csrf']=bin2hex(random_bytes(24));
    return (string)$_SESSION['heavy_industries_csrf'];
}
function heavy_industries_verify_csrf(string $token): void {
    if ($token==='' || !hash_equals(heavy_industries_csrf(),$token)) throw new DomainException('Sitzung abgelaufen. Bitte lade die Seite neu.');
}

function heavy_industries_send_request(string $steamId,string $productKey,int $price): void {
    $catalog=heavy_industries_catalog(); if (!isset($catalog[$productKey])) throw new DomainException('Unbekanntes Gerät.');
    $config=heavy_industries_config();
    if (empty($config[$productKey]['enabled']) || (int)$config[$productKey]['price']<=0 || (int)$config[$productKey]['price']!==$price) throw new DomainException('Preis oder Verfügbarkeit wurde geändert. Bitte lade die Seite neu.');
    $last=(int)($_SESSION['heavy_industries_last_request']??0); if (time()-$last<60) throw new DomainException('Bitte warte eine Minute vor der nächsten Anfrage.');
    $webhook=function_exists('shop_discord_webhook') ? shop_discord_webhook() : '';
    if ($webhook==='') throw new RuntimeException('Der Discord-Kanal ist noch nicht konfiguriert.');
    $product=$catalog[$productKey];
    $payload=['username'=>'Heavy Industries','embeds'=>[array_filter([
        'title'=>'🏭 Neue Heavy-Industries-Terminanfrage','color'=>0xC99431,
        'description'=>'Nur Anfrage: keine Abbuchung und keine automatische Platzierung.',
        'fields'=>[
            ['name'=>'Spieler','value'=>'SteamID `'.substr($steamId,0,25).'`','inline'=>false],
            ['name'=>'Gerät','value'=>$product['name'].' (`'.$product['type'].'`)','inline'=>true],
            ['name'=>'Preis','value'=>number_format($price,0,',','.').' Scummies','inline'=>true],
            ['name'=>'Nächster Schritt','value'=>'Termin mit dem Spieler vereinbaren und Gerät anschließend als Admin über ggHaul platzieren.','inline'=>false],
        ],'timestamp'=>gmdate('c')
    ])]];
    $ch=curl_init($webhook); curl_setopt_array($ch,[CURLOPT_POST=>true,CURLOPT_HTTPHEADER=>['Content-Type: application/json'],CURLOPT_POSTFIELDS=>json_encode($payload,JSON_UNESCAPED_UNICODE|JSON_UNESCAPED_SLASHES),CURLOPT_RETURNTRANSFER=>true,CURLOPT_CONNECTTIMEOUT=>3,CURLOPT_TIMEOUT=>6]);
    curl_exec($ch); $status=(int)curl_getinfo($ch,CURLINFO_HTTP_CODE); $error=curl_error($ch); curl_close($ch);
    if ($status<200 || $status>=300) throw new RuntimeException('Discord-Anfrage fehlgeschlagen'.($error!==''?': '.$error:'.'));
    $_SESSION['heavy_industries_last_request']=time();
}
