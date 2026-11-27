// Short synthesized sound cues (Web Audio, no audio files): a lift-off on Run, a bright arpeggio when a run
// finishes well, a soft low tone when it fails. Muted state is remembered in localStorage.
const KEY = 'algosharp-sound';
let ctx = null;

export function isMuted() {
    try { return localStorage.getItem(KEY) === 'off'; } catch { return false; }
}

export function toggleMuted() {
    const muted = !isMuted();
    try { localStorage.setItem(KEY, muted ? 'off' : 'on'); } catch { /* private mode */ }
    if (!muted) play('start');
    return muted;
}

function audio() {
    if (typeof AudioContext === 'undefined') return null;
    ctx ??= new AudioContext();
    if (ctx.state === 'suspended') ctx.resume().catch(() => { });
    return ctx;
}

// One note: a soft attack and an exponential decay, plus a quiet octave for a little shimmer.
function note(ac, freq, at, length, { type = 'sine', volume = 0.12, shimmer = 0.25 } = {}) {
    const out = ac.createGain();
    out.connect(ac.destination);
    out.gain.setValueAtTime(0.0001, at);
    out.gain.exponentialRampToValueAtTime(volume, at + 0.012);
    out.gain.exponentialRampToValueAtTime(0.0001, at + length);
    for (const [f, level] of [[freq, 1], [freq * 2, shimmer]]) {
        if (level <= 0) continue;
        const osc = ac.createOscillator();
        const g = ac.createGain();
        osc.type = type;
        osc.frequency.setValueAtTime(f, at);
        g.gain.value = level;
        osc.connect(g).connect(out);
        osc.start(at);
        osc.stop(at + length + 0.05);
    }
}

const CUES = {
    // C5 → G5: "here we go"
    start: ac => { const t = ac.currentTime; note(ac, 523.25, t, 0.16); note(ac, 783.99, t + 0.07, 0.22); },
    // C5 E5 G5 C6: done, all good
    success: ac => {
        const t = ac.currentTime;
        [523.25, 659.25, 783.99, 1046.5].forEach((f, i) => note(ac, f, t + i * 0.085, i === 3 ? 0.6 : 0.3, { type: 'triangle', volume: 0.11 }));
    },
    // finished, nothing graded (practice / plain visualization): a lighter two-note chime
    done: ac => { const t = ac.currentTime; note(ac, 659.25, t, 0.25, { type: 'triangle' }); note(ac, 987.77, t + 0.09, 0.45, { type: 'triangle', volume: 0.1 }); },
    // G4 → E♭4, low and soft: something to look at, not a buzzer
    fail: ac => { const t = ac.currentTime; note(ac, 392.0, t, 0.22, { type: 'triangle', volume: 0.1, shimmer: 0 }); note(ac, 311.13, t + 0.13, 0.38, { type: 'triangle', volume: 0.1, shimmer: 0 }); },
};

// Per-step sounds while a visualization plays: quieter and shorter than the cues above so 4× playback
// (a step every 250 ms) stays pleasant. Pitches come from the values involved (220–880 Hz).
const soft = { volume: 0.05, shimmer: 0 };
const STEPS = {
    tick: (ac, t) => note(ac, 1046.5, t, 0.04, { volume: 0.025, shimmer: 0 }),
    compare: (ac, t, p) => p.forEach((f, i) => note(ac, f, t + i * 0.035, 0.08, soft)),
    swap: (ac, t, p) => p.forEach((f, i) => note(ac, f, t + i * 0.05, 0.11, { type: 'triangle', volume: 0.065, shimmer: 0.15 })),
    place: (ac, t, p) => note(ac, (p[0] ?? 523.25) * 2, t, 0.22, { type: 'triangle', volume: 0.06, shimmer: 0.3 }),
    add: (ac, t, p) => { const f = p[0] ?? 523.25; note(ac, f, t, 0.08, soft); note(ac, f * 1.5, t + 0.05, 0.12, soft); },
    remove: (ac, t, p) => { const f = p[0] ?? 523.25; note(ac, f * 1.5, t, 0.08, soft); note(ac, f, t + 0.05, 0.12, soft); },
    error: (ac, t) => note(ac, 196, t, 0.22, { type: 'triangle', volume: 0.07, shimmer: 0 }),
};

export function step(kind, pitches) {
    if (isMuted() || !STEPS[kind]) return;
    const ac = audio();
    if (!ac) return;
    const p = (Array.isArray(pitches) ? pitches : []).filter(f => Number.isFinite(f) && f > 20 && f < 5000).slice(0, 2);
    try { STEPS[kind](ac, ac.currentTime, p); } catch { /* audio is a nicety */ }
}

export function play(kind) {
    if (isMuted() || !CUES[kind]) return;
    const ac = audio();
    if (!ac) return;
    try { CUES[kind](ac); } catch { /* audio is a nicety */ }
}
