"""Generates every sound effect and music loop the videos use - from math, not downloads.

Why synthesize instead of downloading "free" sounds: CLAUDE.md section 13 allows only audio
with clear usage rights, and the clearest right there is is having made it yourself. Every
file here is built from sine waves, filtered noise and envelopes, so it is original to this
project. The script is deterministic (fixed noise seed): running it again reproduces the
same files byte for byte.

Run with the TTS venv (it already has numpy + soundfile):
    tools/tts/.venv/Scripts/python.exe tools/audio/generate_audio_assets.py
"""

import os

import numpy as np
import soundfile as sf

RATE = 44100
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
SFX_DIR = os.path.join(ROOT, "assets", "sfx")
MUSIC_DIR = os.path.join(ROOT, "assets", "music")

rng = np.random.default_rng(seed=17)


def seconds(duration: float) -> np.ndarray:
    return np.arange(int(duration * RATE)) / RATE


def envelope(n: int, attack: float, release: float) -> np.ndarray:
    """Linear attack, exponential-ish release - avoids clicks at both ends."""
    env = np.ones(n)
    a = max(1, int(attack * RATE))
    env[:a] = np.linspace(0, 1, a)
    r = max(1, int(release * RATE))
    env[-r:] *= np.linspace(1, 0, r) ** 2
    return env


def lowpass(signal: np.ndarray, width: int) -> np.ndarray:
    """Moving-average low-pass: cheap and good enough for noise shaping."""
    kernel = np.ones(width) / width
    return np.convolve(signal, kernel, mode="same")


def note(freq: float, duration: float, harmonics=(1.0, 0.35, 0.15)) -> np.ndarray:
    t = seconds(duration)
    wave = sum(amp * np.sin(2 * np.pi * freq * (i + 1) * t) for i, amp in enumerate(harmonics))
    return wave / sum(harmonics)


def normalize(signal: np.ndarray, peak: float) -> np.ndarray:
    return signal / (np.max(np.abs(signal)) + 1e-9) * peak


def write(directory: str, name: str, signal: np.ndarray, rate: int = RATE) -> None:
    os.makedirs(directory, exist_ok=True)
    path = os.path.join(directory, f"{name}.wav")
    sf.write(path, signal.astype(np.float32), rate, subtype="PCM_16")
    print(f"{os.path.relpath(path, ROOT)}  {len(signal) / rate:.2f}s  {os.path.getsize(path) // 1024} kB")


def half_rate(signal: np.ndarray) -> np.ndarray:
    """44.1 kHz -> 22.05 kHz. The loops are soft pads sitting under a voice; nothing in
    them needs the top octave, and it halves the size of files kept in the repo."""
    return lowpass(signal, 2)[::2]


# --- Sound effects: short, soft-edged, one clear gesture each -------------------------

def sfx_pop() -> np.ndarray:
    t = seconds(0.12)
    freq = np.linspace(500, 1100, len(t))            # upward chirp reads as "appear"
    wave = np.sin(2 * np.pi * np.cumsum(freq) / RATE)
    return normalize(wave * envelope(len(t), 0.003, 0.1), 0.8)


def sfx_whoosh() -> np.ndarray:
    n = int(0.45 * RATE)
    noise = rng.standard_normal(n)
    # Swept smoothing: the noise gets brighter then darker, like air passing by.
    bright = lowpass(noise, 6)
    dark = lowpass(noise, 60)
    mix = np.linspace(0, 1, n) ** 0.7
    shaped = dark * (1 - np.sin(np.pi * mix)) + bright * np.sin(np.pi * mix)
    swell = np.sin(np.pi * np.linspace(0, 1, n)) ** 2
    return normalize(shaped * swell, 0.6)


def sfx_click() -> np.ndarray:
    n = int(0.03 * RATE)
    tick = lowpass(rng.standard_normal(n), 3) * np.exp(-np.linspace(0, 12, n))
    return normalize(tick, 0.7)


def sfx_impact() -> np.ndarray:
    t = seconds(0.35)
    freq = np.linspace(120, 45, len(t))             # falling sub thump
    body = np.sin(2 * np.pi * np.cumsum(freq) / RATE) * np.exp(-t * 9)
    crack = lowpass(rng.standard_normal(len(t)), 4) * np.exp(-t * 60) * 0.4
    return normalize(body + crack, 0.9)


