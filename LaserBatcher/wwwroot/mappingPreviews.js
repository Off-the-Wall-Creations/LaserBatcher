// Mapping thumbnails are browser-rendered SVGs; the original XML is never modified.
const sessions = new WeakMap();
const svgNs = 'http://www.w3.org/2000/svg';

function isolate(source, selected) {
    const root = source.cloneNode(false);
    // Images need a single self-contained SVG. Preserve ancestor styles and transforms.
    let branch = selected.cloneNode(true);
    for (let parent = selected.parentElement; parent && parent !== source; parent = parent.parentElement) {
        const shell = parent.cloneNode(false);
        shell.append(branch);
        branch = shell;
    }
    if (selected === source) return source.cloneNode(true);
    root.append(branch);
    const defs = document.createElementNS(svgNs, 'defs');
    const present = new Set([...root.querySelectorAll('[id]')].map(e => e.id));
    for (const definition of source.querySelectorAll('defs')) {
        if ([...definition.children].every(e => e.id && present.has(e.id))) continue;
        const cloned = definition.cloneNode(true);
        for (const child of [...cloned.children]) {
            if (child.id && present.has(child.id)) child.remove();
        }
        defs.append(cloned);
        for (const element of cloned.querySelectorAll('[id]')) present.add(element.id);
    }
    root.prepend(defs);
    const byId = new Map([...source.querySelectorAll('[id]')].map(e => [e.id, e]));
    // Include local clone/paint dependencies even when they were authored outside <defs>.
    const pending = [root];
    while (pending.length) {
        for (const node of pending.pop().querySelectorAll('*')) {
            for (const attribute of node.attributes) {
                const refs = attribute.localName === 'href' && attribute.value.startsWith('#')
                    ? [attribute.value.slice(1)]
                    : [...attribute.value.matchAll(/url\(\s*['"]?#([^)'"\s]+)['"]?\s*\)/gi)].map(m => m[1]);
                for (const id of refs) {
                    if (present.has(id) || !byId.has(id)) continue;
                    present.add(id);
                    const dependency = byId.get(id).cloneNode(true);
                    for (const element of dependency.querySelectorAll('[id]')) present.add(element.id);
                    defs.append(dependency);
                    pending.push(defs);
                }
            }
        }
    }
    return root;
}

function thumbnail(source, selected, stage) {
    const root = isolate(source, selected);
    // Safety in depth: rejected SVGs never reach this module, and previews never run scripts.
    for (const node of [...root.querySelectorAll('script, foreignObject, animate, animateTransform, animateMotion, set')]) node.remove();
    for (const node of [root, ...root.querySelectorAll('*')]) {
        for (const attribute of [...node.attributes]) {
            if (attribute.localName.startsWith('on')) node.removeAttributeNode(attribute);
        }
    }
    root.setAttribute('overflow', 'visible');
    root.style.overflow = 'visible';
    stage.append(root);
    try {
        const box = root.getBBox();
        if (![box.x, box.y, box.width, box.height].every(Number.isFinite) || (box.width === 0 && box.height === 0)) return null;
        const padding = Math.max(box.width, box.height) * .08 || 1;
        root.setAttribute('viewBox', [box.x - padding, box.y - padding, Math.max(box.width, padding) + padding * 2, Math.max(box.height, padding) + padding * 2].join(' '));
        root.setAttribute('width', '128');
        root.setAttribute('height', '80');
        root.setAttribute('preserveAspectRatio', 'xMidYMid meet');
        return 'data:image/svg+xml;charset=utf-8,' + encodeURIComponent(new XMLSerializer().serializeToString(root));
    } finally { root.remove(); }
}

export function initialize(container, xml) {
    dispose(container);
    const parsed = new DOMParser().parseFromString(xml, 'image/svg+xml');
    if (parsed.querySelector('parsererror')) return;
    const source = parsed.documentElement;
    const byKey = new Map();
    function index(element, key) {
        byKey.set(key, element);
        [...element.children].forEach((child, i) => index(child, key + '/' + i));
    }
    index(source, '0');
    const stage = document.createElement('div');
    stage.setAttribute('aria-hidden', 'true');
    Object.assign(stage.style, { position: 'fixed', left: '-100000px', top: '0', opacity: '0', pointerEvents: 'none' });
    document.body.append(stage);
    const cache = new Map();
    const observer = new IntersectionObserver(entries => {
        for (const entry of entries) {
            if (!entry.isIntersecting) continue;
            const image = entry.target;
            const key = image.dataset.sourceKey;
            observer.unobserve(image);
            try {
                if (!cache.has(key)) cache.set(key, thumbnail(source, byKey.get(key), stage));
                const url = cache.get(key);
                if (url) { image.src = url; image.dataset.previewReady = 'true'; }
                else { image.alt = 'No visible geometry'; image.dataset.previewReady = 'empty'; }
            } catch { image.alt = 'Preview unavailable'; image.dataset.previewReady = 'error'; }
        }
    }, { root: container, rootMargin: '120px' });
    for (const image of container.querySelectorAll('img[data-source-key]')) observer.observe(image);
    sessions.set(container, { observer, stage });
}

export function dispose(container) {
    const session = sessions.get(container);
    if (session) { session.observer.disconnect(); session.stage.remove(); sessions.delete(container); }
}
