<?php
declare(strict_types=1);
header('Content-Type: application/json; charset=utf-8');
header('Cache-Control: private, no-cache, must-revalidate');
header('Vary: Cookie, If-None-Match');
require_once __DIR__ . '/../functions/insurance_function.php';
if (session_status() !== PHP_SESSION_ACTIVE) session_start();
$steam=(string)($_SESSION['steamid'] ?? '');
session_write_close();
if (!preg_match('/^\d{17}$/',$steam)) insurance_reply(['ok'=>false,'error'=>'login_required'],401);
try {
    insurance_load_config(); $snapshot=insurance_read(); $mine=[];
    foreach ($snapshot['policies'] ?? [] as $policy) {
        if (!hash_equals($steam,(string)($policy['steamId'] ?? ''))) continue;
        unset($policy['steamId']);
        if ($policy['status']==='Active' && strtotime($policy['endUtc'])<=time()) $policy['status']='Expired';
        elseif ($policy['status']==='Active' && strtotime($policy['startUtc'])>time()) $policy['status']='Scheduled';
        $mine[]=$policy;
    }
    $body=['ok'=>true,'generatedAtUtc'=>$snapshot['generatedAtUtc'] ?? null,'receivedAtUtc'=>$snapshot['receivedAtUtc'] ?? null,'policies'=>$mine,'vehicles'=>insurance_vehicles($steam)];
    $json=json_encode($body,JSON_THROW_ON_ERROR | JSON_UNESCAPED_UNICODE);
    $etag='"'.hash('sha256',$steam.'|'.$json).'"'; header('ETag: '.$etag);
    if (trim((string)($_SERVER['HTTP_IF_NONE_MATCH'] ?? ''))===$etag) { http_response_code(304); exit; }
    echo $json;
} catch (Throwable $e) { error_log('Insurance data: '.$e->getMessage()); insurance_reply(['ok'=>false,'error'=>'data_unavailable'],503); }
