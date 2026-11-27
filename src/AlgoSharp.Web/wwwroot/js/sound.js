// AlgoSharp sound effects: tiny synthesized sounds (Web Audio, no files).
//  - Cues: start (Run), success / done / fail (a run's playback reached its end).
//  - Step sounds, one per real step transition: tick (comparison), pop (swap/movement), click (access or
//    variable update), place (final position / found), warn (error).
// Everything goes through one master gain (the volume setting). Step sounds never pile up: a new one cuts
// the previous one short, and steps closer together than MIN_GAP_MS are dropped, so fast playback stays
// in sync instead of lagging behind. Browsers only allow audio after a user gesture, so the audio context is
// unlocked on the first click / key press, and sounds are skipped (not queued) while it is still suspended.
const MUTE_KEY = 'algosharp-sound';
const VOLUME_KEY = 'algosharp-volume';
const MIN_GAP_MS = 35;

let ctx = null;
let master = null;
let voice = null;      // the step sound currently sounding
let lastStepAt = 0;

function stored(key, fallback) {
    try { return localStorage.getItem(key) ?? fallback; } catch { return fallback; }
}
function store(key, value) {
    try { localStorage.setItem(key, value); } catch { /* private mode */ }
}

export function isMuted() { return stored(MUTE_KEY, 'on') === 'off'; }

export function getVolume() {
    const v = parseFloat(stored(VOLUME_KEY, '0.8'));
    return Number.isFinite(v) ? Math.min(1, Math.max(0, v)) : 0.8;
}

export function setVolume(value) {
    const v = Math.min(1, Math.max(0, Number(value) || 0));
    store(VOLUME_KEY, String(v));
    if (master) master.gain.setTargetAtTime(v, master.context.currentTime, 0.01);
    step('tick', 0, true); // preview at the new level
    return v;
}

export function setMuted(muted) {
    store(MUTE_KEY, muted ? 'off' : 'on');
    if (!muted) play('start');
    return muted;
}

export function toggleMuted() { return setMuted(!isMuted()); }

function context() {
    if (typeof AudioContext === 'undefined') return null;
    if (!ctx) {
        ctx = new AudioContext({ latencyHint: 'interactive' });
        master = ctx.createGain();
        master.gain.value = getVolume();
        master.connect(ctx.destination);
    }
    return ctx;
}

// Browsers keep audio suspended until the page gets a user gesture: resume on the first one.
function unlock() {
    const ac = context();
    if (ac && ac.state !== 'running') ac.resume().catch(() => { });
}
for (const type of ['pointerdown', 'keydown', 'touchstart']) {
    document.addEventListener(type, unlock, { capture: true, passive: true });
}

/** A ready context, or null when audio isn't allowed yet (then the sound is skipped, never delayed). */
function ready() {
    const ac = context();
    if (!ac) return null;
    if (ac.state !== 'running') { ac.resume().catch(() => { }); return null; }
    return ac;
}

// One tone into `out`: quick attack, exponential decay; optional pitch glide (for pops).
function tone(ac, out, { freq, at, length, type = 'sine', volume = 0.1, glideTo = null, shimmer = 0 }) {
    const env = ac.createGain();
    env.connect(out);
    env.gain.setValueAtTime(0.0001, at);
    env.gain.exponentialRampToValueAtTime(volume, at + 0.006);
    env.gain.exponentialRampToValueAtTime(0.0001, at + length);
    for (const [ratio, level] of [[1, 1], [2, shimmer]]) {
        if (level <= 0) continue;
        const osc = ac.createOscillator();
        const g = ac.createGain();
        osc.type = type;
        osc.frequency.setValueAtTime(freq * ratio, at);
        if (glideTo) osc.frequency.exponentialRampToValueAtTime(glideTo * ratio, at + length * 0.8);
        g.gain.value = level;
        osc.connect(g).connect(env);
        osc.start(at);
        osc.stop(at + length + 0.03);
    }
}

// ---------------------------------------------------------------- step sounds

