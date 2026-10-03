(() => {
  const root = document.querySelector('[data-gym-root]');
  if (!root) return;

  const fmt = new Intl.NumberFormat('de-DE');
  const fmtNum = (v, d = 2) => (v === null || v === undefined || Number.isNaN(Number(v))) ? '-' : Number(v).toLocaleString('de-DE', { maximumFractionDigits: d });
  const apiUrl = 'api/gym_data.php';
  let attrChart = null;
  let skillChart = null;
  let lastUpdatedAt = null;
  let latestPayload = null;

  function dt(value) {
    if (!value) return '-';
    const d = new Date(value);
    if (Number.isNaN(d.getTime())) return String(value);
    return d.toLocaleString('de-DE', { day:'2-digit', month:'2-digit', hour:'2-digit', minute:'2-digit' });
  }

  function setStatus(kind, title, sub) {
    const dot = root.querySelector('[data-sync-state]');
    dot?.classList.remove('ok','warn','err');
    if (kind) dot?.classList.add(kind);
    const t = root.querySelector('[data-sync-title]');
    const s = root.querySelector('[data-sync-sub]');
    if (t) t.textContent = title;
    if (s) s.textContent = sub || '';
  }

  function labels(snaps) {
    return snaps.map((s, idx) => {
      const d = s.capturedAt ? new Date(s.capturedAt) : null;
      return d && !Number.isNaN(d.getTime()) ? d.toLocaleDateString('de-DE', { day:'2-digit', month:'2-digit' }) + ' ' + d.toLocaleTimeString('de-DE', { hour:'2-digit', minute:'2-digit' }) : String(idx + 1);
    });
  }

  function attrValue(snapshot, key) {
    return snapshot?.attributes?.[key] ?? null;
  }

  function skillList(snapshot) {
    const list = snapshot?.skills;
    return Array.isArray(list) ? list.slice().sort((a,b) => String(a.name||'').localeCompare(String(b.name||''), 'de')) : [];
  }

  function findSkill(snapshot, name) {
    return skillList(snapshot).find(s => String(s.name) === String(name));
  }

  function getAttrSeries(snaps) {
    return [
      { key: 'strength', label: 'STR' },
      { key: 'constitution', label: 'CON' },
      { key: 'dexterity', label: 'DEX' },
      { key: 'intelligence', label: 'INT' },
    ].map(attr => ({
      ...attr,
      values: snaps.map(s => attrValue(s, attr.key))
    }));
  }

  function attrScale(snaps) {
    const vals = getAttrSeries(snaps)
      .flatMap(s => s.values)
      .map(Number)
      .filter(Number.isFinite);

    if (!vals.length) return {};

    const minVal = Math.min(...vals);
    const maxVal = Math.max(...vals);
    const span = Math.max(maxVal - minVal, 0.02);
    const pad = Math.max(span * 0.35, 0.01);

    return {
      min: Math.max(0, Math.floor((minVal - pad) * 100) / 100),
      max: Math.ceil((maxVal + pad) * 100) / 100,
    };
  }

  function renderAttrChart(snaps) {
    const ctx = document.getElementById('gymAttrChart');
    if (!ctx || !window.Chart) return;

    const series = getAttrSeries(snaps);
    const data = {
      labels: labels(snaps),
      datasets: series.map(s => ({
        label: s.label,
        data: s.values,
        tension: .28,
        pointRadius: 3,
        pointHoverRadius: 5,
        spanGaps: true,
      }))
    };

    const options = chartOptions('Attribute', attrScale(snaps));
    options.plugins.tooltip = {
      callbacks: {
        label: item => `${item.dataset.label}: ${fmtNum(item.parsed.y, 4)}`
      }
    };

    if (attrChart) { attrChart.data = data; attrChart.options = options; attrChart.update(); return; }
    attrChart = new Chart(ctx, { type: 'line', data, options });
  }

  function chartOptions(yTitle, yScale = {}) {
    return {
      responsive: true,
      maintainAspectRatio: false,
      interaction: { mode: 'index', intersect: false },
      plugins: { legend: { labels: { color: '#fff' } } },
      scales: {
        x: { ticks: { color: 'rgba(255,255,255,.72)', maxRotation: 35, minRotation: 0 }, grid: { color: 'rgba(255,255,255,.08)' } },
        y: { ...yScale, title: { display: true, text: yTitle, color: 'rgba(255,255,255,.75)' }, ticks: { color: 'rgba(255,255,255,.72)', precision: 3 }, grid: { color: 'rgba(255,255,255,.08)' } }
      }
    };
  }

  function renderCurrent(player) {
    const latest = player.latest;
    const name = player.characterName || player.steamName || 'Prisoner';
    const nameEl = root.querySelector('[data-player-name]');
    if (nameEl) nameEl.textContent = latest ? `${name} - Snapshot vom ${dt(latest.capturedAt)}` : 'Noch kein Snapshot. Logge dich auf dem Server ein und warte auf den Sync.';

    const wrap = root.querySelector('[data-current-attrs]');
    if (!wrap) return;
    const attrs = latest?.attributes || {};
    const snaps = Array.isArray(player.snapshots) ? player.snapshots : [];
    const first = snaps[0]?.attributes || {};
    const prev = snaps.length > 1 ? (snaps[snaps.length - 2]?.attributes || {}) : {};
    const rows = [
      ['strength','STR'], ['constitution','CON'], ['dexterity','DEX'], ['intelligence','INT']
    ].map(([key,label]) => {
      const val = attrs[key];
      const pct = Math.max(0, Math.min(100, (Number(val || 0) / 8) * 100));
      const deltaLast = Number(val ?? 0) - Number(prev[key] ?? val ?? 0);
      const deltaTotal = Number(val ?? 0) - Number(first[key] ?? val ?? 0);
      const deltaClass = deltaLast > 0 || deltaTotal > 0 ? 'gym-delta-pos' : (deltaLast < 0 || deltaTotal < 0 ? 'gym-delta-neg' : 'gym-muted');
      return `<div class="gym-attr-row">
        <div class="gym-attr-top"><span>${label}</span><span>${fmtNum(val, 4)}</span></div>
        <div class="gym-attr-deltas"><span class="${deltaClass}">Seit letztem Sync: ${deltaLast > 0 ? '+' : ''}${fmtNum(deltaLast, 4)}</span><span>Gesamt: ${deltaTotal > 0 ? '+' : ''}${fmtNum(deltaTotal, 4)}</span></div>
        <div class="gym-bar"><i style="width:${pct}%"></i></div>
      </div>`;
    }).join('');
    wrap.innerHTML = rows || '<div class="gym-muted">Keine Attributdaten vorhanden.</div>';
  }

  function renderSkillSelect(snaps) {
    const select = root.querySelector('[data-skill-select]');
    if (!select) return;
    const current = select.value;
    const names = new Set();
    snaps.forEach(s => skillList(s).forEach(skill => names.add(String(skill.name || ''))));
    const arr = [...names].filter(Boolean).sort((a,b) => a.localeCompare(b, 'de'));
    select.innerHTML = arr.length ? arr.map(n => `<option value="${escapeHtml(n)}">${escapeHtml(n)}</option>`).join('') : '<option value="">Keine Skills</option>';
    if (current && arr.includes(current)) select.value = current;
  }

  function renderSkillChart(snaps) {
    const ctx = document.getElementById('gymSkillChart');
    const select = root.querySelector('[data-skill-select]');
    if (!ctx || !select || !window.Chart) return;
    const name = select.value;
    const data = {
      labels: labels(snaps),
      datasets: [
        { label: `${name} XP`, data: snaps.map(s => findSkill(s, name)?.xp ?? null), yAxisID: 'y', tension: .25 },
        { label: `${name} Level`, data: snaps.map(s => findSkill(s, name)?.level ?? null), yAxisID: 'y1', tension: .25 }
      ]
    };
    const options = chartOptions('XP');
    options.scales.y1 = { position: 'right', min: 0, max: 4, ticks: { color: 'rgba(255,255,255,.72)', stepSize: 1 }, grid: { drawOnChartArea: false } };
    if (skillChart) { skillChart.data = data; skillChart.options = options; skillChart.update(); return; }
    skillChart = new Chart(ctx, { type: 'line', data, options });
  }

  function renderSkillTable(snaps) {
    const tbody = root.querySelector('[data-skill-table]');
    if (!tbody) return;
    const latest = snaps[snaps.length - 1];
    const prev = snaps[snaps.length - 2];
    const skills = skillList(latest);
    if (!skills.length) { tbody.innerHTML = '<tr><td colspan="5">Noch keine Skilldaten vorhanden.</td></tr>'; return; }
    tbody.innerHTML = skills.map(s => {
      const old = findSkill(prev, s.name);
      const delta = old ? Number(s.xp || 0) - Number(old.xp || 0) : 0;
      return `<tr><td>${escapeHtml(s.name || '-')}</td><td>${fmtNum(s.level,0)}</td><td>${escapeHtml(s.levelName || '-')}</td><td>${fmt.format(Math.round(Number(s.xp || 0)))}</td><td class="${delta > 0 ? 'gym-delta-pos' : 'gym-muted'}">${delta > 0 ? '+' : ''}${fmt.format(Math.round(delta))}</td></tr>`;
    }).join('');
  }

  function escapeHtml(v) {
    return String(v ?? '').replace(/[&<>'"]/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;',"'":'&#039;','"':'&quot;'}[c]));
  }

  function render(payload) {
    latestPayload = payload;
    const player = payload.player || {};
    const meta = payload.meta || {};
    const snaps = Array.isArray(player.snapshots) ? player.snapshots : [];

    root.querySelector('[data-stat="snapshots"]').textContent = fmt.format(player.snapshotCount || snaps.length || 0);
    root.querySelector('[data-stat="sync"]').textContent = dt(meta.lastRunAt || meta.updatedAt);
    root.querySelector('[data-stat="seen"]').textContent = dt(player.lastSeenOnline || player.updatedAt);
    root.querySelector('[data-stat="online"]').textContent = fmt.format(meta.onlinePlayers || 0);

    if (meta.lastError) setStatus('err', 'Sync Fehler', meta.lastError);
    else if (snaps.length) setStatus('ok', 'Tracking aktiv', `Letzter Snapshot: ${dt(player.updatedAt)}`);
    else setStatus('warn', 'Noch keine Skilldaten', 'Der Spieler muss beim 15-Minuten-Sync online sein.');

    renderCurrent(player);
    renderAttrChart(snaps);
    renderSkillSelect(snaps);
    renderSkillChart(snaps);
    renderSkillTable(snaps);
  }

  async function load() {
    try {
      const res = await fetch(apiUrl, { cache: 'no-store', credentials: 'same-origin' });
      const payload = await res.json();
      if (!payload.ok) throw new Error(payload.error || 'API Fehler');
      const stamp = payload.player?.updatedAt || payload.meta?.updatedAt || payload.meta?.lastRunAt || '';
      if (stamp !== lastUpdatedAt) {
        lastUpdatedAt = stamp;
        render(payload);
      }
    } catch (err) {
      setStatus('err', 'Daten nicht erreichbar', err.message || String(err));
    }
  }

  root.querySelector('[data-skill-select]')?.addEventListener('change', () => {
    const snaps = latestPayload?.player?.snapshots || [];
    renderSkillChart(snaps);
  });

  load();
  setInterval(load, 60000);
})();
