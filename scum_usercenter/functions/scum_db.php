<?php
// scum_db.php
declare(strict_types=1);

function scum_db_mark_syncing(string $reason = 'syncing'): void
{
    // pro Request merken: DB ist gerade nicht benutzbar
    $GLOBALS['SCUM_DB_SYNCING'] = true;
    $GLOBALS['SCUM_DB_REASON']  = $reason;
}

function scum_db_status(): array
{
    $isSyncing = !empty($GLOBALS['SCUM_DB_SYNCING']);

    return [
        'ok' => !$isSyncing,
        'reason' => $isSyncing ? (string)($GLOBALS['SCUM_DB_REASON'] ?? 'syncing') : 'ok',
    ];
}

function getScumDb(): SQLite3
{
    static $db = null;

    if ($db instanceof SQLite3) return $db;

    $path = __DIR__ . '/../scum_db/SCUM.db';

    try {
        $db = new SQLite3($path, SQLITE3_OPEN_READONLY);
        $db->enableExceptions(true);
        $db->busyTimeout(3000);

        // Minimaler Test (kann trotzdem später beim prepare knallen, deshalb zusätzlich try/catch in Funktionen)
        $db->querySingle("SELECT 1");

        return $db;
    } catch (Throwable $e) {
        $db = null;

        $msg = strtolower($e->getMessage());
        if (str_contains($msg, 'malformed') || str_contains($msg, 'not a database')) {
            scum_db_mark_syncing('copy_in_progress_or_malformed');
        } elseif (str_contains($msg, 'locked') || str_contains($msg, 'busy')) {
            scum_db_mark_syncing('locked');
        } else {
            scum_db_mark_syncing('unavailable');
        }

        throw $e; // getScumDbOrNull() fängt das ab
    }
}

function getScumDbOrNull(): ?SQLite3
{
    try {
        return getScumDb();
    } catch (Throwable $e) {
        return null;
    }
}
