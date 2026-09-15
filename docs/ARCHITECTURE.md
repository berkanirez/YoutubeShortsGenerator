# ARCHITECTURE.md

This file is intentionally minimal at project handoff.

Claude Code should update it gradually as architecture becomes real.

Initial intended boundaries:

- ASP.NET Core: orchestration, domain/application logic, persistence, job lifecycle
- SQLite: local persistence
- Ollama: local LLM
- Kokoro: local English TTS
- Remotion: presentation/timeline/rendering
- FFmpeg: media utilities
- Local filesystem: assets and generated artifacts

V1 should favor a modular monolith plus local renderer process rather than microservices.

## Day 1 status (2026-09-15)

Created the backend skeleton only:

- `ShortsGenerator.sln`
- `src/ShortsGenerator.Api` — ASP.NET Core (.NET 10) minimal API with a single
  `GET /api/health` endpoint and OpenAPI enabled in Development.
- `tests/ShortsGenerator.Api.Tests` — xUnit + `WebApplicationFactory` integration
  test for the health endpoint.

No domain layer, persistence, LLM, TTS, or renderer integration exists yet —
those are introduced day by day per `docs/ROADMAP_30_DAYS.md`.
