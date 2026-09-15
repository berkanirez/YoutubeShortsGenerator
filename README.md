# Local Fitness Shorts Generator

Zero-cost, local-first, automated faceless fitness Shorts generator.
Give it a fitness topic and get back a 9:16 MP4 Short — original mascot
visuals, scene changes, motion, captions, local voice-over, sound effects,
and background music — with as little manual editing as possible.

See `CLAUDE.md` for the full working rules, `docs/PRODUCT_SPEC.md` for what
the product is, `docs/ROADMAP_30_DAYS.md` for the day-by-day build plan, and
`docs/DAY_STATE.md` for what's actually done so far.

## Status

Day 1 of 30 — backend skeleton only. No script generation, TTS, assets, or
video rendering exist yet. Check `docs/DAY_STATE.md` for the current day.

## Prerequisites (checked on Day 1)

| Tool | Required for | Status on this machine |
|---|---|---|
| Git | version control | installed |
| .NET 10 SDK | `src/` backend | installed |
| Node.js / npm | `renderer/` (Remotion, from Day 7) | installed |
| Python | local TTS tooling (from Day 11) | installed |
| FFmpeg | media processing (from Day 12+) | **not installed yet** |
| Ollama | local LLM (from Day 3) | **not installed yet** |

FFmpeg and Ollama aren't needed until later days and are not required to run
what exists today.

## Running the API (Day 1 skeleton)

```
dotnet run --project src/ShortsGenerator.Api
```

In Development, Swagger/OpenAPI JSON is available at
`http://localhost:<port>/openapi/v1.json`. The only endpoint today is:

```
GET /api/health
```

## Running tests

```
dotnet test
```

## Repository layout (grows day by day, see the roadmap)

```
CLAUDE.md              working rules for this project
docs/                   product spec, roadmap, day state, architecture
src/ShortsGenerator.Api ASP.NET Core API (currently the only backend project)
tests/                  automated tests
```

`renderer/`, `assets/`, `web/`, `tools/`, and `output/` are added later, as
the roadmap actually needs them.
