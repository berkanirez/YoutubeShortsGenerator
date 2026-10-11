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


def sfx_levelup() -> np.ndarray:
    # Three quick rising notes and a bright held fourth (C5 E5 G5 C6): "level up".
    tones = [(523.25, 0.07), (659.25, 0.07), (783.99, 0.07), (1046.5, 0.32)]
    parts = [note(f, d, (1.0, 0.45, 0.2)) * envelope(int(d * RATE), 0.004, d * 0.6) for f, d in tones]
    return normalize(np.concatenate(parts), 0.6)


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


# --- Music: old-school boom-bap, meant to sit far below the voice ----------------------
#
# The first loops (sine pads + an arpeggio) sounded like a kids' show. Gym videos live on a
# 90s hip-hop feel: a fat kick, a cracking snare with some room, swung hats, a syncopated
# bass, dusty minor-jazz piano chops and vinyl crackle - all still synthesized here.

music_rng = np.random.default_rng(seed=92)   # separate seed: the SFX above stay byte-identical


def midi(n: float) -> float:
    return 440.0 * 2 ** ((n - 69) / 12)


def highpass(signal: np.ndarray, width: int) -> np.ndarray:
    return signal - lowpass(signal, width)


def place(out: np.ndarray, sound: np.ndarray, at: float, gain: float) -> None:
    s = int(at * RATE)
    if s >= len(out):
        return
    n = min(len(sound), len(out) - s)
    out[s:s + n] += sound[:n] * gain


def room(signal: np.ndarray, length: float = 0.22, mix: float = 0.25) -> np.ndarray:
    """A short, dark room: convolution with exponentially decaying noise."""
    n = int(length * RATE)
    ir = lowpass(music_rng.standard_normal(n), 8) * np.exp(-np.linspace(0, 7, n))
    wet = np.convolve(signal, ir / np.sum(np.abs(ir)) * 6)[: len(signal)]
    return signal + wet * mix


def kick(hard: float = 1.0) -> np.ndarray:
    t = seconds(0.42)
    sweep = np.sin(2 * np.pi * np.cumsum(np.linspace(125, 46, len(t))) / RATE) * np.exp(-t * 7.5)
    click = highpass(music_rng.standard_normal(len(t)), 4) * np.exp(-t * 300) * 0.35
    # The "knock": phone speakers can't play the 50 Hz sweep, so the kick also needs a mid hit.
    knock = np.sin(2 * np.pi * 190 * t) * np.exp(-t * 45) * 0.5
    return np.tanh((sweep + click + knock) * 2.2 * hard) / np.tanh(2.2)


def snare(hard: float = 1.0) -> np.ndarray:
    t = seconds(0.32)
    noise = music_rng.standard_normal(len(t))
    band = lowpass(highpass(noise, 6), 2) * np.exp(-t * 16)
    body = np.sin(2 * np.pi * 185 * t) * np.exp(-t * 28) * 0.7
    return room(np.tanh((band + body) * 1.6 * hard), 0.25, 0.35)


def hat(open_: bool = False, hard: float = 1.0) -> np.ndarray:
    length = 0.18 if open_ else 0.045
    t = seconds(length)
    metal = highpass(music_rng.standard_normal(len(t)), 2)
    return metal * np.exp(-t * (14 if open_ else 70)) * hard


def bass_note(freq: float, duration: float) -> np.ndarray:
    t = seconds(duration)
    # Upper harmonics so the line is audible on a phone, not only felt.
    wave = np.sin(2 * np.pi * freq * t) + 0.5 * np.sin(2 * np.pi * 2 * freq * t) + 0.2 * np.sin(2 * np.pi * 3 * freq * t)
    return np.tanh(wave * 1.4) * envelope(len(t), 0.006, min(0.12, duration * 0.5))


def keys(chord, duration: float) -> np.ndarray:
    """Rhodes-ish: a sine with a soft bell partial, tremolo, quick decay - a sample chop."""
    t = seconds(duration)
    out = np.zeros(len(t))
    for n in chord:
        f = midi(n)
        bell = np.sin(2 * np.pi * f * 4.0 * t) * np.exp(-t * 12) * 0.25
        out += (np.sin(2 * np.pi * f * t) + 0.3 * np.sin(2 * np.pi * 2 * f * t) + bell) * np.exp(-t * 2.2)
    tremolo = 1 + 0.12 * np.sin(2 * np.pi * 5.0 * t)
    return out / len(chord) * tremolo * envelope(len(t), 0.004, 0.05)


def crackle(duration: float, density: float) -> np.ndarray:
    n = int(duration * RATE)
    hiss = lowpass(music_rng.standard_normal(n), 3) * 0.02
    pops = np.zeros(n)
    idx = music_rng.integers(0, n, int(duration * density))
    pops[idx] = music_rng.uniform(-1, 1, len(idx))
    return hiss + highpass(pops, 3) * 0.5


