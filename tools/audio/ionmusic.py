#!/usr/bin/env python3
"""[project]ion music generator: renders the seamless music loops into Assets/Resources/Audio/Music.

Direction (art bible §8): warm lo-fi chamber music in D lydian. G# (the raised 4th) is the colour
note shared with the musical SFX. Soft pads, felt piano and kalimba, tape hiss at -52 dB, sparse
crackle, a slow 1.6 ms tape wobble and a 3.6 s room reverb.

Tracks (all exact loops: the reverb and every note tail wrap around, so AudioSource.loop is seamless)
- rooms     150 s at 64 BPM. Dmaj9 -> E/D -> Bm11 -> Gmaj7#11 (2 bars each). The puzzle wings.
- hub        53 s at 72 BPM. Brighter, with a kalimba ostinato. Hub and ending.
- tutorial   60 s at 64 BPM. Sparse "dawn" variant (pad + a few piano notes). Tutorial zones.
             (Addition to the bible: IonAudio falls back to `hub` if this file is missing.)

Format: Ogg Vorbis, 32 kHz stereo, about -20 LUFS integrated. Unity re-encodes for the web build.

Usage:  python3 tools/audio/ionmusic.py [rooms|hub|tutorial ...]      (default: all)
Needs:  numpy, scipy, soundfile (libsndfile >= 1.0.29 with Vorbis), pyloudnorm  (requirements.txt)
"""
from __future__ import annotations

import os
import sys
import time

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import ionlib as L  # noqa: E402

SR = 32000
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT_DIR = os.path.join(ROOT, "Assets", "Resources", "Audio", "Music")
TARGET_LUFS = -20.0
QUALITY = 0.45
TAIL = 10.0  # seconds rendered past the loop end, then folded onto the start

CHORDS = {
    "Dmaj9":    dict(pad=["D3", "A3", "C#4", "E4", "F#4"], bass="D2",
                     arp=["D3", "A3", "E4", "F#4"], kal=["D5", "F#5", "A5", "E6"]),
    "E/D":      dict(pad=["D3", "G#3", "B3", "E4"], bass="D2",
                     arp=["D3", "B3", "E4", "G#4"], kal=["D5", "G#5", "B5", "E6"]),
    "Bm11":     dict(pad=["B2", "F#3", "A3", "D4", "E4"], bass="B1",
                     arp=["B2", "F#3", "D4", "E4"], kal=["B4", "F#5", "A5", "D6"]),
    "Gmaj7#11": dict(pad=["G2", "D3", "F#3", "B3", "C#4"], bass="G1",
                     arp=["G2", "D3", "B3", "C#4"], kal=["B4", "D5", "F#5", "C#6"]),
    "F#m7":     dict(pad=["F#3", "A3", "C#4", "E4"], bass="F#2",
                     arp=["F#2", "C#3", "A3", "E4"], kal=["C#5", "E5", "F#5", "A5"]),
    "Aadd9":    dict(pad=["A2", "E3", "B3", "C#4", "E4"], bass="A1",
                     arp=["A2", "E3", "B3", "C#4"], kal=["C#5", "E5", "A5", "B5"]),
}


