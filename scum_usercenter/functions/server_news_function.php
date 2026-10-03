<?php

declare(strict_types=1);

require_once __DIR__ . '/db_function.php';
require_once __DIR__ . '/discord_config.php';

function news_list(int $limit = 30, bool $onlyPublished = false): array
{
    $pdo = db();
    $sql = "SELECT * FROM server_news ";
    if ($onlyPublished) $sql .= "WHERE is_published = 1 ";
    $sql .= "ORDER BY created_at DESC LIMIT :lim";
    $st = $pdo->prepare($sql);
    $st->bindValue(':lim', $limit, PDO::PARAM_INT);
    $st->execute();
    return $st->fetchAll(PDO::FETCH_ASSOC) ?: [];
}

function news_get(int $id): ?array
{
    $pdo = db();
    $st = $pdo->prepare("SELECT * FROM server_news WHERE id = :id LIMIT 1");
    $st->execute([':id' => $id]);
    $r = $st->fetch(PDO::FETCH_ASSOC);
    return is_array($r) ? $r : null;
}

function news_save(?int $id, string $title, string $body, bool $published, string $authorSteamId): array
{
    $title = trim($title);
    $body  = trim($body);

    if ($title === '' || mb_strlen($title) < 3) return ['ok' => false, 'msg' => 'Titel zu kurz.'];

    // body ist optional
    if (mb_strlen($body) > 20000) return ['ok' => false, 'msg' => 'Text zu lang.'];

    $pdo = db();

    if ($id) {
        $st = $pdo->prepare("
      UPDATE server_news
      SET title = :t, body = :b, is_published = :p, author_steamid = :a
      WHERE id = :id
    ");
        $st->execute([
            ':t' => $title,
            ':b' => $body,
            ':p' => $published ? 1 : 0,
            ':a' => $authorSteamId,
            ':id' => $id,
        ]);
        return ['ok' => true, 'msg' => 'News aktualisiert.', 'id' => $id];
    }

    $st = $pdo->prepare("
    INSERT INTO server_news (title, body, is_published, author_steamid)
    VALUES (:t, :b, :p, :a)
  ");
    $st->execute([
        ':t' => $title,
        ':b' => $body,
        ':p' => $published ? 1 : 0,
        ':a' => $authorSteamId,
    ]);

    return ['ok' => true, 'msg' => 'News erstellt.', 'id' => (int)$pdo->lastInsertId()];
}


/**
 * Discord Webhook Post (Embed)
 */
function news_build_discord_message(array $news): string
{
    $data = $news['discord_json'] ?? null;
    if (is_string($data)) {
        $decoded = json_decode($data, true);
        if (is_array($decoded)) $data = $decoded;
    }
    if (!is_array($data)) $data = [];

    $cats = $data['categories'] ?? [];
    if (!is_array($cats)) $cats = [];

    $lines = [];
    $lines[] = '# ' . date('d.m.Y');

    foreach ($cats as $c) {
        $name = trim((string)($c['name'] ?? ''));
        if ($name === '') continue;

        // Kategorie immer so:
        $lines[] = '> **' . $name . '**';

        $items = $c['items'] ?? [];
        if (!is_array($items)) $items = [];

        foreach ($items as $it) {
            $it = trim((string)$it);
            if ($it === '') continue;

            // eigene "-" weg, wir setzen sauber neu
            $it = ltrim($it, "- \t");

            // Bullet immer so:
            $lines[] = '> - ' . $it;
        }

        $lines[] = ''; // Abstand zwischen Kategorien
    }

    while (!empty($lines) && trim(end($lines)) === '') array_pop($lines);

    $msg = implode("\n", $lines);

    // Discord content limit
    if (mb_strlen($msg) > 1900) $msg = mb_substr($msg, 0, 1900) . "\n…";
    return $msg;
}
function news_build_discord_embeds(array $news): array
{
    $data = $news['discord_json'] ?? null;
    if (is_string($data)) {
        $decoded = json_decode($data, true);
        if (is_array($decoded)) $data = $decoded;
    }
    if (!is_array($data)) $data = [];

    $cats = $data['categories'] ?? [];
    if (!is_array($cats)) $cats = [];

    $embeds = [];

    // Haupt-Embed
    $title = trim((string)($news['title'] ?? 'Server News'));
    $embeds[] = [
        'title' => $title !== '' ? $title : 'Server News',
        'description' => date('d.m.Y'),
        'color' => 0x2F3136, // dark
    ];

    // Kategorie-Embeds (max 9 zusätzlich, weil 10 max)
    foreach ($cats as $c) {
        if (count($embeds) >= 10) break;

        $name  = trim((string)($c['name'] ?? ''));
        if ($name === '') continue;

        $colorHex = (string)($c['color'] ?? '#5865F2');
        $colorInt = discord_hex_to_int($colorHex);

        $items = $c['items'] ?? [];
        if (!is_array($items)) $items = [];

        $lines = [];
        foreach ($items as $it) {
            $it = trim((string)$it);
            if ($it === '') continue;
            $it = ltrim($it, "- \t");
            $lines[] = '• ' . $it;
        }
        if (!$lines) $lines[] = '• —';

        $desc = implode("\n", $lines);
        if (mb_strlen($desc) > 4000) $desc = mb_substr($desc, 0, 4000) . "\n…";

        $embeds[] = [
            'title' => $name,        // Kategorie fett durch Embed-Title
            'description' => $desc,
            'color' => $colorInt,
        ];
    }

    return $embeds;
}

function discord_hex_to_int(string $hex): int
{
    $hex = ltrim(trim($hex), '#');
    if (!preg_match('/^[0-9a-fA-F]{6}$/', $hex)) return 0x5865F2; // fallback
    return (int)hexdec($hex);
}
function news_post_to_discord(int $newsId): array
{
    $webhook = discord_webhook_url();
    if ($webhook === '') return ['ok' => false, 'msg' => 'Discord Webhook URL fehlt (discord_config.php / ENV).'];

    $news = news_get($newsId);
    if (!$news) return ['ok' => false, 'msg' => 'News nicht gefunden.'];


    $embeds = news_build_discord_embeds($news);

    $payload = [
        'username' => 'SCUM Server',
        'embeds'   => $embeds,
        'allowed_mentions' => ['parse' => []],
    ];


    $ch = curl_init($webhook);
    curl_setopt_array($ch, [
        CURLOPT_POST => true,
        CURLOPT_HTTPHEADER => ['Content-Type: application/json'],
        CURLOPT_POSTFIELDS => json_encode($payload, JSON_UNESCAPED_UNICODE),
        CURLOPT_RETURNTRANSFER => true,
        CURLOPT_TIMEOUT => 10,
    ]);

    $resp = curl_exec($ch);
    $code = (int)curl_getinfo($ch, CURLINFO_HTTP_CODE);
    $err  = curl_error($ch);
    curl_close($ch);

    if ($resp === false) return ['ok' => false, 'msg' => 'cURL Fehler: ' . $err];

    // Webhook returns 204 No Content by default (oder JSON, falls ?wait=true genutzt wird)
    if ($code !== 204 && ($code < 200 || $code >= 300)) {
        return ['ok' => false, 'msg' => "Discord Fehler ($code): " . (string)$resp];
    }


    $pdo = db();
    $st = $pdo->prepare("UPDATE server_news SET discord_posted_at = NOW() WHERE id = :id");
    $st->execute([':id' => $newsId]);

    return ['ok' => true, 'msg' => 'News zu Discord gepostet.'];
}
function news_delete(int $id): array {
  $pdo = db();
  $st = $pdo->prepare("DELETE FROM server_news WHERE id = :id");
  $st->execute([':id' => $id]);
  return ['ok'=>true, 'msg'=>'News gelöscht.'];
}