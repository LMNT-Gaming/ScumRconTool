<?php
// Local visual fixture only. No sessions, credentials or live API requests.
$path = parse_url($_SERVER['REQUEST_URI'], PHP_URL_PATH);
$stage = dirname(__DIR__, 2) . '/deploy/vehicle-insurance/scum';
$assets = [
 '/assets/theme.css'=>'S:/Homepage/scum/assets/theme.css',
 '/assets/style.css'=>'S:/Homepage/scum/assets/style.css',
 '/assets/modern.css'=>'S:/Homepage/scum/assets/modern.css',
 '/assets/insurance.css'=>$stage.'/assets/insurance.css',
 '/assets/insurance.js'=>$stage.'/assets/insurance.js',
];
if (isset($assets[$path])) { header('Content-Type: '.(str_ends_with($path,'.css')?'text/css':'text/javascript')); readfile($assets[$path]); return; }
if ($path === '/api/insurance_data.php') {
 header('Content-Type: application/json');
 echo json_encode(['ok'=>true,'receivedAtUtc'=>gmdate('c'),'policies'=>[
  ['id'=>'V-012345ABCDEF','vehicleId'=>'6382416','vehicleName'=>'Barba','spawnClass'=>'','status'=>'Active','price'=>19000,'weeks'=>2,'startUtc'=>gmdate('c',time()-86400),'endUtc'=>gmdate('c',time()+864000)]
 ],'vehicles'=>['status'=>'ok','databaseAtUtc'=>gmdate('c'),'items'=>[
  ['id'=>'6382416','name'=>'Barba','lastAccess'=>'03.09.2026 10:12'],
  ['id'=>'7777777','name'=>'Laika','lastAccess'=>'03.09.2026 09:45'],
  ['id'=>'8888888','name'=>'Rager','lastAccess'=>'Unbekannt']
 ]]]);return;
}
if ($path !== '/') { http_response_code(404); return; }
?>
<!doctype html><html lang="de"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<link rel="stylesheet" href="/assets/theme.css"><link rel="stylesheet" href="/assets/style.css"><link rel="stylesheet" href="/assets/modern.css">
<title>Insurance layout test</title></head><body class="scum-app page-insurance"><div class="app-shell"><div style="padding:24px;max-width:1200px;margin:auto">
<section class="app-context"><div><span class="context-kicker">SCUM / INSURANCE</span><h1>Fahrzeugversicherung<b>.</b></h1><p>Deine Verträge, Laufzeiten und verfügbaren Ersatzfahrzeuge.</p></div></section>
<?php $_SESSION=['steamid'=>'76561198000000001']; require $stage.'/pages/insurance.php'; ?>
</div></div></body></html>
