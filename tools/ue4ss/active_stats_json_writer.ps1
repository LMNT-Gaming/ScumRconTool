param(
    [Parameter(Mandatory = $true)][string]$ScumLog,
    [Parameter(Mandatory = $true)][string]$ProbeLog,
    [Parameter(Mandatory = $true)][string]$OutputJson,
    [int]$IntervalSeconds = 3
)

$ErrorActionPreference = 'Stop'

$statNames = @(
    'highest_positive_fame_points','doors_claimed','animals_killed','minutes_survived','kills','deaths','locks_picked','puppets_killed','guns_crafted','bullets_crafted','arrows_crafted','clothing_crafted','longest_kill_distance','melee_kills','archery_kills','players_knocked_out','total_defecations','total_urinations','lights_fired','containers_looted','items_put_into_containers','deaths_by_prisoners','animals_skinned','food_eaten','distance_travelled_by_foot','wounds_patched','items_picked_up','liquid_drank','teeth_lost','total_calories_intake','shots_fired','shots_hit','headshots','melee_weapon_swings','melee_weapon_hits','melee_weapons_crafted','drone_kills','sentry_kills','prisoner_kills','puppets_knocked_out','diarrheas','vomits','distance_travelled_in_vehicle','mushrooms_eaten','highest_muscle_mass','highest_fat','heart_attacks','overdose','starvation','highest_damage_taken','highest_weight_carried','lowest_negative_fame_points','distance_travelled_swimming','crows_killed','seagulls_killed','horses_killed','boars_killed','bears_killed','goats_killed','deers_killed','chickens_killed','rabbits_killed','donkeys_killed','times_mauled_by_bear','longest_animal_kill_distance','alcohol_drank','foliage_cut','distance_travel_by_boat','distance_sailed','times_caught_by_shark','times_escaped_shark_bite','wolves_killed','last_fame_point_award_consecutive_days','firearm_kills','bare_handed_kills'
)
$statSet = @{}
foreach ($name in $statNames) { $statSet[$name] = $true }

function Convert-Number([string]$text) {
    $integer = 0L
    if ([long]::TryParse($text, [Globalization.NumberStyles]::Integer, [Globalization.CultureInfo]::InvariantCulture, [ref]$integer)) { return $integer }
    $floating = 0.0
    if ([double]::TryParse($text, [Globalization.NumberStyles]::Float, [Globalization.CultureInfo]::InvariantCulture, [ref]$floating)) { return $floating }
    return $null
}

while ($true) {
    try {
        $active = @{}
        if (Test-Path -LiteralPath $ScumLog) {
            foreach ($line in Get-Content -LiteralPath $ScumLog) {
                if ($line -match "'(?:[^']* )?(?<steam>\d{17}):.*\((?<profile>\d+)\)' logged in at:") {
                    $active[$Matches.profile] = $Matches.steam
                }
                elseif ($line -match "'(?<steam>\d{17}):.*\((?<profile>\d+)\)' logged out at:") {
                    $active.Remove($Matches.profile)
                }
            }
        }

        $latest = @{}
        if (Test-Path -LiteralPath $ProbeLog) {
            foreach ($line in Get-Content -LiteralPath $ProbeLog) {
                if ($line -notmatch ' (?<event>LOAD_STATS|SAVE_STATS) .*arg_profile_id=(?<profile>\d+) ') { continue }
                $stats = [ordered]@{}
                foreach ($token in ($line -split ' ')) {
                    $separator = $token.IndexOf('=')
                    if ($separator -lt 1) { continue }
                    $key = $token.Substring(0, $separator)
                    if (-not $statSet.ContainsKey($key)) { continue }
                    $value = Convert-Number $token.Substring($separator + 1)
                    if ($null -ne $value) { $stats[$key] = $value }
                }
                $latest[$Matches.profile] = $stats
            }
        }

        $players = @()
        foreach ($profile in ($active.Keys | Sort-Object {[long]$_})) {
            if (-not $latest.ContainsKey($profile)) { continue }
            $players += [ordered]@{ steam_id = $active[$profile]; stats = $latest[$profile] }
        }

        $document = [ordered]@{
            schema_version = 1
            generated_at_utc = [DateTime]::UtcNow.ToString('o')
            players = $players
        }
        $json = $document | ConvertTo-Json -Depth 5
        $directory = Split-Path -Parent $OutputJson
        if (-not (Test-Path -LiteralPath $directory)) { New-Item -ItemType Directory -Path $directory -Force | Out-Null }
        $temporary = $OutputJson + '.tmp'
        [IO.File]::WriteAllText($temporary, $json, [Text.UTF8Encoding]::new($false))
        Move-Item -LiteralPath $temporary -Destination $OutputJson -Force
    }
    catch {
        # Keep the last valid JSON and retry; never interfere with the game server.
    }
    Start-Sleep -Seconds $IntervalSeconds
}
