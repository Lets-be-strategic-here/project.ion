"""Shared DSP for the [project]ion offline audio generators (ionmusic.py, ionsfx.py).

Everything here is deterministic (seeded) so a re-render produces the same files. No samples from
anywhere else are used except the CC0 Kenney footsteps/impacts in sources/ (see CREDITS).

Conventions
- Signals are float64 numpy arrays in [-1, 1]; stereo is shape (n, 2).
- "Loop" helpers are circular: the end of the buffer flows into the start with no seam.
"""
from __future__ import annotations

import os
import re

import numpy as np
import scipy.signal as sg
import soundfile as sf

TAU = 2.0 * np.pi

# ---------------------------------------------------------------- pitch

_NOTE_RE = re.compile(r"^([A-G])(#?)(-?\d)$")
_SEMI = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


def midi(name: str) -> int:
    m = _NOTE_RE.match(name)
    if not m:
        raise ValueError(f"bad note {name!r}")
    letter, sharp, octave = m.groups()
    return 12 * (int(octave) + 1) + _SEMI[letter] + (1 if sharp else 0)


def hz(name_or_midi) -> float:
    n = midi(name_or_midi) if isinstance(name_or_midi, str) else float(name_or_midi)
    return 440.0 * 2.0 ** ((n - 69) / 12.0)


def cents(c: float) -> float:
    return 2.0 ** (c / 1200.0)


# ---------------------------------------------------------------- basic helpers

def secs(sr: int, s: float) -> int:
    return max(1, int(round(s * sr)))


def tvec(sr: int, n: int) -> np.ndarray:
    return np.arange(n) / sr


def db(x: float) -> float:
    return 10.0 ** (x / 20.0)


def pan2(x: np.ndarray, pan: float) -> np.ndarray:
    """Equal-power pan, pan in [-1, 1]."""
    a = (pan + 1.0) * 0.25 * np.pi
    return np.stack([x * np.cos(a), x * np.sin(a)], axis=1)


def add_at(buf: np.ndarray, x: np.ndarray, start: int) -> None:
    """buf[start:start+len(x)] += x, clipped to the buffer."""
    if start >= len(buf):
        return
    if start < 0:
        x = x[-start:]
        start = 0
    end = min(len(buf), start + len(x))
    buf[start:end] += x[: end - start]


def fold(buf: np.ndarray, n: int) -> np.ndarray:
    """Folds everything past n back onto the start (circular), returns buf[:n]."""
    out = buf[:n].copy()
    k = n
    while k < len(buf):
        seg = buf[k:k + n]
        out[: len(seg)] += seg
        k += n
    return out


def edge_fade(x: np.ndarray, sr: int, fin: float = 0.002, fout: float = 0.02) -> np.ndarray:
    x = x.copy()
    a, b = min(len(x), secs(sr, fin)), min(len(x), secs(sr, fout))
    ramp_in = np.linspace(0.0, 1.0, a, endpoint=False)
    ramp_out = np.linspace(1.0, 0.0, b)
    if x.ndim == 2:
        ramp_in, ramp_out = ramp_in[:, None], ramp_out[:, None]
    x[:a] *= ramp_in
    x[len(x) - b:] *= ramp_out
    return x


def normalize_peak(x: np.ndarray, peak_db: float) -> np.ndarray:
    m = float(np.max(np.abs(x))) or 1e-9
    return x * (db(peak_db) / m)


def trim_tail(x: np.ndarray, sr: int, floor_db: float = -62.0, min_len: float = 0.05) -> np.ndarray:
    """Cuts the silent tail (relative to the peak) and fades the last 20 ms."""
    a = np.abs(x) if x.ndim == 1 else np.max(np.abs(x), axis=1)
    thr = float(a.max()) * db(floor_db)
    idx = np.nonzero(a > thr)[0]
    end = max(secs(sr, min_len), (int(idx[-1]) + 1) if len(idx) else 0)
    end = min(len(x), end + secs(sr, 0.02))
    return edge_fade(x[:end], sr, 0.0005, 0.02)


def smoothstep(u: np.ndarray) -> np.ndarray:
    u = np.clip(u, 0.0, 1.0)
    return u * u * (3.0 - 2.0 * u)


# ---------------------------------------------------------------- filters

def _sos(kind: str, fc, sr: int, order: int = 2):
    nyq = sr * 0.5
    if isinstance(fc, (tuple, list)):
        wn = [min(0.999, max(1e-4, f / nyq)) for f in fc]
    else:
        wn = min(0.999, max(1e-4, fc / nyq))
    return sg.butter(order, wn, btype=kind, output="sos")