class Track:
    """Collects instrument buses (dry, stereo) on a timeline of `length` seconds (+ TAIL)."""

    def __init__(self, name: str, bpm: float, bars: int, seed: int):
        self.name = name
        self.beat = 60.0 / bpm
        self.bar = 4 * self.beat
        self.length = bars * self.bar
        self.n = int(round(self.length * SR))
        self.total = self.n + int(TAIL * SR)
        self.rng = np.random.default_rng(seed)
        self.buses: dict[str, np.ndarray] = {}
        self.sends: dict[str, float] = {}
        self._pad_cache: dict = {}

    def bus(self, name: str, send: float) -> np.ndarray:
        if name not in self.buses:
            self.buses[name] = np.zeros((self.total, 2))
            self.sends[name] = send
        return self.buses[name]

    def t(self, bar: float, beat: float = 0.0) -> float:
        return bar * self.bar + beat * self.beat

    def place(self, bus: str, x: np.ndarray, at: float, send: float = 0.3, human: float = 0.0):
        if human:
            at += self.rng.uniform(-human, human)
        L.add_at(self.bus(bus, send), x, int(round(at * SR)))

    # -------------------------------------------------------- instruments on the timeline

    def pad(self, chord: str, bar: float, bars: float, cutoff: float, level: float = 1.0,
            attack: float = 1.6, release: float = 2.4):
        dur = bars * self.bar
        for i, note in enumerate(CHORDS[chord]["pad"]):
            key = (note, round(dur, 3), cutoff, attack, release)
            if key not in self._pad_cache:
                self._pad_cache[key] = L.pad_note(L.hz(note), dur, SR, self.rng, cutoff=cutoff,
                                                  attack=attack, release=release)
            # Upper voices a touch quieter so the pad stays warm.
            g = level * (1.0 - 0.07 * i)
            self.place("pad", self._pad_cache[key] * g, self.t(bar), send=0.55)

    def bass(self, chord: str, bar: float, bars: float, level: float = 1.0):
        x = L.sub_bass(L.hz(CHORDS[chord]["bass"]), bars * self.bar - 0.3, SR, level=level)
        self.place("bass", x, self.t(bar), send=0.06)

    def piano(self, note: str, bar: float, beat: float, beats: float, vel: float, bus: str = "piano",
              bright: float = 1.0, send: float = 0.38):
        f = L.hz(note)
        pan = float(np.clip((L.midi(note) - 64) / 30.0, -0.5, 0.5))
        vel = float(np.clip(vel * self.rng.uniform(0.92, 1.06), 0.05, 1.0))
        x = L.felt_piano(f, beats * self.beat, vel, SR, self.rng, bright=bright, pan=pan)
        self.place(bus, x, self.t(bar, beat), send=send, human=0.012)

    def kal(self, note: str, bar: float, beat: float, vel: float):
        pan = float(np.clip((L.midi(note) - 76) / 18.0, -0.6, 0.6)) + self.rng.uniform(-0.1, 0.1)
        vel = float(np.clip(vel * self.rng.uniform(0.85, 1.08), 0.05, 1.0))
        self.place("kal", L.kalimba(L.hz(note), vel, SR, self.rng, pan=pan), self.t(bar, beat),
                   send=0.42, human=0.008)

    def tick(self, bar: float, beat: float, vel: float):
        n = L.secs(SR, 0.03)
        x = L.hp(self.rng.standard_normal(n), 6500.0, SR, 2) * np.exp(-np.arange(n) / (0.005 * SR)) * vel
        self.place("tick", L.pan2(x, self.rng.uniform(-0.3, 0.3)), self.t(bar, beat), send=0.2, human=0.006)

    # -------------------------------------------------------- mixdown

    def mix(self, levels: dict[str, float], wobble: bool = True) -> np.ndarray:
        ir = L.reverb_ir(SR, t60=3.6, t60_hi=1.5, seed=11)
        dry = np.zeros((self.n, 2))
        send = np.zeros((self.n, 2))
        for name, buf in self.buses.items():
            b = L.fold(buf, self.n)
            target = levels.get(name)
            if target is not None:
                b = b * L.db(target - L.lufs(b, SR))
            dry += b
            send += b * self.sends[name]
        wet = L.convolve_circular(send, ir)
        out = dry + wet * 0.85
        if wobble:
            out = L.tape_wobble(out, SR, depth_ms=1.6, wow_hz=0.33)
        rng = np.random.default_rng(99)
        out = L.to_lufs(out, SR, TARGET_LUFS)
        out += L.tape_hiss(self.n, SR, rng, level_db=-52.0)
        out += L.crackle(self.n, SR, rng, rate=1.6, level_db=-46.0)
        out = L.soft_limit(out, -1.0)
        return out


# ---------------------------------------------------------------- arrangements

def arp_bar(tr: Track, chord: str, bar: int, which: int, vel: float):
    """Gentle syncopated left-hand broken chord (8th grid)."""
    a = CHORDS[chord]["arp"]
    pattern = [(0, a[0]), (3, a[2]), (5, a[3])] if which == 0 else [(0, a[1]), (3, a[3]), (6, a[2])]
    for pos, note in pattern:
        tr.piano(note, bar, pos * 0.5, 1.6, vel * (1.0 if pos == 0 else 0.86), bus="lh", send=0.42)


ROOMS_PROG = ["Dmaj9", "E/D", "Bm11", "Gmaj7#11"]