const STEPS = {
    // comparison / condition: a soft, bright tick, pitched by the values compared
    tick: (ac, out, t, p) => tone(ac, out, { freq: p ? p * 2 : 1320, at: t, length: 0.08, volume: 0.32, shimmer: 0.15 }),
    // swap or movement: a short pop (quick downward glide)
    pop: (ac, out, t, p) => { const f = p || 520; tone(ac, out, { freq: f * 1.7, glideTo: f * 0.85, at: t, length: 0.1, volume: 0.4 }); },
    // element access, pointer move or variable update: the quietest, shortest click
    click: (ac, out, t) => tone(ac, out, { freq: 1700, at: t, length: 0.045, type: 'triangle', volume: 0.22 }),
    // a value reaches its final place / is found: a tiny two-note chime
    place: (ac, out, t, p) => {
        const f = (p || 440) * 2;
        tone(ac, out, { freq: f, at: t, length: 0.14, type: 'triangle', volume: 0.28, shimmer: 0.2 });
        tone(ac, out, { freq: f * 1.5, at: t + 0.05, length: 0.2, type: 'triangle', volume: 0.24, shimmer: 0.2 });
    },
    // error: gentle, low, two descending notes
    warn: (ac, out, t) => {
        tone(ac, out, { freq: 330, at: t, length: 0.18, type: 'triangle', volume: 0.32 });
        tone(ac, out, { freq: 262, at: t + 0.1, length: 0.26, type: 'triangle', volume: 0.32 });
    },
};

/** One step's sound. `pitch` is the value-derived frequency (0 = none). */
export function step(kind, pitch, force = false) {
    if (isMuted() || !STEPS[kind]) return;
    const now = performance.now();
    if (!force && now - lastStepAt < MIN_GAP_MS && kind !== 'warn' && kind !== 'place') return;
    const ac = ready();
    if (!ac) return;
    lastStepAt = now;
    const t = ac.currentTime + 0.003;
    // Cut the previous step sound short so steps never overlap.
    if (voice) {
        const old = voice;
        old.gain.cancelScheduledValues(t);
        old.gain.setTargetAtTime(0, t, 0.004);
        setTimeout(() => { try { old.disconnect(); } catch { } }, 80);
    }
    voice = ac.createGain();
    voice.connect(master);
    const f = Number.isFinite(pitch) && pitch > 20 && pitch < 5000 ? pitch : 0;
    try { STEPS[kind](ac, voice, t, f); } catch { /* audio is a nicety */ }
}

// ---------------------------------------------------------------- cues

const CUES = {
    // C5 → G5: "here we go"
    start: (ac, out, t) => { tone(ac, out, { freq: 523.25, at: t, length: 0.14, volume: 0.24, shimmer: 0.2 }); tone(ac, out, { freq: 783.99, at: t + 0.07, length: 0.2, volume: 0.24, shimmer: 0.2 }); },
    // C5 E5 G5 C6: completed successfully
    success: (ac, out, t) => [523.25, 659.25, 783.99, 1046.5].forEach((f, i) =>
        tone(ac, out, { freq: f, at: t + i * 0.08, length: i === 3 ? 0.6 : 0.28, type: 'triangle', volume: 0.2, shimmer: 0.25 })),
    // completed, nothing graded: a lighter two-note chime
    done: (ac, out, t) => { tone(ac, out, { freq: 659.25, at: t, length: 0.25, type: 'triangle', volume: 0.2, shimmer: 0.25 }); tone(ac, out, { freq: 987.77, at: t + 0.09, length: 0.45, type: 'triangle', volume: 0.18, shimmer: 0.25 }); },
    // finished with a failure or error: a gentle warning
    fail: (ac, out, t) => { tone(ac, out, { freq: 392, at: t, length: 0.22, type: 'triangle', volume: 0.24 }); tone(ac, out, { freq: 311.13, at: t + 0.13, length: 0.38, type: 'triangle', volume: 0.24 }); },
};

export function play(kind) {
    if (isMuted() || !CUES[kind]) return;
    const ac = ready();
    if (!ac) return;
    if (voice) { try { voice.gain.setTargetAtTime(0, ac.currentTime, 0.004); } catch { } }
    try { CUES[kind](ac, master, ac.currentTime + 0.003); } catch { /* audio is a nicety */ }
}
