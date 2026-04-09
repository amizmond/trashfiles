let dotNetRef = null;
let draggingEl = null;
let dragOverEl = null;
let beforeUnloadHandler = null;

export function init(ref) {
    dotNetRef = ref;
    document.addEventListener('dragstart', onDragStart, true);
    document.addEventListener('dragover', onDragOver, true);
    document.addEventListener('dragleave', onDragLeave, true);
    document.addEventListener('drop', onDrop, true);
    document.addEventListener('dragend', onDragEnd, true);
}

export function dispose() {
    document.removeEventListener('dragstart', onDragStart, true);
    document.removeEventListener('dragover', onDragOver, true);
    document.removeEventListener('dragleave', onDragLeave, true);
    document.removeEventListener('drop', onDrop, true);
    document.removeEventListener('dragend', onDragEnd, true);
    setBeforeUnload(false);
    dotNetRef = null;
}

export function setBeforeUnload(enabled) {
    if (enabled && !beforeUnloadHandler) {
        beforeUnloadHandler = (e) => { e.preventDefault(); e.returnValue = ''; };
        window.addEventListener('beforeunload', beforeUnloadHandler);
    } else if (!enabled && beforeUnloadHandler) {
        window.removeEventListener('beforeunload', beforeUnloadHandler);
        beforeUnloadHandler = null;
    }
}

function getCard(el) {
    return el.closest('[data-feature-id]');
}

function getStack(el) {
    return el.closest('[data-pi-id]');
}

function onDragStart(e) {
    const card = getCard(e.target);
    if (!card) return;

    draggingEl = card;
    card.classList.add('dragging');

    const featureId = parseInt(card.dataset.featureId, 10);
    const stack = getStack(card);
    const piId = stack ? parseInt(stack.dataset.piId, 10) : 0;

    dotNetRef.invokeMethodAsync('OnJsDragStart', featureId, piId);
}

function onDragOver(e) {
    const stack = getStack(e.target);
    if (!stack) return;

    e.preventDefault();

    const card = getCard(e.target);
    if (!card || card === draggingEl) {
        if (dragOverEl) {
            dragOverEl.classList.remove('drag-over');
            dragOverEl = null;
        }
        return;
    }

    if (dragOverEl === card) return;

    if (dragOverEl) dragOverEl.classList.remove('drag-over');
    dragOverEl = card;
    card.classList.add('drag-over');
}

function onDragLeave(e) {
    const card = getCard(e.target);
    if (!card) return;

    if (card.contains(e.relatedTarget)) return;

    if (dragOverEl === card) {
        card.classList.remove('drag-over');
        dragOverEl = null;
    }
}

function onDrop(e) {
    e.preventDefault();
    e.stopPropagation();

    cleanup();

    const stack = getStack(e.target);
    if (!stack) return;

    const targetPiId = parseInt(stack.dataset.piId, 10);
    const card = getCard(e.target);
    const targetFeatureId = card ? parseInt(card.dataset.featureId, 10) : -1;

    dotNetRef.invokeMethodAsync('OnJsDrop', targetFeatureId, targetPiId);
}

function onDragEnd() {
    cleanup();
    dotNetRef.invokeMethodAsync('OnJsDragEnd');
}

function cleanup() {
    if (draggingEl) {
        draggingEl.classList.remove('dragging');
        draggingEl = null;
    }
    if (dragOverEl) {
        dragOverEl.classList.remove('drag-over');
        dragOverEl = null;
    }
}
