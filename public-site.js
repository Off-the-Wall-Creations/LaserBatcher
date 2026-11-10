// Progressive enhancement only: document content and links work without JS.
(() => {
    const select = document.getElementById('public-appearance');
    if (!select || !window.laserBatcherTheme) return;
    select.value = window.laserBatcherTheme.get();
    select.addEventListener('change', () => window.laserBatcherTheme.set(select.value));
    select.closest('.public-theme').hidden = false;
})();
