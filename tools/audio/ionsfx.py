#!/usr/bin/env python3
"""[project]ion SFX generator: renders every sound effect into Assets/Resources/Audio/Sfx.

Direction (art bible §8): mechanical sounds (shutter, tape, switches) are tactile and close; place,
rewind, pickup and checkpoint sounds land musically in D lydian (G# is the colour note). Reverb is
baked in. Mono, 44.1 kHz, Ogg Vorbis, peaks at -6 dBFS (IonAudio sets the per-sound mix level).

Everything is synthesised here except the footsteps and three impact layers, which come from
Kenney "Impact Sounds" (CC0 1.0, https://kenney.nl/assets/impact-sounds), committed unmodified in
sources/kenney_impact_sounds/ and converted to mono, trimmed and filtered below.

Loops (*_loop) are exact loops: every periodic component has a whole number of cycles over the loop
and their reverb wraps around, so AudioSource.loop is seamless.

Usage:  python3 tools/audio/ionsfx.py [name ...]      (default: everything)
"""
from __future__ import annotations

import os
import sys
import time

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import ionlib as L  # noqa: E402

SR = 44100
HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
OUT_DIR = os.path.join(ROOT, "Assets", "Resources", "Audio", "Sfx")
KENNEY = os.path.join(HERE, "sources", "kenney_impact_sounds")
PEAK_DB = -6.0
QUALITY = 0.5

_ROOM = None
_HALL = None


def room_ir():
    """Small, close room (mechanical sounds)."""
    global _ROOM
    if _ROOM is None:
        _ROOM = L.reverb_ir(SR, t60=0.45, t60_hi=0.25, predelay=0.004, seed=3, stereo=False)
    return _ROOM


def hall_ir():
    """The long 3.6 s room shared with the music (musical sounds)."""
    global _HALL
    if _HALL is None:
        _HALL = L.reverb_ir(SR, t60=3.6, t60_hi=1.5, predelay=0.022, seed=11, stereo=False)
    return _HALL


def rng(seed):
    return np.random.default_rng(seed)


def buf(seconds):
    return np.zeros(L.secs(SR, seconds))


def at(b, x, t):
    L.add_at(b, x, int(round(t * SR)))


def t_(n):
    return L.tvec(SR, n)


def mono(x):
    return x.mean(axis=1) if x.ndim == 2 else x


def finish(x, wet=0.0, ir=None, loop=False, trim=True):
    if wet > 0.0:
        x = L.with_reverb(x, SR, wet, ir, circular=loop)
    if loop:
        return L.normalize_peak(x, PEAK_DB)
    x = L.trim_tail(x, SR, -60.0) if trim else x
    return L.normalize_peak(L.edge_fade(x, SR, 0.0005, 0.01), PEAK_DB)


def kenney(name):
    x, _ = L.read_mono(os.path.join(KENNEY, name + ".ogg"), SR)
    a = np.abs(x)
    start = int(np.argmax(a > a.max() * L.db(-40.0)))
    return x[max(0, start - L.secs(SR, 0.002)):]


# ---------------------------------------------------------------- building blocks

def click(r, freq=2400.0, decay=0.004, ping_decay=0.012, amp=1.0, length=0.04, noise=0.7, ping=0.6):
    n = L.secs(SR, length)
    t = t_(n)
    w = r.standard_normal(n)
    w = np.diff(np.concatenate([[0.0], w]))  # crude high-pass
    x = noise * w * np.exp(-t / decay) + ping * np.sin(L.TAU * freq * t) * np.exp(-t / ping_decay)
    return x * amp


def thump(r, f0=110.0, decay=0.05, amp=1.0, length=0.25, noise_fc=260.0):
    n = L.secs(SR, length)
    t = t_(n)
    f = f0 * (0.7 + 0.3 * np.exp(-t / 0.02))
    body = np.sin(L.TAU * np.cumsum(f) / SR) * np.exp(-t / decay)
    scuff = L.lp(r.standard_normal(n), noise_fc, SR, 2) * np.exp(-t / (decay * 0.6)) * 2.0
    return (body * 0.8 + scuff * 0.5) * (1.0 - np.exp(-t / 0.001)) * amp


