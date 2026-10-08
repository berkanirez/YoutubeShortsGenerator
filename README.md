# Local Fitness Shorts Generator

Zero-cost, local-first, automated faceless fitness Shorts generator.
Give it a fitness topic and get back a 9:16 MP4 Short — original mascot
visuals, scene changes, motion, captions, local voice-over, sound effects,
and background music — with as little manual editing as possible.

See `CLAUDE.md` for the full working rules, `docs/PRODUCT_SPEC.md` for what
the product is, `docs/ROADMAP_30_DAYS.md` for the day-by-day build plan, and
`docs/DAY_STATE.md` for what's actually done so far.

## Status

Day 25 of 30 — the first fully automatic Short has been generated from a
topic alone (~1 minute on a laptop CPU). `POST /api/videos` returns immediately (202) and a
background worker runs the job through script (local LLM, or your own
script) -> validated scene plan -> local Kokoro voice-over, with every scene
re-timed from the real audio -> Remotion render, ending in
`output/<job-id>/final.mp4`. Poll `GET /api/videos/{id}` for progress.
Check `docs/DAY_STATE.md` for the current day.

## Prerequisites

| Tool | Required for | Status on this machine |
|---|---|---|
| Git | version control | installed |
| .NET 10 SDK | `src/` backend | installed |
| Node.js / npm | `renderer/` (Remotion, from Day 7) | installed |
| Python 3.11 | local Kokoro TTS (`tools/tts/`, from Day 11) | installed; venv + model set up |
| FFmpeg | media processing (from Day 12+) | not installed yet |
| Ollama | local LLM (from Day 3) | **installed** — `llama3.2:3b` pulled |

Ollama runs as a local background app/service on `http://localhost:11434`
(installed via `winget install Ollama.Ollama`). FFmpeg isn't needed until
Day 12 and is not required to run what exists today.

## Running the API

```
dotnet run --project src/ShortsGenerator.Api
```

Then open **http://localhost:5007** — the local dashboard (`web/`): type a
topic (or paste your own script), press Generate, watch the progress, play
the finished video, read its review checklist, open its folder. In a
video's scene plan, "Change" swaps one scene's visual for another catalog
asset, and "Edit" changes a scene's on-screen text, second text, items,
animation, sound effect, emphasis, type (Hook/Outro stay put) or narration;
the caption style and music mood can be switched above the table. Every
edit is validated with the same rules as a generated plan. "Re-render"
applies the edits: only a render if visuals changed, or a new voice-over
(and timing) first if narration text changed.

In Development, Swagger/OpenAPI JSON is available at
`http://localhost:<port>/openapi/v1.json`. Endpoints today:

```
GET  /api/health
POST /api/videos        { "topic": "...", "sourceScript": "..." (optional), "autoGenerate": true (default) }
                        -> 202 Accepted; the background worker generates the whole video
GET  /api/videos        recent jobs (?take=20), newest first
GET  /api/videos/{id}   status: Created -> Planning -> Narrating -> Rendering -> Completed | Failed
POST /api/videos/{id}/generate  queue a job to continue from wherever it is (e.g. autoGenerate=false)
POST /api/videos/{id}/render    queue a (re-)render of a Narrating/Completed job
PUT  /api/videos/{id}/scenes/{sceneId}        edit one scene (only the fields sent; "" clears)
PUT  /api/videos/{id}/scenes/{sceneId}/asset  { "assetId": "..." } swap a scene's visual
PUT  /api/videos/{id}/settings  { "captionStyle": "bold|boxed", "musicMood": "upbeat|calm" }
POST /api/videos/{id}/apply-edits  re-render, regenerating the voice-over first if narration changed

Manual, synchronous stage endpoints (debugging; don't use them on a job that is queued/running):
POST /api/videos/{id}/script   generates and attaches a narration for this job (stays "Planning")
POST /api/videos/{id}/plan     splits the narration into a validated scene plan, saves plan.json
GET  /api/videos/{id}/plan     reads back the saved plan.json for this job
POST /api/videos/{id}/narration  speaks the plan scene by scene with local Kokoro TTS -> narration.wav,
                               and re-times plan.json from the real audio
GET  /api/tts/health    is the local TTS engine installed (venv, script, model files)?
GET  /api/renderer/health      is Node.js available and renderer/node_modules installed?
GET  /api/assets        lists every known asset's metadata (id, category, tags, ...)
GET  /api/llm/health    Ollama reachable? Is the configured model pulled?
POST /api/llm/ping      sends a harmless test prompt, returns Ollama's text reply
```

If `sourceScript` is given, it is used as the narration as written (it
still has to pass the same length/safety checks) and the LLM is not asked
to write one.

A local SQLite database is created and migrated automatically on startup.
Every finished video gets a `review.md` next to it: what was checked
automatically, any variety/originality warnings, a scene table and a
checklist for you to go through before publishing — nothing is ever
uploaded automatically. Generated scripts are checked against earlier
videos for reused phrases, and plans against earlier plans for repeated
structure and takeaways.

Each job's files (`plan.json`, `narration.wav`, `final.mp4`, `review.md`) are
written under the repository's `output/<job-id>/` (git-ignored). Relative
paths in `appsettings.json` are resolved against `src/ShortsGenerator.Api/`,
so it doesn't matter which folder you start the API from.

### Local TTS setup (once)

```
powershell -ExecutionPolicy Bypass -File tools/tts/setup.ps1
```

Creates an isolated Python 3.11 environment in `tools/tts/.venv` and
downloads the Kokoro-82M model (Apache-2.0, ~350 MB) into
`tools/tts/models/`. After that, TTS runs fully offline. Voice and speed
are configured under `"KokoroTts"` in `appsettings.json` (default voice
`af_heart`). The
Ollama base URL, model, and generation length cap are configured under
`"Ollama"` in `appsettings.json` (default: `http://localhost:11434`,
`llama3.2:3b`, 2048 tokens) — change the model there, not in code, if you
have a different one pulled.

