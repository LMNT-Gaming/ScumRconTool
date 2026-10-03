<?php

declare(strict_types=1);

if (session_status() !== PHP_SESSION_ACTIVE) session_start();

if (empty($_SESSION['steamid'])) {
    http_response_code(401);
    exit('Bitte einloggen.');
}

require_once __DIR__ . '/../functions/quest_function.php';
$currentPage = 'quests_overview';

$quests = $quests ?? null;

if (!is_array($quests)) {
    if (function_exists('quests_list')) {
        $quests = quests_list();
    } elseif (function_exists('quest_list')) {
        $quests = quest_list();
    } elseif (function_exists('read_quests')) {
        $quests = read_quests(__DIR__ . '/../Quests');
    } else {
        $quests = [];
    }
}

// Fallback, falls quest_function.php nichts liefert.
if (!is_array($quests) || !count($quests)) {
    $questDirCandidates = [
        __DIR__ . '/../Quests',
        __DIR__ . '/../quests',
        __DIR__ . '/quests',
    ];

    foreach ($questDirCandidates as $dir) {
        if (!is_dir($dir)) continue;

        $loaded = [];
        foreach (glob($dir . '/*.json') ?: [] as $file) {
            $raw = file_get_contents($file);
            if ($raw === false) continue;
            $data = json_decode($raw, true);
            if (!is_array($data)) continue;
            $data['_file'] = basename($file);
            $data['_mtime'] = filemtime($file) ?: 0;
            $loaded[] = $data;
        }

        if ($loaded) {
            $quests = $loaded;
            break;
        }
    }
}