def whoosh(r, dur, peak_at, f_lo, f_hi, amp=1.0, band_hp=0.0):
    n = L.secs(SR, dur)
    u = np.arange(n) / n
    e = np.where(u < peak_at, u / max(peak_at, 1e-4), 1.0 - (u - peak_at) / max(1.0 - peak_at, 1e-4))
    e = L.smoothstep(e)
    x = L.sweep_lp(r.standard_normal(n), f_lo + (f_hi - f_lo) * e, SR) * 3.0
    if band_hp:
        x = L.hp(x, band_hp, SR, 2)
    return x * e * amp


def paper(r, dur, f_from, f_to, density=0.004, amp=1.0):
    """Paper slide/rustle: band noise made of crackle grains, cutoff sweeping f_from -> f_to."""
    n = L.secs(SR, dur)
    u = np.arange(n) / n
    grains = np.zeros(n)
    g = 0.0
    trig = r.random(n) < density * (1.2 - u)
    amps = r.uniform(0.4, 1.0, n)
    for i in range(n):
        if trig[i]:
            g = amps[i]
        g *= 0.9965
        grains[i] = g
    w = r.standard_normal(n)
    band = L.hp(L.sweep_lp(w, f_from + (f_to - f_from) * u, SR), 700.0, SR, 2)
    env = np.minimum(1.0, u * 14.0) * (1.0 - u) ** 1.6
    return band * (0.3 + grains) * env * amp * 3.0


def kal(note, vel=1.0, length=2.0, seed=0):
    return mono(L.kalimba(L.hz(note), vel, SR, rng(seed), length=length))


def piano(note, dur, vel, bright=1.0, seed=0, tail=1.5):
    return mono(L.felt_piano(L.hz(note), dur, vel, SR, rng(seed), bright=bright, tail=tail))


def tape_spool(r, dur, f_curve, amp=1.0):
    """Tape rewinding past the heads: chattering, pitch-following buzz + hiss."""
    n = L.secs(SR, dur)
    u = np.arange(n) / n
    f = f_curve(u)
    ph = np.cumsum(f) / SR
    saw = 2.0 * (ph % 1.0) - 1.0
    chatter = 0.6 + 0.4 * np.sign(np.sin(L.TAU * ph * 0.125))
    tone = L.bp(saw * chatter, 300.0, 3500.0, SR, 2)
    hiss = L.bp(r.standard_normal(n), 2000.0, 9000.0, SR, 2) * 0.35
    env = np.minimum(1.0, u * 12.0) * np.minimum(1.0, (1.0 - u) * 10.0)
    return (tone * 0.8 + hiss) * env * amp


def periodic_tone(freq, n, phase=0.0):
    return np.sin(L.TAU * freq * t_(n) + phase)


# ---------------------------------------------------------------- photo + camera

def photo_raise():
    r = rng(101)
    b = buf(0.5)
    at(b, paper(r, 0.42, 1500.0, 6500.0, amp=1.0), 0.0)
    at(b, thump(r, 160.0, 0.02, 0.25, 0.08, 400.0), 0.01)
    return finish(b, 0.12, room_ir())


def photo_lower():
    r = rng(102)
    b = buf(0.34)
    at(b, paper(r, 0.28, 5200.0, 1300.0, amp=0.8), 0.0)
    return finish(b, 0.1, room_ir())


def photo_place():
    r = rng(103)
    b = buf(2.6)
    at(b, thump(r, 105.0, 0.045, 1.0, 0.25, 240.0), 0.0)                 # press-in "thock"
    at(b, whoosh(r, 0.42, 0.25, 300.0, 5200.0, amp=0.45), 0.0)          # projection air
    at(b, kal("D5", 0.7, seed=1), 0.10)                                   # world swap at 0.10 s
    at(b, kal("A5", 0.55, seed=2), 0.10)
    at(b, kal("C#6", 0.42, seed=3), 0.17)
    at(b, L.glass(L.hz("G#6"), SR, 1.6, 1.1) * 0.12, 0.22)               # colour-note sparkle
    return finish(b, 0.32, hall_ir())


