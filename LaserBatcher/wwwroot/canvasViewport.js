const views = new WeakMap();
export function initialize(element, dotnet) {
    const paper = element.querySelector('.canvas-paper');
    const state = { width: 0, height: 0, scale: 1, x: 0, y: 0, fitted: true };
    let drag;
    const paint = () => {
        paper.style.transform = `translate(${state.x}px, ${state.y}px) scale(${state.scale})`;
        element.dataset.zoom = String(state.scale);
        dotnet.invokeMethodAsync('ZoomChanged', Math.round(state.scale * 1000) / 10);
    };
    const fit = () => {
        if (!state.width || !state.height) return;
        state.fitted = true;
        state.scale = Math.max(.01, Math.min(16, (element.clientWidth - 48) / state.width, (element.clientHeight - 48) / state.height));
        state.x = (element.clientWidth - state.width * state.scale) / 2;
        state.y = (element.clientHeight - state.height * state.scale) / 2; paint();
    };
    const zoom = scale => {
        state.fitted = false;
        scale = Math.max(.01, Math.min(16, scale));
        const ratio = scale / state.scale, cx = element.clientWidth / 2, cy = element.clientHeight / 2;
        state.x = cx - (cx - state.x) * ratio; state.y = cy - (cy - state.y) * ratio; state.scale = scale; paint();
    };
    const down = event => {
        if (event.button !== 0 || !state.width) return;
        element.focus({ preventScroll: true }); element.setPointerCapture(event.pointerId);
        drag = { id: event.pointerId, x: event.clientX, y: event.clientY, left: state.x, top: state.y }; element.classList.add('panning');
    };
    const move = event => {
        if (drag?.id !== event.pointerId) return;
        state.fitted = false; state.x = drag.left + event.clientX - drag.x; state.y = drag.top + event.clientY - drag.y; paint();
    };
    const up = () => { drag = null; element.classList.remove('panning'); };
    const key = event => {
        if (event.ctrlKey || event.metaKey || event.target !== element) return;
        const delta = event.shiftKey ? 100 : 32;
        if (event.key === 'Home') fit();
        else if (['+', '='].includes(event.key)) zoom(state.scale * 1.25);
        else if (event.key === '-') zoom(state.scale / 1.25);
        else if (event.key.startsWith('Arrow')) {
            state.fitted = false;
            if (event.key === 'ArrowLeft') state.x += delta;
            if (event.key === 'ArrowRight') state.x -= delta;
            if (event.key === 'ArrowUp') state.y += delta;
            if (event.key === 'ArrowDown') state.y -= delta; paint();
        } else return;
        event.preventDefault();
    };
    const wheel = event => {
        if (event.ctrlKey || event.metaKey) return; // Browser zoom remains available.
        event.preventDefault();
        if (event.altKey) zoom(state.scale * (event.deltaY < 0 ? 1.15 : 1 / 1.15));
        else { state.fitted = false; state.x -= event.shiftKey ? event.deltaY : event.deltaX; state.y -= event.shiftKey ? 0 : event.deltaY; paint(); }
    };
    const resize = new ResizeObserver(() => { if (state.fitted) fit(); }); resize.observe(element);
    for (const [name, handler] of Object.entries({ pointerdown: down, pointermove: move, pointerup: up, pointercancel: up, keydown: key })) element.addEventListener(name, handler);
    element.addEventListener('wheel', wheel, { passive: false });
    views.set(element, { state, fit, zoom, dispose() {
        resize.disconnect();
        for (const [name, handler] of Object.entries({ pointerdown: down, pointermove: move, pointerup: up, pointercancel: up, keydown: key, wheel })) element.removeEventListener(name, handler);
    } });
}
export function setImage(element, width, height) { const view = views.get(element); Object.assign(view.state, { width, height }); view.fit(); }
export function command(element, value) {
    const view = views.get(element);
    if (value === 'fit') view.fit(); else view.zoom(value === 'actual' ? 1 : view.state.scale * (value === 'in' ? 1.25 : .8));
}
export function dispose(element) { views.get(element)?.dispose(); views.delete(element); }
