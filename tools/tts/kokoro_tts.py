"""Local Kokoro TTS command-line wrapper used by ShortsGenerator.Infrastructure.

Contract with the .NET side (KokoroTtsProvider):
  * input arrives on stdin as UTF-8 JSON, never as command-line arguments, so
    quotes/newlines/odd characters in narration can't break argument parsing:
        {"segments": ["First scene's narration.", "Second scene's narration."]}
  * each segment is synthesized separately and the clips are joined with
    --pause seconds of silence after each one, so the caller learns exactly
    where every segment (= every scene) starts and ends in the final audio;
  * the WAV is written to a temporary file next to --output and renamed into
    place only on success, so a crash never leaves a truncated narration.wav;
  * on success, exactly one JSON object is printed to stdout:
        {"durationSeconds", "sampleRate", "voice",
         "segments": [{"startSeconds", "durationSeconds"}, ...]}
  * on failure, a one-line human-readable reason goes to stderr and the exit
    code says which kind of failure it was.
"""

import argparse
import json
import os
import sys
from typing import NoReturn

EXIT_BAD_INPUT = 2
EXIT_MODEL_MISSING = 3
EXIT_SYNTHESIS_FAILED = 4


def fail(code: int, message: str) -> NoReturn:
    print(message, file=sys.stderr)
    sys.exit(code)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Synthesize narration with Kokoro (local, offline).")
    parser.add_argument("--output", required=True, help="Target .wav path.")
    parser.add_argument("--model", required=True, help="Path to kokoro-v1.0.onnx.")
    parser.add_argument("--voices", required=True, help="Path to voices-v1.0.bin.")
    parser.add_argument("--voice", default="af_heart", help="Kokoro voice id, e.g. af_heart, am_michael.")
    parser.add_argument("--speed", type=float, default=1.0, help="Speaking speed multiplier (0.5-2.0).")
    parser.add_argument("--lang", default="en-us", help="Phonemizer language code.")
    parser.add_argument("--pause", type=float, default=0.3, help="Silence after each segment, in seconds (0-2).")
    return parser.parse_args()


def read_segments() -> list[str]:
    # utf-8-sig: some callers (e.g. Windows PowerShell pipes) prepend a BOM.
    raw = sys.stdin.buffer.read().decode("utf-8-sig").strip()
    if not raw:
        fail(EXIT_BAD_INPUT, "No input was provided on stdin.")
    try:
        payload = json.loads(raw)
    except json.JSONDecodeError as error:
        fail(EXIT_BAD_INPUT, f"stdin is not valid JSON: {error}")

    segments = payload.get("segments") if isinstance(payload, dict) else None
    if not isinstance(segments, list) or not segments:
        fail(EXIT_BAD_INPUT, "stdin JSON must be an object with a non-empty 'segments' array.")
    if not all(isinstance(text, str) and text.strip() for text in segments):
        fail(EXIT_BAD_INPUT, "Every segment must be a non-empty string.")
    return [text.strip() for text in segments]


def main() -> None:
    args = parse_args()
    segments = read_segments()

    if not 0.5 <= args.speed <= 2.0:
        fail(EXIT_BAD_INPUT, f"Speed {args.speed} is outside the supported range 0.5-2.0.")
    if not 0.0 <= args.pause <= 2.0:
        fail(EXIT_BAD_INPUT, f"Pause {args.pause} is outside the supported range 0-2.")
    if not args.output.lower().endswith(".wav"):
        fail(EXIT_BAD_INPUT, "Output path must end with .wav.")
    for label, path in (("Model", args.model), ("Voices", args.voices)):
        if not os.path.isfile(path):
            fail(EXIT_MODEL_MISSING, f"{label} file not found at '{path}'. Run tools/tts/setup.ps1 first.")

    # Imported late so that argument/model errors above are reported instantly,
    # without paying the ~1s cost of loading onnxruntime first.
    import numpy as np
    import soundfile as sf
    from kokoro_onnx import Kokoro

    try:
        kokoro = Kokoro(args.model, args.voices)
        if args.voice not in kokoro.get_voices():
            fail(EXIT_BAD_INPUT, f"Unknown Kokoro voice '{args.voice}'.")

        clips = []
        timings = []
        sample_rate = None
        cursor = 0
        for text in segments:
            samples, rate = kokoro.create(text, voice=args.voice, speed=args.speed, lang=args.lang)
            sample_rate = sample_rate or rate
            pause = np.zeros(int(round(args.pause * rate)), dtype=samples.dtype)
            timings.append({
                "startSeconds": round(cursor / rate, 4),
                "durationSeconds": round(len(samples) / rate, 4),
            })
            clips.extend([samples, pause])
            cursor += len(samples) + len(pause)
    except SystemExit:
        raise
    except Exception as error:  # the engine's own exceptions are not a stable API
        fail(EXIT_SYNTHESIS_FAILED, f"Kokoro synthesis failed: {type(error).__name__}: {error}")

    audio = np.concatenate(clips)
    output_dir = os.path.dirname(os.path.abspath(args.output))
    os.makedirs(output_dir, exist_ok=True)
    temp_path = args.output + ".tmp.wav"
    sf.write(temp_path, audio, sample_rate, subtype="PCM_16")
    os.replace(temp_path, args.output)

    print(json.dumps({
        "durationSeconds": round(len(audio) / sample_rate, 4),
        "sampleRate": sample_rate,
        "voice": args.voice,
        "segments": timings,
    }))


if __name__ == "__main__":
    main()