def photo_rewind():
    r = rng(104)
    b = buf(2.2)
    at(b, tape_spool(r, 0.5, lambda u: 260.0 + 900.0 * np.sin(np.pi * u) ** 0.7, amp=0.35), 0.0)
    swell = buf(0.24)
    at(swell, kal("A5", 0.6, length=0.24, seed=4), 0.0)
    at(swell, kal("F#5", 0.5, length=0.24, seed=5), 0.0)
    swell = swell[::-1] * np.linspace(0.2, 1.0, len(swell))               # reversed swell into the swap
    at(b, swell, 0.0)
    at(b, whoosh(r, 0.3, 0.75, 300.0, 3000.0, amp=0.3)[::-1], 0.0)
    at(b, kal("D5", 0.55, seed=6), 0.22)                                  # lands at the swap (0.22 s)
    at(b, piano("A4", 0.3, 0.35, seed=7), 0.225)
    return finish(b, 0.3, hall_ir())


def camera_shutter():
    r = rng(105)
    b = buf(1.0)
    at(b, click(r, 2600.0, 0.003, 0.010, 1.0), 0.0)                       # blades close
    at(b, click(r, 1900.0, 0.003, 0.014, 0.75), 0.05)                     # and spring open
    at(b, np.sin(L.TAU * 3150.0 * t_(L.secs(SR, 0.12))) * np.exp(-t_(L.secs(SR, 0.12)) / 0.03) * 0.08, 0.052)
    # Print eject: small geared motor 0.12 -> 0.62 s.
    n = L.secs(SR, 0.5)
    t = t_(n)
    u = t / t[-1]
    f = 118.0 + 10.0 * np.sin(u * 3.0) - 18.0 * u
    ph = np.cumsum(f) / SR
    saw = 2.0 * (ph % 1.0) - 1.0
    motor = L.lp(saw, 1500.0, SR, 2) * (0.65 + 0.35 * np.sin(L.TAU * 31.0 * t))
    motor += L.lp(r.standard_normal(n), 2600.0, SR, 2) * 0.25
    motor *= np.minimum(1.0, u * 10.0) * np.minimum(1.0, (1.0 - u) * 6.0) * 0.45
    at(b, motor, 0.12)
    at(b, click(r, 900.0, 0.004, 0.02, 0.35), 0.62)                       # print clears the slot
    return finish(b, 0.08, room_ir())


def camera_empty():
    r = rng(106)
    b = buf(0.3)
    at(b, L.lp(click(r, 1400.0, 0.004, 0.01, 1.0), 2500.0, SR, 2), 0.0)
    at(b, thump(r, 140.0, 0.025, 0.5, 0.1, 500.0), 0.004)
    return finish(b, 0.08, room_ir())


def photo_pickup():
    r = rng(107)
    b = buf(2.2)
    at(b, paper(r, 0.12, 4000.0, 2200.0, density=0.006, amp=0.6), 0.0)
    at(b, kal("D5", 0.6, seed=11), 0.0)
    at(b, kal("F#5", 0.55, seed=12), 0.07)
    at(b, kal("A5", 0.55, seed=13), 0.14)
    at(b, L.glass(L.hz("G#6"), SR, 1.4, 1.0) * 0.08, 0.3)
    return finish(b, 0.3, hall_ir())


def photo_rotate():
    r = rng(108)
    b = buf(0.08)
    at(b, L.bp(click(r, 3200.0, 0.0015, 0.004, 1.0, 0.03), 1800.0, 6000.0, SR, 2), 0.0)
    at(b, L.bp(click(r, 2700.0, 0.0015, 0.004, 0.55, 0.03), 1800.0, 6000.0, SR, 2), 0.012)
    return finish(b, 0.05, room_ir())


# ---------------------------------------------------------------- teleporter

