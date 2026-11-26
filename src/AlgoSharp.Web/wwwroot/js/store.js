// AlgoSharp personal library: IndexedDB storage for imported problems, drafts and progress.
// Data lives in this browser profile only. Values are JSON strings produced by the C# side.
//
// Each owner has its own database: "algosharp" is the guest library (what existed before accounts),
// and "algosharp-u-<account id>" belongs to one signed-in account. Every call names its database,
// so the app never reads one account's library while another account is signed in.

const GUEST_DB = "algosharp";
const DB_VERSION = 1;
const openings = new Map();

/** Schema migrations, applied in order from the stored version up to DB_VERSION. */
const migrations = {
    1: (db) => {
        const problems = db.createObjectStore("problems", { keyPath: "id" });
        problems.createIndex("collection", "collection", { unique: false });
        problems.createIndex("updatedAt", "updatedAt", { unique: false });
        db.createObjectStore("progress", { keyPath: "problemId" });
        db.createObjectStore("prefs", { keyPath: "key" });
    },
    // 2: (db, tx) => { ... future migrations go here ... }
};

function checkName(name) {
    if (name !== GUEST_DB && !/^algosharp-u-[A-Za-z0-9-]{1,64}$/.test(name)) throw new Error("Invalid library name.");
    return name;
}

function open(name) {
    checkName(name);
    let opening = openings.get(name);
    if (opening) return opening;
    opening = new Promise((resolve, reject) => {
        if (!("indexedDB" in window)) {
            reject(new Error("This browser doesn't support IndexedDB, so your problems can't be saved locally."));
            return;
        }
        const request = indexedDB.open(name, DB_VERSION);
        request.onupgradeneeded = (event) => {
            const db = request.result;
            for (let v = event.oldVersion + 1; v <= DB_VERSION; v++) migrations[v]?.(db, request.transaction);
        };
        request.onsuccess = () => {
            const db = request.result;
            db.onversionchange = () => { db.close(); openings.delete(name); };
            resolve(db);
        };
        request.onerror = () => { openings.delete(name); reject(request.error); };
        request.onblocked = () => reject(new Error("The local database is open in another tab with an older version. Close other AlgoSharp tabs and reload."));
    });
    openings.set(name, opening);
    return opening;
}

/** True if a library database exists, without creating one (used to offer the guest-library copy). */
export async function exists(name) {
    checkName(name);
    try {
        if (indexedDB.databases) return { ok: true, value: (await indexedDB.databases()).some(d => d.name === name) };
        return { ok: true, value: true }; // can't tell (older browsers): the caller just finds it empty
    } catch (error) {
        return { ok: false, error: describe(error) };
    }
}

function describe(error) {
    const name = error?.name ?? "Error";
    if (name === "QuotaExceededError") return "Browser storage is full. Export a backup and delete some problems, or free up space for this site.";
    if (name === "InvalidStateError") return "The local database isn't available (private browsing or storage disabled?).";
    return `${name}: ${error?.message ?? error}`;
}

async function run(name, storeNames, mode, work) {
    try {
        const db = await open(name);
        return await new Promise((resolve, reject) => {
            const tx = db.transaction(storeNames, mode);
            let result;
            Promise.resolve(work(tx)).then(r => { result = r; }, reject);
            tx.oncomplete = () => resolve({ ok: true, value: result ?? null });
            tx.onerror = () => reject(tx.error);
            tx.onabort = () => reject(tx.error ?? new Error("Transaction aborted."));
        });
    } catch (error) {
        return { ok: false, error: describe(error) };
    }
}

const req = (r) => new Promise((resolve, reject) => { r.onsuccess = () => resolve(r.result); r.onerror = () => reject(r.error); });

/** Rows are stored as { key fields..., json: "<C# JSON>" } so C# owns the record shape. */
export const getAll = (db, store) => run(db, [store], "readonly", async tx => (await req(tx.objectStore(store).getAll())).map(r => r.json));

export const get = (db, store, key) => run(db, [store], "readonly", async tx => (await req(tx.objectStore(store).get(key)))?.json ?? null);

export function put(db, store, json) {
    const value = JSON.parse(json);
    const row = { json };
    if (store === "problems") Object.assign(row, { id: value.id, collection: value.collection ?? "", updatedAt: value.updatedAt });
    if (store === "progress") Object.assign(row, { problemId: value.problemId });
    if (store === "prefs") Object.assign(row, { key: value.key });
    return run(db, [store], "readwrite", tx => { tx.objectStore(store).put(row); });
}

export const remove = (db, store, key) => run(db, [store], "readwrite", tx => { tx.objectStore(store).delete(key); });

/** Deletes a problem and its progress together. */
export const removeProblem = (db, id) => run(db, ["problems", "progress"], "readwrite", tx => {
    tx.objectStore("problems").delete(id);
    tx.objectStore("progress").delete(id);
});

/** Writes many problems (and optional progress) in one transaction: all or nothing. */
export function putMany(db, problemsJson, progressJson) {
    const problems = JSON.parse(problemsJson);
    const progress = JSON.parse(progressJson ?? "[]");
    return run(db, ["problems", "progress"], "readwrite", tx => {
        for (const p of problems) tx.objectStore("problems").put({ id: p.id, collection: p.collection ?? "", updatedAt: p.updatedAt, json: JSON.stringify(p) });
        for (const g of progress) tx.objectStore("progress").put({ problemId: g.problemId, json: JSON.stringify(g) });
    });
}

export async function usage() {
    try {
        const estimate = await navigator.storage?.estimate?.();
        const persisted = await navigator.storage?.persisted?.();
        return { ok: true, value: JSON.stringify({ usage: estimate?.usage ?? null, quota: estimate?.quota ?? null, persisted: !!persisted }) };
    } catch (error) {
        return { ok: false, error: describe(error) };
    }
}

/** Asks the browser not to evict this site's storage under pressure (it may say no). */
export async function persist() {
    try { return !!(await navigator.storage?.persist?.()); } catch { return false; }
}

/** Saves text as a file download (for JSON backups). */
export function download(filename, text) {
    const blob = new Blob([text], { type: "application/json" });
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = filename;
    document.body.appendChild(a);
    a.click();
    a.remove();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
}

/** Reads the first file of an <input type=file> as text. */
export async function readFile(input) {
    const file = input?.files?.[0];
    if (!file) return null;
    if (file.size > 20 * 1024 * 1024) throw new Error("That file is too large (20 MB max).");
    return await file.text();
}

export async function copyText(text) {
    try {
        await navigator.clipboard.writeText(text);
        return true;
    } catch {
        return false;
    }
}