function h($s): string
{
    return htmlspecialchars((string)$s, ENT_QUOTES, 'UTF-8');
}
?>
<style>
        :root {
            --q-bg: rgba(0, 0, 0, .30);
            --q-bg2: rgba(255, 255, 255, .035);
            --q-border: rgba(148, 163, 184, .18);
            --q-border-strong: rgba(255, 255, 255, .45);
            --q-text-soft: rgba(226, 232, 240, .78);
            --q-good: rgb(134, 239, 172);
            --q-warn: rgb(253, 224, 71);
            --q-info: rgb(147, 197, 253);
            --q-bad: rgb(252, 165, 165);
        }

        .layout-3col {
            gap: 14px;
        }

        .side.left,
        .side.right {
            height: 80vh;
        }

        .panel.panel-left,
        .panel.panel-right,
        .center > .panel {
            height: 80vh;
            display: flex;
            flex-direction: column;
            min-height: 0;
        }

        .panel-section {
            min-height: 0;
            overflow: hidden;
        }

        .side .panel-section {
            overflow: auto;
            padding-right: 4px;
        }

        .side .panel-section::-webkit-scrollbar,
        #centerList::-webkit-scrollbar,
        .itemchips::-webkit-scrollbar {
            width: 8px;
            height: 8px;
        }

        .side .panel-section::-webkit-scrollbar-thumb,
        #centerList::-webkit-scrollbar-thumb,
        .itemchips::-webkit-scrollbar-thumb {
            background: rgba(148, 163, 184, .25);
            border-radius: 999px;
        }

        #centerList {
            overflow: auto;
            height: 100%;
            padding-right: 6px;
        }

        .input,
        select.input,
        input.input {
            width: 100%;
            box-sizing: border-box;
        }

        .filter-block {
            border: 1px solid var(--q-border);
            background: var(--q-bg);
            border-radius: 14px;
            padding: 10px;
            margin-bottom: 10px;
        }

        .filter-grid {
            display: grid;
            gap: 8px;
        }

        .filter-grid label {
            display: block;
            font-size: 11px;
            font-weight: 900;
            letter-spacing: .9px;
            text-transform: uppercase;
            color: var(--q-text-soft);
            margin-bottom: 4px;
        }

        .checkline {
            display: flex;
            align-items: center;
            gap: 8px;
            font-size: 12px;
            color: var(--q-text-soft);
            margin-top: 7px;
        }

        .btnrow {
            display: flex;
            gap: 8px;
            flex-wrap: wrap;
            margin-top: 9px;
        }

        .btn {
            cursor: pointer;
        }

        .mini-stats {
            display: grid;
            grid-template-columns: 1fr 1fr;
            gap: 8px;
            margin-bottom: 10px;
        }

        .statbox {
            border: 1px solid var(--q-border);
            background: rgba(0, 0, 0, .24);
            border-radius: 12px;
            padding: 9px;
        }

        .statbox b {
            display: block;
            font-size: 20px;
            line-height: 1;
        }

        .statbox span {
            font-size: 11px;
            color: var(--q-text-soft);
            text-transform: uppercase;
            letter-spacing: .8px;
        }

        .quest-mini-list {
            display: flex;
            flex-direction: column;
            gap: 7px;
        }

        .quest-mini {
            border: 1px solid var(--q-border);
            background: rgba(0, 0, 0, .25);
            border-radius: 12px;
            padding: 9px;
            cursor: pointer;
        }

        .quest-mini:hover,
        .quest-mini.active {
            border-color: var(--q-border-strong);
            background: rgba(255, 255, 255, .055);
        }

        .quest-mini-title {
            font-weight: 900;
            font-size: 13px;
            line-height: 1.25;
        }

        .quest-mini-sub {
            color: var(--q-text-soft);
            font-size: 11px;
            margin-top: 3px;
        }

        .qcard {
            border: 1px solid var(--q-border);
            border-radius: 16px;
            background: var(--q-bg);
            padding: 12px;
            margin-bottom: 12px;
        }

        .qcard.active {
            border-color: var(--q-border-strong);
            box-shadow: 0 0 0 2px rgba(0, 0, 0, .35) inset;
        }

        .qhead {
            display: flex;
            justify-content: space-between;
            align-items: flex-start;
            gap: 12px;
            flex-wrap: wrap;
        }

        .qtitle {
            font-size: 17px;
            font-weight: 950;
            letter-spacing: .2px;
        }

        .qdesc {
            margin-top: 8px;
            color: var(--q-text-soft);
            font-size: 13px;
            line-height: 1.42;
        }

        .qmeta,
        .muted2 {
            color: var(--q-text-soft);
            font-size: 12px;
        }

        .pill {
            display: inline-flex;
            align-items: center;
            gap: 4px;
            padding: 4px 8px;
            border-radius: 999px;
            border: 1px solid rgba(255, 255, 255, .10);
            background: rgba(0, 0, 0, .35);
            font-size: 11px;
            font-weight: 900;
            letter-spacing: .35px;
            margin: 3px 4px 3px 0;
            white-space: nowrap;
        }

        .pill-fetch { border-color: rgba(34, 197, 94, .45); color: var(--q-good); background: rgba(34, 197, 94, .10); }
        .pill-interact { border-color: rgba(59, 130, 246, .45); color: var(--q-info); background: rgba(59, 130, 246, .10); }
        .pill-kill { border-color: rgba(248, 113, 113, .45); color: var(--q-bad); background: rgba(248, 113, 113, .10); }
        .pill-kuna { border-color: rgba(34, 197, 94, .55); color: var(--q-good); background: rgba(34, 197, 94, .10); }
        .pill-fame { border-color: rgba(250, 204, 21, .55); color: var(--q-warn); background: rgba(250, 204, 21, .10); }
        .pill-skill { border-color: rgba(59, 130, 246, .55); color: var(--q-info); background: rgba(59, 130, 246, .10); }
        .pill-gold { border-color: rgba(234, 179, 8, .55); color: rgb(253, 230, 138); background: rgba(234, 179, 8, .10); }
        .pill-deal { border-color: rgba(168, 85, 247, .55); color: rgb(216, 180, 254); background: rgba(168, 85, 247, .10); }

        .sectionline {
            margin-top: 11px;
            border-top: 1px solid rgba(148, 163, 184, .12);
            padding-top: 10px;
        }

        .small-title {
            font-size: 11px;
            font-weight: 950;
            letter-spacing: 1px;
            text-transform: uppercase;
            color: var(--q-text-soft);
            margin-bottom: 6px;
        }

        .need-list {
            display: grid;
            gap: 8px;
        }

        .need-row {
            border: 1px solid rgba(148, 163, 184, .15);
            border-radius: 12px;
            background: var(--q-bg2);
            padding: 9px;
        }

        .need-main {
            display: flex;
            justify-content: space-between;
            gap: 10px;
            align-items: flex-start;
            flex-wrap: wrap;
        }

        .need-caption {
            font-weight: 900;
            font-size: 13px;
        }

        .need-extra {
            color: var(--q-text-soft);
            font-size: 11px;
            margin-top: 4px;
        }

        .itemchips {
            display: flex;
            flex-wrap: wrap;
            gap: 5px;
            max-height: 105px;
            overflow: auto;
            padding-top: 7px;
        }

        .itemchip {
            display: inline-flex;
            align-items: center;
            border: 1px solid rgba(148, 163, 184, .18);
            border-radius: 999px;
            padding: 4px 8px;
            background: rgba(0, 0, 0, .24);
            font-size: 11px;
            font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
            color: rgba(226, 232, 240, .92);
        }

        .steps {
            display: grid;
            gap: 7px;
        }

        .step {
            display: grid;
            grid-template-columns: 26px 1fr;
            gap: 8px;
            align-items: start;
        }

        .stepnum {
            width: 24px;
            height: 24px;
            border-radius: 999px;
            display: flex;
            align-items: center;
            justify-content: center;
            border: 1px solid rgba(255, 255, 255, .16);
            background: rgba(0, 0, 0, .35);
            font-weight: 900;
            font-size: 11px;
        }

        .qmap {
            margin-top: 10px;
            border: 1px solid rgba(148, 163, 184, .18);
            border-radius: 12px;
            background: rgba(0, 0, 0, .25);
            padding: 8px;
        }

        .qmap canvas {
            display: block;
            width: 100%;
            height: 180px;
            border-radius: 10px;
        }

        .qmap .cap {
            margin-top: 6px;
            font-size: 12px;
            color: var(--q-text-soft);
        }

        .detail-empty {
            color: var(--q-text-soft);
            font-size: 13px;
            line-height: 1.4;
        }

        .detail-title {
            font-weight: 950;
            font-size: 16px;
        }

        .detail-scroll {
            max-height: 56vh;
            overflow: auto;
            padding-right: 2px;
        }

        @media (max-width: 1100px) {
            .layout-3col {
                display: block;
            }

            .side.left,
            .side.right,
            .panel.panel-left,
            .panel.panel-right,
            .center > .panel {
                height: auto;
                max-height: none;
                margin-bottom: 14px;
            }

            #centerList {
                height: auto;
                max-height: none;
            }
        }
    </style>