def teleporter_hum_loop():
    loop = 4.0
    n = L.secs(SR, loop)
    r = rng(109)
    # Every frequency is k / 4 Hz, so each completes whole cycles over the 4 s loop.
    x = (periodic_tone(73.5, n) * 0.5 + periodic_tone(73.75, n, 0.5) * 0.5        # D2, 0.25 Hz beat
         + periodic_tone(110.0, n, 1.0) * 0.35 + periodic_tone(110.25, n, 2.0) * 0.3
         + periodic_tone(147.0, n, 0.3) * 0.12 + periodic_tone(220.0, n, 0.7) * 0.07)
    shimmer = periodic_tone(415.25, n) * (0.5 + 0.5 * periodic_tone(0.5, n)) * 0.05  # G#4, the colour note
    air = L.periodic_noise(n, SR, r, L.band_shape(1800.0, 5500.0, 2.0)) * 0.035
    air *= 0.6 + 0.4 * periodic_tone(0.75, n, 0.4)
    x = x * (0.85 + 0.15 * periodic_tone(0.25, n)) + shimmer + air
    return finish(x, 0.15, room_ir(), loop=True)


def teleport_travel():
    r = rng(110)
    b = buf(3.2)
    scale = ["D4", "F#4", "A4", "C#5", "E5", "G#5", "A5", "D6"]
    for i, note in enumerate(scale):
        at(b, kal(note, 0.25 + 0.05 * i, length=1.0, seed=20 + i), 0.04 * i)
    at(b, whoosh(r, 0.75, 0.55, 300.0, 8000.0, amp=0.5), 0.0)             # fade out to Frost (0.40 s)
    at(b, L.bell(L.hz("D5"), SR, 2.5, 2.0) * 0.18, 0.55)                    # arrival as it fades in
    at(b, L.glass(L.hz("G#6"), SR, 2.0, 1.4) * 0.07, 0.6)
    at(b, L.glass(L.hz("A6"), SR, 2.0, 1.4) * 0.05, 0.62)
    return finish(b, 0.32, hall_ir())


# ---------------------------------------------------------------- UI

def ui_hover():
    n = L.secs(SR, 0.07)
    t = t_(n)
    x = np.sin(L.TAU * L.hz("G#6") * t) * np.exp(-t / 0.012) * (1.0 - np.exp(-t / 0.0008))
    return finish(x, 0.05, room_ir())


def ui_click():
    r = rng(112)
    b = buf(0.22)
    at(b, L.bp(click(r, 2200.0, 0.002, 0.006, 1.0, 0.03), 900.0, 7000.0, SR, 2), 0.0)
    n = L.secs(SR, 0.2)
    t = t_(n)
    at(b, np.sin(L.TAU * L.hz("D6") * t) * np.exp(-t / 0.035) * 0.5, 0.004)
    return finish(b, 0.06, room_ir())


# ---------------------------------------------------------------- ambience

def amb_wind_loop():
    loop = 12.0
    n = L.secs(SR, loop)
    r = rng(113)

    def lfo(cycles, phase):
        return np.sin(L.TAU * cycles / loop * t_(n) + phase)

    gust = np.clip(0.55 + 0.25 * lfo(1, 0.0) + 0.14 * lfo(2, 1.3) + 0.06 * lfo(5, 0.4), 0.0, 1.0)
    low = L.periodic_noise(n, SR, r, L.band_shape(40.0, 220.0, 1.5)) * (0.6 + 0.6 * gust)
    mid = L.periodic_noise(n, SR, r, L.band_shape(300.0, 900.0, 1.5)) * gust ** 2 * 0.6
    high = L.periodic_noise(n, SR, r, L.band_shape(900.0, 2600.0, 1.5)) * gust ** 3 * 0.35
    return finish(low + mid + high, 0.0, None, loop=True)


def amb_bird(seed):
    r = rng(seed)
    syll = int(r.integers(2, 6))
    base = r.uniform(2300.0, 3600.0)
    gap, syl_len = r.uniform(0.05, 0.11), r.uniform(0.05, 0.09)
    b = buf(syll * (syl_len + gap) + 0.1)
    t0 = 0.01
    for _ in range(syll):
        f0 = base * r.uniform(0.85, 1.1)
        f1 = f0 * r.uniform(1.15, 1.6) * (0.6 if r.random() < 0.3 else 1.0)
        dur = syl_len * r.uniform(0.8, 1.2)
        n = L.secs(SR, dur)
        u = np.arange(n) / n
        vib = r.uniform(25.0, 45.0)
        f = (f0 + (f1 - f0) * L.smoothstep(u)) * (1.0 + 0.03 * np.sin(L.TAU * vib * t_(n)))
        ph = L.TAU * np.cumsum(f) / SR
        env = np.sin(u * np.pi) ** 2
        at(b, (np.sin(ph) + 0.12 * np.sin(2 * ph)) * env * r.uniform(0.6, 1.0), t0)
        t0 += dur + gap * r.uniform(0.7, 1.3)
    outdoor = L.reverb_ir(SR, t60=0.9, t60_hi=0.5, predelay=0.03, seed=seed, stereo=False)
    return finish(b, 0.18, outdoor)


