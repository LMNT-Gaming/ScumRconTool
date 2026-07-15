<?php
// Standalone SCUM Kartographer
// Upload-Ordner: /scum_kartographer/
// Aufruf: https://deine-domain.tld/scum_kartographer/index.php

$mapImage = 'assets/scum_map.jpg';

// World-Bounds aus der vorhandenen SCUM-Karte /scum/pages/map.php
// Links oben:  X  618000, Y  618000
// Rechts unten: X -898000, Y -900000
$worldLeftX = 618000;
$worldRightX = -898000;
$worldTopY = 618000;
$worldBottomY = -900000;
?><!doctype html>
<html lang="de">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>SCUM Kartographer</title>
  <style>
    :root {
      --bg: #070b12;
      --panel: rgba(15, 23, 42, .88);
      --panel2: rgba(2, 6, 23, .72);
      --border: rgba(148, 163, 184, .22);
      --text: #e5e7eb;
      --muted: #9ca3af;
      --accent: #ef4444;
      --accent2: #3b82f6;
      --good: #22c55e;
      --shadow: 0 20px 60px rgba(0, 0, 0, .45);
    }

    * { box-sizing: border-box; }

    html, body { min-height: 100%; }

    body {
      margin: 0;
      color: var(--text);
      font-family: system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif;
      background:
        radial-gradient(circle at top left, rgba(239, 68, 68, .22), transparent 34rem),
        radial-gradient(circle at bottom right, rgba(59, 130, 246, .18), transparent 30rem),
        linear-gradient(135deg, #030712 0%, var(--bg) 48%, #111827 100%);
    }

    .page {
      width: min(1500px, calc(100% - 28px));
      margin: 0 auto;
      padding: 18px 0 24px;
    }

    .topbar {
      display: flex;
      justify-content: space-between;
      align-items: end;
      gap: 14px;
      margin-bottom: 14px;
    }

    h1 {
      margin: 0;
      font-size: clamp(24px, 3vw, 38px);
      letter-spacing: .02em;
    }

    .subtitle {
      margin: 5px 0 0;
      color: var(--muted);
      font-size: 14px;
    }

    .badge {
      display: inline-flex;
      align-items: center;
      gap: 8px;
      white-space: nowrap;
      padding: 8px 11px;
      border: 1px solid rgba(34, 197, 94, .35);
      border-radius: 999px;
      background: rgba(20, 83, 45, .28);
      color: #bbf7d0;
      font-weight: 800;
      font-size: 12px;
      text-transform: uppercase;
      letter-spacing: .08em;
    }

    .layout {
      display: grid;
      grid-template-columns: minmax(460px, 1fr) 380px;
      gap: 14px;
      align-items: start;
    }

    .panel {
      background: var(--panel);
      border: 1px solid var(--border);
      border-radius: 18px;
      box-shadow: var(--shadow);
      backdrop-filter: blur(10px);
    }

    .map-panel { padding: 12px; }

    .mapbox {
      position: relative;
      width: 100%;
      aspect-ratio: 1 / 1;
      overflow: hidden;
      border-radius: 14px;
      border: 1px solid rgba(148, 163, 184, .20);
      background: #020617;
      user-select: none;
      touch-action: none;
    }

    .mapbox img,
    .mapbox canvas {
      position: absolute;
      inset: 0;
      width: 100%;
      height: 100%;
    }

    .mapbox img {
      object-fit: contain;
      display: block;
    }

    .mapbox canvas {
      cursor: crosshair;
    }

    .side {
      display: grid;
      gap: 12px;
    }

    .box { padding: 14px; }

    .box-title {
      display: flex;
      justify-content: space-between;
      align-items: center;
      gap: 10px;
      margin-bottom: 10px;
      font-weight: 900;
      letter-spacing: .02em;
    }

    .muted { color: var(--muted); }

    .stats {
      display: grid;
      grid-template-columns: 1fr 1fr 1fr;
      gap: 8px;
    }

    .stat {
      background: var(--panel2);
      border: 1px solid rgba(148, 163, 184, .14);
      border-radius: 12px;
      padding: 9px;
      min-width: 0;
    }

    .stat .label {
      font-size: 11px;
      color: var(--muted);
      text-transform: uppercase;
      letter-spacing: .08em;
    }

    .stat .value {
      margin-top: 4px;
      font-weight: 900;
      font-size: 16px;
      overflow-wrap: anywhere;
    }

    label {
      display: block;
      margin-top: 10px;
      margin-bottom: 5px;
      color: var(--muted);
      font-size: 12px;
      font-weight: 700;
    }

    input, textarea {
      width: 100%;
      color: var(--text);
      background: rgba(0, 0, 0, .30);
      border: 1px solid rgba(148, 163, 184, .24);
      border-radius: 11px;
      outline: none;
    }

    input {
      height: 39px;
      padding: 0 10px;
    }

    input:focus, textarea:focus {
      border-color: rgba(239, 68, 68, .60);
      box-shadow: 0 0 0 3px rgba(239, 68, 68, .14);
    }

    textarea {
      min-height: 154px;
      resize: vertical;
      padding: 10px;
      font: 12px/1.45 Consolas, Monaco, monospace;
    }

    .actions {
      display: flex;
      flex-wrap: wrap;
      gap: 8px;
      margin-top: 10px;
    }

    button {
      appearance: none;
      border: 1px solid rgba(148, 163, 184, .24);
      color: var(--text);
      background: rgba(15, 23, 42, .92);
      border-radius: 999px;
      padding: 9px 12px;
      font-weight: 900;
      font-size: 12px;
      cursor: pointer;
    }

    button:hover {
      border-color: rgba(239, 68, 68, .70);
      background: rgba(127, 29, 29, .55);
    }

    button.primary {
      border-color: rgba(239, 68, 68, .55);
      background: linear-gradient(135deg, rgba(239, 68, 68, .92), rgba(127, 29, 29, .92));
    }

    .help {
      color: #cbd5e1;
      font-size: 12px;
      line-height: 1.5;
    }

    .marker-list {
      max-height: 260px;
      overflow: auto;
      display: grid;
      gap: 8px;
    }

    .marker {
      border: 1px solid rgba(148, 163, 184, .16);
      background: rgba(0, 0, 0, .25);
      border-radius: 12px;
      padding: 9px 10px;
      cursor: pointer;
    }

    .marker:hover {
      border-color: rgba(59, 130, 246, .70);
      background: rgba(30, 64, 175, .28);
    }

    .marker-name { font-weight: 900; }

    .marker-coords {
      margin-top: 3px;
      color: var(--muted);
      font-size: 12px;
      font-family: Consolas, Monaco, monospace;
    }

    #tooltip {
      position: fixed;
      z-index: 50;
      display: none;
      max-width: 340px;
      padding: 7px 9px;
      border-radius: 9px;
      background: rgba(15, 23, 42, .96);
      border: 1px solid rgba(148, 163, 184, .45);
      color: #e5e7eb;
      font: 12px/1.35 Consolas, Monaco, monospace;
      pointer-events: none;
      box-shadow: 0 12px 26px rgba(0, 0, 0, .55);
    }

    .toast {
      position: fixed;
      right: 16px;
      bottom: 16px;
      z-index: 60;
      display: none;
      padding: 10px 13px;
      border-radius: 12px;
      border: 1px solid rgba(34, 197, 94, .40);
      background: rgba(20, 83, 45, .92);
      color: #dcfce7;
      font-weight: 900;
      box-shadow: var(--shadow);
    }

    @media (max-width: 1080px) {
      .layout { grid-template-columns: 1fr; }
      .side { grid-template-columns: repeat(2, minmax(0, 1fr)); }
      .side .wide { grid-column: 1 / -1; }
    }

    @media (max-width: 720px) {
      .topbar { align-items: start; flex-direction: column; }
      .side { grid-template-columns: 1fr; }
      .stats { grid-template-columns: 1fr; }
      .page { width: min(100% - 18px, 1500px); padding-top: 10px; }
    }
  </style>
    <link rel="stylesheet" href="../theme.css">
