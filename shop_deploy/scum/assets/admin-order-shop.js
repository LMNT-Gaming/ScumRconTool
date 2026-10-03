(() => {
  const modal = document.getElementById('shopItemCatalog');
  if (!modal) return;
  // Escape the Admincenter cards' overflow clipping so the dialog truly uses the viewport.
  document.body.appendChild(modal);
  const grid = document.getElementById('catalogGrid');
  const search = document.getElementById('catalogSearch');
  const category = document.getElementById('catalogCategory');
  const count = document.getElementById('catalogCount');
  const apply = document.getElementById('catalogApply');
  let catalog = [];
  let target = null;
  let selected = new Map();

  const parseLines = value => {
    const result = new Map();
    String(value || '').split(/\r?\n/).forEach(line => {
      const [code, rawQuantity] = line.split('|').map(part => part.trim());
      if (code) result.set(code, Math.max(1, Math.min(100, Number.parseInt(rawQuantity || '1', 10) || 1)));
    });
    return result;
  };

  const close = () => { modal.hidden = true; document.body.classList.remove('catalog-open'); };
  const matches = item => {
    const needle = search.value.trim().toLocaleLowerCase('de');
    return (!category.value || item.category === category.value) &&
      (!needle || item.code.toLocaleLowerCase('de').includes(needle) || item.name.toLocaleLowerCase('de').includes(needle));
  };

  const render = () => {
    const filtered = catalog.filter(matches);
    count.textContent = `${filtered.length.toLocaleString('de-DE')} Treffer · ${selected.size} ausgewählt`;
    grid.replaceChildren();
    filtered.slice(0, 120).forEach(item => {
      const card = document.createElement('article');
      card.className = 'catalog-item' + (selected.has(item.code) ? ' selected' : '');
      const image = document.createElement('img'); image.src = item.imageUrl; image.alt = ''; image.loading = 'lazy';
      image.addEventListener('error', () => { image.hidden = true; card.classList.add('no-image'); });
      const copy = document.createElement('div');
      const title = document.createElement('strong'); title.textContent = item.name;
      const code = document.createElement('code'); code.textContent = item.code;
      const meta = document.createElement('small'); meta.textContent = item.category + (item.tier === null ? '' : ` · Tier ${item.tier}`);
      copy.append(title, code, meta);
      const controls = document.createElement('div'); controls.className = 'catalog-item-controls';
      const toggle = document.createElement('button'); toggle.type = 'button'; toggle.textContent = selected.has(item.code) ? 'Entfernen' : 'Hinzufügen';
      toggle.addEventListener('click', () => { selected.has(item.code) ? selected.delete(item.code) : selected.set(item.code, 1); render(); });
      controls.append(toggle);
      if (selected.has(item.code)) {
        const quantity = document.createElement('input'); quantity.type = 'number'; quantity.min = '1'; quantity.max = '100'; quantity.value = String(selected.get(item.code)); quantity.setAttribute('aria-label', `Menge ${item.name}`);
        quantity.addEventListener('change', () => selected.set(item.code, Math.max(1, Math.min(100, Number.parseInt(quantity.value, 10) || 1))));
        controls.prepend(quantity);
      }
      card.append(image, copy, controls); grid.append(card);
    });
    if (!filtered.length) { const empty = document.createElement('p'); empty.className = 'catalog-empty'; empty.textContent = 'Keine passenden spawnfähigen Items gefunden.'; grid.append(empty); }
  };

  const loadCatalog = async () => {
    if (catalog.length) return;
    const response = await fetch('assets/item-catalog.json', { cache: 'no-cache', credentials: 'same-origin' });
    if (!response.ok) throw new Error(`HTTP ${response.status}`);
    catalog = await response.json();
    [...new Set(catalog.map(item => item.category))].sort((a,b) => a.localeCompare(b, 'de')).forEach(name => {
      const option = document.createElement('option'); option.value = name; option.textContent = `${name} (${catalog.filter(item => item.category === name).length})`; category.append(option);
    });
  };

  document.querySelectorAll('.open-item-catalog').forEach(button => button.addEventListener('click', async () => {
    target = button.closest('form').querySelector('[name="payload_lines"]');
    selected = parseLines(target.value); modal.hidden = false; document.body.classList.add('catalog-open'); search.focus();
    try { await loadCatalog(); render(); } catch (error) { count.textContent = 'Katalog konnte nicht geladen werden.'; grid.textContent = ''; }
  }));
  document.querySelectorAll('.catalog-close').forEach(button => button.addEventListener('click', close));
  modal.addEventListener('click', event => { if (event.target === modal) close(); });
  document.addEventListener('keydown', event => { if (event.key === 'Escape' && !modal.hidden) close(); });
  search.addEventListener('input', render); category.addEventListener('change', render);
  apply.addEventListener('click', () => { if (target) target.value = [...selected].map(([code, quantity]) => `${code}|${quantity}`).join('\n'); close(); });

  document.querySelectorAll('.shop-admin-form').forEach(form => {
    const url = form.querySelector('[name="image_url"]'); const preview = form.querySelector('.image-check img'); const status = form.querySelector('.image-check small');
    const check = () => {
      const value = url.value.trim(); preview.hidden = true;
      if (!value) { status.textContent = 'Keine Bildadresse geprüft'; status.className = ''; return; }
      status.textContent = 'Bild wird geprüft …'; status.className = '';
      preview.onload = () => { preview.hidden = false; status.textContent = 'Bild gefunden'; status.className = 'found'; };
      preview.onerror = () => { preview.hidden = true; status.textContent = 'Unter dieser Adresse wurde kein Bild gefunden'; status.className = 'missing'; };
      preview.src = value + (value.includes('?') ? '&' : '?') + 'check=' + Date.now();
    };
    form.querySelector('.image-from-first').addEventListener('click', () => {
      const first = parseLines(form.querySelector('[name="payload_lines"]').value).keys().next().value;
      if (!first) { status.textContent = 'Bitte zuerst mindestens ein Item auswählen'; status.className = 'missing'; return; }
      url.value = `https://lmnt-gaming.net/images/items/${encodeURIComponent(first)}.png`; check();
    });
    url.addEventListener('change', check);
    if (url.value.trim()) check();
  });
})();