def lp(x, fc, sr, order=2):
    return sg.sosfilt(_sos("lowpass", fc, sr, order), x, axis=0)


def hp(x, fc, sr, order=2):
    return sg.sosfilt(_sos("highpass", fc, sr, order), x, axis=0)


def bp(x, f1, f2, sr, order=2):
    return sg.sosfilt(_sos("bandpass", (f1, f2), sr, order), x, axis=0)


def lp_circular(x, fc, sr, order=2):
    """Zero-phase-free circular low-pass: filter a wrapped copy so the loop seam stays continuous."""
    pad = min(len(x), sr)  # one second of warm-up is plenty for these filters
    y = lp(np.concatenate([x[-pad:], x]), fc, sr, order)
    return y[pad:]


def hp_circular(x, fc, sr, order=2):
    pad = min(len(x), sr)
    y = hp(np.concatenate([x[-pad:], x]), fc, sr, order)
    return y[pad:]


def sweep_lp(x: np.ndarray, cutoff: np.ndarray, sr: int) -> np.ndarray:
    """Two-pole (2x one-pole) low-pass with a per-sample cutoff (Hz). Mono."""
    a = 1.0 - np.exp(-TAU * np.maximum(cutoff, 5.0) / sr)
    y1 = np.empty_like(x)
    s1 = s2 = 0.0
    for i in range(len(x)):  # plain loop: only used on short SFX
        s1 += a[i] * (x[i] - s1)
        s2 += a[i] * (s1 - s2)
        y1[i] = s2
    return y1


# ---------------------------------------------------------------- noise

def periodic_noise(n: int, sr: int, rng: np.random.Generator, shape=None) -> np.ndarray:
    """White (or spectrally shaped) noise that is exactly periodic over n samples."""
    spec = np.fft.rfft(rng.standard_normal(n))
    if shape is not None:
        f = np.fft.rfftfreq(n, 1.0 / sr)
        spec *= shape(np.maximum(f, 1.0))
    y = np.fft.irfft(spec, n)
    return y / (np.std(y) + 1e-12)


def pink_shape(f):
    return 1.0 / np.sqrt(f)


def band_shape(lo, hi, slope=2.0):
    def s(f):
        return 1.0 / np.sqrt(1.0 + (lo / f) ** (2 * slope)) / np.sqrt(1.0 + (f / hi) ** (2 * slope))
    return s


# ---------------------------------------------------------------- reverb

def reverb_ir(sr: int, t60: float = 3.6, t60_hi: float = 1.5, predelay: float = 0.022,
              seed: int = 7, stereo: bool = True, xover: float = 2800.0, early: bool = True) -> np.ndarray:
    """Synthetic room IR: two decaying noise bands (the highs die sooner) + a few early taps."""
    rng = np.random.default_rng(seed)
    n = secs(sr, t60 * 1.05)
    t = tvec(sr, n)
    chans = []
    for _ in range(2 if stereo else 1):
        w = rng.standard_normal(n)
        lo = lp(w, xover, sr, 2)
        hi = w - lo
        ir = lo * np.exp(-6.91 * t / t60) + 0.55 * hi * np.exp(-6.91 * t / t60_hi)
        ir *= 1.0 - np.exp(-t / 0.012)  # soft onset of the diffuse tail
        if early:
            for k in range(6):
                d = secs(sr, rng.uniform(0.004, 0.045))
                if d < n:
                    ir[d] += rng.uniform(-1.0, 1.0) * 6.0
        ir = np.concatenate([np.zeros(secs(sr, predelay)), ir])
        chans.append(ir / np.sqrt(np.sum(ir ** 2)))
    if stereo:
        m = max(len(c) for c in chans)
        return np.stack([np.pad(c, (0, m - len(c))) for c in chans], axis=1)
    return chans[0]


def convolve(x: np.ndarray, ir: np.ndarray) -> np.ndarray:
    """Linear convolution (output is len(x) + len(ir) - 1). Mono x with stereo ir -> stereo."""
    if x.ndim == 1 and ir.ndim == 1:
        return sg.fftconvolve(x, ir)
    if x.ndim == 1:
        return np.stack([sg.fftconvolve(x, ir[:, c]) for c in range(ir.shape[1])], axis=1)
    if ir.ndim == 1:
        return np.stack([sg.fftconvolve(x[:, c], ir) for c in range(x.shape[1])], axis=1)
    return np.stack([sg.fftconvolve(x[:, c], ir[:, c]) for c in range(x.shape[1])], axis=1)


