<?php
declare(strict_types=1);

ini_set('display_errors', '1');
ini_set('display_startup_errors', '1');
error_reporting(E_ALL);

/**
 * Download: den aktuellen PHP-Datei-Quelltext als Datei ausliefern
 * -> sicher, weil es ein expliziter Download-Endpunkt ist.
 */
if (isset($_GET['download']) && $_GET['download'] === '1') {
    $code = file_get_contents(__FILE__);
    if ($code === false) {
        http_response_code(500);
        exit('Could not read file.');
    }

    header('Content-Type: application/octet-stream; charset=utf-8');
    header('Content-Disposition: attachment; filename="scum_blob_decoder.php"');
    header('Content-Length: ' . strlen($code));
    echo $code;
    exit;
}

function extract_strings(string $bin, int $minLen, int $maxOut): array {
    $out = [];
    $len = strlen($bin);
    $cur = '';

    for ($i = 0; $i < $len; $i++) {
        $c = ord($bin[$i]);
        if ($c >= 32 && $c <= 126) {
            $cur .= chr($c);
        } else {
            if (strlen($cur) >= $minLen) {
                $out[] = $cur;
                if (count($out) >= $maxOut) break;
            }
            $cur = '';
        }
    }
    if (count($out) < $maxOut && strlen($cur) >= $minLen) {
        $out[] = $cur;
    }

    // dedupe preserve order
    $seen = [];
    $uniq = [];
    foreach ($out as $s) {
        if (!isset($seen[$s])) {
            $seen[$s] = true;
            $uniq[] = $s;
        }
    }
    return $uniq;
}

function scum_parse_body_blob_hex(string $hex): array
{
    $hex = preg_replace('/[^0-9a-fA-F]/', '', $hex) ?? '';
    if ($hex === '' || (strlen($hex) % 2) !== 0) {
        return ['ok' => false, 'error' => 'Invalid HEX input'];
    }

    $bin = hex2bin($hex);
    if ($bin === false) {
        return ['ok' => false, 'error' => 'hex2bin failed'];
    }

    $u32 = static function(string $s, int $off): ?int {
        if ($off < 0 || $off + 4 > strlen($s)) return null;
        $v = unpack('V', substr($s, $off, 4));
        return $v ? (int)$v[1] : null;
    };

    $readFloatLE = static function(string $s, int $off): ?float {
        if ($off < 0 || $off + 4 > strlen($s)) return null;
        $v = unpack('g', substr($s, $off, 4)); // little-endian float
        return $v ? (float)$v[1] : null;
    };
    $readDoubleLE = static function(string $s, int $off): ?float {
        if ($off < 0 || $off + 8 > strlen($s)) return null;
        $v = unpack('e', substr($s, $off, 8)); // little-endian double
        return $v ? (float)$v[1] : null;
    };
    $readInt64LE = static function(string $s, int $off): ?int {
        if ($off < 0 || $off + 8 > strlen($s)) return null;
        $v = unpack('P', substr($s, $off, 8)); // unsigned 64-bit LE
        return $v ? (int)$v[1] : null;
    };

    $findValue = static function(string $bin, string $propName, string $type, callable $reader) use ($u32) {
        $p = strpos($bin, $propName);
        if ($p === false) return null;

        $typePos = strpos($bin, $type, $p);
        if ($typePos === false) return null;

        $afterType = $typePos + strlen($type);
        $size = $u32($bin, $afterType);
        if ($size === null || $size <= 0 || $size > 2048) return null;

        $payloadOff = $afterType + 4 + 6; // size + 6 bytes zeros seen in SCUM blobs
        if ($payloadOff + $size > strlen($bin)) return null;

        return $reader($bin, $payloadOff);
    };

    return [
        'ok' => true,
        'base_strength'     => $findValue($bin, 'BaseStrength',     'DoubleProperty', $readDoubleLE),
        'base_constitution' => $findValue($bin, 'BaseConstitution', 'DoubleProperty', $readDoubleLE),
        'base_dexterity'    => $findValue($bin, 'BaseDexterity',    'DoubleProperty', $readDoubleLE),
        'base_intelligence' => $findValue($bin, 'BaseIntelligence', 'DoubleProperty', $readDoubleLE),
        'initial_age'       => $findValue($bin, 'InitialAge',       'FloatProperty',  $readFloatLE),
        'stamina'           => $findValue($bin, 'Stamina',          'FloatProperty',  $readFloatLE),
        'heart_rate'        => $findValue($bin, 'HeartRate',        'FloatProperty',  $readFloatLE),
        'breathing_rate'    => $findValue($bin, 'BreathingRate',    'FloatProperty',  $readFloatLE),
        'oxygen_saturation' => $findValue($bin, 'OxygenSaturation', 'FloatProperty',  $readFloatLE),
        'body_temperature'  => $findValue($bin, 'BodyTemperature',  'FloatProperty',  $readFloatLE),
        'time_of_death'     => $findValue($bin, 'TimeOfDeath',      'Int64Property',  $readInt64LE),
        'time_of_revive'    => $findValue($bin, 'TimeOfRevive',     'Int64Property',  $readInt64LE),
    ];
}

