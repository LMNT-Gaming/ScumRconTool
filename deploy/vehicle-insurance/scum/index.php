<?php
declare(strict_types=1);

ini_set('display_errors', '1');
ini_set('display_startup_errors', '1');
error_reporting(E_ALL);
if (!headers_sent()) ob_start();

require_once __DIR__ . '/includes/auth_guard.php';
require_once __DIR__ . '/includes/admin_guard.php';
require_once __DIR__ . '/functions/scum_user_function.php';

requireSteamLogin();
initAdminFlag();

$headerStats = [
    'fame' => 0,
    'kuna' => 0,
    'gold' => 0,
];

if (!empty($_SESSION['steamid'])) {
    $steamId = (string) $_SESSION['steamid'];
    $profile = scum_get_user_profile_by_steamid($steamId);
    $profileId = (int) ($profile['id'] ?? 0);
    $balances = scum_get_balances_by_user_profile_id($profileId);

    $headerStats['fame'] = (int) round((float) ($profile['fame_points'] ?? 0));
    $headerStats['kuna'] = (int) ($balances['kuna'] ?? 0);
    $headerStats['gold'] = (int) ($balances['gold'] ?? 0);
}

$page = (string) ($_GET['page'] ?? 'home');
$routes = [
    'home' => __DIR__ . '/pages/home.php',
    'map' => __DIR__ . '/pages/map.php',
    'quests_overview' => __DIR__ . '/pages/quests_overview.php',
    'challenges' => __DIR__ . '/pages/challenges.php',
    'insurance' => __DIR__ . '/pages/insurance.php',
    'squad' => __DIR__ . '/pages/squad.php',
    'base' => __DIR__ . '/pages/base_stock.php',
    'stats' => __DIR__ . '/pages/stats.php',
    'gym' => __DIR__ . '/pages/gym.php',
    'casino' => __DIR__ . '/Casino/index.php',
    'admin' => __DIR__ . '/pages/admin.php',
];

if (!isset($routes[$page])) {
    http_response_code(404);
    $page = 'home';
}

$currentPage = $page;

include __DIR__ . '/includes/header.php';
include $routes[$page];
include __DIR__ . '/includes/footer.php';