# ---------------------------------------------------------------- switches, movers

def switch_press():
    r = rng(114)
    b = buf(0.4)
    at(b, kenney("impactMetal_light_000") * 0.35, 0.0)
    at(b, click(r, 1250.0, 0.003, 0.007, 0.9), 0.0)                      # plastic cap bottoming out
    at(b, thump(r, 135.0, 0.045, 0.8, 0.2, 380.0), 0.002)                # the body of the button
    at(b, click(r, 2100.0, 0.002, 0.008, 0.35), 0.016)                   # contact snap
    return finish(b, 0.08, room_ir())


def switch_release():
    r = rng(115)
    b = buf(0.3)
    at(b, click(r, 1750.0, 0.002, 0.016, 0.8), 0.0)
    n = L.secs(SR, 0.09)
    t = t_(n)
    at(b, np.sin(L.TAU * (420.0 + 120.0 * np.exp(-t / 0.02)) * t) * np.exp(-t / 0.025) * 0.15, 0.004)  # spring
    at(b, thump(r, 180.0, 0.02, 0.3, 0.1, 600.0), 0.0)
    return finish(b, 0.08, room_ir())


def switch_denied():
    r = rng(116)
    b = buf(0.32)
    at(b, L.lp(click(r, 900.0, 0.004, 0.012, 1.0), 900.0, SR, 2), 0.0)
    at(b, thump(r, 120.0, 0.03, 0.7, 0.12, 300.0), 0.0)
    n = L.secs(SR, 0.09)
    t = t_(n)
    buzz = np.sign(np.sin(L.TAU * 98.0 * t)) * np.exp(-t / 0.03) * 0.08
    at(b, L.lp(buzz, 700.0, SR, 2), 0.03)
    return finish(b, 0.06, room_ir())


def mover_loop():
    loop = 3.0
    n = L.secs(SR, loop)
    r = rng(117)
    t = t_(n)
    f = 179.0 / 3.0  # 59.67 Hz: whole cycles per loop
    ph = f * t
    saw = 2.0 * (ph % 1.0) - 1.0
    motor = L.lp_circular(saw, 420.0, SR, 2) * 0.55 + periodic_tone(2 * f, n) * 0.12
    # Gear ticks: 8 per second.
    ticks = np.zeros(n)
    for k in range(24):
        c = click(r, 1500.0 + 300.0 * (k % 3), 0.002, 0.006, 0.25 if k % 2 else 0.4, 0.02)
        idx = (int(k * n / 24) + np.arange(len(c))) % n
        ticks[idx] += c
    scrape = L.periodic_noise(n, SR, r, L.band_shape(250.0, 1400.0, 1.5)) * 0.12
    scrape *= 0.8 + 0.2 * np.sin(L.TAU * t / loop)
    return finish(motor + L.bp(ticks, 700.0, 5000.0, SR, 2) + scrape, 0.12, room_ir(), loop=True)


def mover_stop():
    r = rng(118)
    b = buf(0.9)
    at(b, L.lp(kenney("impactWood_heavy_000"), 2200.0, SR, 2) * 0.7, 0.0)
    at(b, thump(r, 72.0, 0.08, 1.0, 0.4, 220.0), 0.0)
    for k, tt in enumerate((0.07, 0.12, 0.2, 0.27)):
        at(b, click(r, 1100.0 + 250.0 * k, 0.003, 0.01, 0.25 / (k + 1)), tt)  # settle rattle
    return finish(b, 0.12, room_ir())


