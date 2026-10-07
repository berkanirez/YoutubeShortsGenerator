# Local Fitness Shorts Generator

Zero-cost, local-first, automated faceless fitness Shorts generator.
Give it a fitness topic and get back a 9:16 MP4 Short — original mascot
visuals, scene changes, motion, captions, local voice-over, sound effects,
and background music — with as little manual editing as possible.

See `CLAUDE.md` for the full working rules, `docs/PRODUCT_SPEC.md` for what
the product is, `docs/ROADMAP_30_DAYS.md` for the day-by-day build plan, and
`docs/DAY_STATE.md` for what's actually done so far.

## Status

Day 4 of 30 — topic → real English narration via Ollama, validated (length +
basic medical-claim safety) with bounded retry, persisted on the `VideoJob`.
No scene planning, TTS audio, assets, or video rendering exist yet. Check
`docs/DAY_STATE.md` for the current day.

## Prerequisites

| Tool | Required for | Status on this machine |
|---|---|---|
| Git | version control | installed |
| .NET 10 SDK | `src/` backend | installed |
| Node.js / npm | `renderer/` (Remotion, from Day 7) | installed |
| Python | local TTS tooling (from Day 11) | installed |
| FFmpeg | media processing (from Day 12+) | not installed yet |
| Ollama | local LLM (from Day 3) | **installed** — `llama3.2:3b` pulled |

Ollama runs as a local background app/service on `http://localhost:11434`
(installed via `winget install Ollama.Ollama`). FFmpeg isn't needed until
Day 12 and is not required to run what exists today.

## Running the API (Day 1 skeleton)

```
dotnet run --project src/ShortsGenerator.Api
```

In Development, Swagger/OpenAPI JSON is available at
`http://localhost:<port>/openapi/v1.json`. Endpoints today:

```
GET  /api/health
POST /api/videos        { "topic": "...", "sourceScript": "..." (optional) }
GET  /api/videos/{id}
POST /api/videos/{id}/script   generates and attaches a narration for this job (Created -> Narrating)
GET  /api/llm/health    Ollama reachable? Is the configured model pulled?
POST /api/llm/ping      sends a harmless test prompt, returns Ollama's text reply
```

A local SQLite database (`src/ShortsGenerator.Api/shortsgenerator.db`,
git-ignored) is created and migrated automatically on startup. The Ollama
base URL and model are configured under `"Ollama"` in `appsettings.json`
(default: `http://localhost:11434`, `llama3.2:3b`) — change the model there,
not in code, if you have a different one pulled.

## Running tests

```
dotnet test
```

## Repository layout (grows day by day, see the roadmap)

```
CLAUDE.md                        working rules for this project
docs/                            product spec, roadmap, day state, architecture
src/ShortsGenerator.Api          ASP.NET Core API (DI wiring, endpoints)
src/ShortsGenerator.Domain       VideoJob entity, status rules, repository interface
src/ShortsGenerator.Application  use-case services (VideoJobService)
src/ShortsGenerator.Infrastructure  EF Core + SQLite, Ollama ILlmProvider adapter
tests/ShortsGenerator.Application.Tests  prompt/validation/orchestration tests
tests/                           automated tests (mirrors the src/ layout)
```

`renderer/`, `assets/`, `web/`, `tools/`, and `output/` are added later, as
the roadmap actually needs them.