MELODY_A = [
    [(0, 1.5, "F#5"), (1.5, .5, "E5"), (2, 2, "A4"), (4, 1, "C#5"), (5, 1, "E5"), (6, 2, "F#5")],
    [(0, 1.5, "G#5"), (1.5, .5, "F#5"), (2, 2, "E5"), (4, 2, "B4"), (6, 2, "G#4")],
    [(0, 2, "F#5"), (2, 1, "E5"), (3, 1, "D5"), (4, 3, "A4")],
    [(0, 1, "B4"), (1, 1, "C#5"), (2, 1, "D5"), (3, 1, "F#5"), (4, 2, "E5"), (6, 2, "C#5")],
]
MELODY_B = [
    [(1, 1, "A5"), (2, 3, "F#5"), (6, 1, "E5"), (7, 1, "C#5")],
    [(0, 3, "B5"), (4, 1, "G#5"), (5, 3, "E5")],
    [(0, 1, "D6"), (1, 1, "C#6"), (2, 2, "A5"), (4, 4, "F#5")],
    [(0, 3, "C#6"), (4, 2, "B5"), (6, 2, "F#5")],
]
MELODY_C = [
    [(0, 1.5, "F#5"), (1.5, .5, "E5"), (2, 1.5, "A4"), (3.5, .5, "B4"), (4, 1, "C#5"), (5, 1, "E5"), (6, 2, "A5")],
    [(0, 1.5, "G#5"), (1.5, .5, "F#5"), (2, 2, "E5"), (4, 1, "B4"), (5, 1, "E5"), (6, 2, "G#5")],
    [(0, 2, "F#5"), (2, 1, "E5"), (3, 1, "D5"), (4, 2, "A4"), (6, 2, "B4")],
    [(0, 1, "D5"), (1, 1, "E5"), (2, 2, "F#5"), (4, 4, "A5")],
]
BELLS = [
    [(0, 4, "A5")],
    [(0, 4, "G#5")],
    [(0, 4, "F#5")],
    [(2, 6, "C#6")],
]


def build_rooms(cutoff: float, bright: float) -> Track:
    tr = Track("rooms", 64, 40, seed=640)
    variants = [None, MELODY_A, MELODY_B, MELODY_C, BELLS]
    bass_level = [0.55, 1.0, 1.0, 1.0, 0.6]
    for cycle in range(5):
        for ci, chord in enumerate(ROOMS_PROG):
            bar = cycle * 8 + ci * 2
            tr.pad(chord, bar, 2, cutoff)
            tr.bass(chord, bar, 2, level=bass_level[cycle])
            arp_vel = 0.34 if cycle in (0, 4) else 0.3
            arp_bar(tr, chord, bar, 0, arp_vel)
            arp_bar(tr, chord, bar + 1, 1, arp_vel * 0.92)
            mel = variants[cycle]
            if mel:
                vel = 0.42 if mel is BELLS else (0.5 if mel is MELODY_B else 0.56)
                for beat, beats, note in mel[ci]:
                    tr.piano(note, bar, beat, beats, vel * (1.0 if beat % 2 == 0 else 0.9), bright=bright)
    return tr


ROOMS_LEVELS = {"pad": -24.0, "bass": -28.5, "lh": -29.0, "piano": -25.5}


def render_rooms() -> np.ndarray:
    # The bible asks for >= 1% of the energy above 2 kHz: raise the pad / piano filters until it is.
    last = None
    for cutoff, bright in ((2400.0, 1.5), (2800.0, 1.9), (3200.0, 2.3), (3600.0, 2.7), (4000.0, 3.1)):
        tr = build_rooms(cutoff, bright)
        out = tr.mix(ROOMS_LEVELS)
        ratio = L.energy_above(out, SR, 2000.0)
        print(f"  rooms: pad cutoff {cutoff:.0f} Hz, piano bright {bright:.2f} -> energy > 2 kHz = {ratio * 100:.2f}%")
        last = out
        if ratio >= 0.01:
            break
    return last


HUB_PROG = ["Dmaj9", "E/D", "Bm11", "Gmaj7#11", "Dmaj9", "E/D", "F#m7", "Aadd9"]
KAL_P1 = [0, 2, 1, 3, 2, None, 1, 2]
KAL_P2 = [0, None, 2, 3, 1, 2, None, 3]
KAL_SPARSE = [0, None, 2, None, 1, None, 3, None]
HUB_MELODY = [
    [(0, 2, "A5"), (2, 1, "F#5"), (3, 1, "E5")],
    [(0, 2, "G#5"), (2, 2, "B5")],
    [(0, 3, "F#5"), (3, 1, "E5")],
    [(0, 2, "D5"), (2, 2, "C#5")],
    [(0, 1, "E5"), (1, 1, "F#5"), (2, 2, "A5")],
    [(0, 3, "B5"), (3, 1, "G#5")],
    [(0, 2, "A5"), (2, 2, "E5")],
    [(0, 2, "E5"), (2, 2, "C#5")],
]


