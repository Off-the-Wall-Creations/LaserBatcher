// Keep static HTML useful to non-JavaScript readers. Only real browser failures
// populate the error UI; do not interpret a loading shell as a failed app.
(() => {
    const base = new URL('./', document.baseURI);
    const errorBox = document.getElementById('blazor-error-ui');
    const updateNotice = document.getElementById('app-update-notice');
    let starting = true;
    const button = (label, action) => {
        const control = document.createElement('button');
        control.type = 'button'; control.className = 'btn btn-sm btn-outline-secondary';
        control.textContent = label; control.addEventListener('click', action);
        return control;
    };
    function showError(message) {
        errorBox.replaceChildren();
        const detail = document.createElement('p'); detail.textContent = message;
        const help = document.createElement('p');
        help.textContent = 'Check your connection and try reloading. If this persists, close every LaserBatcher tab and installed app window, then reopen the site. Download any available work before reloading.';
        errorBox.append(detail, help, button('Reload', () => location.reload()), button('Dismiss error', () => { errorBox.style.display = 'none'; }));
        errorBox.style.display = 'block';
    }
    // Blazor 7 signals runtime failures by setting display:block on this element.
    // Own the controls so they also work when framework startup never completed.
    new MutationObserver(() => {
        if (errorBox.style.display !== 'none' && !errorBox.childElementCount)
            showError(starting ? 'LaserBatcher could not start.' : 'LaserBatcher encountered an error.');
    }).observe(errorBox, { attributes: true, attributeFilter: ['style'] });

    function updateReady(registration) {
        if (!registration.waiting || !navigator.serviceWorker.controller) return;
        updateNotice.replaceChildren();
        const message = document.createElement('span');
        message.textContent = 'A LaserBatcher update is ready. Download your work, then close all LaserBatcher tabs and app windows and reopen the site.';
        updateNotice.append(message, button('Dismiss update notice', () => { updateNotice.hidden = true; }));
        updateNotice.hidden = false;
    }
    if ('serviceWorker' in navigator) {
        navigator.serviceWorker.register(new URL('service-worker.js', base).href, { updateViaCache: 'none' })
            .then(registration => {
                updateReady(registration);
                registration.addEventListener?.('updatefound', () => {
                    const installing = registration.installing;
                    installing?.addEventListener('statechange', () => {
                        if (installing.state === 'installed') updateReady(registration);
                    });
                });
            })
            .catch(error => console.warn('LaserBatcher offline support is unavailable.', error));
    }
    (async () => {
        try {
            if (typeof WebAssembly !== 'object') throw new Error('This browser does not support WebAssembly.');
            if (typeof window.Blazor?.start !== 'function') throw new Error('The application startup script could not be downloaded.');
            await Blazor.start();
            starting = false;
        } catch (error) {
            console.error('LaserBatcher startup failed.', error);
            const status = document.getElementById('startup-status');
            if (status) status.textContent = 'The interactive workspace could not open.';
            for (const indicator of document.querySelectorAll('.loading-progress, .loading-progress-text')) indicator.hidden = true;
            showError(typeof WebAssembly !== 'object'
                ? 'LaserBatcher needs a browser with JavaScript and WebAssembly enabled.'
                : 'LaserBatcher could not start. Some application files may be unavailable or blocked.');
        }
    })();
})();
