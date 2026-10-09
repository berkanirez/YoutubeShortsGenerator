# Configuration

All settings live in `src/ShortsGenerator.Api/appsettings.json`. Any of them can be overridden
for one run with an environment variable named `Section__Key`, e.g.
`$env:Ollama__Model = "qwen2.5:3b"` (the prerequisite check in `start.cmd` reads the same
values). Relative paths are resolved against `src/ShortsGenerator.Api/`, so the folder you
start from doesn't matter. Restart the API after a change.

## Ollama (local LLM)

| Key | Default | Meaning |
|---|---|---|
| `BaseUrl` | `http://127.0.0.1:11434` | Use `127.0.0.1`, not `localhost`: Ollama listens on IPv4 only and "localhost" cost ~2s per video (Day 27). |
| `Model` | `llama3.2:3b` | Any pulled model (`ollama list`). Bigger models write better scripts but are slower; plans are constrained by a JSON Schema, so most instruction-following models work. |
| `MaxOutputTokens` | `2048` | Cap per answer; plans need ~500-650 tokens. |
| `MaxAttempts` | `2` | Tries for transient failures only (connection refused, HTTP 503). |
| `RetryDelaySeconds` | `3` | Wait between those tries. |
| `KeepAlive` | `15m` | How long Ollama keeps the model in memory after a call (~2.3 GB RAM). `0` unloads at once. Loading costs ~6.6s. |

## KokoroTts (local voice)

| Key | Default | Meaning |
|---|---|---|
| `Voice` | `am_puck` | Kokoro voice id. Male English voices: `am_michael`, `am_fenrir`, `am_puck`, `am_onyx`, `am_adam`, `am_echo`, `am_eric`, `am_liam` (American), `bm_george`, `bm_lewis`, `bm_daniel`, `bm_fable` (British). Samples: `output/voice-samples/`. |
| `Speed` | `1.0` | 0.5-2.0. |
| `Language` | `en-us` | Phonemizer language. |
| `PauseSeconds` | `0.3` | Silence after each scene's sentence. |
| `TimeoutSeconds` | `300` | A synthesis that takes longer is stopped. |
| `PythonPath`, `ScriptPath`, `ModelPath`, `VoicesPath` | `tools/tts/...` | Where `tools/tts/setup.ps1` put things. |

## Remotion (renderer)

| Key | Default | Meaning |
|---|---|---|
| `NodePath` | `node` | Node.js on PATH. |
| `RendererDirectory` | `../../renderer` | |
| `TimeoutSeconds` | `900` | A render that takes longer is stopped. |
| `Concurrency` | `null` | Frames rendered in parallel; `null` = Remotion's default (half the logical cores). Measured on a 30s Short: 2 → 33.9s, 4 → 26.1s, 6 → 22.7s, 12 → 21.6s. Lower it for a quieter PC. |
| `SkipUnchangedRenders` | `true` | Reuse `final.mp4` when plan, narration, renderer code and assets are unchanged. |

## Other

| Key | Default | Meaning |
|---|---|---|
| `ConnectionStrings:Default` | `Data Source=shortsgenerator.db` | SQLite file (next to the API project). |
| `OutputStorage:RootDirectory` | `../../output` | Where job folders are written. |
| `AssetManifest:ManifestPath` | `../../assets/manifests/assets.json` | The asset catalog (see `docs/ASSETS.md`). |
| `Dashboard:Directory` | `../../web` | The dashboard's files. |

## Fixed by design (not settings)

1080x1920, 30 fps; 6-10 scenes; scripts 65-140 words (target 85-120, ~25-40s); at most 3 sound
effects; speech ~16 dB above the music. These are product rules (CLAUDE.md sections 11-13),
enforced and tested in code.
