<?php
declare(strict_types=1);

function getShopImages(): array {
    $dir = __DIR__ . '/../assets/img/shop/';
    $webPath = 'assets/img/shop/';

    if (!is_dir($dir)) return [];

    $files = scandir($dir);
    $images = [];

    foreach ($files as $file) {
        if ($file === '.' || $file === '..') continue;

        if (preg_match('/\.(png|jpe?g|webp)$/i', $file)) {
            $images[] = [
                'file' => $file,
                'path' => $webPath . $file
            ];
        }
    }

    return $images;
}