<main class="content layout-3col">
    <aside class="side left">
        <section class="panel panel-left">
            <div class="panel-topbar">
                <div class="panel-topbar-title">QUEST-FILTER</div>
            </div>

            <div class="panel-section">
                <div class="filter-block">
                    <div class="filter-grid">
                        <div>
                            <label for="fSearch">Suche</label>
                            <input id="fSearch" class="input" placeholder="Titel, Item, Beschreibung, Datei...">
                        </div>
                        <div>
                            <label for="fNpc">Trader</label>
                            <select id="fNpc" class="input"><option value="">Alle Trader</option></select>
                        </div>
                        <div>
                            <label for="fTier">Tier</label>
                            <select id="fTier" class="input">
                                <option value="">Alle Tiers</option>
                                <option value="1">Tier 1</option>
                                <option value="2">Tier 2</option>
                                <option value="3">Tier 3</option>
                            </select>
                        </div>
                        <div>
                            <label for="fType">Questtyp</label>
                            <select id="fType" class="input">
                                <option value="">Alle Typen</option>
                            </select>
                        </div>
                        <div>
                            <label for="fItem">Item enthält</label>
                            <input id="fItem" class="input" placeholder="z.B. Vodka, Battery...">
                        </div>
                        <label class="checkline">
                            <input id="fOnlyItems" type="checkbox">
                            <span>Nur Quests mit benötigten Items</span>
                        </label>
                        <label class="checkline">
                            <input id="fOnlyMap" type="checkbox">
                            <span>Nur Quests mit Map/Interaction</span>
                        </label>
                    </div>
                    <div class="btnrow">
                        <button id="btnReset" class="btn">Reset</button>
                    </div>
                </div>

                <div class="mini-stats">
                    <div class="statbox"><b id="statFound">0</b><span>Gefunden</span></div>
                    <div class="statbox"><b id="statItems">0</b><span>mit Items</span></div>
                </div>

                <div class="small-title">Gefilterte Quests</div>
                <div id="questMiniList" class="quest-mini-list"></div>
            </div>
        </section>
    </aside>

    <section class="center">
        <section class="panel">
            <div class="panel-topbar">
                <div class="panel-topbar-title">QUESTÜBERSICHT</div>
            </div>
            <div class="panel-section" style="overflow:hidden;">
                <div id="centerStats" class="muted" style="margin-bottom:10px; font-size:12px;"></div>
                <div id="centerList"></div>
            </div>
        </section>
    </section>

    <aside class="side right">
        <section class="panel panel-right">
            <div class="panel-topbar">
                <div class="panel-topbar-title">DETAILS</div>
            </div>
            <div class="panel-section">
                <div id="questDetail" class="filter-block detail-empty">Klick auf eine Quest, dann siehst du hier kompakt: was zu tun ist, welche Items erlaubt sind und welche Interactions/Mapmarker dazugehören.</div>
            </div>
        </section>
    </aside>
