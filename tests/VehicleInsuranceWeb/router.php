<?php
// Copied to an isolated temporary directory and bound exclusively to loopback by test.php.
if (parse_url($_SERVER['REQUEST_URI'], PHP_URL_PATH) === '/test-session') {
    session_start();
    $_SESSION['steamid'] = ($_GET['user'] ?? '') === 'one' ? '76561198000000001' : '76561198000000002';
    echo 'test session';
    return true;
}
return false;
