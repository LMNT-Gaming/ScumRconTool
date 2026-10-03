#!/usr/bin/env node
/* scripts/gym-sync.js
 * Holt alle 15 Minuten /players.json von ggCON und speichert Skill-Snapshots je SteamID.
 */
const fs = require('fs');
const path = require('path');

const ROOT = path.resolve(__dirname, '..');
loadEnv(path.join(ROOT, 'private', '.env'));

const BASE_URL = cleanBase(process.env.GGCON_BASE_URL || '');
const PASSWORD = process.env.GGCON_PASSWORD || '';
const DATA_DIR = process.env.GYM_DATA_DIR || path.join(ROOT, 'private', 'gym_data');
const PLAYERS_DIR = path.join(DATA_DIR, 'players');
const INTERVAL_MS = Number(process.env.GYM_SYNC_INTERVAL_MS || 15 * 60 * 1000);
const SAVE_UNCHANGED = String(process.env.GYM_SAVE_UNCHANGED || '0') === '1';
const MAX_SNAPSHOTS = Number(process.env.GYM_MAX_SNAPSHOTS || 1000);

if (!BASE_URL || !PASSWORD) {
  console.error('[gym-sync] GGCON_BASE_URL oder GGCON_PASSWORD fehlt in private/.env');
  process.exit(1);
}

fs.mkdirSync(PLAYERS_DIR, { recursive: true });

function loadEnv(file) {
  if (!fs.existsSync(file)) return;
  const lines = fs.readFileSync(file, 'utf8').split(/\r?\n/);
  for (const line of lines) {
    const trimmed = line.trim();
    if (!trimmed || trimmed.startsWith('#') || !trimmed.includes('=')) continue;
    const idx = trimmed.indexOf('=');
    const key = trimmed.slice(0, idx).trim();
    let val = trimmed.slice(idx + 1).trim();
    val = val.replace(/^['"]|['"]$/g, '');
    if (!process.env[key]) process.env[key] = val;
  }
}

function cleanBase(v) { return String(v || '').replace(/\/+$/, ''); }
function safeSteamId(v) { return String(v || '').replace(/[^0-9]/g, '') || 'unknown'; }
function nowIso() { return new Date().toISOString(); }
function playerFile(steamId) { return path.join(PLAYERS_DIR, `${safeSteamId(steamId)}.json`); }
function metaFile() { return path.join(DATA_DIR, 'meta.json'); }

function readJson(file, fallback) {
  try { return JSON.parse(fs.readFileSync(file, 'utf8')); }
  catch { return fallback; }
}
function writeJsonAtomic(file, data) {
  fs.mkdirSync(path.dirname(file), { recursive: true });
  const tmp = `${file}.tmp`;
  fs.writeFileSync(tmp, JSON.stringify(data, null, 2), 'utf8');
  fs.renameSync(tmp, file);
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
function levelName(level) {
  return ['None', 'Basic', 'Medium', 'Advanced', 'Above Advanced'][Number(level)] || String(level ?? '');
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
function numOrNull(v) { return v === null || v === undefined || Number.isNaN(Number(v)) ? null : Number(v); }
function snapshotSignature(s) {
  return JSON.stringify({ a: s.attributes, s: (s.skills || []).map(x => [x.name, x.xp, x.level]) });
}
function hasUsableGymData(player) {
  return player && player.userId && player.attributes && Array.isArray(player.skills) && player.skills.length > 0;
}

async function fetchPlayers() {
  const res = await fetch(`${BASE_URL}/players.json`, { headers: { 'X-Password': PASSWORD, 'Accept': 'application/json' } });
  const text = await res.text();
  let data;
  try { data = JSON.parse(text); } catch { throw new Error(`ggCON Antwort ist kein JSON: HTTP ${res.status}`); }
  if (!res.ok || !data.ok) throw new Error(data.reason || data.error || `ggCON HTTP ${res.status}`);
  return data;
}

async function runOnce() {
  const startedAt = nowIso();
  try {
    const data = await fetchPlayers();
    const players = Array.isArray(data.players) ? data.players : [];
    let saved = 0;
    let skipped = 0;

    for (const p of players) {
      if (!hasUsableGymData(p)) { skipped++; continue; }
      const steamId = safeSteamId(p.userId);
      const snap = {
        capturedAt: startedAt,
        attributes: normalizeAttributes(p.attributes),
        skills: p.skills.map(compactSkill).filter(s => s.name).sort((a, b) => a.name.localeCompare(b.name)),
        fame: numOrNull(p.fame),
        health: numOrNull(p.health),
        gearWeightKg: numOrNull(p.gearWeightKg),
      };

      const file = playerFile(steamId);
      const existing = readJson(file, { steamId, snapshots: [] });
      const snapshots = Array.isArray(existing.snapshots) ? existing.snapshots : [];
      const prev = snapshots[snapshots.length - 1];
      const changed = !prev || snapshotSignature(prev) !== snapshotSignature(snap);

      if (changed || SAVE_UNCHANGED) {
        snapshots.push(snap);
        while (snapshots.length > MAX_SNAPSHOTS) snapshots.shift();
        existing.snapshots = snapshots;
        saved++;
      }

      existing.steamId = steamId;
      existing.characterName = p.characterName || p.realName || existing.characterName || null;
      existing.steamName = p.steamName || existing.steamName || null;
      existing.updatedAt = changed || SAVE_UNCHANGED ? startedAt : (existing.updatedAt || startedAt);
      existing.lastSeenOnline = startedAt;
      writeJsonAtomic(file, existing);
    }

    writeJsonAtomic(metaFile(), {
      ok: true,
      updatedAt: startedAt,
      lastRunAt: startedAt,
      onlinePlayers: Number(data.count ?? players.length),
      savedSnapshots: saved,
      skippedPlayers: skipped,
      lastError: null,
    });
    console.log(`[gym-sync] ${startedAt} ok online=${players.length} saved=${saved} skipped=${skipped}`);
  } catch (err) {
    writeJsonAtomic(metaFile(), {
      ok: false,
      updatedAt: startedAt,
      lastRunAt: startedAt,
      onlinePlayers: 0,
      savedSnapshots: 0,
      skippedPlayers: 0,
      lastError: err.message || String(err),
    });
    console.error('[gym-sync]', err.message || err);
  }
}

runOnce();
setInterval(runOnce, INTERVAL_MS);