</main>

<script>
const QUESTS = <?= json_encode(array_values($quests ?: []), JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES) ?>;

const el = (id) => document.getElementById(id);
const fSearch = el('fSearch');
const fNpc = el('fNpc');
const fTier = el('fTier');
const fType = el('fType');
const fItem = el('fItem');
const fOnlyItems = el('fOnlyItems');
const fOnlyMap = el('fOnlyMap');
const btnReset = el('btnReset');
const questMiniList = el('questMiniList');
const centerStats = el('centerStats');
const centerList = el('centerList');
const questDetail = el('questDetail');
const statFound = el('statFound');
const statItems = el('statItems');

let activeQuestId = null;
const MAP_IMG = '/scum/assets/scum_map.jpg'; // bei Bedarf anpassen
const WORLD = { xmin: -900000, xmax: 615000, ymin: -900000, ymax: 615000 };
const FLIP_X = true, FLIP_Y = true, SWAP_XY = false;
const mapBase = new Image();
mapBase.src = MAP_IMG;
mapBase.decoding = 'async';

function esc(s) {
    return String(s ?? '').replace(/[&<>"']/g, m => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[m]));
}

function val(obj, keys, fallback = '') {
    for (const k of keys) {
        if (obj && obj[k] !== undefined && obj[k] !== null && obj[k] !== '') return obj[k];
    }
    return fallback;
}

function normNpc(s) {
    const v = String(s ?? '').trim();
    if (!v) return 'Unknown';
    const map = {
        armorer:'Armorer', banker:'Banker', barber:'Barber', bartender:'Bartender', doctor:'Doctor',
        generalgoods:'GeneralGoods', general_goods:'GeneralGoods', mechanic:'Mechanic', harbourmaster:'Harbourmaster',
        harbor_master:'Harbourmaster', harbour_master:'Harbourmaster', merchant:'GeneralGoods'
    };
    return map[v.toLowerCase()] || v;
}

function qId(q, idx = 0) { return String(val(q, ['id','ID','Id','_file','file','File'], 'quest_' + idx)); }
function qTitle(q) { return String(val(q, ['Title','title','Name','name','_file','file','File'], 'Quest')); }
function qDesc(q) { return String(val(q, ['Description','description','desc'], '')); }
function qNpc(q) { return normNpc(val(q, ['AssociatedNpc','AssociatedNPC','associatedNpc','npc','NPC','Trader'], 'Unknown')); }
function qTier(q) { return Number(val(q, ['Tier','tier'], 0)) || 0; }
function qFile(q) { return String(val(q, ['_file','file','File'], '')); }
function qMtime(q) { return Number(val(q, ['_mtime','mtime','MTime'], 0)) || 0; }
function qTimeLimit(q) { return Number(val(q, ['TimeLimitHours','timeLimitHours','time_limit_hours'], 0)) || 0; }
function qConditions(q) { return Array.isArray(q?.Conditions) ? q.Conditions : (Array.isArray(q?.conditions) ? q.conditions : []); }
function qRewards(q) { return Array.isArray(q?.RewardPool) ? q.RewardPool : (Array.isArray(q?.rewards) ? q.rewards : []); }

function fmtTime(ts) {
    if (!ts) return '—';
    try { return new Date(ts * 1000).toLocaleString('de-DE'); } catch { return '—'; }
}

function fmtHealth(v) {
    const n = Number(v);
    if (!Number.isFinite(n)) return '';
    if (n <= 1) return Math.round(n * 100) + '% Haltbarkeit';
    return n + '% Haltbarkeit';
}

function conditionType(c) {
    return String(val(c, ['Type','type'], 'Unknown'));
}

function typeClass(type) {
    const t = String(type).toLowerCase();
    if (t.includes('fetch')) return 'pill-fetch';
    if (t.includes('interaction') || t.includes('interact')) return 'pill-interact';
    if (t.includes('kill')) return 'pill-kill';
    return '';
}

function extractRequirements(q) {
    const out = [];
    qConditions(q).forEach((c, ci) => {
        const reqs = Array.isArray(c?.RequiredItems) ? c.RequiredItems : (Array.isArray(c?.requiredItems) ? c.requiredItems : []);
        reqs.forEach((r, ri) => {
            const items = Array.isArray(r?.AcceptedItems) ? r.AcceptedItems : (Array.isArray(r?.acceptedItems) ? r.acceptedItems : []);
            out.push({
                conditionIndex: ci,
                rowIndex: ri,
                type: conditionType(c),
                caption: String(val(c, ['TrackingCaption','trackingCaption','Caption','caption'], 'Items sammeln')),
                requiredNum: Number(val(r, ['RequiredNum','requiredNum'], 1)) || 1,
                minHealth: val(r, ['MinAcceptedItemHealth','minAcceptedItemHealth'], null),
                minResource: val(r, ['MinAcceptedItemResourceRatio','minAcceptedItemResourceRatio'], null),
                playerKeeps: !!val(c, ['PlayerKeepsItems','playerKeepsItems'], false),
                purchaseDisabled: !!val(c, ['DisablePurchaseOfRequiredItems','disablePurchaseOfRequiredItems'], false),
                items
            });
        });
    });
    return out;
}

function extractSteps(q) {
    return qConditions(q)
        .slice()
        .sort((a, b) => (Number(val(a, ['SequenceIndex','sequenceIndex'], 0)) || 0) - (Number(val(b, ['SequenceIndex','sequenceIndex'], 0)) || 0))
        .map((c, i) => ({
            no: i + 1,
            seq: Number(val(c, ['SequenceIndex','sequenceIndex'], i)) || i,
            type: conditionType(c),
            caption: String(val(c, ['TrackingCaption','trackingCaption','Caption','caption'], 'Questziel')),
            min: Number(val(c, ['MinNeeded','minNeeded'], 0)) || 0,
            max: Number(val(c, ['MaxNeeded','maxNeeded'], 0)) || 0,
            auto: !!val(c, ['CanBeAutoCompleted','canBeAutoCompleted'], false),
            locations: extractLocationsFromCondition(c).length
        }));
}

function extractLocationsFromCondition(c) {
    const out = [];
    const markers = Array.isArray(c?.LocationsShownOnMap) ? c.LocationsShownOnMap : (Array.isArray(c?.locationsShownOnMap) ? c.locationsShownOnMap : []);
    markers.forEach(m => {
        const loc = m?.Location || m?.location || m;
        const x = Number(val(loc, ['X','x'], NaN));
        const y = Number(val(loc, ['Y','y'], NaN));
        const z = Number(val(loc, ['Z','z'], 0));
        if (Number.isFinite(x) && Number.isFinite(y)) out.push({x, y, z});
    });
    return out;
}

function extractLocations(q) {
    return qConditions(q).flatMap(extractLocationsFromCondition);
}

function allAcceptedItems(q) {
    const set = new Set();
    extractRequirements(q).forEach(r => r.items.forEach(i => set.add(String(i))));
    return Array.from(set).sort((a, b) => a.localeCompare(b, undefined, {sensitivity:'base'}));
}

function questTypes(q) {
    const set = new Set(qConditions(q).map(c => conditionType(c)).filter(Boolean));
    return Array.from(set);
}

function hasItems(q) { return extractRequirements(q).some(r => r.items.length); }
function hasMap(q) { return extractLocations(q).length > 0 || qConditions(q).some(c => String(conditionType(c)).toLowerCase().includes('interact')); }

function rewardPills(q) {
    const pills = [];
    const add = (cls, txt) => pills.push(`<span class="pill ${cls}">${esc(txt)}</span>`);
    qRewards(q).forEach(r => {
        if (Number(r?.CurrencyNormal || 0) > 0) add('pill-kuna', `Kunas ${Number(r.CurrencyNormal)}`);
        if (Number(r?.CurrencyGold || 0) > 0) add('pill-gold', `Gold ${Number(r.CurrencyGold)}`);
        if (Number(r?.Fame || 0) > 0) add('pill-fame', `Fame ${Number(r.Fame)}`);
        if (Array.isArray(r?.Skills)) r.Skills.forEach(s => {
            if (s?.Skill) add('pill-skill', `${s.Skill}${s.Experience ? ' +' + Number(s.Experience) + ' XP' : ''}`);
        });
        if (Array.isArray(r?.TradeDeals) && r.TradeDeals.length) add('pill-deal', `Deals ${r.TradeDeals.length}`);
    });
    return pills.join('') || '<span class="muted2">Keine Rewards gefunden</span>';
}

function actionPills(q) {
    return questTypes(q).map(t => `<span class="pill ${typeClass(t)}">${esc(t)}</span>`).join('');
}

function buildNeedHtml(q, compact = false) {
    const reqs = extractRequirements(q);
    if (!reqs.length) {
        const steps = extractSteps(q);
        if (steps.length) return `<div class="muted2">Keine benötigten Items. Diese Quest besteht aus Interactions/anderen Zielen.</div>`;
        return `<div class="muted2">Keine Bedingungen in der Quest gefunden.</div>`;
    }

    return `<div class="need-list">${reqs.map(r => {
        const extras = [];
        if (r.minHealth !== null && r.minHealth !== '') extras.push('Min. ' + fmtHealth(r.minHealth));
        if (r.minResource !== null && r.minResource !== '') extras.push('Füllstand/Ressource min. ' + Number(r.minResource) + '%');
        if (r.purchaseDisabled) extras.push('Kaufen deaktiviert');
        if (r.playerKeeps) extras.push('Spieler behält Items');
        const shownItems = compact ? r.items.slice(0, 12) : r.items;
        const rest = compact && r.items.length > shownItems.length ? `<span class="itemchip">+${r.items.length - shownItems.length} weitere</span>` : '';
        return `
            <div class="need-row">
                <div class="need-main">
                    <div>
                        <div class="need-caption">${esc(r.caption)}</div>
                        <div class="need-extra">Benötigt: <b>${r.requiredNum}</b> · Typ: ${esc(r.type)}${extras.length ? ' · ' + esc(extras.join(' · ')) : ''}</div>
                    </div>
                    <span class="pill ${typeClass(r.type)}">${esc(r.type)}</span>
                </div>
                <div class="itemchips">${shownItems.map(i => `<span class="itemchip">${esc(i)}</span>`).join('')}${rest}</div>
            </div>`;
    }).join('')}</div>`;
}

function buildStepsHtml(q) {
    const steps = extractSteps(q);
    if (!steps.length) return `<div class="muted2">Keine Quest-Schritte gefunden.</div>`;
    return `<div class="steps">${steps.map(s => `
        <div class="step">
            <div class="stepnum">${s.no}</div>
            <div>
                <div class="need-caption">${esc(s.caption)}</div>
                <div class="need-extra">Typ: ${esc(s.type)}${s.min || s.max ? ` · Ziel: ${s.min || 1}${s.max ? ' / max. ' + s.max : ''}` : ''}${s.locations ? ' · Mapmarker: ' + s.locations : ''}${s.auto ? ' · Auto-complete möglich' : ''}</div>
            </div>
        </div>`).join('')}</div>`;
}

function worldToPixel(x, y, iw, ih) {
    let u = (x - WORLD.xmin) / (WORLD.xmax - WORLD.xmin);
    let v = (y - WORLD.ymin) / (WORLD.ymax - WORLD.ymin);
    if (SWAP_XY) { const t = u; u = v; v = t; }
    if (FLIP_X) u = 1 - u;
    if (FLIP_Y) v = 1 - v;
    return {px: u * iw, py: v * ih};
}

function renderMapThumb(canvas, locs, zoomPx = 520) {
    const ctx = canvas.getContext('2d');
    const cw = canvas.width, ch = canvas.height;
    const iw = mapBase.naturalWidth, ih = mapBase.naturalHeight;
    if (!iw || !ih) { ctx.clearRect(0, 0, cw, ch); return; }

    if (!locs?.length) {
        ctx.clearRect(0, 0, cw, ch);
        ctx.drawImage(mapBase, 0, 0, iw, ih, 0, 0, cw, ch);
        return;
    }

    const mx = locs.reduce((s, p) => s + (+p.x || 0), 0) / locs.length;
    const my = locs.reduce((s, p) => s + (+p.y || 0), 0) / locs.length;
    const mp = worldToPixel(mx, my, iw, ih);
    const sw = Math.min(zoomPx, iw);
    const sh = Math.min(Math.round(zoomPx * (ch / cw)), ih);
    let sx = Math.round(mp.px - sw / 2);
    let sy = Math.round(mp.py - sh / 2);
    sx = Math.max(0, Math.min(iw - sw, sx));
    sy = Math.max(0, Math.min(ih - sh, sy));

    ctx.clearRect(0, 0, cw, ch);
    ctx.imageSmoothingEnabled = true;
    ctx.imageSmoothingQuality = 'high';
    ctx.drawImage(mapBase, sx, sy, sw, sh, 0, 0, cw, ch);

    ctx.save();
    ctx.strokeStyle = 'rgba(255,255,255,.18)';
    ctx.strokeRect(.5, .5, cw - 1, ch - 1);
    locs.forEach(p => {
        const pp = worldToPixel(+p.x || 0, +p.y || 0, iw, ih);
        const cx = (pp.px - sx) * (cw / sw);
        const cy = (pp.py - sy) * (ch / sh);
        ctx.beginPath();
        ctx.arc(cx, cy, 5, 0, Math.PI * 2);
        ctx.fillStyle = '#00ff15';
        ctx.fill();
        ctx.lineWidth = 2;
        ctx.strokeStyle = '#ffffff';
        ctx.stroke();
    });
    ctx.restore();
}

function drawAllQuestThumbs(root = document) {
    root.querySelectorAll('canvas[data-locs]').forEach(cv => {
        let locs = [];
        try { locs = JSON.parse(decodeURIComponent(cv.dataset.locs || '[]')) || []; } catch {}
        renderMapThumb(cv, locs, Number(cv.dataset.zoom || 520));
    });
}

function filteredQuests() {
    const search = fSearch.value.trim().toLowerCase();
    const npc = fNpc.value.trim();
    const tier = Number(fTier.value || 0);
    const type = fType.value.trim().toLowerCase();
    const item = fItem.value.trim().toLowerCase();

    return QUESTS.filter((q, idx) => {
        if (npc && qNpc(q) !== npc) return false;
        if (tier && qTier(q) !== tier) return false;
        if (type && !questTypes(q).some(t => t.toLowerCase() === type)) return false;
        if (fOnlyItems.checked && !hasItems(q)) return false;
        if (fOnlyMap.checked && !hasMap(q)) return false;

        const items = allAcceptedItems(q);
        if (item && !items.some(i => i.toLowerCase().includes(item))) return false;

        if (!search) return true;
        const hay = [
            qTitle(q), qDesc(q), qNpc(q), qFile(q), qTier(q),
            questTypes(q).join(' '), items.join(' '),
            extractSteps(q).map(s => s.caption).join(' ')
        ].join(' ').toLowerCase();
        return hay.includes(search);
    });
}

function buildFilterOptions() {
    const npcs = Array.from(new Set(QUESTS.map(qNpc))).sort((a,b) => a.localeCompare(b, undefined, {sensitivity:'base'}));
    fNpc.innerHTML = '<option value="">Alle Trader</option>' + npcs.map(n => `<option value="${esc(n)}">${esc(n)}</option>`).join('');

    const types = Array.from(new Set(QUESTS.flatMap(questTypes))).filter(Boolean).sort((a,b) => a.localeCompare(b, undefined, {sensitivity:'base'}));
    fType.innerHTML = '<option value="">Alle Typen</option>' + types.map(t => `<option value="${esc(t)}">${esc(t)}</option>`).join('');
}

function renderMiniList(list) {
    if (!list.length) {
        questMiniList.innerHTML = '<div class="muted2">Keine Treffer.</div>';
        return;
    }
    questMiniList.innerHTML = list.map((q, idx) => {
        const id = qId(q, idx);
        const items = allAcceptedItems(q);
        return `<div class="quest-mini ${String(activeQuestId) === String(id) ? 'active' : ''}" data-qid="${esc(id)}">
            <div class="quest-mini-title">${esc(qTitle(q))}</div>
            <div class="quest-mini-sub">${esc(qNpc(q))} · Tier ${qTier(q) || '—'} · ${questTypes(q).map(esc).join(', ') || 'ohne Typ'} · Items: ${items.length}</div>
        </div>`;
    }).join('');

    questMiniList.querySelectorAll('[data-qid]').forEach(elm => {
        elm.addEventListener('click', () => {
            activeQuestId = elm.dataset.qid;
            const card = centerList.querySelector(`[data-qid="${CSS.escape(activeQuestId)}"]`);
            renderAll();
            requestAnimationFrame(() => {
                const nextCard = centerList.querySelector(`[data-qid="${CSS.escape(activeQuestId)}"]`);
                if (nextCard) nextCard.scrollIntoView({block:'center', behavior:'smooth'});
            });
        });
    });
}

function renderCard(q, idx) {
    const id = qId(q, idx);
    const locs = extractLocations(q);
    const locsAttr = encodeURIComponent(JSON.stringify(locs));
    const timeLimit = qTimeLimit(q);
    const items = allAcceptedItems(q);
    const active = String(activeQuestId) === String(id);

    return `<div class="qcard ${active ? 'active' : ''}" data-qid="${esc(id)}">
        <div class="qhead">
            <div>
                <div class="qtitle">${esc(qTitle(q))}</div>
                <div class="qmeta">${esc(qNpc(q))} · Tier ${qTier(q) || '—'}${timeLimit ? ' · Zeitlimit: ' + timeLimit + 'h' : ''} · Datei: <code>${esc(qFile(q) || '—')}</code></div>
            </div>
            <div>${actionPills(q)}</div>
        </div>

        ${qDesc(q) ? `<div class="qdesc">${esc(qDesc(q))}</div>` : ''}

        <div class="sectionline">
            <div class="small-title">Was muss ich machen?</div>
            ${buildStepsHtml(q)}
        </div>

        <div class="sectionline">
            <div class="small-title">Zugelassene Items${items.length ? ' (' + items.length + ')' : ''}</div>
            ${buildNeedHtml(q, false)}
        </div>

        <div class="sectionline">
            <div class="small-title">Rewards</div>
            <div>${rewardPills(q)}</div>
        </div>

        <div class="qmap">
            ${locs.length ? `<canvas width="520" height="220" data-locs="${locsAttr}" data-zoom="520"></canvas><div class="cap">Mapmarker / Interaction Locations: ${locs.length}</div>` : `<div class="cap">Keine Mapmarker in dieser Quest.</div>`}
        </div>
    </div>`;
}

function renderCenter(list) {
    centerStats.textContent = `Gefunden: ${list.length} von ${QUESTS.length} Quests`;
    if (!list.length) {
        centerList.innerHTML = '<div class="filter-block detail-empty">Keine Quests gefunden. Filter links prüfen.</div>';
        return;
    }

    const sorted = list.slice().sort((a,b) =>
        qNpc(a).localeCompare(qNpc(b), undefined, {sensitivity:'base'}) ||
        (qTier(a) - qTier(b)) ||
        qTitle(a).localeCompare(qTitle(b), undefined, {sensitivity:'base'})
    );
    centerList.innerHTML = sorted.map(renderCard).join('');

    centerList.querySelectorAll('[data-qid]').forEach(card => {
        card.addEventListener('click', () => {
            activeQuestId = card.dataset.qid;
            renderDetail(findQuestById(activeQuestId));
            renderAll(false);
        });
    });
}

function findQuestById(id) {
    return QUESTS.find((q, idx) => String(qId(q, idx)) === String(id)) || null;
}

function renderDetail(q) {
    if (!q) {
        questDetail.className = 'filter-block detail-empty';
        questDetail.innerHTML = 'Klick auf eine Quest, dann siehst du hier kompakt: was zu tun ist, welche Items erlaubt sind und welche Interactions/Mapmarker dazugehören.';
        return;
    }

    const items = allAcceptedItems(q);
    const locs = extractLocations(q);
    questDetail.className = 'filter-block';
    questDetail.innerHTML = `<div class="detail-scroll">
        <div class="detail-title">${esc(qTitle(q))}</div>
        <div class="qmeta" style="margin-top:4px;">${esc(qNpc(q))} · Tier ${qTier(q) || '—'} · Update: ${esc(fmtTime(qMtime(q)))}</div>
        <div style="margin-top:8px;">${actionPills(q)}</div>

        <div class="sectionline">
            <div class="small-title">Kurzfassung</div>
            ${buildStepsHtml(q)}
        </div>

        <div class="sectionline">
            <div class="small-title">Items</div>
            ${items.length ? `<div class="itemchips">${items.map(i => `<span class="itemchip">${esc(i)}</span>`).join('')}</div>` : '<div class="muted2">Keine Item-Anforderung.</div>'}
        </div>

        <div class="sectionline">
            <div class="small-title">Item-Regeln</div>
            ${buildNeedHtml(q, true)}
        </div>

        <div class="sectionline">
            <div class="small-title">Map / Interactions</div>
            <div class="muted2">Mapmarker: ${locs.length}</div>
        </div>

        <div class="sectionline">
            <div class="small-title">Rewards</div>
            ${rewardPills(q)}
        </div>
    </div>`;
}

function renderAll(keepDetail = true) {
    const list = filteredQuests();
    statFound.textContent = String(list.length);
    statItems.textContent = String(list.filter(hasItems).length);
    renderMiniList(list);
    renderCenter(list);

    const doDraw = () => drawAllQuestThumbs(centerList);
    if (mapBase.complete) doDraw();
    else mapBase.addEventListener('load', doDraw, {once:true});

    if (keepDetail && activeQuestId) renderDetail(findQuestById(activeQuestId));
}

[fSearch, fNpc, fTier, fType, fItem, fOnlyItems, fOnlyMap].forEach(input => {
    input.addEventListener(input.tagName === 'INPUT' && input.type !== 'checkbox' ? 'input' : 'change', () => renderAll());
});

btnReset.addEventListener('click', (e) => {
    e.preventDefault();
    fSearch.value = '';
    fNpc.value = '';
    fTier.value = '';
    fType.value = '';
    fItem.value = '';
    fOnlyItems.checked = false;
    fOnlyMap.checked = false;
    activeQuestId = null;
    renderDetail(null);
    renderAll(false);
});

buildFilterOptions();
renderAll(false);
</script>
