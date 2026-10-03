<?php
declare(strict_types=1);

function startsWith(string $haystack, string $needle): bool {
    return $needle === '' || substr($haystack, 0, strlen($needle)) === $needle;
}

function normalizeLine(string $s): string {
    $s = trim($s);
    // UTF-8 BOM entfernen (Notepad)
    $s = preg_replace('/^\xEF\xBB\xBF/', '', $s) ?? $s;
    return $s;
}

function isSteamAdmin(string $steamId, string $filePath): bool {
    if (!is_file($filePath) || !is_readable($filePath)) {
        return false;
    }

    $steamId = normalizeLine($steamId);

    $lines = file($filePath, FILE_IGNORE_NEW_LINES | FILE_SKIP_EMPTY_LINES);
    if ($lines === false) return false;

    foreach ($lines as $line) {
        $line = normalizeLine($line);
        if ($line === '' || startsWith($line, '#')) continue;

        if (hash_equals($line, $steamId)) {
            return true;
        }
    }

    return false;
}

function initAdminFlag(): void {
    if (session_status() !== PHP_SESSION_ACTIVE) {
        session_start();
    }

    if (empty($_SESSION['steamid'])) {
        $_SESSION['isAdmin'] = false;
        $_SESSION['isAdminChecked'] = true;
        return;
    }

    // Cache optional – zum Testen kannst du die nächste Zeile aktivieren:
    // unset($_SESSION['isAdminChecked']);

    if (!empty($_SESSION['isAdminChecked'])) {
        return;
    }

    $adminFile = __DIR__ . '/adminsteamid.txt';

    $_SESSION['isAdmin'] = isSteamAdmin((string)$_SESSION['steamid'], $adminFile);
    $_SESSION['isAdminChecked'] = true;
}