def collapse_crack():
    r = rng(119)
    b = buf(0.8)
    at(b, kenney("impactPlank_medium_000") * 0.6, 0.0)
    at(b, kenney("impactPlank_medium_001") * 0.4, 0.09)
    tt = 0.0
    for k in range(9):
        tt += r.uniform(0.012, 0.05) * (1.0 - k / 12.0)
        c = L.bp(click(r, r.uniform(1500.0, 3800.0), 0.002, 0.006, r.uniform(0.3, 0.8), 0.03), 900.0, 6000.0, SR, 2)
        at(b, c, tt)
    n = L.secs(SR, 0.5)
    t = t_(n)
    groan = np.sin(L.TAU * np.cumsum(92.0 - 22.0 * t / t[-1]) / SR) * np.exp(-t / 0.15) * 0.3
    at(b, groan, 0.02)
    return finish(b, 0.12, room_ir())


def collapse_fall():
    r = rng(120)
    n = L.secs(SR, 1.7)
    t = t_(n)
    u = t / t[-1]
    rumble = L.sweep_lp(r.standard_normal(n), 1600.0 * (1.0 - u) ** 2 + 120.0, SR) * 3.0
    rumble *= np.minimum(1.0, u * 8.0) * (1.0 - u) ** 1.5
    debris = np.zeros(n)
    for _ in range(26):
        p = int(r.uniform(0.0, 0.7) * n)
        c = L.lp(click(r, r.uniform(500.0, 1500.0), 0.003, 0.01, r.uniform(0.1, 0.4), 0.03), 2500.0, SR, 2)
        L.add_at(debris, c * (1.0 - p / n), p)
    return finish(rumble + debris, 0.25, hall_ir())


def fall_whoosh():
    r = rng(121)
    n = L.secs(SR, 1.5)
    u = np.arange(n) / n
    cutoff = 300.0 + 2700.0 * L.smoothstep(u * 1.4)
    x = L.sweep_lp(r.standard_normal(n), cutoff, SR) * 3.0
    x = L.hp(x, 120.0, SR, 2)
    env = L.smoothstep(u * 1.6) * np.minimum(1.0, (1.0 - u) * 4.0)
    flutter = 1.0 + 0.25 * np.sin(L.TAU * 7.0 * u * 1.5) * u
    return finish(x * env * flutter, 0.1, room_ir())


def limbo_drone_loop():
    loop = 8.0
    n = L.secs(SR, loop)
    r = rng(122)
    # k / 8 Hz components: whole cycles over the 8 s loop; detuned pairs beat once per loop.
    x = (periodic_tone(73.375, n) * 0.5 + periodic_tone(73.5, n, 1.0) * 0.45            # D2
         + periodic_tone(110.0, n, 0.3) * 0.3 + periodic_tone(110.125, n, 2.0) * 0.25    # A2
         + periodic_tone(207.625, n, 0.9) * 0.08 * (0.5 + 0.5 * periodic_tone(0.25, n)))  # G#3, faint
    x *= 0.8 + 0.2 * periodic_tone(0.125, n)
    air = L.periodic_noise(n, SR, r, L.band_shape(200.0, 800.0, 1.5)) * 0.06
    air *= 0.7 + 0.3 * periodic_tone(0.375, n, 1.0)
    return finish(x + air, 0.35, hall_ir(), loop=True)


# ---------------------------------------------------------------- rewind / checkpoints / hub

def rewind_nothing():
    r = rng(123)
    b = buf(1.2)
    at(b, L.lp(click(r, 1200.0, 0.003, 0.008, 0.6), 1800.0, SR, 2), 0.0)   # soft muted tick
    at(b, piano("A4", 0.12, 0.32, bright=0.55, seed=31, tail=0.6), 0.05)    # falling minor second
    at(b, piano("G#4", 0.2, 0.28, bright=0.5, seed=32, tail=0.7), 0.2)
    return finish(b, 0.16, hall_ir())


