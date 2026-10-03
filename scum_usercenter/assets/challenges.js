(() => {
  const root = document.querySelector('[data-challenge-root]');
  const status = document.querySelector('[data-challenge-status]');
  if (!root || !status) return;

  const apiUrl = 'api/challenge_data.php';
  const number = new Intl.NumberFormat('de-DE', { maximumFractionDigits: 1 });
  let etag = '';
  let timer = 0;
  let snapshot = null;

  const node = (tag, cls, text) => {
    const item = document.createElement(tag);
    if (cls) item.className = cls;
    if (text !== undefined && text !== null) item.textContent = String(text);
    return item;
  };
  const clamp = value => Math.max(0, Math.min(100, Number(value) || 0));
  const formatValue = value => number.format(Number(value) || 0);

  function remaining(endUtc) {
    if (!endUtc) return 'Ohne Enddatum';
    const ms = new Date(endUtc).getTime() - Date.now();
    if (!Number.isFinite(ms)) return 'Enddatum unbekannt';
    if (ms <= 0) return 'Beendet';
    const hours = Math.ceil(ms / 3600000);
    if (hours < 48) return `Läuft noch ${hours} Std.`;
    return `Läuft noch ${Math.ceil(hours / 24)} Tage`;
  }

  function progressBar(percent, extraClass = '') {
    const wrap = node('div', `challenge-progress ${extraClass}`.trim());
    const fill = node('i');
    fill.style.width = `${clamp(percent)}%`;
    wrap.append(fill);
    return wrap;
  }

  function renderGoal(goal, personal) {
    const article = node('article', 'challenge-goal');
    const head = node('div', 'challenge-goal-head');
    head.append(node('strong', '', goal.name || goal.key || 'Ziel'));
    const current = personal ? goal.mine?.progress : goal.progress;
    head.append(node('span', '', `${formatValue(current)} / ${formatValue(goal.target)}`));
    article.append(head, progressBar(personal ? goal.mine?.percent : goal.percent));

    if (!personal) {
      const mine = node('div', 'challenge-contribution');
      mine.append(node('span', '', 'Dein Beitrag'));
      mine.append(node('strong', '', `${formatValue(goal.mine?.progress)} / ${formatValue(goal.mine?.target || goal.target)} (${clamp(goal.mine?.percent).toFixed(1)} %)`));
      article.append(mine, progressBar(goal.mine?.percent, 'is-personal'));
    }
    return article;
  }

  function renderChallenge(challenge) {
    const personal = challenge.goalScope === 'PerPlayer';
    const card = node('article', `challenge-card ${challenge.isCompleted ? 'is-complete' : ''}`);
    const header = node('header', 'challenge-card-head');
    const title = node('div');
    const badges = node('div', 'challenge-badges');
    badges.append(node('span', personal ? 'is-personal' : 'is-community', personal ? 'PERSÖNLICH' : 'COMMUNITY'));
    badges.append(node('span', '', challenge.goalLogic === 'All' ? 'ALLE ZIELE' : 'EIN ZIEL'));
    title.append(badges, node('h3', '', challenge.title || 'Challenge'));
    if (challenge.description) title.append(node('p', '', challenge.description));
    const runtime = node('div', 'challenge-runtime');
    runtime.append(node('strong', '', remaining(challenge.endUtc)), node('span', '', challenge.isCompleted ? 'Abgeschlossen' : `${clamp(challenge.percent).toFixed(1)} %`));
    header.append(title, runtime);
    card.append(header, progressBar(challenge.percent, 'is-overall'));

    const meta = node('div', 'challenge-meta');
    if (challenge.reward) meta.append(node('span', 'challenge-reward', `★ ${challenge.reward}`));
    const rule = challenge.goalLogic === 'All' ? 'Alle Ziele müssen erfüllt werden' : 'Ein Ziel genügt';
    meta.append(node('span', '', rule));
    card.append(meta);

    const goals = node('div', 'challenge-goals');
    (challenge.goals || []).forEach(goal => goals.append(renderGoal(goal, personal)));
    card.append(goals);
    return card;
  }

  function render(data) {
    snapshot = data;
    root.replaceChildren();
    const challenges = Array.isArray(data.challenges) ? data.challenges : [];
    if (!challenges.length) {
      const empty = node('article', 'challenge-empty');
      empty.append(node('strong', '', 'Noch keine Challenges verfügbar.'), node('span', '', 'Sobald das Tool einen Scan überträgt, erscheinen sie hier automatisch.'));
      root.append(empty);
    } else challenges.forEach(item => root.append(renderChallenge(item)));
    root.setAttribute('aria-busy', 'false');
    const updated = data.receivedAtUtc ? new Date(data.receivedAtUtc).toLocaleString('de-DE', { dateStyle: 'short', timeStyle: 'short' }) : 'noch nie';
    status.className = 'challenge-sync is-ok';
    status.querySelector('span').textContent = `Aktuell · ${updated}`;
  }

  function renderError(message) {
    status.className = 'challenge-sync is-error';
    status.querySelector('span').textContent = message;
    if (!snapshot) {
      const empty = node('article', 'challenge-empty is-error');
      empty.append(node('strong', '', 'Challenge-Daten konnten nicht geladen werden.'), node('span', '', 'Die Anzeige versucht es automatisch erneut.'));
      root.replaceChildren(empty);
      root.setAttribute('aria-busy', 'false');
    }
  }

  function schedule(delay = 30000) {
    clearTimeout(timer);
    if (!document.hidden) timer = window.setTimeout(refresh, delay);
  }

  async function refresh() {
    if (document.hidden || !navigator.onLine) { schedule(60000); return; }
    try {
      const headers = etag ? { 'If-None-Match': etag } : {};
      const response = await fetch(apiUrl, { credentials: 'same-origin', cache: 'no-cache', headers });
      if (response.status === 304) { status.querySelector('span').textContent = 'Aktuell · keine Änderungen'; schedule(); return; }
      if (response.status === 401) { location.href = 'auth/login.php'; return; }
      if (!response.ok) throw new Error(`HTTP ${response.status}`);
      etag = response.headers.get('ETag') || etag;
      const data = await response.json();
      if (!data.ok) throw new Error(data.error || 'Unbekannter Fehler');
      render(data);
    } catch (error) {
      renderError(`Verbindung unterbrochen · ${error.message}`);
    }
    schedule();
  }

  document.addEventListener('visibilitychange', () => {
    clearTimeout(timer);
    if (!document.hidden) refresh();
  });
  window.addEventListener('online', refresh);
  window.setInterval(() => {
    if (snapshot && !document.hidden) root.querySelectorAll('.challenge-card').forEach((card, i) => {
      const label = card.querySelector('.challenge-runtime strong');
      if (label) label.textContent = remaining(snapshot.challenges?.[i]?.endUtc);
    });
  }, 60000);
  refresh();
})();