<?php
declare(strict_types=1);
header('Content-Type: application/json; charset=utf-8');
header('Cache-Control: no-store');
require_once __DIR__ . '/../functions/order_shop_function.php';

function sw_reply(array $data, int $status=200): never { http_response_code($status); echo json_encode($data,JSON_UNESCAPED_SLASHES|JSON_UNESCAPED_UNICODE); exit; }
function sw_token(): string {
    $header=(string)($_SERVER['HTTP_AUTHORIZATION']??'');
    if(preg_match('/^Bearer\s+(.+)$/i',$header,$m)) return trim($m[1]);
    return trim((string)($_SERVER['HTTP_X_SHOP_TOKEN']??''));
}
if($_SERVER['REQUEST_METHOD']!=='POST') sw_reply(['ok'=>false,'error'=>'method_not_allowed'],405);
$configured=order_shop_env('SHOP_WORKER_TOKEN');
if(strlen($configured)<24 || !hash_equals($configured,sw_token())) sw_reply(['ok'=>false,'error'=>'unauthorized'],401);
$input=json_decode((string)file_get_contents('php://input'),true);
if(!is_array($input)) sw_reply(['ok'=>false,'error'=>'invalid_json'],400);
$action=(string)($input['action']??''); $code=strtoupper(trim((string)($input['code']??''))); $steam=preg_replace('/\D/','',(string)($input['steamId']??''))?:'';
try {
    order_shop_schema(); $pdo=db();
    if($action==='list'){
        $stmt=$pdo->prepare("SELECT code,pack_name,price,expires_at FROM rr_shop_orders WHERE steam_id=:sid AND status='pending' AND expires_at>UTC_TIMESTAMP() ORDER BY id LIMIT 10");$stmt->execute([':sid'=>$steam]);sw_reply(['ok'=>true,'orders'=>$stmt->fetchAll()?:[]]);
    }
    if($code===''||strlen($steam)!==17) sw_reply(['ok'=>false,'error'=>'invalid_request'],400);
    if($action==='reserve'){
        $pdo->beginTransaction();
        $stmt=$pdo->prepare('SELECT * FROM rr_shop_orders WHERE code=:code LIMIT 1 FOR UPDATE');$stmt->execute([':code'=>$code]);$row=$stmt->fetch();
        if(!$row||!hash_equals((string)$row['steam_id'],$steam)){ $pdo->rollBack(); sw_reply(['ok'=>false,'error'=>'order_not_found'],404); }
        if($row['status']!=='pending'){ $pdo->rollBack(); sw_reply(['ok'=>false,'error'=>'order_not_pending','status'=>$row['status']],409); }
        $expiry=$pdo->prepare('SELECT expires_at<=UTC_TIMESTAMP() FROM rr_shop_orders WHERE id=:id');$expiry->execute([':id'=>$row['id']]);
        if((int)$expiry->fetchColumn()===1){ $pdo->prepare("UPDATE rr_shop_orders SET status='expired' WHERE id=:id")->execute([':id'=>$row['id']]);$pdo->commit();sw_reply(['ok'=>false,'error'=>'order_expired'],410); }
        $lease=bin2hex(random_bytes(32));$pdo->prepare("UPDATE rr_shop_orders SET status='processing',lease_token=:lease,processed_at=UTC_TIMESTAMP() WHERE id=:id AND status='pending'")->execute([':lease'=>$lease,':id'=>$row['id']]);$pdo->commit();
        sw_reply(['ok'=>true,'order'=>['code'=>$row['code'],'steamId'=>$row['steam_id'],'name'=>$row['pack_name'],'price'=>(int)$row['price'],'fulfillment'=>json_decode((string)$row['order_json'],true)],'leaseToken'=>$lease]);
    }
    $lease=(string)($input['leaseToken']??''); if(strlen($lease)!==64) sw_reply(['ok'=>false,'error'=>'invalid_lease'],400);
    $states=['release'=>'pending','complete'=>'completed','review'=>'review']; if(!isset($states[$action])) sw_reply(['ok'=>false,'error'=>'invalid_action'],400);
    $status=$states[$action];$details=substr(trim((string)($input['details']??'')),0,1000);
    $sql="UPDATE rr_shop_orders SET status=:status,details=:details,lease_token=NULL".($status==='completed'?',completed_at=UTC_TIMESTAMP()':'')." WHERE code=:code AND steam_id=:sid AND status='processing' AND lease_token=:lease";
    $stmt=$pdo->prepare($sql);$stmt->execute([':status'=>$status,':details'=>$details,':code'=>$code,':sid'=>$steam,':lease'=>$lease]);
    if($stmt->rowCount()!==1) sw_reply(['ok'=>false,'error'=>'lease_conflict'],409);
    sw_reply(['ok'=>true,'status'=>$status]);
} catch(Throwable $e){ error_log('Shop worker: '.$e->getMessage()); sw_reply(['ok'=>false,'error'=>'server_error'],500); }
