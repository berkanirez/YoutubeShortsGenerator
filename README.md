# Local Fitness Shorts Generator

Give it a fitness topic (or your own script) and get back a 1080x1920 MP4 Short: an
original mascot and drawings, scene changes and motion, phrase-timed captions, a local
English voice-over, sound effects and background music. Everything runs on your PC - no
paid API, no account, no upload. You review each video and publish it yourself.

Status: **V1 complete** (30-day build, see `docs/DAY_STATE.md`). A video takes about a
minute on a laptop CPU.

## Quick start

```
start.cmd                                           checks prerequisites, starts the app, opens the dashboard
generate.cmd "Why Your Pull-Ups Aren't Increasing"  or: one video from the command line
```

In the dashboard (http://localhost:5007): type a topic (or paste a 65-140 word script),
press **Generate**, watch the stages, play the result. Then:

* **Scene plan** - swap a scene's drawing (**Change**), edit its text, animation, sound,
  type or narration (**Edit**), switch caption style or music; **Re-render** applies it
  (regenerating the voice-over first if narration changed).
* **Review checklist** - tick the seven checks while you watch; the video shows
  **Reviewed** once all are ticked. A re-render makes the review stale.
* **Retry** a failed video after fixing the cause - finished stages are kept.

Videos are in `output/<job-id>/final.mp4`; `output/INDEX.md` lists them by date and title.

## Setup (once)

| Need | Install | Check |
|---|---|---|
| .NET 10 SDK | https://dotnet.microsoft.com/download | `dotnet --version` |
| Node.js 18+ | https://nodejs.org | `node --version` |
| Python 3.11 | https://python.org (or `winget install Python.Python.3.11`) | `py -3.11 --version` |
| Ollama | `winget install Ollama.Ollama` | `ollama --version` |

Then:

```
cd renderer; npm ci; cd ..                                         # renderer packages
powershell -ExecutionPolicy Bypass -File tools/tts/setup.ps1       # Kokoro venv + model (~350 MB)
ollama pull llama3.2:3b                                            # the local model (~2 GB)
start.cmd -CheckOnly                                               # should print "All set."
```

`start.cmd` offers to run the missing steps itself. Everything is free and works offline
after setup.

## How it works

```
topic ─► script (Ollama, checked for length, medical claims, repeats)
      ─► scene plan (narration split in code; the model picks visuals under a JSON Schema;
                     variety, asset and on-screen-number rules)
      ─► voice-over (Kokoro, per scene) ─► scenes timed from the real audio
      ─► render (Remotion: templates, animations, captions, music, sfx) ─► final.mp4 + review.md
```

A background worker runs one video at a time; interrupted work resumes on the next start.
Details: `docs/ARCHITECTURE.md`.

## API (http://localhost:5007, OpenAPI at /openapi/v1.json in Development)

```
POST /api/videos                       { "topic": "...", "sourceScript": "..." (optional) } -> 202
GET  /api/videos                       recent videos          GET /api/videos/{id}   status
POST /api/videos/{id}/generate         continue a paused job  POST /api/videos/{id}/retry   retry a failed one
POST /api/videos/{id}/render           re-render              POST /api/videos/{id}/apply-edits
PUT  /api/videos/{id}/scenes/{sceneId} edit a scene           PUT  /api/videos/{id}/scenes/{sceneId}/asset
PUT  /api/videos/{id}/settings         caption style, music   GET/PUT /api/videos/{id}/review-state
GET  /api/videos/{id}/plan | /video | /review                 GET  /api/assets, /api/assets/{id}/svg
GET  /api/health, /api/llm/health, /api/tts/health, /api/renderer/health
```

## Documentation

| | |
|---|---|
| `docs/CONFIGURATION.md` | every setting (model, voice, speed, render concurrency, ...) |
| `docs/TROUBLESHOOTING.md` | error messages and their fixes |
| `docs/ARCHITECTURE.md` | components, job lifecycle, files, key decisions |
| `docs/ASSETS.md` | adding or replacing drawings (`tools/ShortsGenerator.AssetTool`) |
| `docs/TESTING.md` | test suites and what they guard |
| `docs/LICENSES.md` | licenses of tools, models and assets |
| `docs/PHASE_2_OPTIONS.md` | possible next steps (not built) |
| `docs/daily-code-notes/` | day-by-day explanation of every piece of code (Turkish) |

## Tests

```
dotnet test                  # backend (no Ollama/Kokoro/Remotion needed)
cd renderer; npm test        # renderer unit tests
cd renderer; npm run smoke   # real low-resolution render of every scene type
```