def rewind_checkpoint():
    r = rng(124)
    b = buf(4.2)

    def curve(u):  # spools up, then brakes to a stop at 0.9 s
        return 180.0 + 1500.0 * np.sin(np.pi * np.clip(u * 1.15, 0.0, 1.0)) ** 0.6

    at(b, tape_spool(r, 0.9, curve, amp=0.4), 0.0)
    at(b, whoosh(r, 0.45, 0.85, 250.0, 3500.0, amp=0.3)[::-1], 0.0)
    at(b, L.bell(L.hz("D3"), SR, 3.6, 3.0) * 0.55, 0.40)                    # swap at 0.40 s
    at(b, kal("D5", 0.35, seed=41), 0.42)
    at(b, kal("A4", 0.3, seed=42), 0.42)
    return finish(b, 0.3, hall_ir())


def rewind_tape_loop():
    """The rewind glide's tape: an exact 1 s loop of tape chattering past the heads at a steady speed.
    IonAudio plays it with AudioSource.pitch following the glide's speed (slow -> fast -> settle), so its
    pitch and its chatter rate rise and fall together, like a real tape being rewound."""
    loop = 1.0
    n = L.secs(SR, loop)
    r = rng(127)
    t = t_(n)
    ph = 300.0 * t                                                         # 300 whole cycles per loop
    saw = 2.0 * (ph % 1.0) - 1.0
    chatter = 0.62 + 0.38 * np.sign(np.sin(L.TAU * 12.0 * t))              # 12 whole cycles: the spool's flutter
    tone = L.lp_circular(L.hp_circular(saw * chatter, 300.0, SR, 2), 3500.0, SR, 2)
    whir = periodic_tone(150.0, n) * 0.18 + periodic_tone(450.0, n, 0.7) * 0.06
    hiss = L.periodic_noise(n, SR, r, L.band_shape(2000.0, 9000.0, 2.0)) * 0.22
    x = (tone * 0.8 + whir) * (0.86 + 0.14 * periodic_tone(4.0, n)) + hiss
    return finish(x, 0.08, room_ir(), loop=True)


def rewind_settle():
    """The glide settling: the tape stops with a soft clunk and lands in key (D5 + A4 kalimba, F#4 felt piano)."""
    r = rng(128)
    b = buf(2.2)
    at(b, click(r, 900.0, 0.004, 0.012, 0.35, 0.05), 0.0)                  # the tape stops
    at(b, thump(r, 96.0, 0.04, 0.3, 0.2, 240.0), 0.0)
    at(b, kal("D5", 0.42, seed=61), 0.01)
    at(b, kal("A4", 0.32, seed=62), 0.03)
    at(b, piano("F#4", 0.3, 0.22, seed=63), 0.05)
    x = finish(b, 0.3, hall_ir())
    return L.edge_fade(x[:L.secs(SR, 2.4)], SR, 0.0, 0.6)                 # the tail fades by 2.4 s (download size)


def checkpoint_set():
    b = buf(2.0)
    at(b, kal("D5", 0.7, seed=51), 0.0)
    at(b, kal("A5", 0.6, seed=52), 0.13)
    return finish(b, 0.32, hall_ir())


def exhibit_wake():
    b = buf(2.6)
    n = L.secs(SR, 2.4)
    t = t_(n)
    swell = 1.0 - np.exp(-t / 0.25)
    at(b, L.glass(L.hz("G#5"), SR, 2.4, 2.0, shimmer=6.5)[:n] * swell * 0.5, 0.0)
    at(b, L.glass(L.hz("G#6"), SR, 2.4, 1.6, shimmer=7.3)[:n] * swell * 0.25, 0.08)
    at(b, L.glass(L.hz("D6"), SR, 2.0, 1.4, shimmer=5.1) * 0.12, 0.25)
    return finish(b, 0.35, hall_ir())


def hatch_rise():
    r = rng(125)
    b = buf(2.4)
    n = L.secs(SR, 1.6)
    t = t_(n)
    u = t / t[-1]
    f = 70.0 + 45.0 * L.smoothstep(u * 1.5) - 30.0 * L.smoothstep((u - 0.8) * 5.0)
    ph = np.cumsum(f) / SR
    motor = L.lp(2.0 * (ph % 1.0) - 1.0, 500.0, SR, 2) * 0.5
    slide = L.bp(r.standard_normal(n), 200.0, 1300.0, SR, 2) * 0.35
    env = L.smoothstep(u * 5.0) * (1.0 - L.smoothstep((u - 0.85) * 6.6) * 0.8)
    at(b, (motor + slide) * env, 0.0)
    at(b, L.lp(kenney("impactWood_heavy_000"), 1800.0, SR, 2) * 0.6, 1.58)
    at(b, thump(r, 68.0, 0.09, 1.0, 0.45, 200.0), 1.58)
    return finish(b, 0.15, room_ir())


