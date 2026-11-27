// Page side of browser execution: owns the dedicated Web Worker, enforces the wall-clock timeout by
// terminating it, and replaces it with a fresh worker after every run so no state survives between runs.
const WORKER_URL = '/wasm-runner/worker.js';
let current = null; // { worker, ready: Promise<void> }

function spawn() {
    const worker = new Worker(WORKER_URL, { type: 'module', name: 'algosharp-runner' });
    const ready = new Promise((resolve, reject) => {
        const cleanup = () => { worker.removeEventListener('message', onMessage); worker.removeEventListener('error', onError); };
        const onMessage = e => {
            if (e.data?.type === 'ready' && e.data.locked === true) { cleanup(); resolve(); }
            else if (e.data?.type === 'ready') { cleanup(); reject(new Error('The runner is not isolated.')); }
            else if (e.data?.type === 'failed') { cleanup(); reject(new Error(e.data.error)); }
        };
        const onError = e => { cleanup(); reject(new Error(e.message || 'The browser runner failed to start.')); };
        worker.addEventListener('message', onMessage);
        worker.addEventListener('error', onError);
    });
    ready.catch(() => { });
    return { worker, ready };
}

function retire(entry) {
    try { entry?.worker.terminate(); } catch { }
    if (current === entry) current = null;
}

function withTimeout(promise, ms, onTimeout) {
    let timer;
    return Promise.race([
        promise.finally(() => clearTimeout(timer)),
        new Promise((_, reject) => { timer = setTimeout(() => { onTimeout(); reject(new Error('timeout')); }, ms); }),
    ]);
}

function result(obj) {
    // Blazor streams a returned typed array to .NET when the caller asks for an IJSStreamReference,
    // which avoids SignalR's per-message size limit for large traces.
    return new TextEncoder().encode(JSON.stringify(obj));
}

/** Warms up a runner so the first Run doesn't pay the whole download. Safe to call repeatedly. */
export function prepare() {
    if (typeof Worker === 'undefined' || typeof WebAssembly === 'undefined') return false;
    current ??= spawn();
    return true;
}

/**
 * Runs one RunRequest (JSON) in the worker. Resolves to a stream of
 * { status: 'ok' | 'timeout' | 'crashed' | 'unsupported' | 'startup', response?, error?, ms? }.
 */
export async function run(requestJson, runTimeoutMs, startupTimeoutMs) {
    if (!prepare()) return result({ status: 'unsupported', error: 'This browser does not support Web Workers and WebAssembly.' });
    const entry = current;
    try {
        await withTimeout(entry.ready, startupTimeoutMs, () => retire(entry));
    } catch (err) {
        retire(entry);
        return result({ status: 'startup', error: err.message === 'timeout' ? 'The browser runner took too long to start.' : err.message });
    }

    const id = crypto.randomUUID();
    const outcome = new Promise((resolve, reject) => {
        entry.worker.onmessage = e => { if (e.data?.type === 'result' && e.data.id === id) resolve(e.data); };
        entry.worker.onerror = e => { e.preventDefault?.(); reject(new Error(e.message || 'crashed')); };
    });
    entry.worker.postMessage({ type: 'run', id, request: requestJson });
    try {
        const data = await withTimeout(outcome, runTimeoutMs, () => retire(entry));
        if (data.error) return result({ status: 'crashed', error: data.error });
        return result({ status: 'ok', response: data.response, ms: data.ms });
    } catch (err) {
        return result(err.message === 'timeout' ? { status: 'timeout' } : { status: 'crashed', error: err.message });
    } finally {
        // One worker per run: discard this one and warm up the next.
        retire(entry);
        current = spawn();
    }
}

export function dispose() { retire(current); }
