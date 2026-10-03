<?php
// scum-tools.php – Landing/subpage for SCUM RCON Tool + Scum Script Studio
// Optional images under:
// /assets/scum-tools/
// Example files:
// logo.png
// rcon_dashboard.png
// rcon_discord.png
// rcon_scripts.png
// studio_simple.png
// studio_loot.png
// studio_raw.png
//
// Optional downloads via /download.php?file=...
// Expected keys in /data/downloads.json:
// scum_rcon_tool
// scum_script_studio

$pageTitle = "Red Raven – RCON Tool & Script Studio";
require_once __DIR__ . '/includes/header.php';

$donateUrl = "https://www.paypal.com/donate/?hosted_button_id=RCFQSGQFBPWJA";
$discordUrl = "https://discord.gg/EYhabuyt5T";
$gghostAffiliateUrl = "https://www.gghost.games/aff.php?aff=506";

// TODO: Add your YouTube link here
$youtubeUrl = "https://youtu.be/QM01HwUD2kE?si=ZXIrJ6e15m4cbDi0";

$downloads = [
  "scum_rcon_tool" => 0,
  "scum_script_studio" => 0,
];

$jsonPath = $_SERVER['DOCUMENT_ROOT'] . "/data/downloads.json";
if (is_file($jsonPath)) {
  $obj = json_decode(file_get_contents($jsonPath), true);
  foreach ($downloads as $key => $_) {
    $downloads[$key] = (int)($obj["files"][$key] ?? 0);
  }
}

$rconDownloadUrl = "/download.php?file=scum_rcon_tool";
$studioDownloadUrl = "/download.php?file=scum_script_studio";

