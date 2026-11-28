// AlgoSharp interactive-session load test: N real Solve It sessions in headless Firefox (WebDriver BiDi, no npm
// packages), each its own window, cookie jar and throwaway account on a LOCAL database. Every virtual user edits
// Two Sum (live checks over the Blazor circuit) and about every sixth action presses Run & visualize (server compile,
// execution in that browser's WebAssembly worker, trace back to the server, playback). Never point this at Render
// or the Neon production database.
//
//   node tools/loadtest/sessions.mjs --firefox "C:/Program Files/Mozilla Firefox/firefox.exe" \
//        --base https://localhost:5331 --container algosharp-sessions --users 5 10 20 --seconds 90
//
// The server should run the production image with Render Free limits (--memory 512m --cpus 0.1) and
// Diagnostics:LoadTestEndpoints=true (for /diag/stats). Accounts are registered 6.5 s apart to stay under the
// sign-up rate limit (10 per minute per IP).
import { spawn, execFileSync } from 'node:child_process';
import { mkdtempSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { randomBytes } from 'node:crypto';

const args = process.argv.slice(2);
const opt = (name, fallback) => { const i = args.indexOf('--' + name); return i < 0 ? fallback : args[i + 1]; };
const firefox = opt('firefox', 'C:/Program Files/Mozilla Firefox/firefox.exe');
const base = opt('base', 'https://localhost:5331');
const container = opt('container', 'algosharp-sessions');
const seconds = Number(opt('seconds', '90'));
const levels = (() => { const i = args.indexOf('--users'); if (i < 0) return [5, 10, 20]; const v = []; for (let j = i + 1; j < args.length && !args[j].startsWith('--'); j++) v.push(Number(args[j])); return v; })();
const sleep = ms => new Promise(r => setTimeout(r, ms));

// ------------------------------------------------------------------ BiDi plumbing
const profile = mkdtempSync(join(process.env.SCRATCH || tmpdir(), 'ff-sessions-'));
const port = 9335;
const ff = spawn(firefox, ['-headless', '-no-remote', '-profile', profile, `--remote-debugging-port=${port}`, 'about:blank'], { stdio: 'ignore' });
let ws;
for (let i = 0; i < 60 && !ws; i++) {
    await sleep(500);
    try { const s = new WebSocket(`ws://127.0.0.1:${port}/session`); await new Promise((res, rej) => { s.onopen = res; s.onerror = rej; }); ws = s; } catch { }
}
if (!ws) { console.log('Firefox did not start'); ff.kill(); process.exit(1); }
let seq = 0; const pending = new Map();
ws.onmessage = e => { const m = JSON.parse(e.data); if (m.id && pending.has(m.id)) { pending.get(m.id)(m); pending.delete(m.id); } };
const send = (method, params) => new Promise(res => { const id = ++seq; pending.set(id, res); ws.send(JSON.stringify({ id, method, params })); });
const evaluate = async (context, expression) => {
    const r = await send('script.evaluate', { expression, target: { context }, awaitPromise: true });
    return r.result?.result?.value ?? `ERR ${r.result?.exceptionDetails?.text ?? JSON.stringify(r.error ?? r).slice(0, 200)}`;
};
const navigate = (context, url) => send('browsingContext.navigate', { context, url, wait: 'complete' });

// ------------------------------------------------------------------ server-side sampling
function dockerSample() {
    try {
        const out = execFileSync('docker', ['stats', '--no-stream', '--format', '{{.MemUsage}}|{{.CPUPerc}}', container], { encoding: 'utf8', timeout: 10000 }).trim();
        const [mem, cpu] = out.split('|');
        const used = mem.split('/')[0].trim();
        const mb = used.endsWith('GiB') ? parseFloat(used) * 1024 : used.endsWith('MiB') ? parseFloat(used) : parseFloat(used) / 1024;
        return { mb, cpu: parseFloat(cpu) };
    } catch { return null; }
}
function serverStats() {
    try { return JSON.parse(execFileSync('curl', ['-sk', base + '/diag/stats'], { encoding: 'utf8', timeout: 10000 })); } catch { return null; }
}

// ------------------------------------------------------------------ one virtual user (runs inside its page)
const TWO_SUM = `public class Solution
{
    public int[] TwoSum(int[] nums, int target)
    {
        var seenAt = new Dictionary<int, int>();
        for (int idx = 0; idx < nums.Length; idx++)
        {
            int need = target - nums[idx];
            if (seenAt.ContainsKey(need)) return new int[] { seenAt[need], idx };
            seenAt[nums[idx]] = idx;
        }
        return new int[0];
    }
}`;
const virtualUser = (secs, seed) => `(async () => {
    const sleep = ms => new Promise(r => setTimeout(r, ms));
    let rnd = ${seed}; const random = () => (rnd = (rnd * 16807) % 2147483647) / 2147483647;
    for (let i = 0; i < 80 && typeof monaco === 'undefined'; i++) await sleep(500);
    if (typeof monaco === 'undefined') return JSON.stringify({ fatal: 'editor never loaded' });
    const ed = monaco.editor.getEditors()[0];
    const base = ${JSON.stringify(TWO_SUM)};
    const s = { checks: 0, runs: 0, passed: 0, runMs: [], errors: [], reconnects: 0, busy: 0 };
    const button = () => [...document.querySelectorAll('button')].find(b => /Run & visualize|Running/.test(b.textContent));
    const end = performance.now() + ${secs} * 1000;
    await sleep(random() * 2000);
    let n = 0;
    while (performance.now() < end) {
        n++;
        if (document.querySelector('#components-reconnect-modal[open]')) s.reconnects++;
        const note = document.querySelector('.busy-note')?.innerText;
        if (note) s.busy++;
        if (n % 6 === 0) {
            const t0 = performance.now();
            button()?.click();
            await sleep(300);
            while (performance.now() - t0 < 60000) { await sleep(150); const b = button(); if (b && !b.textContent.includes('Running')) break; }
            s.runs++;
            s.runMs.push(Math.round(performance.now() - t0));
            const err = document.querySelector('.message.error')?.innerText.replace(/\\s+/g, ' ');
            if (err) s.errors.push(err.slice(0, 90));
            if (/5 \\/ 5 passed/.test(document.querySelector('.case-summary')?.innerText || '')) s.passed++;
        } else {
            ed.setValue(base.replace('public class Solution', '// edit ' + n + '\\npublic class Solution'));
            s.checks++;
        }
        await sleep(1500 + random() * 1500);
    }
    return JSON.stringify(s);
})()`;

const pct = (xs, p) => xs.length ? [...xs].sort((a, b) => a - b)[Math.min(xs.length - 1, Math.floor(xs.length * p))] : null;

try {
    await send('session.new', { capabilities: { alwaysMatch: { acceptInsecureCerts: true } } });
    const maxUsers = Math.max(...levels);
    const runId = randomBytes(3).toString('hex');
    const users = [];
    console.log(`registering ${maxUsers} local throwaway accounts (paced for the sign-up rate limit)...`);
    for (let i = 0; i < maxUsers; i++) {
        const uc = (await send('browser.createUserContext', {})).result.userContext;
        const context = (await send('browsingContext.create', { type: 'window', userContext: uc })).result.context;
        await navigate(context, base + '/account/register');
        const name = `load_${runId}_${i}`, pw = 'Lt-' + randomBytes(12).toString('base64url') + '9a';
        await evaluate(context, `(() => { const f = document.querySelector('.auth-card form');
            f.querySelector('[name="Input.UserName"]').value = ${JSON.stringify(name)};
            f.querySelector('[name="Input.Email"]').value = ${JSON.stringify(name + '@example.test')};
            f.querySelector('[name="Input.Password"]').value = ${JSON.stringify(pw)};
            f.querySelector('[name="Input.ConfirmPassword"]').value = ${JSON.stringify(pw)};
            f.requestSubmit(); return 'ok'; })()`);
        users.push(context);
        await sleep(6500);
    }

    for (const level of levels) {
        const active = users.slice(0, level);
        await Promise.all(active.map(c => navigate(c, base + '/problems/two-sum')));
        await sleep(4000);
        const before = serverStats();
        const samples = [];
        let sampling = true;
        const sampler = (async () => { while (sampling) { const d = dockerSample(); if (d) samples.push(d); await sleep(1000); } })();
        const started = Date.now();
        const results = await Promise.all(active.map((c, i) => evaluate(c, virtualUser(seconds, 1000 + i * 7919))));
        const wall = (Date.now() - started) / 1000;
        sampling = false;
        await sampler;
        const after = serverStats();

        const parsed = results.map(r => { try { return JSON.parse(r); } catch { return { fatal: String(r).slice(0, 120) }; } });
        const ok = parsed.filter(p => !p.fatal);
        const runMs = ok.flatMap(p => p.runMs);
        const sum = k => ok.reduce((a, p) => a + p[k], 0);
        const errors = ok.flatMap(p => p.errors);
        const g0 = before?.gate ?? {}, g1 = after?.gate ?? {};
        console.log(`\n=== ${level} interactive sessions, ${wall.toFixed(0)} s ===`);
        console.log(`  sessions ok ${ok.length}/${level}${parsed.some(p => p.fatal) ? ' fatal: ' + parsed.filter(p => p.fatal).map(p => p.fatal).join('; ') : ''}`);
        console.log(`  live checks ${sum('checks')}, runs ${sum('runs')} (all 5/5 passed: ${sum('passed')}), run errors ${errors.length}, reconnect banners ${sum('reconnects')}, busy notes ${sum('busy')}`);
        console.log(`  Run & visualize (click to result, includes browser WebAssembly): p50 ${pct(runMs, .5)} ms, p95 ${pct(runMs, .95)} ms, max ${runMs.length ? Math.max(...runMs) : null} ms`);
        if (errors.length) console.log('  errors: ' + [...new Set(errors)].slice(0, 5).join(' | '));
        console.log(`  server compiles +${(g1.ok ?? 0) - (g0.ok ?? 0)} (busy +${(g1.busy ?? 0) - (g0.busy ?? 0)}, cooldown +${(g1.cooldown ?? 0) - (g0.cooldown ?? 0)}, timeout +${(g1.timedOut ?? 0) - (g0.timedOut ?? 0)}, superseded +${(g1.superseded ?? 0) - (g0.superseded ?? 0)}), compile avg ${g1.avgMs} ms max ${g1.maxMs} ms`);
        console.log(`  runs: ${after?.runs}, trace interpret avg ${after?.interpretAvgMs} ms max ${after?.interpretMaxMs} ms, circuits now ${after?.circuits} peak ${after?.peakCircuits}`);
        if (samples.length) {
            const mem = samples.map(s => s.mb), cpu = samples.map(s => s.cpu);
            console.log(`  container memory peak ${Math.max(...mem).toFixed(0)} MiB; CPU avg ${(cpu.reduce((a, b) => a + b, 0) / cpu.length).toFixed(1)}% max ${Math.max(...cpu).toFixed(1)}% (cap 10%)`);
        }
        console.log(`  process working set ${after?.workingSetMb} MB, GC heap ${after?.gcHeapMb} MB`);
        await sleep(5000);
    }
} finally {
    try { await send('session.end', {}); } catch { }
    ff.kill();
}
