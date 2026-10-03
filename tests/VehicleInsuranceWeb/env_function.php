<?php
// Test fixture only. Never load the production private/.env in offline tests.
function load_env(string $path): void { throw new RuntimeException('Unexpected private config in test'); }
