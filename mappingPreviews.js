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

function thumbnail(source, selected, stage, mode, width = 64, height = 48) {
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
        if (![box.x, box.y, box.width, box.height].every(Number.isFinite)) return null;
        let stroke = 0;
        const inverse = root.getScreenCTM()?.inverse();
        const graphics = [...root.querySelectorAll('path,rect,circle,ellipse,line,polyline,polygon,text,tspan,use')]
            .filter(node => !node.closest('clipPath,mask,pattern,marker'));
        const styles = graphics.map(node => ({ node, style: getComputedStyle(node), matrix: inverse && node.getScreenCTM() ? inverse.multiply(node.getScreenCTM()) : null }));
        // Chromium's getBBox() omits strokes. Pad in root units using the largest
        // transformed stroke, including definition targets used by clones.
        for (const { style, matrix } of styles) {
            if (style.stroke === 'none') continue;
            const scale = matrix ? Math.max(Math.hypot(matrix.a, matrix.b), Math.hypot(matrix.c, matrix.d)) : 1;
            const join = style.strokeLinejoin === 'miter' ? Math.max(1, parseFloat(style.strokeMiterlimit) || 4) : 1;
            stroke = Math.max(stroke, (parseFloat(style.strokeWidth) || 0) * scale * join / 2);
        }
        if (mode === 'enhanced') {
            // These styles exist only on the isolated preview. Preserve clipping,
            // transforms, raster pixels and hidden-content visibility semantics.
            const hasRaster = !!root.querySelector('image');
            for (const group of root.querySelectorAll('g')) if (!hasRaster && !group.closest('clipPath,mask,pattern,marker')) group.style.opacity = '1';
            for (const { node, style } of styles) {
                if (hasRaster && node.localName === 'use') continue;
                const filled = style.fill !== 'none';
                node.style.setProperty('fill', filled ? '#6d9ec7' : 'none');
                node.style.setProperty('stroke', '#173d59');
                node.style.setProperty('stroke-width', '2px');
                node.style.setProperty('vector-effect', 'non-scaling-stroke');
                node.style.setProperty('opacity', '1'); node.style.setProperty('fill-opacity', '1'); node.style.setProperty('stroke-opacity', '1');
            }
            // A few screen pixels of padding remain even for zero-width lines.
            stroke = Math.max(box.width, box.height, 1) * 4 / Math.min(width, height);
        }
        const padding = Math.max(Math.max(box.width, box.height) * .08, stroke, .01);
        root.setAttribute('viewBox', [box.x - padding, box.y - padding, box.width + padding * 2, box.height + padding * 2].join(' '));
        root.setAttribute('width', String(width));
        root.setAttribute('height', String(height));
        root.setAttribute('preserveAspectRatio', 'xMidYMid meet');
        return 'data:image/svg+xml;charset=utf-8,' + encodeURIComponent(new XMLSerializer().serializeToString(root));
    } finally { root.remove(); }
}

export function initialize(container, xml, mode = 'enhanced') {
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
    const session = { stage, cache, source, byKey, mode };
    const observer = new IntersectionObserver(entries => {
        for (const entry of entries) {
            if (!entry.isIntersecting) continue;
            const image = entry.target;
            const key = image.dataset.sourceKey;
            observer.unobserve(image);
            try {
                const url = preview(session, key);
                if (url) { image.src = url; image.dataset.previewReady = 'true'; }
                else { image.alt = 'No visible geometry'; image.dataset.previewReady = 'empty'; }
            } catch { image.alt = 'Preview unavailable'; image.dataset.previewReady = 'error'; }
        }
    }, { root: container, rootMargin: '120px' });
    for (const image of container.querySelectorAll('img[data-source-key]')) observer.observe(image);
    // The observer closure and public mode changes must share the same state.
    sessions.set(container, Object.assign(session, { observer }));
}

function preview(session, key, width = 64, height = 48) {
    const cacheKey = `${key}/${session.mode}/${width}/${height}`;
    if (!session.byKey.has(key)) return null;
    if (!session.cache.has(cacheKey)) session.cache.set(cacheKey, thumbnail(session.source, session.byKey.get(key), session.stage, session.mode, width, height));
    return session.cache.get(cacheKey);
}
export function setMode(container, mode) {
    const session = sessions.get(container); if (!session) return;
    session.mode = mode;
    for (const image of container.querySelectorAll('img[data-source-key]')) { image.removeAttribute('src'); delete image.dataset.previewReady; session.observer.observe(image); }
}
export function inspect(container, key, image) {
    const session = sessions.get(container); if (!session) return;
    try { const url = preview(session, key, 320, 125); if (url) image.src = url; else image.removeAttribute('src'); }
    catch { image.removeAttribute('src'); }
}

export function dispose(container) {
    const session = sessions.get(container);
    if (session) { session.observer.disconnect(); session.stage.remove(); sessions.delete(container); }
}
