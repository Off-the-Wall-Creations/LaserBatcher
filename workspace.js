const shells = new WeakMap();
export function openDialog(dialog) { if (!dialog.open) dialog.showModal(); }
export function closeDialog(dialog) { if (dialog.open) dialog.close(); }
export function syncPanel(dialog, compact, enabled) {
    if (dialog.dataset.compact !== String(compact) || !enabled) closeDialog(dialog);
    dialog.dataset.compact = String(compact);
    if (enabled && !compact && !dialog.open) {
        const focused = document.activeElement;
        dialog.show();
        focused?.focus({ preventScroll: true });
    }
}
export function initializeShell(area, splitter, dotnet) {
    const media = matchMedia('(max-width: 1199px), (max-height: 639px)');
    const change = () => dotnet.invokeMethodAsync('SetCompact', media.matches);
    media.addEventListener('change', change);
    let width = 360, pointer;
    const set = value => {
        width = Math.max(280, Math.min(480, value));
        area.style.setProperty('--mapping-width', width + 'px');
        splitter.setAttribute('aria-valuenow', String(width));
    };
    const down = event => { if (event.button !== 0) return; pointer = { id: event.pointerId, x: event.clientX, width }; splitter.setPointerCapture(event.pointerId); event.preventDefault(); };
    const move = event => { if (pointer?.id === event.pointerId) set(pointer.width + event.clientX - pointer.x); };
    const up = () => { pointer = null; };
    const key = event => {
        const values = { ArrowLeft: width - 16, ArrowRight: width + 16, Home: 280, End: 480 };
        if (event.key in values) { event.preventDefault(); set(values[event.key]); }
    };
    splitter.addEventListener('pointerdown', down); splitter.addEventListener('pointermove', move);
    splitter.addEventListener('pointerup', up); splitter.addEventListener('pointercancel', up); splitter.addEventListener('keydown', key);
    shells.set(area, () => {
        media.removeEventListener('change', change);
        splitter.removeEventListener('pointerdown', down); splitter.removeEventListener('pointermove', move);
        splitter.removeEventListener('pointerup', up); splitter.removeEventListener('pointercancel', up); splitter.removeEventListener('keydown', key);
    });
    return media.matches;
}
export function disposeShell(area) { shells.get(area)?.(); shells.delete(area); }
