const sessions = new WeakMap();

// The vendor uses document.writeln. Run the supplied snippet only in its own,
// opaque-origin document, never in Blazor's live document or beside uploaded SVGs.
const buttonDocument = `<!doctype html><html lang="en"><head><meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<style>
html, body { margin: 0; background: transparent; color-scheme: normal; }
body { height: 80px; display: flex; align-items: center; justify-content: center; }
body > div { flex: none; }
.bmc-btn:focus-visible { outline: 3px solid #9A4C0A; outline-offset: 3px; }
</style></head><body>
<script type="text/javascript" src="https://cdnjs.buymeacoffee.com/1.0.0/button.prod.min.js" data-name="bmc-button" data-slug="tbm0115" data-color="#FFDD00" data-emoji="" data-font="Cookie" data-text="Buy me a coffee" data-outline-color="#000000" data-font-color="#000000" data-coffee-color="#ffffff"></script>
<script>
const button = document.querySelector('.bmc-btn');
if (button) {
    button.rel = 'noopener noreferrer';
    button.setAttribute('aria-label', 'Buy me a coffee (opens in a new tab)');
    // Cookie may load later or be blocked. Keep either font readable and within
    // the available footer width, including narrow screens and browser zoom.
    const holder = button.parentElement;
    function fit() {
        holder.style.transform = '';
        const width = Math.max(holder.scrollWidth, holder.getBoundingClientRect().width);
        holder.style.transform = 'scale(' + Math.min(1, (innerWidth - 12) / width) + ')';
    }
    fit();
    addEventListener('resize', fit);
    document.fonts.ready.then(fit);
    parent.postMessage({ type: 'laserbatcher:support-ready' }, '*');
}
</script></body></html>`;

export function initialize(container) {
    if (sessions.has(container)) return;
    const frame = container.querySelector('iframe');
    const ready = event => {
        // Sandboxed documents have an opaque origin: identify the exact sender.
        if (event.source !== frame.contentWindow || event.data?.type !== 'laserbatcher:support-ready') return;
        container.dataset.supportReady = 'true';
        frame.removeAttribute('aria-hidden');
        frame.removeAttribute('tabindex');
    };
    window.addEventListener('message', ready);
    sessions.set(container, { frame, ready });
    frame.srcdoc = buttonDocument;
}

export function dispose(container) {
    const session = sessions.get(container);
    if (!session) return;
    window.removeEventListener('message', session.ready);
    session.frame.srcdoc = '';
    sessions.delete(container);
}
