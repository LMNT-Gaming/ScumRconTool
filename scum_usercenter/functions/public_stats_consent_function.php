<?php
declare(strict_types=1);

require_once __DIR__ . '/db_function.php';

const PUBLIC_STATS_REVOKE_LOCK_SECONDS = 86400; // 24h

function public_stats_get_consent(string $steamid): array
{
    $pdo = db();
    $stmt = $pdo->prepare("
        SELECT steamid, consent, show_name, consented_at, locked_until
        FROM public_stats_consent
        WHERE steamid = :sid
        LIMIT 1
    ");
    $stmt->execute([':sid' => $steamid]);
    $row = $stmt->fetch();

    if (!$row) {
        return [
            'steamid' => $steamid,
            'consent' => 0,
            'show_name' => 1,
            'consented_at' => null,
            'locked_until' => null,
        ];
    }

    return [
        'steamid' => (string)$row['steamid'],
        'consent' => (int)$row['consent'],
        'show_name' => (int)$row['show_name'],
        'consented_at' => $row['consented_at'],
        'locked_until' => $row['locked_until'],
    ];
}

/**
 * Setzt consent/show_name.
 * Regel: Wechsel von consent=1 -> 0 ist erst nach locked_until erlaubt.
 * locked_until wird beim Opt-In auf jetzt + 24h gesetzt.
 *
 * Rückgabe: ['ok'=>bool,'msg'=>string]
 */
function public_stats_set_consent(string $steamid, bool $consent, bool $showName = true): array
{
    $pdo = db();

    // Aktuellen Zustand holen (für Lock-Check)
    $cur = public_stats_get_consent($steamid);

    $now = time();
    $lockedUntilTs = null;
    if (!empty($cur['locked_until'])) {
        $lockedUntilTs = strtotime((string)$cur['locked_until']) ?: null;
    }

    // Widerruf blocken, wenn noch gelockt
    if ($cur['consent'] === 1 && $consent === false) {
        if ($lockedUntilTs !== null && $now < $lockedUntilTs) {
            return [
                'ok' => false,
                'msg' => 'Widerruf erst möglich ab: ' . date('d.m.Y H:i', $lockedUntilTs),
            ];
        }
    }

    $consentedAt = $consent ? date('Y-m-d H:i:s') : null;

    // Beim Opt-In lock_until = now + 24h, beim Opt-Out lassen wir es leer
    $lockedUntil = null;
    if ($consent) {
        $lockedUntil = date('Y-m-d H:i:s', $now + PUBLIC_STATS_REVOKE_LOCK_SECONDS);
    }

    $stmt = $pdo->prepare("
        INSERT INTO public_stats_consent
            (steamid, consent, show_name, consented_at, locked_until)
        VALUES
            (:sid, :consent, :show_name, :consented_at, :locked_until)
        ON DUPLICATE KEY UPDATE
            consent = VALUES(consent),
            show_name = VALUES(show_name),
            consented_at = VALUES(consented_at),
            locked_until = VALUES(locked_until)
    ");

    $stmt->execute([
        ':sid' => $steamid,
        ':consent' => $consent ? 1 : 0,
        ':show_name' => $showName ? 1 : 0,
        ':consented_at' => $consentedAt,
        ':locked_until' => $lockedUntil,
    ]);

    return ['ok' => true, 'msg' => 'Einstellungen gespeichert.'];
}

function public_stats_list_allowed_steamids(): array
{
    $pdo = db();
    $stmt = $pdo->query("
        SELECT steamid, show_name
        FROM public_stats_consent
        WHERE consent = 1
    ");

    $out = [];
    while ($row = $stmt->fetch()) {
        $out[(string)$row['steamid']] = (int)$row['show_name'];
    }
    return $out; // [steamid => show_name]
}