def render_hub() -> np.ndarray:
    tr = Track("hub", 72, 16, seed=720)
    swing = 0.08  # beats: off-beat 8ths land a little late (lo-fi lilt)
    for bar in range(16):
        chord = HUB_PROG[bar % 8]
        second = bar >= 8
        tr.pad(chord, bar, 1, 3000.0, level=0.9, attack=0.9, release=2.0)
        tr.bass(chord, bar, 1, level=0.9)
        k = CHORDS[chord]["kal"]
        pattern = KAL_SPARSE if bar < 2 else (KAL_P1 if bar % 2 == 0 else KAL_P2)
        for i, idx in enumerate(pattern):
            if idx is None:
                continue
            beat = i * 0.5 + (swing if i % 2 else 0.0)
            vel = (0.62 if i % 2 == 0 else 0.48) * (1.0 if second else 0.85)
            tr.kal(k[idx], bar, beat, vel)
        a = CHORDS[chord]["arp"]
        tr.piano(a[0], bar, 0, 2.2, 0.3, bus="lh", send=0.4)
        tr.piano(a[2], bar, 0.02, 2.2, 0.26, bus="lh", send=0.4)
        tr.piano(a[1], bar, 2.5, 1.4, 0.24, bus="lh", send=0.4)
        if second:
            for beat, beats, note in HUB_MELODY[bar - 8]:
                tr.piano(note, bar, beat, beats, 0.52, bright=2.0)
        for i in (1, 3, 5, 7):
            tr.tick(bar, i * 0.5 + swing, 0.5 if i in (3, 7) else 0.35)
    return tr.mix({"pad": -26.0, "bass": -28.5, "kal": -25.0, "lh": -30.0, "piano": -26.5, "tick": -41.0})


TUT_PROG = ["Dmaj9", "Gmaj7#11", "Dmaj9", "E/D", "Bm11", "Gmaj7#11", "Dmaj9", "E/D"]
TUT_MELODY = [
    [(2, 3, "A5"), (5, 3, "F#5")],
    [(0, 4, "C#6"), (4, 4, "B5")],
    [(2, 2, "E5"), (4, 4, "F#5")],
    [(0, 3, "G#5"), (4, 4, "B5")],
    [(1, 3, "D6"), (4, 4, "A5")],
    [(0, 6, "F#5")],
    [(2, 2, "A5"), (4, 2, "E6"), (6, 2, "C#6")],
    [(0, 8, "G#5")],
]


def render_tutorial() -> np.ndarray:
    tr = Track("tutorial", 64, 16, seed=641)
    for ci, chord in enumerate(TUT_PROG):
        bar = ci * 2
        tr.pad(chord, bar, 2, 1500.0, attack=2.2, release=3.0)
        if ci >= 4:
            tr.bass(chord, bar, 2, level=0.6)
        a = CHORDS[chord]["arp"]
        tr.piano(a[0], bar, 0, 3.5, 0.26, bus="lh", send=0.45)
        tr.piano(a[2], bar + 1, 1, 2.5, 0.2, bus="lh", send=0.45)
        for beat, beats, note in TUT_MELODY[ci]:
            tr.piano(note, bar, beat, beats, 0.46, bright=1.2)
    tr.kal("D6", 12, 2.5, 0.5)
    tr.kal("A5", 14, 1.5, 0.45)
    tr.kal("E6", 14, 3.0, 0.35)
    return tr.mix({"pad": -23.5, "bass": -30.0, "lh": -30.5, "piano": -25.5, "kal": -29.0})


RENDERERS = {"rooms": render_rooms, "hub": render_hub, "tutorial": render_tutorial}


def seam_report(x: np.ndarray) -> str:
    """Compares the jump across the loop seam with typical sample-to-sample steps."""
    step = np.abs(np.diff(x, axis=0)).max(axis=1)
    seam = float(np.abs(x[0] - x[-1]).max())
    return f"seam step {seam:.4f} vs p99 step {np.percentile(step, 99):.4f}"


def main(argv):
    names = argv or list(RENDERERS)
    total = 0
    for name in names:
        t0 = time.time()
        print(f"[ionmusic] rendering {name} ...")
        x = RENDERERS[name]()
        path = os.path.join(OUT_DIR, f"{name}.ogg")
        size = L.write_ogg(path, x, SR, QUALITY)
        total += size
        back, sr = L.sf.read(path, always_2d=True)
        print(f"  {name}: {len(x) / SR:.2f} s, {L.lufs(x, SR):.1f} LUFS, peak {20 * np.log10(np.abs(x).max()):.1f} dBFS, "
              f">2kHz {L.energy_above(x, SR, 2000.0) * 100:.2f}%, {seam_report(x)}, "
              f"decoded {len(back)} samples (rendered {len(x)}), {size / 1024:.0f} KB, {time.time() - t0:.1f} s")
    print(f"[ionmusic] wrote {total / 1024:.0f} KB to {OUT_DIR}")


if __name__ == "__main__":
    main(sys.argv[1:])
