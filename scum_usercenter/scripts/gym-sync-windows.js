#!/usr/bin/env node
/* scripts/gym-sync-windows.js
 * Laeuft auf dem Windows-Server, holt alle 15 Minuten /players.json von ggCON
 * und pusht die Skill-Snapshots zur Strato-Webseite nach /api/gym_push.php.
 * Benoetigt Node.js 18 oder neuer.
 */
const fs = require('fs');
const path = require('path');

const SCRIPT_DIR = __dirname;
const PROJECT_ROOT = path.resolve(SCRIPT_DIR, '..');
loadEnv(path.join(PROJECT_ROOT, 'private', '.env'));
loadEnv(path.join(SCRIPT_DIR, '.env'));
loadEnv(path.join(process.cwd(), '.env'));

const GGCON_BASE_URL = cleanBase(process.env.GGCON_BASE_URL || '');
const GGCON_PASSWORD = process.env.GGCON_PASSWORD || '';
const GYM_PUSH_URL = process.env.GYM_PUSH_URL || '';
const GYM_PUSH_TOKEN = process.env.GYM_PUSH_TOKEN || '';
const INTERVAL_MS = Number(process.env.GYM_SYNC_INTERVAL_MS || 15 * 60 * 1000);

if (!GGCON_BASE_URL || !GGCON_PASSWORD || !GYM_PUSH_URL || !GYM_PUSH_TOKEN) {
  console.error('[gym-sync-windows] Bitte GGCON_BASE_URL, GGCON_PASSWORD, GYM_PUSH_URL und GYM_PUSH_TOKEN in .env setzen.');
  process.exit(1);
}

function loadEnv(file) {
  if (!fs.existsSync(file)) return;
  const lines = fs.readFileSync(file, 'utf8').split(/\r?\n/);
  for (const line of lines) {
    const trimmed = line.trim();
    if (!trimmed || trimmed.startsWith('#') || !trimmed.includes('=')) continue;
    const idx = trimmed.indexOf('=');
    const key = trimmed.slice(0, idx).trim();
    let val = trimmed.slice(idx + 1).trim();
    val = val.replace(/^["']|["']$/g, '');
    if (!process.env[key]) process.env[key] = val;
  }
}

function cleanBase(v) { return String(v || '').replace(/\/+$/, ''); }
function safeSteamId(v) { return String(v || '').replace(/[^0-9]/g, '') || 'unknown'; }
function nowIso() { return new Date().toISOString(); }
function numOrNull(v) { return v === null || v === undefined || Number.isNaN(Number(v)) ? null : Number(v); }
function levelName(level) {
  return ['None', 'Basic', 'Medium', 'Advanced', 'Above Advanced'][Number(level)] || String(level ?? '');
}
function compactSkill(skill) {
  return {
    id: skill.id ?? null,
    name: String(skill.name || ''),
    xp: Number(skill.xp ?? 0),
    level: Number(skill.level ?? 0),
    levelName: String(skill.levelName || levelName(skill.level)),
  };
}
function normalizeAttributes(attrs) {
  attrs = attrs || {};
  return {
    strength: numOrNull(attrs.strength),
    constitution: numOrNull(attrs.constitution),
    dexterity: numOrNull(attrs.dexterity),
    intelligence: numOrNull(attrs.intelligence),
  };
}
function hasUsableGymData(player) {
  return player && player.userId && player.attributes && Array.isArray(player.skills) && player.skills.length > 0;
}

async function fetchPlayers() {
  const res = await fetch(`${GGCON_BASE_URL}/players.json`, {
    headers: { 'X-Password': GGCON_PASSWORD, 'Accept': 'application/json' },
  });
  const text = await res.text();
  let data;
  try { data = JSON.parse(text); } catch { throw new Error(`ggCON Antwort ist kein JSON: HTTP ${res.status}`); }
  if (!res.ok || !data.ok) throw new Error(data.reason || data.error || `ggCON HTTP ${res.status}`);
  return data;
}

async function pushToWebsite(payload) {
  const res = await fetch(GYM_PUSH_URL, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'Accept': 'application/json',
      'X-Gym-Token': GYM_PUSH_TOKEN,
    },
    body: JSON.stringify(payload),
  });
  const text = await res.text();
  let data;
  try { data = JSON.parse(text); } catch { throw new Error(`Webseite antwortet nicht mit JSON: HTTP ${res.status} ${text.slice(0, 200)}`); }
  if (!res.ok || !data.ok) throw new Error(data.error || `Webseite HTTP ${res.status}`);
  return data;
}

async function runOnce() {
  const capturedAt = nowIso();
  try {
    const data = await fetchPlayers();
    const rawPlayers = Array.isArray(data.players) ? data.players : [];
    const players = rawPlayers.filter(hasUsableGymData).map((p) => ({
      steamId: safeSteamId(p.userId),
      characterName: p.characterName || p.realName || null,
      steamName: p.steamName || null,
      snapshot: {
        capturedAt,
        attributes: normalizeAttributes(p.attributes),
        skills: p.skills.map(compactSkill).filter(s => s.name).sort((a, b) => a.name.localeCompare(b.name)),
        fame: numOrNull(p.fame),
        health: numOrNull(p.health),
        gearWeightKg: numOrNull(p.gearWeightKg),
      },
    }));

    const result = await pushToWebsite({
      capturedAt,
      onlinePlayers: Number(data.count ?? rawPlayers.length),
      players,
    });

    console.log(`[gym-sync-windows] ${capturedAt} ok online=${rawPlayers.length} pushed=${players.length} saved=${result.savedSnapshots ?? '?'}`);
  } catch (err) {
    console.error(`[gym-sync-windows] ${capturedAt} FEHLER:`, err.message || err);
  }
}

runOnce();
setInterval(runOnce, INTERVAL_MS);
