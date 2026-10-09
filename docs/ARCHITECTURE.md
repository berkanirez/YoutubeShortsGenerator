# Architecture (V1)

A modular monolith plus two local helper processes. Everything runs on one PC; nothing
calls a paid or remote service.

```
 Browser (web/ dashboard)  ─┐            generate.cmd / curl
                            ▼                    │
 ┌───────────── ShortsGenerator.Api (ASP.NET Core, .NET 10) ─────────────┐
 │ minimal API endpoints · dashboard static files · error handler        │
 │                                                                       │
 │  Application: ScriptGenerationService → ScenePlanningService →       │
 │               NarrationService → RenderService   (VideoGenerationPipeline)
 │               PlanEditService · ReviewService · OutputIndexService    │
 │               VideoJobScheduler · JobRecoveryService · AssetMatcher   │
 │  Domain:      VideoJob (state machine) · VideoPlan / Scene · rules    │
 │  Infrastructure: EF Core SQLite · Ollama HTTP · Kokoro / Remotion via │
 │               ProcessRunner · file storage · job.log provider         │
 │  Background:  Channel<T> queue → VideoJobWorker (one job at a time)   │
 └──────┬──────────────────┬───────────────────────┬────────────────────┘
        │ HTTP              │ child process          │ child process
        ▼                   ▼                        ▼
   Ollama (llama3.2:3b)  Python + Kokoro-82M      Node + Remotion → headless Chrome + ffmpeg
   127.0.0.1:11434        tools/tts/               renderer/
        │                   │ narration.wav          │ final.mp4
        └───────────────────┴──────► output/<job-id>/ ◄┘   (plan.json, review.md, job.log, ...)
```

## Projects and boundaries

| Project | Owns | Depends on |
|---|---|---|
| `Domain` | `VideoJob` and its status rules, `VideoPlan`/`Scene`, controlled vocabularies (scene types, animations, sfx, music, caption styles), validators and rules (`VideoPlanValidator`, `ScriptValidator`, `PlanVariationRules`, `OnScreenNumbers`, `NarrationSegmenter`, `SceneTimingCalculator`), the ports (`ILlmProvider`, `ITtsProvider`, `IRenderer`, `IAssetRepository`, `IVideoJobRepository`, `IVideoPlanStore`, ...) | nothing |
| `Application` | use cases: script, plan, narration, render, edits, review, recovery, scheduling, the pipeline; prompt building; asset matching | Domain |
| `Infrastructure` | adapters: EF Core + SQLite, Ollama, Kokoro, Remotion, process running (Job Object, power throttling), files, logging, the queue and worker | Domain, Application |
| `Api` | HTTP endpoints, DTOs, DI wiring, dashboard hosting, error responses | all |
| `renderer/` (Node) | presentation: Zod schema of the plan, scene templates, animations, captions, audio mix | the plan.json contract only |
| `tools/` | Kokoro script + setup, audio asset generator, asset tool, perf and workflow scripts | — |

The replaceable parts are behind ports (CLAUDE.md section 21): another LLM, TTS engine or
renderer is a new adapter class and one DI registration, no change to the use cases.

## A job's life

```
POST /api/videos ─► VideoJob Created ─► VideoJobScheduler: PendingWork saved, then queued
  worker ─► Script (Ollama, or the user's own script; validated, near-duplicate check)
         ─► Plan   (narration split in code; Ollama picks visuals under a JSON Schema;
                    validation, variety rules, AssetMatcher, on-screen numbers rule)
         ─► Narration (Kokoro, one segment per scene) ─► scene timing from the real audio
         ─► Render (Remotion; skipped when the inputs' fingerprint is unchanged)
         ─► Completed: final.mp4, review.md, output/INDEX.md updated, PendingWork cleared
```

* **States** (`VideoJob`): Created → Planning → Narrating → Rendering → Completed, any of the
  first four → Failed. Completed → Rendering (re-render) and Completed → Narrating (narration
  edit) are allowed; Failed → Created/Planning/Narrating only through Retry. The whole table
  is one test.
* **Resumable**: each stage looks at what is persisted (script, plan, timed plan + audio), so
  a job continues where it is - after a manual step, a Retry, or a crash.
* **Crash recovery**: `PendingWork` is the durable half of the in-memory queue; at startup
  `JobRecoveryHostedService` resumes it (a render restarts from Narrating) and gives up after
  three interruptions in a row. Child processes are in a Windows Job Object and die with the API.
* **Edits** (`PlanEditService`) go through the same validators as a generated plan and are
  locked while a job is queued or running; a narration edit marks the plan
  `NarrationOutdated`, and `apply-edits` regenerates the voice-over before rendering.

## The plan.json contract

The backend writes `output/<job-id>/plan.json`; the renderer validates it with Zod
(`renderer/src/schemas/videoPlan.ts`, including cross-field rules) before drawing anything.
`samples/contract-plan.json` is produced by the backend's own serializer
(`PlanContractTests`) and parsed by the renderer's tests, so a change on either side fails a
test instead of a render.

## Files on disk

```
output/<job-id>/plan.json            the plan (atomic writes)
               narration.wav         Kokoro output, one segment per scene
               final.mp4             the video (written as final.rendering.mp4, then renamed)
               final.inputs.sha256   fingerprint of what final.mp4 was made from
               review.md             automatic checks, warnings, scenes, the human checklist
               review-state.json     which checklist items a person ticked, for which render
               job.log               every log line of this job
output/INDEX.md                      finished videos by date and title
src/ShortsGenerator.Api/shortsgenerator.db   SQLite: jobs and their state
```

## Decisions worth knowing (with the day they were made)

* LLM never writes narration-dependent structure: the narration is split in code, the model
  only chooses visuals, constrained by a JSON Schema (Day 15).
* Scene timing comes from the real audio, not from estimates (Day 12).
* Rules that a small model breaks often are repaired in code instead of retried
  (asset repetition, Day 29); rules that protect facts are enforced in code even when the
  model keeps breaking them (on-screen numbers, Day 29).
* Variety is checked against videos that were actually made, not drafts (Day 27).
* Processes are started with `ArgumentList`, never a shell; nothing from an LLM or a user
  becomes a path or a command (CLAUDE.md section 18).
* One render at a time; no external queue, cache or database server (CLAUDE.md section 15).
