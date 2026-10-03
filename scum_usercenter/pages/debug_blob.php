<?php
declare(strict_types=1);
header('Content-Type: text/plain; charset=utf-8');

require_once __DIR__ . '/../functions/scum_user_function.php';

$steamId = '76561197976019189';

$userProfile = scum_get_user_profile_by_steamid($steamId);
$userProfileId = (int)($userProfile['id'] ?? 0);

$blob = scum_get_prisoner_body_blob_by_user_profile_id($userProfileId);

echo "DEBUG body_simulation (UE parse)\n";
echo "steamId: $steamId\n";
echo "user_profile.id: $userProfileId\n";
echo "blob_type: " . gettype($blob) . "\n";
echo "blob_len: " . ($blob !== null ? strlen($blob) : 0) . "\n\n";

if (!$blob) {
  exit("NO BLOB\n");
}

$keys = ['BaseStrength','BaseConstitution','BaseDexterity','BaseIntelligence'];

foreach ($keys as $k) {
    echo "=============================\n";
    echo "KEY: $k\n";
    $r = scum_debug_parse_double_property($blob, $k);

    echo "key_offset: " . var_export($r['key_offset'], true) . "\n";
    echo "after_key_byte(hex): " . var_export($r['after_key_byte'], true) . "\n";
    echo "lenType: " . var_export($r['lenType'], true) . "\n";
    echo "typeName: " . var_export($r['typeName'], true) . "\n";
    echo "propSize: " . var_export($r['propSize'], true) . "\n";
    echo "arrayIndex: " . var_export($r['arrayIndex'], true) . "\n";
    echo "valueHex: " . var_export($r['valueHex'], true) . "\n";
    echo "valueDouble: " . var_export($r['value'], true) . "\n";
    echo "ok: " . ($r['ok'] ? "YES" : "NO") . "\n";
    
    if (!$r['ok']) echo "error: " . $r['error'] . "\n";
    echo "\n";
}
$tests = ['BaseStrength','BaseConstitution','BaseDexterity','BaseIntelligence'];
foreach ($tests as $k) {
    $r = scum_debug_double_gist($blob, $k);
    echo "=============================\n";
    echo "KEY: {$k}\n";
    echo "keyOffset: {$r['keyOffset']}\n";
    echo "typeOffset: {$r['typeOffset']}\n";
    echo "valueOffset: {$r['valueOffset']}\n";
    echo "typeOk: " . ($r['typeOk'] ? 'YES' : 'NO') . "\n";
    echo "valueHex: {$r['valueHex']}\n";
    echo "value: " . var_export($r['value'], true) . "\n";
}