$hasRconDownload = true;
$hasStudioDownload = true;
?>
<!doctype html>
<html lang="de">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <title><?= htmlspecialchars($pageTitle) ?></title>

  <style>
    :root{
      --bg:#0d0d10;
      --bg2:#121218;
      --card:#17171d;
      --card2:#1f1f28;
      --card3:#252530;
      --text:#eeeeee;
      --muted:#b5b5bf;
      --border:#343443;
      --border2:#444457;
      --accent:#e53939;
      --accent2:#9e2020;
      --accent3:#ff4b4b;
      --yellow:#ffc439;
      --green:#39d98a;
      --blue:#4aa3ff;
    }

    *{box-sizing:border-box}

    html{scroll-behavior:smooth}

    body{
      margin:0;
      color:var(--text);
      font-family:system-ui,-apple-system,Segoe UI,Roboto,Arial,sans-serif;
      background:
        radial-gradient(circle at 18% 0%, rgba(229,57,57,.16), transparent 32%),
        radial-gradient(circle at 82% 10%, rgba(120,30,30,.14), transparent 34%),
        linear-gradient(180deg,#08080a 0%, var(--bg) 38%, #0b0b0e 100%);
    }

    a{color:inherit}

    .wrap{
      max-width:1180px;
      margin:0 auto;
      padding:28px 16px 70px;
    }

    .topbar{
      display:flex;
      align-items:center;
      justify-content:space-between;
      gap:14px;
      margin-bottom:18px;
    }

    .brand{
      display:flex;
      align-items:center;
      gap:12px;
      min-width:0;
    }

    .logo{
      width:48px;
      height:48px;
      border-radius:14px;
      background:linear-gradient(135deg,var(--card2),#111116);
      border:1px solid var(--border);
      display:grid;
      place-items:center;
      overflow:hidden;
      box-shadow:0 8px 30px rgba(0,0,0,.25);
      flex:0 0 auto;
    }

    .logo img{
      width:100%;
      height:100%;
      object-fit:cover;
    }

    .logo-fallback{
      font-weight:900;
      color:var(--accent);
      letter-spacing:.5px;
    }

    .brand-title{
      display:flex;
      align-items:center;
      flex-wrap:wrap;
      gap:10px;
    }

    .brand strong{
      font-size:17px;
    }

    .brand-sub{
      color:var(--muted);
      font-size:12px;
      margin-top:2px;
    }

    .pill,
    .badge{
      display:inline-flex;
      align-items:center;
      gap:8px;
      font-size:12px;
      border-radius:999px;
      white-space:nowrap;
    }

    .pill{
      color:var(--muted);
      border:1px solid var(--border);
      background:rgba(255,255,255,.035);
      padding:6px 10px;
    }

    .badge{
      color:white;
      background:linear-gradient(135deg,var(--accent),var(--accent2));
      border:1px solid rgba(255,255,255,.14);
      padding:7px 11px;
      font-weight:800;
      box-shadow:0 8px 24px rgba(229,57,57,.18);
    }

    .hero{
      display:grid;
      grid-template-columns:1.15fr .85fr;
      gap:18px;
      align-items:stretch;
      margin-top:12px;
    }

    @media (max-width:920px){
      .hero{grid-template-columns:1fr}
      .topbar{align-items:flex-start;flex-direction:column}
    }

    .card{
      background:linear-gradient(180deg,rgba(23,23,29,.94),rgba(18,18,24,.92));
      border:1px solid var(--border);
      border-radius:20px;
      padding:20px;
      box-shadow:0 14px 42px rgba(0,0,0,.28);
    }

    .card.glow{
      position:relative;
      overflow:hidden;
    }

    .card.glow:before{
      content:"";
      position:absolute;
      inset:-2px;
      background:
        radial-gradient(circle at 14% 0%, rgba(229,57,57,.18), transparent 30%),
        radial-gradient(circle at 86% 20%, rgba(255,75,75,.12), transparent 26%);
      pointer-events:none;
    }

    .card.glow > *{position:relative}

    h1{
      margin:0 0 10px;
      font-size:clamp(32px,5vw,54px);
      line-height:1.02;
      letter-spacing:-.8px;
    }

    h2{
      margin:0 0 10px;
      font-size:22px;
    }

    h3{
      margin:0 0 8px;
      font-size:17px;
    }

    p{
      margin:0 0 10px;
      color:var(--muted);
      line-height:1.55;
    }

    .lead{
      font-size:16px;
      max-width:760px;
    }

    .accent-text{
      color:#ff6868;
    }

    .btnrow{
      display:flex;
      flex-wrap:wrap;
      gap:10px;
      margin-top:16px;
      align-items:center;
    }

    .btn{
      display:inline-flex;
      align-items:center;
      justify-content:center;
      gap:8px;
      min-height:42px;
      padding:0 15px;
      border-radius:13px;
      border:1px solid var(--border2);
      background:var(--card2);
      color:var(--text);
      text-decoration:none;
      font-weight:700;
      transition:transform .12s ease, filter .12s ease, border-color .12s ease, background .12s ease;
    }

    .btn:hover{
      transform:translateY(-1px);
      filter:brightness(1.08);
      border-color:var(--accent3);
    }

    .btn-primary{
      background:linear-gradient(135deg,var(--accent),var(--accent2));
      border-color:rgba(255,255,255,.16);
      box-shadow:0 10px 26px rgba(229,57,57,.18);
    }

    .btn-paypal{
      background:var(--yellow);
      color:#111;
      border-color:#d9a400;
    }

    .btn-youtube{
      background:#ff0000;
      color:white;
      border-color:#cf0000;
    }

    .btn-gghost{
      background:linear-gradient(135deg,#2d7dff,#1840a8);
      color:white;
      border-color:#4c8dff;
      box-shadow:0 10px 26px rgba(45,125,255,.18);
    }

    .btn-ghost{
      background:transparent;
    }

    .note{
      font-size:12px;
      color:var(--muted);
      margin-top:10px;
    }

    .tool-grid{
      display:grid;
      grid-template-columns:repeat(2,1fr);
      gap:14px;
      margin-top:18px;
    }

    @media (max-width:920px){
      .tool-grid{grid-template-columns:1fr}
    }

    .tool-card{
      background:rgba(31,31,40,.78);
      border:1px solid var(--border);
      border-radius:18px;
      padding:16px;
      position:relative;
      overflow:hidden;
    }

    .tool-card:before{
      content:"";
      position:absolute;
      left:0;
      top:0;
      bottom:0;
      width:4px;
      background:linear-gradient(180deg,var(--accent),var(--accent2));
    }

    .tool-head{
      display:flex;
      align-items:flex-start;
      justify-content:space-between;
      gap:12px;
      margin-bottom:8px;
    }

    .tool-tag{
      padding:5px 8px;
      border-radius:999px;
      font-size:11px;
      font-weight:800;
      background:rgba(229,57,57,.14);
      border:1px solid rgba(229,57,57,.34);
      color:#ffb0b0;
      white-space:nowrap;
    }

    .feature-grid{
      display:grid;
      grid-template-columns:repeat(3,1fr);
      gap:12px;
      margin-top:14px;
    }

    @media (max-width:920px){
      .feature-grid{grid-template-columns:1fr}
    }

    .feat{
      background:rgba(37,37,48,.72);
      border:1px solid var(--border);
      border-radius:16px;
      padding:14px;
    }

    .feat p{
      margin:0;
      font-size:13px;
    }

    .section{
      margin-top:22px;
    }

    .steps{
      display:grid;
      grid-template-columns:repeat(4,1fr);
      gap:12px;
      margin-top:12px;
    }

    @media (max-width:920px){
      .steps{grid-template-columns:1fr}
    }

    .step{
      background:rgba(23,23,29,.82);
      border:1px solid var(--border);
      border-radius:16px;
      padding:14px;
    }

    .step-num{
      width:28px;
      height:28px;
      display:grid;
      place-items:center;
      border-radius:9px;
      background:linear-gradient(135deg,var(--accent),var(--accent2));
      color:white;
      font-weight:900;
      margin-bottom:9px;
    }

    .shots{
      display:grid;
      grid-template-columns:repeat(3,1fr);
      gap:12px;
      margin-top:12px;
    }

    @media (max-width:920px){
      .shots{grid-template-columns:1fr}
    }

    figure{
      margin:0;
      background:rgba(23,23,29,.92);
      border:1px solid var(--border);
      border-radius:16px;
      overflow:hidden;
    }

    figure a{
      display:block;
      background:#09090b;
      min-height:160px;
    }

    figure img{
      width:100%;
      height:220px;
      display:block;
      object-fit:cover;
      opacity:.96;
      transition:transform .16s ease, opacity .16s ease;
    }

    figure a:hover img{
      transform:scale(1.015);
      opacity:1;
    }

    figcaption{
      padding:10px 12px;
      color:var(--muted);
      font-size:12px;
    }

    .compare{
      width:100%;
      border-collapse:separate;
      border-spacing:0;
      overflow:hidden;
      border:1px solid var(--border);
      border-radius:16px;
      margin-top:12px;
      background:rgba(23,23,29,.84);
    }

    .compare th,
    .compare td{
      padding:12px;
      border-bottom:1px solid var(--border);
      vertical-align:top;
      text-align:left;
    }

    .compare th{
      background:rgba(229,57,57,.12);
      color:#ffd1d1;
      font-size:13px;
    }

    .compare tr:last-child td{
      border-bottom:0;
    }

    .compare td{
      color:var(--muted);
      font-size:13px;
    }

    .footer{
      margin-top:24px;
      color:var(--muted);
      font-size:12px;
      border-top:1px solid var(--border);
      padding-top:14px;
    }

    .lightbox{
      position:fixed;
      inset:0;
      background:rgba(0,0,0,.84);
      display:none;
      align-items:center;
      justify-content:center;
      padding:24px;
      z-index:9999;
    }

    .lightbox.open{display:flex}

    .lightbox-inner{
      max-width:min(1280px,96vw);
      max-height:92vh;
      background:rgba(20,20,26,.98);
      border:1px solid var(--border);
      border-radius:18px;
      overflow:hidden;
      box-shadow:0 18px 48px rgba(0,0,0,.6);
    }

    .lightbox-img{
      display:block;
      width:100%;
      height:auto;
      max-height:80vh;
      object-fit:contain;
      background:#08080a;
    }

    .lightbox-bar{
      display:flex;
      align-items:center;
      justify-content:space-between;
      gap:12px;
      padding:10px 12px;
      color:var(--muted);
      font-size:12px;
    }

    .lightbox-btn{
      height:32px;
      padding:0 12px;
      border-radius:10px;
      border:1px solid var(--border);
      background:rgba(255,255,255,.04);
      color:var(--text);
      cursor:pointer;
      font-weight:800;
    }

    .lightbox-btn:hover{
      border-color:var(--accent);
      background:rgba(229,57,57,.16);
    }
  </style>
</head>

<body>
  <div class="wrap">
    <div class="topbar">
      <div class="brand">
        <div class="logo">
          <img src="/assets/scum-tools/logo.png" alt="SCUM Tools Logo" onerror="this.style.display='none';this.nextElementSibling.style.display='block'">
          <span class="logo-fallback" style="display:none;">ST</span>
        </div>
        <div>
          <div class="brand-title">
            <strong>SCUM Admin Tools</strong>
            <span class="pill">RCON · Discord · Events · Script Editor</span>
          </div>
          <div class="brand-sub">Community tools for SCUM server admins</div>
        </div>
      </div>
      <span class="badge">BLACK / RED EDITION</span>
    </div>

    <section class="hero">
      <div class="card glow">
        <h1>Red Raven</h1>
        <p class="lead">
          Two tools built specifically for <span class="accent-text">GGcon by GGhost</span>: the <span class="accent-text">SCUM RCON Tool</span>
          for live operations, Discord, logs, commands and events – plus <span class="accent-text">Scum Script Studio</span>
          for quickly creating and editing your GGcon event scripts.
        </p>
        <p class="note">
          Community project for GGcon/GGhost servers. Not an official tool by Gamepires/SCUM or GGhost.
        </p>

        <div class="btnrow">
          <?php if ($hasRconDownload): ?>
            <a class="btn btn-primary" href="<?= htmlspecialchars($rconDownloadUrl) ?>">⬇️ RCON Tool Download</a>
          <?php endif; ?>

          <?php if ($hasStudioDownload): ?>
            <a class="btn" href="<?= htmlspecialchars($studioDownloadUrl) ?>">⬇️ Script Studio Download</a>
          <?php endif; ?>

          <a class="btn btn-youtube" href="<?= htmlspecialchars($youtubeUrl) ?>" target="_blank" rel="noopener noreferrer">▶️ YouTube Video</a>
          <a class="btn btn-gghost" href="<?= htmlspecialchars($gghostAffiliateUrl) ?>" target="_blank" rel="noopener noreferrer">🚀 Rent GGcon at GGhost</a>
          <a class="btn" href="<?= htmlspecialchars($discordUrl) ?>" target="_blank" rel="noopener noreferrer">🛠️ Discord Support</a>
          <a class="btn btn-youtube" href="https://github.com/LMNT-Gaming/ScumRconTool" target="_blank" rel="noopener noreferrer">▶🚰 Source</a>
          <a class="btn btn-paypal" href="<?= htmlspecialchars($donateUrl) ?>" target="_blank" rel="noopener noreferrer">💛 Donate</a>
        </div>

        <div class="feature-grid" id="features">
          <div class="feat">
            <h3>Live Admin Operations</h3>
            <p>RCON, player list, join commands and debug logs in one interface.</p>
          </div>
          <div class="feat">
            <h3>Discord Integration</h3>
            <p>Chat log embeds, player list, active random events and an optional Discord → game bridge.</p>
          </div>
          <div class="feat">
            <h3>Event Automation</h3>
            <p>Silent quests, random quests after restart schedule, NPCs, puppets, loot and cleanup.</p>
          </div>
        </div>
      </div>

      <aside class="card">
        <h2>Quick Overview</h2>
        <p>✅ Dark WPF frontend in black/red</p>
        <p>✅ GGcon-compatible SFTP/FTP log downloads with saved read positions</p>
        <p>✅ Discord bot with status, player list and embeds</p>
        <p>✅ Script engine for GGcon events, guards, puppets and loot</p>
        <p>✅ Separate script editor for easy event creation</p>

        <div class="section">
          <h2>Downloads</h2>
          <p class="note">Download counters are read from <code>/data/downloads.json</code>, if present.</p>
          <div class="btnrow">
            <span class="pill">RCON Tool: <?= number_format($downloads["scum_rcon_tool"], 0, ",", ".") ?> Downloads</span>
            <span class="pill">Script Studio: <?= number_format($downloads["scum_script_studio"], 0, ",", ".") ?> Downloads</span>
          </div>
        </div>
      </aside>
    </section>

    <section class="tool-grid" id="tools">
      <article class="tool-card">
        <div class="tool-head">
          <div>
            <h2>SCUM RCON Tool</h2>
            <p>The main program for running your GGcon/SCUM server.</p>
          </div>
          <span class="tool-tag">MAIN TOOL</span>
        </div>

        <div class="feature-grid" style="grid-template-columns:repeat(2,1fr)">
          <div class="feat">
            <h3>RCON & Commands</h3>
            <p>Manual commands, chat commands, join commands and scheduled actions.</p>
          </div>
          <div class="feat">
            <h3>Discord Bot</h3>
            <p>Status, chat logs as embeds, player list and active random events.</p>
          </div>
          <div class="feat">
            <h3>Kill Feed</h3>
            <p>Read kill logs and broadcast colorized kill messages ingame.</p>
          </div>
          <div class="feat">
            <h3>Script Engine</h3>
            <p>Silent quests keep running, random quests start according to schedule.</p>
          </div>
        </div>

        <div class="btnrow">
          <a class="btn btn-primary" href="<?= htmlspecialchars($rconDownloadUrl) ?>">⬇️ Download</a>
          <a class="btn btn-ghost" href="#screens">📸 Screenshots</a>
        </div>
      </article>

      <article class="tool-card">
        <div class="tool-head">
          <div>
            <h2>Scum Script Studio</h2>
            <p>Separate editor for GGcon event scripts without JSON hassle.</p>
          </div>
          <span class="tool-tag">SCRIPT EDITOR</span>
        </div>

        <div class="feature-grid" style="grid-template-columns:repeat(2,1fr)">
          <div class="feat">
            <h3>Simple Mode</h3>
            <p>Manage name, NPC/puppet spawns, loot spots and loot packs.</p>
          </div>
          <div class="feat">
            <h3>Auto Zone</h3>
            <p>Activation zone is calculated automatically from spawn points.</p>
          </div>
          <div class="feat">
            <h3>Chest Loot</h3>
            <p>Loot packs are automatically generated as chest commands.</p>
          </div>
          <div class="feat">
            <h3>Validation</h3>
            <p>Checks missing fields, invalid zones and suspicious settings.</p>
          </div>
        </div>

        <div class="btnrow">
          <a class="btn btn-primary" href="<?= htmlspecialchars($studioDownloadUrl) ?>">⬇️ Download</a>
          <a class="btn btn-ghost" href="#workflow">⚙️ Workflow</a>
        </div>
      </article>
    </section>

    <section class="section" id="workflow">
      <h2>Recommended Workflow</h2>
      <div class="steps">
        <div class="step">
          <div class="step-num">1</div>
          <h3>Build event</h3>
          <p>Set NPCs, puppets, loot spots and loot packs in Script Studio Simple Mode.</p>
        </div>
        <div class="step">
          <div class="step-num">2</div>
          <h3>Save JSON</h3>
          <p>Script Studio automatically generates the activation zone, cleanup and chest commands.</p>
        </div>
        <div class="step">
          <div class="step-num">3</div>
          <h3>Load into RCON Tool</h3>
          <p>Place the script in the script folder and start the Script Engine or let it autostart.</p>
        </div>
        <div class="step">
          <div class="step-num">4</div>
          <h3>Monitor</h3>
          <p>Dashboard, debug log and Discord event status show what is initialized or live.</p>
        </div>
      </div>
    </section>

    <section class="section">
      <h2>Tool Comparison</h2>
      <table class="compare">
        <thead>
          <tr>
            <th>Area</th>
            <th>SCUM RCON Tool</th>
            <th>Scum Script Studio</th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <td>Main purpose</td>
            <td>Server operations, Discord bot, logs, commands, Script Engine</td>
            <td>Create and edit event scripts comfortably</td>
          </tr>
          <tr>
            <td>Who is it for?</td>
            <td>Admins who want to monitor and automate their running server</td>
            <td>Admins who manage events, NPC spawns, puppets and loot packs</td>
          </tr>
          <tr>
            <td>Key feature</td>
            <td>Automation after server restart, chat/kill/join workflows</td>
            <td>Simple Mode with automatic activation zone and cleanup generation</td>
          </tr>
        </tbody>
      </table>
    </section>

    <section class="section" id="screens">
      <h2>Screenshots</h2>
      <div class="shots">
        <figure>
          <a href="/assets/scum-tools/rcon_dashboard.png" class="shot" data-title="RCON Tool – Dashboard">
            <img src="/assets/scum-tools/rcon_dashboard.png" alt="RCON Tool Dashboard" onerror="this.src='/assets/scum-tools/placeholder.png'">
          </a>
          <figcaption>RCON Tool – dashboard, services and script status</figcaption>
        </figure>

        <figure>
          <a href="/assets/scum-tools/rcon_discord.png" class="shot" data-title="RCON Tool – Discord Integration">
            <img src="/assets/scum-tools/rcon_discord.png" alt="Discord Integration" onerror="this.src='/assets/scum-tools/placeholder.png'">
          </a>
          <figcaption>Discord – chat logs, player list and active events, Schreibe via Discord auf dem Server</figcaption>
        </figure>

        <figure>
          <a href="/assets/scum-tools/studio_simple.png" class="shot" data-title="Script Studio – Simple Mode">
            <img src="/assets/scum-tools/studio_simple.png" alt="Script Studio Simple Mode" onerror="this.src='/assets/scum-tools/placeholder.png'">
          </a>
          <figcaption>Script Studio – Simple Mode for fast event creation</figcaption>
        </figure>

        <figure>
          <a href="/assets/scum-tools/rcon_scripts.png" class="shot" data-title="RCON Tool – Script Engine">
            <img src="/assets/scum-tools/rcon_scripts.png" alt="Script Engine" onerror="this.src='/assets/scum-tools/placeholder.png'">
          </a>
          <figcaption>Script Engine – silent quests and random quests</figcaption>
        </figure>

      </div>
    </section>

    <section class="section card">
      <h2>Made for GGcon by GGhost</h2>
      <p>
        The tools are designed for running <strong>GGcon by GGhost</strong>:
        RCON commands, log workflows, Discord output, event scripts and the script structure are aligned
        with the GGcon setup.
      </p>
      <p>
        If you do not have a suitable server yet, you can support the project by using my GGhost/GGcon affiliate link.
      </p>
      <div class="btnrow">
        <a class="btn btn-gghost" href="<?= htmlspecialchars($gghostAffiliateUrl) ?>" target="_blank" rel="noopener noreferrer">🚀 Go to GGhost / GGcon</a>
      </div>
    </section>

    <section class="section card">
      <h2>Support & Video</h2>
      <p>
        You can link a short video walkthrough via the YouTube button. The link opens in a new tab.
        For questions, bugs or feature requests, Discord is the best place.
      </p>
      <div class="btnrow">
        <a class="btn btn-youtube" href="<?= htmlspecialchars($youtubeUrl) ?>" target="_blank" rel="noopener noreferrer">▶️ Watch YouTube Video</a>
        <a class="btn" href="<?= htmlspecialchars($discordUrl) ?>" target="_blank" rel="noopener noreferrer">🛠️ Discord Support</a>
        <a class="btn btn-paypal" href="<?= htmlspecialchars($donateUrl) ?>" target="_blank" rel="noopener noreferrer">💛 Support the project</a>
      </div>
    </section>

    <div class="footer">
      <p>
        Note: These tools are community projects for GGcon/GGhost servers and are not officially affiliated with Gamepires or SCUM.
        Use at your own risk. Please test on a test server first.
      </p>
    </div>

    <?php require_once __DIR__ . '/includes/footer.php'; ?>
  </div>

  <div class="lightbox" id="lightbox" aria-hidden="true">
    <div class="lightbox-inner" role="dialog" aria-modal="true" aria-label="Screenshot preview">
      <img class="lightbox-img" id="lightboxImg" src="" alt="">
      <div class="lightbox-bar">
        <div id="lightboxTitle">Screenshot</div>
        <button class="lightbox-btn" id="lightboxClose" type="button">Close ✕</button>
      </div>
    </div>
  </div>

  <script>
    (function(){
      const box = document.getElementById('lightbox');
      const img = document.getElementById('lightboxImg');
      const title = document.getElementById('lightboxTitle');
      const close = document.getElementById('lightboxClose');

      document.querySelectorAll('a.shot').forEach((link) => {
        link.addEventListener('click', function(ev){
          ev.preventDefault();
          img.src = this.getAttribute('href');
          img.alt = this.dataset.title || 'Screenshot';
          title.textContent = this.dataset.title || 'Screenshot';
          box.classList.add('open');
          box.setAttribute('aria-hidden', 'false');
        });
      });

      function closeLightbox(){
        box.classList.remove('open');
        box.setAttribute('aria-hidden', 'true');
        img.src = '';
      }

      close.addEventListener('click', closeLightbox);
      box.addEventListener('click', function(ev){
        if (ev.target === box) closeLightbox();
      });
      document.addEventListener('keydown', function(ev){
        if (ev.key === 'Escape') closeLightbox();
      });
    })();
  </script>
</body>
</html>