def sfx_success() -> np.ndarray:
    # Two rising notes (C6 -> G6): the classic "got it" interval.
    a = note(1046.5, 0.14) * envelope(int(0.14 * RATE), 0.005, 0.08)
    b = note(1568.0, 0.28) * envelope(int(0.28 * RATE), 0.005, 0.22)
    return normalize(np.concatenate([a, b]), 0.6)


def sfx_error() -> np.ndarray:
    # Two falling, slightly buzzy notes: "nope", without being harsh.
    buzzy = (1.0, 0.5, 0.33, 0.25)
    a = note(392.0, 0.13, buzzy) * envelope(int(0.13 * RATE), 0.004, 0.05)
    b = note(311.1, 0.22, buzzy) * envelope(int(0.22 * RATE), 0.004, 0.16)
    return normalize(np.concatenate([a, b]), 0.55)


# --- Music loops: simple, steady, meant to sit far below the voice --------------------

def midi(n: float) -> float:
    return 440.0 * 2 ** ((n - 69) / 12)


def pad(chord, duration: float) -> np.ndarray:
    tone = sum(note(midi(n), duration, (1.0, 0.2)) for n in chord) / len(chord)
    return tone * envelope(len(tone), 0.25, 0.4)


def music_loop(bpm: float, progression, with_drums: bool) -> np.ndarray:
    beat = 60 / bpm
    bar = beat * 4
    total = int(bar * len(progression) * RATE)
    out = np.zeros(total)

    for i, chord in enumerate(progression):
        start = int(i * bar * RATE)
        segment = pad(chord, bar) * 0.5
        # Bass: chord root an octave down, on every beat.
        for b in range(4):
            bass = note(midi(chord[0] - 12), beat * 0.9, (1.0, 0.5)) * envelope(int(beat * 0.9 * RATE), 0.01, 0.2)
            s = int(b * beat * RATE)
            segment[s:s + len(bass)] += bass * 0.35
        # Arpeggio: chord tones up an octave, eighth notes.
        for e in range(8):
            tone = chord[e % len(chord)] + 12
            pluck = note(midi(tone), beat / 2, (1.0, 0.3, 0.1)) * np.exp(-seconds(beat / 2) * 9)
            s = int(e * beat / 2 * RATE)
            segment[s:s + len(pluck)] += pluck * 0.18
        out[start:start + len(segment)] += segment[: total - start]

    if with_drums:
        for k in range(int(len(progression) * 4)):
            s = int(k * beat * RATE)
            if k % 2 == 0:  # kick on 1 and 3
                t = seconds(0.25)
                kick = np.sin(2 * np.pi * np.cumsum(np.linspace(110, 45, len(t))) / RATE) * np.exp(-t * 14)
                out[s:s + len(kick)] += kick[: total - s] * 0.5
            hat_start = s + int(beat / 2 * RATE)  # hi-hat on the off-beats
            if hat_start < total:
                hat = lowpass(rng.standard_normal(int(0.05 * RATE)), 2) * np.exp(-np.linspace(0, 10, int(0.05 * RATE)))
                out[hat_start:hat_start + len(hat)] += hat[: total - hat_start] * 0.08

    return normalize(out, 0.7)


def main() -> None:
    effects = {
        "pop": sfx_pop, "whoosh": sfx_whoosh, "click": sfx_click,
        "impact": sfx_impact, "success": sfx_success, "error": sfx_error,
    }
    for name, build in effects.items():
        write(SFX_DIR, name, build())

    # C - G - Am - F and Am - F - C - G, two passes each so the loop is long enough to
    # not sound repetitive within a 30-45s Short.
    upbeat = music_loop(104, [(60, 64, 67), (55, 59, 62), (57, 60, 64), (53, 57, 60)] * 2, True)
    calm = music_loop(80, [(57, 60, 64), (53, 57, 60), (60, 64, 67), (55, 59, 62)] * 2, False)
    write(MUSIC_DIR, "upbeat", half_rate(upbeat), RATE // 2)
    write(MUSIC_DIR, "calm", half_rate(calm), RATE // 2)


if __name__ == "__main__":
    main()
