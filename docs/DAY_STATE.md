# DAY_STATE.md

Current day: 4
Status: WAITING_APPROVAL
Date: 2026-10-06

Completed:
- Domain: `ScriptValidator` (static, pure) — enforces a 40-130 word window
  (≈25-45s of spoken narration) and rejects a short denylist of hard
  medical-claim phrases ("cure", "diagnose", "prescribe", "guaranteed
  results", etc.) per `CLAUDE.md` §4. `ScriptValidationResult` record.
  `ScriptGenerationException` for "could not produce a valid script after
  retries".
- Domain: `IVideoJobRepository` gained `UpdateAsync` — a real gap found
  while wiring the first actual job-lifecycle flow; Day 2's interface only
  had `AddAsync`/`GetByIdAsync`/`ListRecentAsync`, nothing to persist a
  status transition on an existing job. Implemented in
  `VideoJobRepository` via `DbSet.Update` + `SaveChangesAsync`.
- Application: `ScriptPromptBuilder` (pure, testable prompt assembly —
  includes the topic, word-count target, safety rules, and, on retries, the
  previous attempt's rejection reasons so the model can self-correct).
  `ScriptTextCleaner` (strips wrapping quotes, known preambles like "Here's
  your script:", collapses whitespace). `ScriptGenerationService`
  (orchestrates: load job -> `MarkPlanning` -> generate+validate with up to
  3 attempts, feeding failures back into the prompt -> `MarkNarrating` on
  success or `MarkFailed` on exhaustion/Ollama outage -> persist).
- Api: `POST /api/videos/{id}/script` — triggers generation for an existing
  job; 200 with the updated job on success, 404 unknown job, 503 + message
  if Ollama is unavailable, 422 + message if validation never passed after
  retries.
- New test project `ShortsGenerator.Application.Tests` (first one) with
  hand-written `FakeLlmProvider` (queued canned responses/exceptions,
  records received prompts) and `InMemoryVideoJobRepository` — same
  "no mocking library" convention as Day 3's `FakeHttpMessageHandler`.

Tests:
- `dotnet build` — succeeded, 0 warnings, 0 errors (9 projects).
- `dotnet test` — 46/46 passed (20 Domain, 16 Application, 9 Infrastructure,
  1 Api).
- Manual end-to-end check against the real running Ollama + `llama3.2:3b`:
  `POST /api/videos` (topic "Why Your Pull-Ups Aren't Increasing") -> `POST
  /api/videos/{id}/script` -> real 110-word, on-topic, safe narration
  returned on the FIRST attempt, job status `Narrating`. `GET
  /api/videos/{id}` confirmed the script round-tripped through SQLite.
  `POST .../script` on a random unknown id -> 404.

Known issues:
- `ScriptGenerationService` only marks the job `Failed` for
  `LlmUnavailableException`/`ScriptGenerationException`; any other
  unexpected exception propagates without updating job status (intentional
  for now — silently converting every exception into "job failed" would
  hide real bugs during development; recovery for arbitrary crashes is
  Day 14/26 territory).
- The medical-claim denylist is a deliberately small, last-resort net, not
  a real safety classifier — acceptable for Day 4's "turn a topic into a
  narration" scope; broader myth-busting/evidence review is Day 29.
- No scene/animation plan yet — `GeneratedScript` is just narration text;
  Day 5 turns it into a validated, schema-checked `VideoPlan`/scene list.

User approval:
- Day 1 approved ("geçebilirsin") on 2026-09-15.
- Day 2 approved ("diğer güne geçelim") on 2026-10-06.
- Day 3 approved ("geçebilirsin") on 2026-10-06.
- Day 4 not yet approved — waiting for explicit "devam/onay/sonraki güne
  geç" before starting Day 5.