def convolve_circular(x: np.ndarray, ir: np.ndarray) -> np.ndarray:
    """Circular convolution over len(x): the reverb tail of the end rings into the start (loops)."""
    n = len(x)
    y = convolve(x, ir)
    return fold(y, n)


def with_reverb(x: np.ndarray, sr: int, wet: float, ir: np.ndarray, circular: bool = False,
                dry: float = 1.0) -> np.ndarray:
    if circular:
        w = convolve_circular(x, ir)
    else:
        w = convolve(x, ir)
        x = np.concatenate([x, np.zeros((len(w) - len(x),) + x.shape[1:])])
    if x.ndim == 1 and w.ndim == 2:
        x = np.stack([x, x], axis=1)
    return x * dry + w * wet


# ---------------------------------------------------------------- tape character

def tape_wobble(x: np.ndarray, sr: int, depth_ms: float = 1.6, wow_hz: float = 0.35,
                flutter_ms: float = 0.06, flutter_hz: float = 5.8) -> np.ndarray:
    """Circular modulated delay (wow + a little flutter). Rates are snapped to whole cycles per loop."""
    n = len(x)
    dur = n / sr
    wow = max(1.0, round(wow_hz * dur)) / dur
    flt = max(1.0, round(flutter_hz * dur)) / dur
    t = tvec(sr, n)
    d = (depth_ms * 0.5 * (1.0 + np.sin(TAU * wow * t)) +
         flutter_ms * 0.5 * (1.0 + np.sin(TAU * flt * t + 1.1))) * 1e-3 * sr
    pos = np.arange(n) - d
    i0 = np.floor(pos).astype(np.int64)
    frac = pos - i0
    i0 %= n
    i1 = (i0 + 1) % n
    if x.ndim == 2:
        frac = frac[:, None]
    return x[i0] * (1.0 - frac) + x[i1] * frac


def tape_hiss(n: int, sr: int, rng, level_db: float = -52.0, stereo: bool = True) -> np.ndarray:
    """Periodic pink-ish hiss with an RMS of level_db dBFS."""
    def shape(f):
        return pink_shape(f) * band_shape(120.0, 9000.0, 1.0)(f)
    chans = [periodic_noise(n, sr, rng, shape) * db(level_db) for _ in range(2 if stereo else 1)]
    return np.stack(chans, axis=1) if stereo else chans[0]


def crackle(n: int, sr: int, rng, rate: float = 2.2, level_db: float = -44.0, stereo: bool = True) -> np.ndarray:
    """Sparse dust ticks (circular placement)."""
    out = np.zeros((n, 2)) if stereo else np.zeros(n)
    count = int(rate * n / sr)
    for _ in range(count):
        p = int(rng.integers(0, n))
        ln = int(rng.integers(2, 9))
        amp = db(level_db) * rng.uniform(0.25, 1.0) * (1.0 if rng.random() < 0.85 else 2.2)
        burst = rng.standard_normal(ln) * amp * np.exp(-np.arange(ln) / 2.0)
        idx = (p + np.arange(ln)) % n
        if stereo:
            g = rng.uniform(0.3, 1.0)
            ch = int(rng.integers(0, 2))
            out[idx, ch] += burst
            out[idx, 1 - ch] += burst * g
        else:
            out[idx] += burst
    return hp_circular(out, 900.0, sr, 2)


# ---------------------------------------------------------------- loudness

def lufs(x: np.ndarray, sr: int) -> float:
    import pyloudnorm as pyln
    meter = pyln.Meter(sr)
    return float(meter.integrated_loudness(x))


def to_lufs(x: np.ndarray, sr: int, target: float) -> np.ndarray:
    return x * db(target - lufs(x, sr))


def soft_limit(x: np.ndarray, ceiling_db: float = -1.0) -> np.ndarray:
    c = db(ceiling_db)
    m = float(np.max(np.abs(x)))
    if m <= c:
        return x
    # Gentle tanh knee above 70% of the ceiling.
    k = 0.7 * c
    a = np.abs(x)
    over = a > k
    y = x.copy()
    y[over] = np.sign(x[over]) * (k + (c - k) * np.tanh((a[over] - k) / (c - k)))
    return y


