// Generates public/soundtrack.wav: ambient bed + sound effects synced to src/Video.jsx
// (30 fps, 570 frames = 19 s). If you change timings in Video.jsx, update the cue times below.
import {mkdirSync, writeFileSync} from 'node:fs';
import {dirname, join} from 'node:path';
import {fileURLToPath} from 'node:url';

const SR = 44100;
const DURATION = 19;
const N = SR * DURATION;
const TAU = Math.PI * 2;
const L = new Float32Array(N);
const R = new Float32Array(N);

// Deterministic noise (mulberry32) so every render sounds the same.
let seed = 1234567;
const rand = () => {
  seed = (seed + 0x6d2b79f5) | 0;
  let t = Math.imul(seed ^ (seed >>> 15), 1 | seed);
  t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
  return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
};
const noise = () => rand() * 2 - 1;
const midi = (n) => 440 * 2 ** ((n - 69) / 12);
const smooth = (x) => x * x * (3 - 2 * x);

/** Mixes gen(t) over [start, start + len) into both channels. pan: -1 (left) to 1 (right). */
function put(start, len, gain, pan, gen) {
  const i0 = Math.max(0, Math.round(start * SR));
  const count = Math.min(Math.round(len * SR), N - i0);
  const angle = ((pan + 1) * Math.PI) / 4;
  const gl = gain * Math.cos(angle);
  const gr = gain * Math.sin(angle);
  for (let i = 0; i < count; i += 1) {
    const s = gen(i / SR);
    L[i0 + i] += s * gl;
    R[i0 + i] += s * gr;
  }
}

function pad(freq, start, len, gain, pan = 0) {
  const attack = 1.2;
  const release = 1.4;
  put(start, len + release, gain, pan, (t) => {
    const up = smooth(Math.min(1, t / attack));
    const down = t < len ? 1 : smooth(Math.max(0, 1 - (t - len) / release));
    const w = TAU * freq * t;
    return (
      up * down * (Math.sin(w) + 0.55 * Math.sin(w * 1.004) + 0.55 * Math.sin(w * 0.996) + 0.22 * Math.sin(2 * w))
    );
  });
}

function bass(freq, start, len, gain) {
  put(start, len, gain, 0, (t) => {
    const env = Math.min(1, t / 0.05) * Math.min(1, (len - t) / 0.3);
    return env * Math.sin(TAU * freq * t);
  });
}

function pluck(freq, start, gain, pan) {
  put(start, 1.4, gain, pan, (t) => {
    const env = Math.min(1, t / 0.004) * Math.exp(-t * 4.5);
    const w = TAU * freq * t;
    return env * (Math.sin(w) + 0.35 * Math.sin(2 * w) * Math.exp(-t * 6) + 0.12 * Math.sin(3 * w));
  });
}

/** Band-limited noise sweep from f0 to f1 (Hz) with a bell-shaped envelope. */
function whoosh(start, len, gain, f0, f1, pan = 0) {
  let a = 0;
  let b = 0;
  put(start, len, gain, pan, (t) => {
    const p = t / len;
    const fc = f0 * (f1 / f0) ** p;
    const k = 1 - Math.exp((-TAU * fc) / SR);
    a += k * (noise() - a);
    b += k * (a - b);
    const env = Math.sin(Math.PI * p) ** 2;
    return env * b * (SR / 2 / fc) ** 0.25 * 0.35;
  });
}

function click(start, gain = 0.5, pan = 0) {
  let lp = 0;
  put(start, 0.1, gain, pan, (t) => {
    lp += 0.35 * (noise() - lp);
    return Math.sin(TAU * 1500 * t) * Math.exp(-t * 110) * 0.6 + lp * Math.exp(-t * 350) * 0.8;
  });
}

function thump(start, gain) {
  let phase = 0;
  put(start, 0.7, gain, 0, (t) => {
    phase += (TAU * (45 + 70 * Math.exp(-t * 18))) / SR;
    return Math.sin(phase) * Math.exp(-t * 7) * Math.min(1, t / 0.003);
  });
}

function kick(start, gain) {
  let phase = 0;
  put(start, 0.3, gain, 0, (t) => {
    phase += (TAU * (48 + 110 * Math.exp(-t * 35))) / SR;
    return Math.sin(phase) * Math.exp(-t * 13) * Math.min(1, t / 0.002);
  });
}

function hat(start, gain, pan) {
  let lp = 0;
  put(start, 0.08, gain, pan, (t) => {
    const n = noise();
    lp += 0.6 * (n - lp);
    return (n - lp) * Math.exp(-t * 75);
  });
}

function chime(freq, start, gain, pan = 0) {
  put(start, 2.4, gain, pan, (t) => {
    const env = Math.min(1, t / 0.003) * Math.exp(-t * 2.6);
    const w = TAU * freq * t;
    return (
      env *
      (Math.sin(w) + 0.4 * Math.sin(2.01 * w) * Math.exp(-t * 2) + 0.18 * Math.sin(3.97 * w) * Math.exp(-t * 5))
    );
  });
}

// ---------------------------------------------------------------- music bed
const BPM = 96;
const BEAT = 60 / BPM;
const BAR = BEAT * 4;
const CHORDS = [
  {bass: 41, notes: [53, 57, 60, 64]}, // Fmaj7
  {bass: 45, notes: [57, 60, 64, 67]}, // Am7
  {bass: 38, notes: [50, 57, 60, 65]}, // Dm7
  {bass: 48, notes: [55, 60, 64, 62]}, // Cadd9
];
const ARP = [0, 2, 1, 3, 2, 1, 3, 2];
const BARS = 7;
const RELEASE = 1.4;

