<?php
declare(strict_types=1);
header('Content-Type: application/json; charset=utf-8');
header('Cache-Control: no-store');
require_once __DIR__ . '/../functions/insurance_function.php';
if (($_SERVER['REQUEST_METHOD'] ?? '') !== 'POST') insurance_reply(['ok'=>false,'error'=>'method_not_allowed'],405);
try {
    insurance_load_config();
    $expected = insurance_env('INSURANCE_PUSH_TOKEN');
    if (strlen($expected) < 16) insurance_reply(['ok'=>false,'error'=>'push_token_not_configured'],503);
    $given = (string)($_SERVER['HTTP_X_INSURANCE_TOKEN'] ?? '');
    if ($given === '' || !hash_equals($expected,$given)) insurance_reply(['ok'=>false,'error'=>'forbidden'],403);
    $raw = file_get_contents('php://input', false, null, 0, 4*1024*1024+1);
    if ($raw === false || strlen($raw) > 4*1024*1024) insurance_reply(['ok'=>false,'error'=>'payload_too_large'],413);
    $body = json_decode($raw,true,32,JSON_THROW_ON_ERROR | JSON_BIGINT_AS_STRING);
    if (!is_array($body) || ($body['schemaVersion'] ?? 0) !== 1 || !is_array($body['policies'] ?? null) || count($body['policies']) > 10000)
        insurance_reply(['ok'=>false,'error'=>'invalid_snapshot'],400);
    $generated = insurance_date($body['generatedAtUtc'] ?? null);
    if ($generated === null || strtotime($generated) > time()+300) insurance_reply(['ok'=>false,'error'=>'invalid_timestamp'],400);
    $policies=[];
    foreach ($body['policies'] as $p) {
        if (!is_array($p)) throw new InvalidArgumentException('Invalid policy');
        $clean=insurance_policy($p);
        if (isset($policies[$clean['id']])) throw new InvalidArgumentException('Duplicate policy');
        $policies[$clean['id']]=$clean;
    }
    $dir=insurance_dir();
    if (!is_dir($dir) && !mkdir($dir,0700,true) && !is_dir($dir)) throw new RuntimeException('Storage directory unavailable');
    $lock=fopen($dir.'/snapshot.lock','c');
    if ($lock===false || !flock($lock,LOCK_EX)) throw new RuntimeException('Lock unavailable');
    try {
        $old=insurance_read();
        if (isset($old['generatedAtUtc']) && strtotime($old['generatedAtUtc']) > strtotime($generated)) insurance_reply(['ok'=>false,'error'=>'stale_snapshot'],409);
        $snapshot=['schemaVersion'=>1,'generatedAtUtc'=>$generated,'receivedAtUtc'=>gmdate('c'),'policies'=>array_values($policies)];
        $json=json_encode($snapshot,JSON_THROW_ON_ERROR | JSON_UNESCAPED_UNICODE);
        $tmp=tempnam($dir,'snapshot-');
        if ($tmp===false || file_put_contents($tmp,$json,LOCK_EX)!==strlen($json)) throw new RuntimeException('Write failed');
        if (!rename($tmp,$dir.'/snapshot.json')) throw new RuntimeException('Commit failed');
    } finally { flock($lock,LOCK_UN); fclose($lock); }
    insurance_reply(['ok'=>true]);
} catch (JsonException|InvalidArgumentException $e) { insurance_reply(['ok'=>false,'error'=>'invalid_payload'],400); }
catch (Throwable $e) { error_log('Insurance push: '.$e->getMessage()); insurance_reply(['ok'=>false,'error'=>'storage_error'],500); }