def energy_above(x: np.ndarray, sr: int, f0: float) -> float:
    m = x.mean(axis=1) if x.ndim == 2 else x
    spec = np.abs(np.fft.rfft(m)) ** 2
    f = np.fft.rfftfreq(len(m), 1.0 / sr)
    return float(spec[f >= f0].sum() / (spec.sum() + 1e-20))


# ---------------------------------------------------------------- io

def write_ogg(path: str, x: np.ndarray, sr: int, quality: float) -> int:
    """Writes Ogg Vorbis (quality 0..1). Returns the file size in bytes."""
    os.makedirs(os.path.dirname(path), exist_ok=True)
    x = np.clip(x, -1.0, 1.0).astype(np.float32)
    channels = 1 if x.ndim == 1 else x.shape[1]
    # Written in blocks: one huge write() crashes some libsndfile Vorbis builds.
    with sf.SoundFile(path, "w", samplerate=sr, channels=channels, format="OGG", subtype="VORBIS",
                      compression_level=float(1.0 - quality)) as f:
        for i in range(0, len(x), 16384):
            f.write(x[i:i + 16384])
    return os.path.getsize(path)


def read_mono(path: str, sr_out: int | None = None):
    d, sr = sf.read(path, always_2d=True)
    m = d.mean(axis=1)
    if sr_out and sr_out != sr:
        m = sg.resample_poly(m, sr_out, sr)
        sr = sr_out
    return m, sr


# ---------------------------------------------------------------- instruments

_TAB_N = 4096


