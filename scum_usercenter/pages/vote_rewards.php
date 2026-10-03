<?php
// /pages/vote_rewards.php
declare(strict_types=1);

if (empty($_SESSION['steamid'])) exit('Bitte einloggen.');

require_once __DIR__ . '/../functions/scum_user_function.php';
require_once __DIR__ . '/../functions/vote_function.php';

$steamId = (string)$_SESSION['steamid'];
$userProfile = scum_get_user_profile_by_steamid($steamId);

$playerName = (string)($userProfile['name'] ?? $userProfile['fake_name'] ?? 'Unbekannt');
$playerName = trim($playerName);

$voucherBalance = vote_get_vouchers($steamId);
?>

<main class="content layout-3col">

  <!-- LEFT -->
  <aside class="side left">
    <section class="panel panel-left" style="background: rgb(26 26 26 / 0%); backdrop-filter: none;border-right: none">

    </section>
  </aside>

  <!-- CENTER -->
  <section class="center">
    <div class="main-card">

      <div class="center-head">
        <div class="userblock">
          <div class="userlabel">Vote Rewards</div>
          <div class="username">Gutscheine verdienen</div>
        </div>

        <div class="moneyblock">
          <div class="userlabel">Status</div>
          <div class="money" id="cooldownText">—</div>
        </div>
      </div>

      <div class="center-body">
        <h1>Vote-Belohnung</h1>
        <div class="muted" style="font-size:12px; line-height:1.4;">
          Durchs Voten erhälst du Gutscheine mit denen du im Shop zahlen kannst
        </div>
        <div class="panel-box" style="margin-top:12px;">
          <div class="box-title">Schritt 1</div>
          <div class="muted" style="font-size:12px; line-height:1.4;">
            Kopiere deinen Namen und vote auf Top-Games. Danach hier claimen.
          </div>
          <div class="box-title">Dein Vote-Name</div>
          <div class="kv">
            <div class="kv-row">
              <span>Name</span>
              <span id="voteName"><?= htmlspecialchars($playerName, ENT_QUOTES | ENT_SUBSTITUTE, 'UTF-8') ?></span>
            </div>
            <div class="kv-row">
              <span>Gutscheine</span>
              <span id="voucherBalance"><?= number_format((int)$voucherBalance, 0, ',', '.') ?></span>
            </div>
          </div>

          <div style="display:flex; gap:8px; margin-top:10px; flex-wrap:wrap;">
            <button class="subtab" type="button" id="copyBtn">NAME KOPIEREN</button>
            <a class="subtab" href="https://top-games.net/scum/pvebeginnerchallangesvoteslmnt-gaming" target="_blank" rel="noopener">ZUR VOTE-SEITE</a>
          </div>

          <div class="muted" id="copyInfo" style="font-size:12px; margin-top:8px;"></div>
        </div>
      </div>

      <div class="panel-box" style="margin-top:12px;">
        <div class="box-title">Schritt 2</div>

        <button class="subtab" type="button" id="claimBtn" style="width:max-content;">VOTE CLAIMEN</button>
        <div id="resultText" style="margin-top:10px; font-size:12px;" class="muted"></div>
        <div id="nextClaimInfo" style="margin-top:6px; font-size:12px;" class="muted"></div>
      </div>

    </div>
    </div>
  </section>

  <!-- RIGHT -->
  <aside class="side right">

  </aside>

</main>

<script>
  document.addEventListener('DOMContentLoaded', () => {
    const copyBtn = document.getElementById('copyBtn');
    const voteName = document.getElementById('voteName');
    const copyInfo = document.getElementById('copyInfo');

    const claimBtn = document.getElementById('claimBtn');
    const resultText = document.getElementById('resultText');
    const nextClaimInfo = document.getElementById('nextClaimInfo');
    const cooldownText = document.getElementById('cooldownText');
    const voucherBalance = document.getElementById('voucherBalance');

    function setClaimDisabled(disabled) {
      claimBtn.disabled = disabled;
      claimBtn.style.opacity = disabled ? '0.5' : '1';
      claimBtn.style.pointerEvents = disabled ? 'none' : 'auto';
    }

    function fmtNext(dtStr) {
      if (!dtStr) return null;
      const d = new Date(dtStr.replace(' ', 'T'));
      if (isNaN(d.getTime())) return null;
      return d.toLocaleString('de-DE', {
        day: '2-digit',
        month: '2-digit',
        year: '2-digit',
        hour: '2-digit',
        minute: '2-digit'
      });
    }

    function applyStatus(data) {
      const next = data.next_claim_after || null;
      if (data.on_cooldown && next) {
        cooldownText.textContent = 'Cooldown';
        nextClaimInfo.textContent = 'Nächster Claim: ' + fmtNext(next);
        setClaimDisabled(true);
      } else {
        cooldownText.textContent = 'Bereit';
        nextClaimInfo.textContent = 'Du kannst voten und danach claimen.';
        setClaimDisabled(false);
      }

      if (typeof data.voucher_balance === 'number') {
        voucherBalance.textContent = data.voucher_balance.toLocaleString('de-DE');
      }
    }

    // Copy
    copyBtn?.addEventListener('click', async () => {
      try {
        await navigator.clipboard.writeText(voteName.textContent.trim());
        copyInfo.textContent = 'Name kopiert ✅';
      } catch (e) {
        copyInfo.textContent = 'Kopieren nicht möglich.';
      }
    });

    // Initial status
    (async () => {
      try {
        const res = await fetch('api/vote_status.php', {
          method: 'GET',
          credentials: 'same-origin'
        });
        const data = await res.json();
        if (data.status === 'ok') applyStatus(data);
      } catch (e) {}
    })();

    // Claim
    claimBtn?.addEventListener('click', async () => {
      resultText.textContent = 'Prüfe Vote...';
      try {
        const res = await fetch('api/vote_claim.php', {
          method: 'POST',
          credentials: 'same-origin'
        });
        const data = await res.json();

        resultText.textContent = data.message || 'OK';

        // danach Status neu laden
        const sRes = await fetch('api/vote_status.php', {
          method: 'GET',
          credentials: 'same-origin'
        });
        const sData = await sRes.json();
        if (sData.status === 'ok') applyStatus(sData);

      } catch (e) {
        resultText.textContent = 'Fehler beim Claim.';
      }
    });
  });
</script>