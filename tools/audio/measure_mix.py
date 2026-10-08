"""Measures how a rendered Short's audio mix is balanced, using its plan's timing.

    tools/tts/.venv/Scripts/python.exe tools/audio/measure_mix.py <mix.wav> <plan.json>

Speech windows are each scene's [start, start + spokenDurationSeconds]; "music only"
windows are the pauses between sentences (after the first 0.1s, so speech tails are
excluded) plus the outro tail after the narration ends. Prints RMS levels in dBFS and the
speech-over-music margin. Used to verify CLAUDE.md section 13 (speech stays dominant)
by measurement rather than by ear.
"""

import json
import sys

import numpy as np
import soundfile as sf


def rms_db(x: np.ndarray) -> float:
    return 20 * np.log10(np.sqrt(np.mean(np.square(x))) + 1e-12)


def main() -> None:
    audio, rate = sf.read(sys.argv[1])
    with open(sys.argv[2], encoding="utf-8") as f:
        plan = json.load(f)

    speech, music = [], []
    start = 0.0
    for scene in sorted(plan["scenes"], key=lambda s: s["order"]):
        spoken = scene.get("spokenDurationSeconds") or 0.0
        end = start + scene["durationSeconds"]
        if spoken:
            speech.append(audio[int(start * rate):int((start + spoken) * rate)])
            gap_start = start + spoken + 0.1
            if end - gap_start > 0.05:
                music.append(audio[int(gap_start * rate):int(end * rate)])
        start = end

    speech_db = rms_db(np.concatenate(speech))
    music_db = rms_db(np.concatenate(music))
    peak_db = 20 * np.log10(np.max(np.abs(audio)) + 1e-12)
    print(f"speech windows: {len(speech)}, music-only windows: {len(music)}")
    print(f"speech RMS     {speech_db:6.1f} dBFS")
    print(f"music-only RMS {music_db:6.1f} dBFS")
    print(f"margin         {speech_db - music_db:6.1f} dB  (speech above music)")
    print(f"peak           {peak_db:6.1f} dBFS  (clipping if > 0)")


if __name__ == "__main__":
    main()