Scene planning splits the narration into 6–10 scenes in code; the local
model only decides how each scene looks, and its answer is constrained by a
JSON Schema while it is generated (Ollama structured outputs: exact scene
count, only known scene types/animations/asset ids). It's still validated,
with up to 5 attempts.

The asset catalog lives in `assets/manifests/assets.json`, pointing at
original, hand-drawn placeholder SVGs under `assets/mascot/`,
`assets/exercises/`, `assets/props/`, and `assets/anatomy/` — an original
mascot ("Spark", teal/orange, not yellow), a few exercise/equipment
placeholders, and two muscle-highlight schematics. The manifest is loaded
once at startup (`AssetManifest:ManifestPath` in `appsettings.json`) and the
app refuses to start if it's missing or points at a missing file. Scene
plans can only reference asset ids that actually exist in this catalog.
Add or replace art with the asset tool (`dotnet run --project
tools/ShortsGenerator.AssetTool -- validate | add | replace | index`) —
rules and workflow in `docs/ASSETS.md`. No code changes are needed for new
assets.

## Running tests

```
dotnet test
```

No Ollama/Kokoro/Remotion needed: the engines are faked. `docs/TESTING.md`
maps each expensive risk (job states, plan validation, LLM/TTS/renderer
failures, path safety, API validation) to the tests that guard it.

## Renderer (Remotion)

```
cd renderer
npm run sync-assets   # mirrors ../assets and ../samples into public/ (required once, or after they change)
npm run dev            # opens Remotion Studio, a live preview in the browser
npx remotion render ShortComposition out/sample.mp4   # renders a real MP4
```

`renderer/` is a separate Node/TypeScript project (not part of the .NET
solution) — Remotion renders React components frame-by-frame into video.
It reads a `VideoPlan` JSON (`samples/sample-plan.json` by default, matching
`ShortsGenerator.Domain.VideoPlan`'s shape exactly), validates it at runtime
with Zod (`src/schemas/videoPlan.ts` — never trusts the JSON just because
TypeScript's compiler is happy), and renders each scene in order for exactly
its `durationSeconds`, with a background/layout that varies by scene type
and the scene's real asset artwork (`src/scenes/`). The API runs it
automatically for every job (Day 13+); `samples/` holds hand-written plans
for working on the renderer alone.

Every scene type has its own template (`src/scenes/templates/`): MythVsFact,
Comparison, List, Progress, AnatomyHighlight, ExerciseDemo (animates an
exercise's two pose drawings into a rep) and Recovery; Hook/Outro use the
generic layout. To see them all:
`npx remotion render ShortComposition out/templates.mp4 --props='{"planPath":"samples/template-showcase-plan.json","narrationPath":null}'`

Sound: narration, a background music loop (`assets/music/`, chosen by the
plan's `musicMood`: `upbeat` or `calm`) and up to 3 sound effects per video
(`assets/sfx/`). All audio files are synthesized by
`tools/audio/generate_audio_assets.py` — original to this project, no
third-party licenses. Speech is kept ~16 dB above the music
(`tools/audio/measure_mix.py` measures a render's mix).

Each scene's `animation` field picks one of nine entrance/emphasis animations
in `src/animations/primitives.ts` (fade, slide, pop, punchZoom, shake, bounce,
pulse, counter, highlight). They're pure functions of the scene-local frame
number, finish within ~0.5s (except a subtle pulse), and only move the
content, never the background. Every scene's `narration` is shown as phrase-level captions
(`src/captions/`): short 1-line chunks, broken at punctuation, timed across
the scene in proportion to their length, kept clear of the Shorts UI (bottom
and right edges), with the scene's `emphasis` phrase highlighted. The plan's
`captionStyle` picks a preset (`bold` default, `boxed`). Run the renderer's
unit tests with `npm test`, and `npm run smoke` for a real low-resolution
render of every scene type plus a deliberately broken plan (~25s).
`samples/contract-plan.json` is the plan.json contract between the backend
and the renderer (see `docs/TESTING.md`).

To see all nine animations side by side:

```
npx remotion render ShortComposition out/animation-showcase.mp4 --props='{"planPath":"samples/animation-showcase-plan.json"}'
```

Remotion renders inside a headless Chrome instance, not raw Node.js, so it
can only read local files via `staticFile()` + `fetch()` from its own
`public/` folder — not arbitrary filesystem paths. `npm run sync-assets`
copies the repo's real `assets/` and `samples/` into `renderer/public/` (a
disposable, git-ignored mirror) so this works; it runs automatically before
`npm run dev` and `npm run render`.

## Repository layout (grows day by day, see the roadmap)

```
CLAUDE.md                        working rules for this project
docs/                            product spec, roadmap, day state, architecture
src/ShortsGenerator.Api          ASP.NET Core API (DI wiring, endpoints)
src/ShortsGenerator.Domain       VideoJob entity, status rules, repository interface
src/ShortsGenerator.Application  use-case services (VideoJobService)
src/ShortsGenerator.Infrastructure  EF Core + SQLite, Ollama ILlmProvider adapter, plan.json + asset storage
tests/ShortsGenerator.Application.Tests  prompt/validation/orchestration tests
tests/                           automated tests (mirrors the src/ layout)
assets/                          original mascot/exercise/equipment/anatomy SVGs + manifest
samples/                         hand-written sample plans for testing the renderer
renderer/                        Remotion + React + TypeScript video renderer (own package.json)
```

`web/` and `tools/` are added later, as the roadmap actually needs them.