def land():
    r = rng(126)
    b = buf(0.3)
    at(b, thump(r, 72.0, 0.06, 1.0, 0.3, 180.0), 0.0)
    return finish(b, 0.06, room_ir())


# ---------------------------------------------------------------- footsteps (Kenney CC0)

FOOTSTEP_SETS = {
    # set: (kenney source, high-pass Hz, low-pass Hz, max seconds)
    "tile": ("footstep_concrete", 260.0, 9000.0, 0.2),   # contact-sheet floors: the concrete set high-passed
    "stone": ("footstep_concrete", 70.0, 5500.0, 0.2),   # terraces, concrete, plaster
    "wood": ("footstep_wood", 80.0, 8000.0, 0.26),
    "grass": ("footstep_grass", 150.0, 7000.0, 0.34),
}


def footstep(set_name, i):
    src, hpf, lpf, max_len = FOOTSTEP_SETS[set_name]
    x = kenney(f"{src}_{i:03d}")
    x = x[:L.secs(SR, max_len)]
    x = L.lp(L.hp(x, hpf, SR, 2), lpf, SR, 2)
    x = L.trim_tail(x, SR, -50.0)
    return L.normalize_peak(L.edge_fade(x, SR, 0.0005, 0.03), PEAK_DB)


# ---------------------------------------------------------------- registry

SFX = {
    "photo_raise": photo_raise, "photo_lower": photo_lower, "photo_place": photo_place,
    "photo_rewind": photo_rewind, "photo_rotate": photo_rotate,
    "camera_shutter": camera_shutter, "camera_empty": camera_empty, "photo_pickup": photo_pickup,
    "teleporter_hum_loop": teleporter_hum_loop, "teleport_travel": teleport_travel,
    "ui_hover": ui_hover, "ui_click": ui_click,
    "amb_wind_loop": amb_wind_loop,
    "amb_bird_0": lambda: amb_bird(211), "amb_bird_1": lambda: amb_bird(105040),
    "amb_bird_2": lambda: amb_bird(209669), "amb_bird_3": lambda: amb_bird(314398),
    "switch_press": switch_press, "switch_release": switch_release, "switch_denied": switch_denied,
    "mover_loop": mover_loop, "mover_stop": mover_stop,
    "collapse_crack": collapse_crack, "collapse_fall": collapse_fall,
    "fall_whoosh": fall_whoosh, "limbo_drone_loop": limbo_drone_loop,
    "rewind_nothing": rewind_nothing, "rewind_checkpoint": rewind_checkpoint, "checkpoint_set": checkpoint_set,
    "rewind_tape_loop": rewind_tape_loop, "rewind_settle": rewind_settle,
    "exhibit_wake": exhibit_wake, "hatch_rise": hatch_rise, "land": land,
}
for _set in FOOTSTEP_SETS:
    for _i in range(5):
        SFX[f"Footsteps/step_{_set}_{_i}"] = (lambda s=_set, i=_i: footstep(s, i))


def main(argv):
    names = argv or list(SFX)
    total = 0
    t0 = time.time()
    for name in names:
        x = SFX[name]()
        path = os.path.join(OUT_DIR, name + ".ogg")
        size = L.write_ogg(path, x, SR, QUALITY)
        total += size
        loop = name.endswith("_loop")
        seam = f", seam {abs(x[0] - x[-1]):.4f}" if loop else ""
        print(f"  {name:32s} {len(x) / SR:5.2f} s  peak {20 * np.log10(np.abs(x).max() + 1e-12):5.1f} dBFS"
              f"  {size / 1024:5.1f} KB{seam}")
    print(f"[ionsfx] {len(names)} files, {total / 1024:.0f} KB, {time.time() - t0:.1f} s -> {OUT_DIR}")


if __name__ == "__main__":
    main(sys.argv[1:])