# Am9 - Dm9 - Fmaj7 - E7(b9): the minor-jazz loop behind a thousand 90s beats.
TARGET_RMS_DB = -16.0

CHORDS = [(57, 60, 64, 67, 71), (50, 53, 57, 60, 64), (53, 57, 60, 64), (52, 56, 59, 62, 65)]
# Bass per bar, in sixteenths: (step, semitones above the chord root, length in steps).
BASS_A = [(0, 0, 3), (3, 0, 1), (6, 12, 2), (8, 0, 3), (11, 7, 2), (14, 10, 2)]
BASS_B = [(0, 0, 4), (6, 7, 2), (8, 0, 2), (10, 12, 2), (13, 7, 3)]


def boom_bap(bpm: float, bars: int, swing: float, energy: float) -> np.ndarray:
    """16 bars: the beat, then on bar 9 a variation (other bassline, open hats); the keys
    drop out for bars 7-8 and come back with a drum fill - change keeps a 40 s loop alive."""
    beat = 60 / bpm
    step = beat / 4
    total = bars * 4 * beat
    out = np.zeros(int(total * RATE) + RATE)
    drums = np.zeros_like(out)

    def at(bar: int, sixteenth: int) -> float:
        # Swing pushes every off-beat sixteenth late: the lazy, head-nodding 90s feel.
        return (bar * 16 + sixteenth) * step + (step * swing if sixteenth % 2 == 1 else 0)

    for bar in range(bars):
        root = CHORDS[(bar // 2) % len(CHORDS)][0] - 24
        second_half = bar >= 8
        fill = bar % 8 == 7

        # Drums: kick on 1, the "and" of 2 and on 3 (+ a ghost before 3 every other bar).
        for k, gain in [(0, 1.0), (6, 0.8), (8, 0.95)] + ([(7, 0.45)] if bar % 2 else []):
            place(drums, kick(gain * energy), at(bar, k), 0.5)
        for sn in (4, 12):
            place(drums, snare(energy), at(bar, sn), 0.75)
        if fill:
            for i, sn in enumerate((13, 14, 15)):
                place(drums, snare(0.5 + 0.15 * i), at(bar, sn), 0.45)
        for h in range(0, 16, 2 if energy < 0.8 else 1):
            open_ = second_half and h == 14
            accent = 1.0 if h % 4 == 0 else 0.6 if h % 2 == 0 else 0.35
            place(drums, hat(open_, accent), at(bar, h), 0.3)

        # Bass.
        for s16, semis, length in (BASS_B if second_half else BASS_A):
            place(out, bass_note(midi(root + semis), length * step * 0.95), at(bar, s16), 0.24)

        # Keys: a chop on 1 and on the "and" of 2, a lighter one on 4; out for bars 7-8.
        if bar % 8 not in (6, 7):
            chord = CHORDS[(bar // 2) % len(CHORDS)]
            for s16, gain in [(0, 1.1), (6, 0.8), (12, 0.5)]:
                place(out, keys(chord, beat * 1.6), at(bar, s16), gain)

    mix = out + drums
    mix += crackle(len(mix) / RATE, 9)
    # Dusty and glued: roll off the top, saturate softly like tape, trim to the loop length.
    mix = np.tanh(lowpass(mix, 3) * 1.3)[: int(total * RATE)]
    # Same loudness (RMS) as the loops the mix was calibrated with (AUDIO_MIX.music, measured
    # on Day 15): a punchy beat peak-normalized came out 5-6 dB louder under the voice.
    mix *= 10 ** (TARGET_RMS_DB / 20) / np.sqrt(np.mean(mix ** 2))
    return np.clip(mix, -0.98, 0.98)


def main() -> None:
    effects = {
        "pop": sfx_pop, "whoosh": sfx_whoosh, "click": sfx_click,
        "impact": sfx_impact, "success": sfx_success, "error": sfx_error, "levelUp": sfx_levelup,
    }
    for name, build in effects.items():
        write(SFX_DIR, name, build())

    # The mood ids stay (plans and the renderer know "upbeat" / "calm"); the sound changes.
    # 16 bars: ~42 s at 92 BPM, ~48 s at 80 - a Short hears the loop about once.
    upbeat = boom_bap(92, 16, swing=0.22, energy=1.0)
    calm = boom_bap(80, 16, swing=0.28, energy=0.7)
    write(MUSIC_DIR, "upbeat", half_rate(upbeat), RATE // 2)
    write(MUSIC_DIR, "calm", half_rate(calm), RATE // 2)


if __name__ == "__main__":
    main()
