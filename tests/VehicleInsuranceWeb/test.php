<?php
declare(strict_types=1);
$stage = dirname(__DIR__, 2) . '/deploy/vehicle-insurance/scum';
$root = sys_get_temp_dir() . '/redraven-insurance-web-' . bin2hex(random_bytes(6));
foreach (['api','functions','private/insurance_data'] as $dir) mkdir($root . '/' . $dir, 0700, true);
foreach (['api/insurance_push.php','api/insurance_data.php','functions/insurance_function.php'] as $file) {
    if (!copy($stage . '/' . $file, $root . '/' . $file)) throw new RuntimeException('Copy failed');
}
copy(__DIR__ . '/env_function.php', $root . '/functions/env_function.php');
copy(__DIR__ . '/scum_user_function.php', $root . '/functions/scum_user_function.php');
copy(__DIR__ . '/router.php', $root . '/router.php');
$socket = stream_socket_server('tcp://127.0.0.1:0', $errno, $error);
if (!$socket) throw new RuntimeException($error);
$address = stream_socket_get_name($socket, false); fclose($socket);
$base = 'http://' . $address;
$token = 'OFFLINE-TEST-TOKEN-NOT-A-REAL-SECRET';
$env = getenv(); $env['INSURANCE_PUSH_TOKEN'] = $token; $env['INSURANCE_DATA_DIR'] = $root . '/private/insurance_data';
$process = proc_open([PHP_BINARY, '-d', 'session.save_path=' . $root, '-S', $address, '-t', $root, $root . '/router.php'],
    [0=>['pipe','r'], 1=>['file',$root.'/server.log','a'], 2=>['file',$root.'/server.log','a']], $pipes, $root, $env);
if (!is_resource($process)) throw new RuntimeException('Cannot start offline test server');
$passed = 0;
function check(bool $value, string $label): void { global $passed; if (!$value) throw new RuntimeException('FAIL ' . $label); echo "PASS $label\n"; $passed++; }
function request(string $path, string $method='GET', array $headers=[], string $body=''): array {
    global $base;
    $context=stream_context_create(['http'=>['method'=>$method,'header'=>implode("\r\n",$headers), 'content'=>$body,'ignore_errors'=>true,'timeout'=>3]]);
    $content=@file_get_contents($base.$path,false,$context);
    $responseHeaders=$http_response_header ?? [];
    preg_match('/\s(\d{3})\s/',$responseHeaders[0] ?? '',$match);
    return [(int)($match[1] ?? 0),$content === false ? '' : $content,$responseHeaders];
}
function headerValue(array $headers, string $key): string {
    foreach ($headers as $header) if (stripos($header,$key.':')===0) return trim(substr($header,strlen($key)+1));
    return '';
}
try {
    fclose($pipes[0]);
    for ($i=0;$i<50;$i++) { if (request('/api/insurance_data.php')[0]===401) break; usleep(100000); }
    check(request('/api/insurance_data.php')[0]===401,'anonymous access blocked');
    check(request('/api/insurance_push.php')[0]===405,'push requires POST');
    check(request('/api/insurance_push.php','POST')[0]===403,'wrong token blocked');
    $policy=['id'=>'V-012345ABCDEF','steamId'=>'76561198000000001','vehicleId'=>6382416,'vehicleName'=>'Barba',
      'playerName'=>'Tester','spawnClass'=>'BPC_Barba','weeks'=>1,'price'=>10000,'status'=>'Active',
      'startUtc'=>gmdate('c',time()-60),'endUtc'=>gmdate('c',time()+3600),'destroyedUtc'=>null,'claimedUtc'=>null,
      'history'=>['Must never be published'],'secret'=>'Must never be published'];
    $other=$policy; $other['id']='V-FFFFFFFFFFFF'; $other['steamId']='76561198000000002'; $other['vehicleName']='Other player vehicle';
    $snapshot=['schemaVersion'=>1,'generatedAtUtc'=>gmdate('c'),'policies'=>[$policy,$other]];
    $headers=['Content-Type: application/json','X-Insurance-Token: '.$token];
    check(request('/api/insurance_push.php','POST',$headers,json_encode($snapshot))[0]===200,'authenticated snapshot accepted');
    $stored=file_get_contents($root.'/private/insurance_data/snapshot.json');
    check(!str_contains($stored,'Must never') && !str_contains($stored,'history'),'push strips internal fields');
    $cookie=explode(';',headerValue(request('/test-session?user=one')[2],'Set-Cookie'))[0];
    [$code,$raw,$responseHeaders]=request('/api/insurance_data.php?steamId=76561198000000002','GET',['Cookie: '.$cookie]);
    $data=json_decode($raw,true,32,JSON_THROW_ON_ERROR);
    check($code===200 && count($data['policies'])===1 && $data['policies'][0]['vehicleName']==='Barba','session identity wins over supplied SteamID');
    check(!str_contains($raw,'76561198000000002') && !isset($data['policies'][0]['steamId']),'other player and SteamIDs not exposed');
    check($data['vehicles']['status']==='ok' && array_column($data['vehicles']['items'],'id')===['6382416','7777777'],'own insured and uninsured vehicles loaded via session');
    check(!str_contains($raw,'9999999'),'foreign vehicle omitted despite query parameter');
    $etag=headerValue($responseHeaders,'ETag');
    check($etag!=='' && request('/api/insurance_data.php','GET',['Cookie: '.$cookie,'If-None-Match: '.$etag])[0]===304,'unchanged response uses ETag');
    $cookie2=explode(';',headerValue(request('/test-session?user=two')[2],'Set-Cookie'))[0];
    $data2=json_decode(request('/api/insurance_data.php','GET',['Cookie: '.$cookie2])[1],true);
    check(count($data2['policies'])===1 && $data2['policies'][0]['vehicleName']==='Other player vehicle','second account sees only own contract');
    check(array_column($data2['vehicles']['items'],'id')===['9999999'],'vehicle list isolated for second account');
    $stale=$snapshot; $stale['generatedAtUtc']=gmdate('c',time()-3600);
    check(request('/api/insurance_push.php','POST',$headers,json_encode($stale))[0]===409,'stale snapshot cannot overwrite newer data');
    $invalid=$snapshot; $invalid['policies'][0]['price']=-1;
    check(request('/api/insurance_push.php','POST',$headers,json_encode($invalid))[0]===400,'invalid premium rejected');
    $invalid=$snapshot; $invalid['policies'][]=$policy;
    check(request('/api/insurance_push.php','POST',$headers,json_encode($invalid))[0]===400,'duplicate contracts rejected');
    check(request('/api/insurance_push.php','POST',$headers,'{bad json')[0]===400,'malformed JSON rejected');
    file_put_contents($root.'/db-unavailable','offline failure fixture');
    [$partialCode,$partialRaw]=request('/api/insurance_data.php','GET',['Cookie: '.$cookie,'If-None-Match: '.$etag]);
    $partial=json_decode($partialRaw,true);
    check($partialCode===200 && count($partial['policies'])===1 && $partial['vehicles']['status']==='unavailable' && $partial['vehicles']['items']===null,'DB outage preserves policies and invalidates ETag');
    echo "$passed checks passed. Offline test data: $root\n";
} finally {
    proc_terminate($process); proc_close($process);
}