def saw_table(f0: float, sr: int, cutoff: float, slope: float = 2.0, max_h: int = 160) -> np.ndarray:
    """One cycle of a band-limited saw, pre-filtered by a Butterworth-like curve at `cutoff`."""
    nh = int(min(max_h, (sr * 0.45) // f0))
    ph = np.arange(_TAB_N) / _TAB_N
    tab = np.zeros(_TAB_N)
    for k in range(1, nh + 1):
        a = (1.0 / k) / np.sqrt(1.0 + ((k * f0) / cutoff) ** (2 * slope))
        if a < 1e-5:
            break
        tab += a * np.sin(TAU * k * ph)
    return tab / (np.max(np.abs(tab)) + 1e-12)


def table_osc(tab: np.ndarray, freq: np.ndarray | float, n: int, sr: int, phase0: float = 0.0) -> np.ndarray:
    if np.isscalar(freq):
        ph = phase0 + np.arange(n) * (freq / sr)
    else:
        ph = phase0 + np.cumsum(freq) / sr
    idx = (ph % 1.0) * _TAB_N
    i0 = idx.astype(np.int64)
    fr = idx - i0
    return tab[i0 % _TAB_N] * (1.0 - fr) + tab[(i0 + 1) % _TAB_N] * fr


def env_ar(n: int, sr: int, attack: float, hold: float, release: float, curve: float = 1.0) -> np.ndarray:
    """Attack (smoothstep) -> sustain until `hold` seconds -> smooth release."""
    t = tvec(sr, n)
    a = smoothstep(t / max(attack, 1e-4))
    r = 1.0 - smoothstep((t - hold) / max(release, 1e-4))
    return (a * r) ** curve


def pad_note(f0: float, dur: float, sr: int, rng, cutoff: float = 1700.0, attack: float = 1.6,
             release: float = 2.4, voices=(-7.0, 0.0, 7.0), pans=(-0.6, 0.0, 0.6), level: float = 1.0) -> np.ndarray:
    """Soft analogue-style pad note (stereo): detuned filtered saws with slow drift."""
    n = secs(sr, dur + release)
    out = np.zeros((n, 2))
    tab = saw_table(f0, sr, cutoff)
    t = tvec(sr, n)
    for det, pan in zip(voices, pans):
        drift = cents(det + 2.5 * np.sin(TAU * rng.uniform(0.07, 0.17) * t + rng.uniform(0, TAU)))
        v = table_osc(tab, f0 * drift, n, sr, rng.uniform(0, 1))
        out += pan2(v, pan)
    e = env_ar(n, sr, attack, dur, release)
    breath = 1.0 + 0.06 * np.sin(TAU * rng.uniform(0.1, 0.2) * t + rng.uniform(0, TAU))
    return out * (e * breath * level / len(voices))[:, None]


def felt_piano(f0: float, dur: float, vel: float, sr: int, rng, bright: float = 1.0,
               pan: float = 0.0, tail: float = 3.0) -> np.ndarray:
    """Felt-muted upright: inharmonic partials, two detuned strings, soft attack, felt thump."""
    n = secs(sr, dur + tail)
    t = tvec(sr, n)
    x = np.zeros(n)
    B = 0.00035
    t0 = float(np.clip(5.5 * np.sqrt(261.6 / f0), 1.6, 7.5))
    fc = (650.0 + 2200.0 * vel) * bright
    for k in range(1, 16):
        fk = k * f0 * np.sqrt(1.0 + B * k * k)
        if fk > sr * 0.45:
            break
        ak = (k ** -1.45) / np.sqrt(1.0 + (fk / fc) ** 4)
        if ak < 2e-4:
            break
        t60 = max(0.18, t0 / (1.0 + 0.55 * (k - 1)))
        dec = np.exp(-6.91 * t / t60)
        det = 1.0 + rng.uniform(0.0003, 0.0009)
        ph = rng.uniform(0, TAU)
        x += ak * dec * 0.5 * (np.sin(TAU * fk * t + ph) + np.sin(TAU * fk * det * t + ph * 0.7))
    x *= 1.0 - np.exp(-t / 0.0045)
    # Damper: notes are released after `dur`.
    x *= np.where(t > dur, np.exp(-6.91 * (t - dur) / 0.45), 1.0)
    # Felt thump.
    th = secs(sr, 0.03)
    thump = lp(rng.standard_normal(th), 260.0, sr, 2) * np.exp(-np.arange(th) / (0.006 * sr)) * 0.35
    x[:th] += thump
    x *= vel
    return pan2(x, pan)


def kalimba(f0: float, vel: float, sr: int, rng, pan: float = 0.0, length: float = 2.4) -> np.ndarray:
    """Kalimba tine: sine with a tiny pitch drop, inharmonic tine overtone and a soft click."""
    n = secs(sr, length)
    t = tvec(sr, n)
    f = f0 * (1.0 + 0.004 * np.exp(-t / 0.02))
    ph = TAU * np.cumsum(f) / sr
    x = (np.sin(ph) * np.exp(-t / 0.55)
         + 0.30 * np.sin(5.4 * ph + 0.3) * np.exp(-t / 0.05)
         + 0.10 * np.sin(2.0 * ph) * np.exp(-t / 0.22)
         + 0.06 * np.sin(12.4 * ph) * np.exp(-t / 0.012))
    x *= 1.0 - np.exp(-t / 0.0015)
    x *= vel
    return pan2(x, pan)


def sub_bass(f0: float, dur: float, sr: int, level: float = 1.0, attack: float = 0.35,
             release: float = 1.2) -> np.ndarray:
    n = secs(sr, dur + release)
    t = tvec(sr, n)
    x = np.sin(TAU * f0 * t) + 0.18 * np.sin(TAU * 2 * f0 * t + 0.4) + 0.05 * np.sin(TAU * 3 * f0 * t)
    x *= env_ar(n, sr, attack, dur, release) * level
    return pan2(x, 0.0)


def glass(f0: float, sr: int, length: float = 2.5, decay: float = 1.4, shimmer: float = 6.0,
          rng=None) -> np.ndarray:
    """Glassy bell (mono): rubbed-glass partial ratios with a slow tremolo shimmer."""
    n = secs(sr, length)
    t = tvec(sr, n)
    ratios = (1.0, 2.32, 4.25, 6.63)
    amps = (1.0, 0.35, 0.16, 0.07)
    x = np.zeros(n)
    for r, a in zip(ratios, amps):
        if f0 * r > sr * 0.45:
            continue
        x += a * np.sin(TAU * f0 * r * t) * np.exp(-6.91 * t / (decay * (1.6 / (r ** 0.5))))
    x *= 1.0 + 0.25 * np.sin(TAU * shimmer * t)
    x *= 1.0 - np.exp(-t / 0.004)
    return x


def bell(f0: float, sr: int, length: float = 3.0, decay: float = 2.5) -> np.ndarray:
    """Soft low bell (mono): slightly inharmonic partials."""
    n = secs(sr, length)
    t = tvec(sr, n)
    parts = ((0.5, 0.45, 1.2), (1.0, 1.0, 1.0), (1.19, 0.4, 0.8), (1.56, 0.25, 0.6), (2.0, 0.3, 0.55),
             (2.51, 0.12, 0.4), (3.01, 0.08, 0.3))
    x = np.zeros(n)
    for r, a, dm in parts:
        x += a * np.sin(TAU * f0 * r * t) * np.exp(-6.91 * t / (decay * dm))
    x *= 1.0 - np.exp(-t / 0.002)
    return x