</head>
<body>
<?php require dirname(__DIR__) . '/shared/menu.php'; ?>
  <main class="page">
    <header class="topbar">
      <div>
        <h1>SCUM Kartographer</h1>
        <p class="subtitle">Öffentliche Admin-Hilfskarte für Koordinaten, Marker und Radius-Zonen. Ausgabe immer als <b>X Y 0</b>.</p>
      </div>
      <div class="badge">Standalone / ohne Login</div>
    </header>

    <section class="layout">
      <div class="panel map-panel">
        <div class="mapbox" id="mapBox">
          <img id="mapImage" src="<?= htmlspecialchars($mapImage, ENT_QUOTES, 'UTF-8') ?>" alt="SCUM Karte">
          <canvas id="mapCanvas"></canvas>
        </div>
      </div>

      <aside class="side">
        <div class="panel box">
          <div class="box-title">Aktueller Punkt <span class="muted" id="modeText">Klick/Ziehen</span></div>
          <div class="stats">
            <div class="stat"><div class="label">X</div><div class="value" id="kgX">-</div></div>
            <div class="stat"><div class="label">Y</div><div class="value" id="kgY">-</div></div>
            <div class="stat"><div class="label">Radius</div><div class="value" id="kgR">0</div></div>
          </div>

          <label for="kgName">Name / Notiz</label>
          <input id="kgName" placeholder="z.B. Spawnzone, Questpunkt, PvP-Zone">

          <label for="kgRadius">Radius manuell</label>
          <input id="kgRadius" type="number" step="1" min="0" value="0">

          <div class="actions">
            <button type="button" class="primary" id="kgSaveMarker">Merken</button>
            <button type="button" id="kgCopyCoords">Coords kopieren</button>
            <button type="button" id="kgCopyJson">Ausgabe kopieren</button>
            <button type="button" id="kgClearCurrent">Punkt leeren</button>
          </div>
        </div>

        <div class="panel box wide">
          <div class="box-title">Ausgabe</div>
          <textarea id="kgOutput" readonly>Auf die Karte klicken...</textarea>
        </div>

        <div class="panel box wide">
          <div class="box-title">Lokal gemerkte Punkte <span class="muted" id="markerCount">0</span></div>
          <div id="kgMarkerList" class="marker-list"></div>
          <div class="actions">
            <button type="button" id="kgExportMarkers">Liste kopieren</button>
            <button type="button" id="kgClearMarkers">Liste leeren</button>
          </div>
        </div>

        <div class="panel box help wide">
          <b>Bedienung:</b><br>
          Klick auf die Karte setzt einen Punkt. Maus gedrückt halten und ziehen erstellt direkt einen Radius.
          Der Radius kann danach manuell angepasst werden. Gemerkte Punkte werden nur lokal in diesem Browser gespeichert und nicht auf dem Server/global geteilt.
          Shift + Klick auf einen gemerkten Punkt löscht nur diesen lokalen Eintrag.
        </div>
      </aside>
    </section>
  </main>

  <div id="tooltip"></div>
  <div class="toast" id="toast">Kopiert</div>

  <script>
  (() => {
    const worldLeftX = <?= (int)$worldLeftX ?>;
    const worldRightX = <?= (int)$worldRightX ?>;
    const worldTopY = <?= (int)$worldTopY ?>;
    const worldBottomY = <?= (int)$worldBottomY ?>;

    const box = document.getElementById('mapBox');
    const img = document.getElementById('mapImage');
    const canvas = document.getElementById('mapCanvas');
    const ctx = canvas.getContext('2d');
    const tip = document.getElementById('tooltip');
    const toast = document.getElementById('toast');

    const elX = document.getElementById('kgX');
    const elY = document.getElementById('kgY');
    const elR = document.getElementById('kgR');
    const elName = document.getElementById('kgName');
    const elRadius = document.getElementById('kgRadius');
    const elOutput = document.getElementById('kgOutput');
    const elList = document.getElementById('kgMarkerList');
    const markerCount = document.getElementById('markerCount');
    const modeText = document.getElementById('modeText');

    const storeKey = 'scum_kartographer_markers_v3_' + location.pathname;
    let markers = loadMarkers();
    let current = null;
    let dragStart = null;
    let dragging = false;
    let toastTimer = null;

    function esc(s) {
      return String(s ?? '').replace(/[&<>"']/g, m => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[m]));
    }

    function round(n) {
      return Math.round(Number(n) || 0);
    }

    function pixelToWorld(px, py) {
      return {
        x: worldLeftX + (px / canvas.width) * (worldRightX - worldLeftX),
        y: worldTopY + (py / canvas.height) * (worldBottomY - worldTopY)
      };
    }

    function worldToPixel(x, y) {
      return {
        px: ((x - worldLeftX) / (worldRightX - worldLeftX)) * canvas.width,
        py: ((y - worldTopY) / (worldBottomY - worldTopY)) * canvas.height
      };
    }

    function eventToCanvas(e) {
      const rect = canvas.getBoundingClientRect();
      const clientX = e.clientX ?? (e.touches?.[0]?.clientX || 0);
      const clientY = e.clientY ?? (e.touches?.[0]?.clientY || 0);
      return {
        px: (clientX - rect.left) * (canvas.width / rect.width),
        py: (clientY - rect.top) * (canvas.height / rect.height),
        clientX,
        clientY
      };
    }

    function distanceWorld(a, b) {
      const dx = Number(a.x) - Number(b.x);
      const dy = Number(a.y) - Number(b.y);
      return Math.sqrt(dx * dx + dy * dy);
    }

    function radiusWorldToPx(radius) {
      const scaleX = canvas.width / Math.abs(worldRightX - worldLeftX);
      const scaleY = canvas.height / Math.abs(worldBottomY - worldTopY);
      return Number(radius || 0) * ((scaleX + scaleY) / 2);
    }

    function resize() {
      const rect = box.getBoundingClientRect();
      canvas.width = Math.max(1, Math.round(rect.width));
      canvas.height = Math.max(1, Math.round(rect.height));
      draw();
    }

    function setCurrent(point) {
      current = {
        name: point.name || elName.value || '',
        x: round(point.x),
        y: round(point.y),
        z: 0,
        radius: Math.max(0, round(point.radius || 0))
      };
      elX.textContent = current.x;
      elY.textContent = current.y;
      elR.textContent = current.radius;
      elRadius.value = current.radius;
      updateOutput();
      draw();
    }

    function clearCurrent() {
      current = null;
      dragStart = null;
      dragging = false;
      elX.textContent = '-';
      elY.textContent = '-';
      elR.textContent = '0';
      elRadius.value = '0';
      elOutput.value = 'Auf die Karte klicken...';
      modeText.textContent = 'Klick/Ziehen';
      draw();
    }

    function updateOutput() {
      if (!current) return;
      const name = (elName.value || current.name || '').trim();
      const coords = `${current.x} ${current.y} 0`;
      const json = {
        name,
        x: current.x,
        y: current.y,
        z: 0,
        radius: current.radius
      };
      const questMarkerSnippet = {
        MapMarkerCoordinates: {
          X: current.x,
          Y: current.y,
          Z: 0
        },
        Radius: current.radius
      };

      elOutput.value = [
        'Coords:',
        coords,
        '',
        'JSON:',
        JSON.stringify(json, null, 2),
        '',
        'Quest/Marker-Snippet:',
        JSON.stringify(questMarkerSnippet, null, 2)
      ].join('\n');
    }

    function drawCross(p, color, radiusColor) {
      const {px, py} = worldToPixel(p.x, p.y);
      const r = radiusWorldToPx(p.radius || 0);

      if (r > 0) {
        ctx.beginPath();
        ctx.arc(px, py, r, 0, Math.PI * 2);
        ctx.fillStyle = radiusColor || 'rgba(248,113,113,.13)';
        ctx.fill();
        ctx.strokeStyle = color;
        ctx.lineWidth = 2;
        ctx.stroke();
      }

      ctx.beginPath();
      ctx.arc(px, py, 5, 0, Math.PI * 2);
      ctx.fillStyle = color;
      ctx.fill();
      ctx.strokeStyle = 'rgba(0,0,0,.82)';
      ctx.lineWidth = 2;
      ctx.stroke();

      ctx.beginPath();
      ctx.moveTo(px - 10, py);
      ctx.lineTo(px + 10, py);
      ctx.moveTo(px, py - 10);
      ctx.lineTo(px, py + 10);
      ctx.strokeStyle = 'rgba(255,255,255,.94)';
      ctx.lineWidth = 1.5;
      ctx.stroke();
    }

    function draw() {
      ctx.clearRect(0, 0, canvas.width, canvas.height);
      markers.forEach(m => drawCross(m, 'rgba(59,130,246,.95)', 'rgba(59,130,246,.11)'));
      if (current) drawCross(current, 'rgba(239,68,68,.98)', 'rgba(239,68,68,.14)');
    }

    function loadMarkers() {
      try {
        const raw = localStorage.getItem(storeKey);
        const data = raw ? JSON.parse(raw) : [];
        return Array.isArray(data) ? data : [];
      } catch (e) {
        return [];
      }
    }

    function saveMarkers() {
      localStorage.setItem(storeKey, JSON.stringify(markers));
    }

    function renderList() {
      markerCount.textContent = String(markers.length);
      if (!markers.length) {
        elList.innerHTML = '<div class="muted" style="font-size:12px">Noch keine Punkte gemerkt.</div>';
        return;
      }
      elList.innerHTML = markers.map((m, i) => `
        <div class="marker" data-i="${i}">
          <div class="marker-name">${esc(m.name || ('Punkt ' + (i + 1)))}</div>
          <div class="marker-coords">${round(m.x)} ${round(m.y)} 0 · Radius ${round(m.radius || 0)}</div>
        </div>
      `).join('');

      elList.querySelectorAll('.marker').forEach(el => {
        el.addEventListener('click', (e) => {
          const i = Number(el.dataset.i);
          if (!Number.isFinite(i) || !markers[i]) return;
          if (e.shiftKey) {
            markers.splice(i, 1);
            saveMarkers();
            renderList();
            draw();
            return;
          }
          elName.value = markers[i].name || '';
          setCurrent(markers[i]);
        });
      });
    }

    function copyText(text, label = 'Kopiert') {
      const done = () => showToast(label);
      if (navigator.clipboard?.writeText) {
        return navigator.clipboard.writeText(text).then(done).catch(() => fallbackCopy(text, done));
      }
      return fallbackCopy(text, done);
    }

    function fallbackCopy(text, done) {
      const ta = document.createElement('textarea');
      ta.value = text;
      ta.style.position = 'fixed';
      ta.style.left = '-9999px';
      document.body.appendChild(ta);
      ta.select();
      document.execCommand('copy');
      ta.remove();
      done();
      return Promise.resolve();
    }

    function showToast(text) {
      clearTimeout(toastTimer);
      toast.textContent = text;
      toast.style.display = 'block';
      toastTimer = setTimeout(() => toast.style.display = 'none', 1200);
    }

    function tooltip(text, x, y) {
      tip.textContent = text;
      tip.style.left = (x + 12) + 'px';
      tip.style.top = (y + 12) + 'px';
      tip.style.display = 'block';
    }

    function hideTooltip() {
      tip.style.display = 'none';
    }

    canvas.addEventListener('pointerdown', (e) => {
      canvas.setPointerCapture?.(e.pointerId);
      const pos = eventToCanvas(e);
      const w = pixelToWorld(pos.px, pos.py);
      dragStart = w;
      dragging = true;
      modeText.textContent = 'Radius ziehen';
      setCurrent({ x: w.x, y: w.y, radius: 0 });
      tooltip(`${round(w.x)} ${round(w.y)} 0`, pos.clientX, pos.clientY);
    });

    canvas.addEventListener('pointermove', (e) => {
      const pos = eventToCanvas(e);
      const w = pixelToWorld(pos.px, pos.py);
      if (dragging && dragStart) {
        setCurrent({
          x: dragStart.x,
          y: dragStart.y,
          radius: distanceWorld(dragStart, w)
        });
        tooltip(`${round(current.x)} ${round(current.y)} 0 · R ${round(current.radius)}`, pos.clientX, pos.clientY);
        return;
      }
      tooltip(`${round(w.x)} ${round(w.y)} 0`, pos.clientX, pos.clientY);
    });

    canvas.addEventListener('pointerup', (e) => {
      canvas.releasePointerCapture?.(e.pointerId);
      dragging = false;
      dragStart = null;
      modeText.textContent = 'Klick/Ziehen';
    });

    canvas.addEventListener('pointercancel', () => {
      dragging = false;
      dragStart = null;
      modeText.textContent = 'Klick/Ziehen';
      hideTooltip();
    });

    canvas.addEventListener('pointerleave', () => {
      if (!dragging) hideTooltip();
    });

    elRadius.addEventListener('input', () => {
      if (!current) return;
      current.radius = Math.max(0, round(elRadius.value));
      elR.textContent = current.radius;
      updateOutput();
      draw();
    });

    elName.addEventListener('input', () => {
      if (current) current.name = elName.value;
      updateOutput();
    });

    document.getElementById('kgCopyCoords').addEventListener('click', () => {
      if (!current) return;
      copyText(`${current.x} ${current.y} 0`, 'Coords kopiert');
    });

    document.getElementById('kgCopyJson').addEventListener('click', () => {
      if (!current) return;
      copyText(elOutput.value, 'Ausgabe kopiert');
    });

    document.getElementById('kgSaveMarker').addEventListener('click', () => {
      if (!current) return;
      markers.push({
        name: (elName.value || current.name || '').trim(),
        x: current.x,
        y: current.y,
        z: 0,
        radius: current.radius
      });
      saveMarkers();
      renderList();
      draw();
      showToast('Punkt lokal gemerkt');
    });

    document.getElementById('kgClearCurrent').addEventListener('click', clearCurrent);

    document.getElementById('kgExportMarkers').addEventListener('click', () => {
      copyText(JSON.stringify(markers, null, 2), 'Liste kopiert');
    });

    document.getElementById('kgClearMarkers').addEventListener('click', () => {
      if (!markers.length) return;
      if (!confirm('Wirklich alle gemerkten Punkte lokal löschen?')) return;
      markers = [];
      saveMarkers();
      renderList();
      draw();
    });

    if (!img.complete) img.addEventListener('load', resize, { once: true });
    resize();
    window.addEventListener('resize', resize);
    renderList();
  })();
  </script>
</body>
</html>
