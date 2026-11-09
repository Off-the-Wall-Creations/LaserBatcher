// Loaded before stylesheets so the initial paint uses the selected appearance.
(() => {
    const key = 'laserbatcher.theme';
    const choices = new Set(['system', 'light', 'dark']);
    const media = window.matchMedia('(prefers-color-scheme: dark)');
    let preference = 'system';
    try {
        const saved = localStorage.getItem(key);
        if (choices.has(saved)) preference = saved;
    } catch { /* Storage is optional; keep an in-memory preference. */ }

    function apply() {
        const theme = preference === 'system' ? (media.matches ? 'dark' : 'light') : preference;
        document.documentElement.dataset.bsTheme = theme;
        document.documentElement.style.colorScheme = theme;
        document.querySelector('meta[name="theme-color"]')?.setAttribute('content', theme === 'dark' ? '#1C1917' : '#F7F3EC');
    }
    window.laserBatcherTheme = {
        get: () => preference,
        set(value) {
            if (!choices.has(value)) return;
            preference = value;
            try { localStorage.setItem(key, value); } catch { }
            apply();
        }
    };
    media.addEventListener('change', () => { if (preference === 'system') apply(); });
    apply();
})();
