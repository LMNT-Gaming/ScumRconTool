<?php
declare(strict_types=1);

require dirname(__DIR__) . '/lib/storage.php';

header('Content-Type: application/json; charset=utf-8');
header('Cache-Control: no-store');

function respond(int $status, array $body): void
{
    http_response_code($status);
    echo json_encode($body, JSON_UNESCAPED_SLASHES | JSON_UNESCAPED_UNICODE);
    exit;
}

if (($_SERVER['REQUEST_METHOD'] ?? '') !== 'POST') respond(405, ['ok' => false, 'error' => 'method_not_allowed']);
if ((int)($_SERVER['CONTENT_LENGTH'] ?? 0) > 16384) respond(413, ['ok' => false, 'error' => 'payload_too_large']);

$raw = file_get_contents('php://input');
$input = is_string($raw) ? json_decode($raw, true) : null;
if (!is_array($input)) respond(400, ['ok' => false, 'error' => 'invalid_json']);

$action = strtolower(trim((string)($input['action'] ?? 'heartbeat')));
$instanceId = strtolower(trim((string)($input['instance_id'] ?? '')));
$token = strtolower(trim((string)($input['instance_token'] ?? '')));
if (!preg_match('/^[a-f0-9]{32}$/', $instanceId) || !preg_match('/^[a-f0-9]{64}$/', $token)) {
    respond(400, ['ok' => false, 'error' => 'invalid_identity']);
}

try {
    if ($action === 'remove') {
        $removed = lmnt_store(function (array &$data) use ($instanceId, $token): bool {
            lmnt_prune($data);
            if (!isset($data['servers'][$instanceId])) return true;
            $stored = (string)($data['servers'][$instanceId]['token_hash'] ?? '');
            if ($stored === '' || !hash_equals($stored, lmnt_token_hash($token))) {
                throw new DomainException('forbidden');
            }
            unset($data['servers'][$instanceId]);
            return true;
        });
        respond(200, ['ok' => $removed, 'removed' => true]);
    }

    if ($action !== 'heartbeat') respond(400, ['ok' => false, 'error' => 'invalid_action']);
    $serverName = trim(strip_tags((string)($input['server_name'] ?? 'SCUM Server')));
    $serverName = preg_replace('/[\x00-\x1F\x7F]/u', '', $serverName) ?? 'SCUM Server';
    $serverName = substr($serverName === '' ? 'SCUM Server' : $serverName, 0, 80);
    $serverIp = trim((string)($input['server_ip'] ?? ''));
    $toolVersion = substr(trim((string)($input['tool_version'] ?? 'unknown')), 0, 30);
    if (filter_var($serverIp, FILTER_VALIDATE_IP, FILTER_FLAG_NO_PRIV_RANGE | FILTER_FLAG_NO_RES_RANGE) === false) {
        respond(400, ['ok' => false, 'error' => 'invalid_or_non_public_server_ip']);
    }
    if (!preg_match('/^[0-9A-Za-z.+_-]{1,30}$/', $toolVersion)) $toolVersion = 'unknown';

    lmnt_store(function (array &$data) use ($instanceId, $token, $serverName, $serverIp, $toolVersion): void {
        lmnt_prune($data);
        $now = time();
        $existing = $data['servers'][$instanceId] ?? null;
        if (is_array($existing)) {
            $stored = (string)($existing['token_hash'] ?? '');
            if ($stored === '' || !hash_equals($stored, lmnt_token_hash($token))) {
                throw new DomainException('forbidden');
            }
        } else {
            $remote = (string)($_SERVER['REMOTE_ADDR'] ?? 'unknown');
            $rateKey = hash_hmac('sha256', $remote, (string)lmnt_config()['rate_limit_salt']);
            $timestamps = $data['registrations'][$rateKey] ?? [];
            if (count($timestamps) >= (int)lmnt_config()['registration_limit_per_day']) {
                throw new OverflowException('registration_rate_limited');
            }
            $timestamps[] = $now;
            $data['registrations'][$rateKey] = $timestamps;
        }

        $data['servers'][$instanceId] = [
            'instance_id' => $instanceId,
            'token_hash' => lmnt_token_hash($token),
            'server_name' => $serverName,
            'server_ip' => $serverIp,
            'tool_version' => $toolVersion,
            'created_at' => is_array($existing) ? (int)($existing['created_at'] ?? $now) : $now,
            'last_seen' => $now,
        ];
    });
    respond(200, ['ok' => true]);
} catch (DomainException $exception) {
    respond(403, ['ok' => false, 'error' => $exception->getMessage()]);
} catch (OverflowException $exception) {
    respond(429, ['ok' => false, 'error' => $exception->getMessage()]);
} catch (Throwable $exception) {
    error_log('LMNT server browser: ' . $exception->getMessage());
    respond(500, ['ok' => false, 'error' => 'server_error']);
}
