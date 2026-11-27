// AlgoSharp browser runner: a dedicated module Web Worker hosting the .NET WebAssembly runtime.
// User code runs here, on the visitor's own device, never on the server.
//
// Isolation layers (defence in depth):
//  1. The server's Roslyn analyzer rejects System.Runtime (JS interop), Reflection, IO, Net, Threading,
//     Type/Activator/Environment, unsafe code and P/Invoke before anything is compiled.
//  2. This worker has no DOM, no window, no page globals and no access to the Blazor circuit.
//  3. The server sends this script with a CSP that only allows fetching /wasm-runner/ files.
//  4. Once the runtime has loaded, every network and storage API is removed from the worker's global scope.
//  5. The page terminates the worker on timeout and starts a fresh one after every run (no state carries over).
import { dotnet } from './_framework/dotnet.js';

const MAX_HEAP_BYTES = 256 * 1024 * 1024;

function lockdown() {
    const names = [
        'fetch', 'XMLHttpRequest', 'WebSocket', 'WebSocketStream', 'WebTransport', 'EventSource',
        'indexedDB', 'caches', 'cookieStore', 'importScripts', 'BroadcastChannel', 'Worker', 'SharedWorker',
        'Request', 'Response', 'Headers', 'navigator', 'localStorage', 'sessionStorage',
    ];
    for (const name of names) {
        try { Object.defineProperty(self, name, { value: undefined, writable: false, configurable: false }); } catch { }
    }
    // Reported to the page, which refuses to run code in a worker that isn't locked down.
    return names.every(name => self[name] === undefined);
}

const exportsPromise = (async () => {
    const runtime = await dotnet
        // Mono's GC heap ceiling: allocations past it fail with OutOfMemoryException inside .NET.
        .withEnvironmentVariable('MONO_GC_PARAMS', `max-heap-size=${MAX_HEAP_BYTES}`)
        .create();
    const exports = await runtime.getAssemblyExports('AlgoSharp.Wasm.dll');
    if (!lockdown()) throw new Error('The runner could not remove network and storage access.');
    return exports;
})();

exportsPromise.then(
    () => postMessage({ type: 'ready', locked: true }),
    err => postMessage({ type: 'failed', error: String((err && err.message) || err) }));

self.onmessage = async e => {
    const msg = e.data;
    if (!msg || msg.type !== 'run' || typeof msg.request !== 'string') return;
    try {
        const exports = await exportsPromise;
        const started = performance.now();
        const response = exports.AlgoSharp.Wasm.BrowserRunner.Run(msg.request);
        postMessage({ type: 'result', id: msg.id, response, ms: Math.round(performance.now() - started) });
    } catch (err) {
        postMessage({ type: 'result', id: msg.id, error: String((err && err.message) || err) });
    }
};