$hexInput = (string)($_POST['hex'] ?? '');
$minLen   = (int)($_POST['minLen'] ?? 6);
$maxOut   = (int)($_POST['maxOut'] ?? 300);

$info = '';
$result = '';
$error = '';
$stats = null; // ✅ wichtig, damit Template nicht “undefined” hat

if ($_SERVER['REQUEST_METHOD'] === 'POST') {
    if ($hexInput === '') {
        $error = "Kein Input.";
    } else {
        $hex = preg_replace('/[^0-9a-fA-F]/', '', $hexInput);
        if ($hex === null || $hex === '') {
            $error = "Nach dem Bereinigen ist nichts übrig. (Nur 0-9, a-f erlaubt)";
        } else {
            if ((strlen($hex) % 2) !== 0) {
                $hex = substr($hex, 0, -1);
            }

            $maxHexChars = 30_000_000;
            if (strlen($hex) > $maxHexChars) {
                $error = "Input zu groß (" . strlen($hex) . " Hex-Zeichen). Limit: $maxHexChars.";
            } else {
                $bin = hex2bin($hex);
                if ($bin === false) {
                    $error = "hex2bin() fehlgeschlagen. Der Input ist kein sauberes Hex.";
                } else {
                    $strings = extract_strings($bin, max(3, $minLen), max(10, $maxOut));
                    $stats = scum_parse_body_blob_hex($hex);

                    $info = "HEX: " . strlen($hex) . " chars | BIN: " . strlen($bin) . " bytes | Strings: " . count($strings);

                    $lines = [];
                    foreach ($strings as $s) {
                        if (
                            str_contains($s, 'ConZ.') ||
                            str_contains($s, '/Script/') ||
                            str_contains($s, 'Property') ||
                            preg_match('/\b(Base|Time|Body|Stamina|Heart|Oxygen|Temperature|Metabolism|Prisoner)\b/i', $s)
                        ) {
                            $lines[] = $s;
                        }
                    }
                    if (!$lines) $lines = $strings;

                    $result = implode("\n", $lines);
                }
            }
        }
    }
}
?>
<!doctype html>
<html lang="de">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>SCUM Blob → Text (Strings)</title>
  <style>
    body { font-family: system-ui, sans-serif; margin: 18px; }
    textarea { width: 100%; height: 260px; font-family: ui-monospace, SFMono-Regular, Menlo, monospace; }
    .row { display:flex; gap:12px; flex-wrap:wrap; margin: 10px 0; }
    .row > div { display:flex; align-items:center; gap:8px; }
    .err { color: #b00020; white-space: pre-wrap; }
    .info { color: #0b5; white-space: pre-wrap; }
    pre { background:#111; color:#eee; padding:12px; overflow:auto; border-radius:10px; }
    button, a.btn { padding:10px 14px; display:inline-block; text-decoration:none; }
    a.btn { border: 1px solid #ccc; border-radius: 8px; }
  </style>
</head>
<body>

<h2>SCUM / UE4 Blob → Menschenlesbarer Text (Strings)</h2>

<form method="post">
  <label>Hex-BLOB (Copy as Hex aus DB Browser, darf Zeilenumbrüche enthalten):</label>
  <textarea name="hex"><?= htmlspecialchars($hexInput, ENT_QUOTES) ?></textarea>

  <div class="row">
    <div>
      <label>Min String-Länge</label>
      <input type="number" name="minLen" value="<?= htmlspecialchars((string)$minLen, ENT_QUOTES) ?>" min="3" max="50">
    </div>
    <div>
      <label>Max Ausgabe</label>
      <input type="number" name="maxOut" value="<?= htmlspecialchars((string)$maxOut, ENT_QUOTES) ?>" min="10" max="2000">
    </div>
    <div>
      <button type="submit">Dekodieren</button>
    </div>
  </div>
</form>

<?php if ($error): ?>
  <div class="err"><b>Fehler:</b> <?= htmlspecialchars($error, ENT_QUOTES) ?></div>
<?php endif; ?>

<?php if ($info): ?>
  <div class="info"><b>Info:</b> <?= htmlspecialchars($info, ENT_QUOTES) ?></div>
<?php endif; ?>

<?php if (is_array($stats) && !empty($stats['ok'])): ?>
  <h3>Parsed Stats</h3>
  <pre><?= htmlspecialchars(json_encode($stats, JSON_PRETTY_PRINT | JSON_UNESCAPED_UNICODE), ENT_QUOTES) ?></pre>
<?php elseif (is_array($stats) && empty($stats['ok']) && !empty($stats['error'])): ?>
  <div class="err"><b>Stats-Parser:</b> <?= htmlspecialchars((string)$stats['error'], ENT_QUOTES) ?></div>
<?php endif; ?>

<?php if ($result): ?>
  <h3>Gefundene Strings</h3>
  <pre><?= htmlspecialchars($result, ENT_QUOTES) ?></pre>
<?php endif; ?>

<h3>Tool herunterladen</h3>
<a class="btn" href="?download=1">⬇ PHP-Datei herunterladen</a>

</body>
</html>