for (let b = 0; b < BARS; b += 1) {
  const last = b === BARS - 1;
  const chord = last ? CHORDS[0] : CHORDS[b % CHORDS.length];
  const start = b * BAR;
  const len = last ? DURATION - start - RELEASE : BAR;
  chord.notes.forEach((n, i) => pad(midi(n), start, len, 0.05, i % 2 === 0 ? -0.3 : 0.3));
  bass(midi(chord.bass), start, last ? len : BAR * 0.95, 0.16);

  if (b >= 1 && !last) {
    ARP.forEach((idx, step) => {
      const t = start + step * (BEAT / 2);
      pluck(midi(chord.notes[idx] + 12), t, t < 2 ? 0.035 : 0.07, step % 2 === 0 ? -0.35 : 0.35);
    });
  }
}

// Light groove during the demo (8 s to 15.5 s).
for (let t = 8; t < 15.5; t += BEAT) {
  kick(t, 0.18);
  hat(t + BEAT / 2, 0.04, 0.3);
}

// ---------------------------------------------------------------- sound effects
// Intro
whoosh(0.05, 0.7, 0.16, 250, 3500);
whoosh(0.6, 0.6, 0.1, 900, 3800, 0.2);
thump(0.9, 0.35);
chime(880, 1.0, 0.16);
chime(1318.5, 1.25, 0.09, 0.3);
// iPhone enters, Control Center
whoosh(2.0, 1.2, 0.2, 200, 2600, -0.2);
click(3.5);
whoosh(3.72, 0.5, 0.12, 1200, 3600);
chime(1760, 3.75, 0.05);
click(5.5);
chime(1046.5, 6.2, 0.16, -0.2);
chime(1568, 6.32, 0.14, 0.2);
// Mirroring starts, phone slides left, window appears
whoosh(6.87, 0.9, 0.18, 400, 3200);
whoosh(8.0, 1.4, 0.22, 180, 2800, -0.3);
whoosh(8.67, 1.3, 0.2, 250, 3200, 0.3);
thump(9.95, 0.4);
// Rotation
whoosh(11.0, 2.0, 0.26, 160, 5200);
thump(12.95, 0.55);
chime(659.25, 13.0, 0.14);
chime(987.77, 13.1, 0.1);
// Outro
thump(16.0, 0.5);
chime(523.25, 16.0, 0.17);
chime(659.25, 16.09, 0.15);
chime(783.99, 16.18, 0.14);
chime(1046.5, 16.27, 0.1);
chime(1318.5, 17.13, 0.08);

// ---------------------------------------------------------------- reverb (4 combs + 2 allpass)
function allpass(x, delay, g) {
  const y = new Float32Array(N);
  for (let i = 0; i < N; i += 1) {
    const xd = i >= delay ? x[i - delay] : 0;
    const yd = i >= delay ? y[i - delay] : 0;
    y[i] = -g * x[i] + xd + g * yd;
  }
  return y;
}

function reverb(input, offset) {
  const g = 0.8;
  const wet = new Float32Array(N);
  for (const d of [0.0297, 0.0371, 0.0411, 0.0437]) {
    const delay = Math.round((d + offset) * SR);
    const y = new Float32Array(N);
    for (let i = 0; i < N; i += 1) {
      y[i] = input[i] + (i >= delay ? g * y[i - delay] : 0);
      wet[i] += y[i] * (1 - g) * 0.25;
    }
  }
  return allpass(allpass(wet, 556, 0.5), 441, 0.5);
}

const wetL = reverb(L, 0);
const wetR = reverb(R, 0.0013);

// ---------------------------------------------------------------- master + WAV
const outL = new Float32Array(N);
const outR = new Float32Array(N);
let peak = 0;
for (let i = 0; i < N; i += 1) {
  const t = i / SR;
  const fadeIn = smooth(Math.min(1, t / 0.4));
  const fadeOut = smooth(Math.min(1, (DURATION - t) / 1.6));
  const g = fadeIn * fadeOut;
  outL[i] = (L[i] + 0.4 * wetL[i]) * g;
  outR[i] = (R[i] + 0.4 * wetR[i]) * g;
  peak = Math.max(peak, Math.abs(outL[i]), Math.abs(outR[i]));
}
const norm = peak > 0 ? 0.89 / peak : 1;

const wav = Buffer.alloc(44 + N * 4);
wav.write('RIFF', 0);
wav.writeUInt32LE(36 + N * 4, 4);
wav.write('WAVE', 8);
wav.write('fmt ', 12);
wav.writeUInt32LE(16, 16);
wav.writeUInt16LE(1, 20); // PCM
wav.writeUInt16LE(2, 22); // stereo
wav.writeUInt32LE(SR, 24);
wav.writeUInt32LE(SR * 4, 28);
wav.writeUInt16LE(4, 32);
wav.writeUInt16LE(16, 34);
wav.write('data', 36);
wav.writeUInt32LE(N * 4, 40);
const toInt16 = (x) => Math.max(-32768, Math.min(32767, Math.round(x * norm * 32767)));
for (let i = 0; i < N; i += 1) {
  wav.writeInt16LE(toInt16(outL[i]), 44 + i * 4);
  wav.writeInt16LE(toInt16(outR[i]), 46 + i * 4);
}

const outDir = join(dirname(fileURLToPath(import.meta.url)), '..', 'public');
mkdirSync(outDir, {recursive: true});
const outFile = join(outDir, 'soundtrack.wav');
writeFileSync(outFile, wav);
console.log(`soundtrack.wav written: ${DURATION}s, ${(wav.length / 1048576).toFixed(1)} MB, raw peak ${peak.toFixed(2)}`);