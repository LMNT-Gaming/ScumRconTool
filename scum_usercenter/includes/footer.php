  </div>
</div>
<script>
(() => {
  const body = document.body;
  const burger = document.getElementById('navBurger');
  const sidebar = document.getElementById('appSidebar');
  const closeButton = document.getElementById('navClose');
  const backdrop = document.querySelector('[data-sidebar-close]');

  const setMenu = (open) => {
    body.classList.toggle('menu-open', open);
    burger?.setAttribute('aria-expanded', String(open));
    sidebar?.setAttribute('aria-hidden', open ? 'false' : 'true');
  };

  burger?.addEventListener('click', () => setMenu(!body.classList.contains('menu-open')));
  closeButton?.addEventListener('click', () => setMenu(false));
  backdrop?.addEventListener('click', () => setMenu(false));
  sidebar?.querySelectorAll('a').forEach((link) => link.addEventListener('click', () => setMenu(false)));
  document.querySelector('[data-sync-close]')?.addEventListener('click', () => document.getElementById('scumSyncModal')?.classList.remove('open'));
  window.addEventListener('keydown', (event) => { if (event.key === 'Escape') setMenu(false); });
  window.addEventListener('resize', () => { if (window.innerWidth > 1120) setMenu(false); });
})();
</script>
</body>
</html>
