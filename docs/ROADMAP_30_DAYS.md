# ROADMAP_30_DAYS.md

This is a gated roadmap. Claude Code must complete only one day at a time and wait for approval.

---

## Day 1 — Foundation and environment

Goal:
Create a clean repository and verify the development machine.

Do:
- inspect existing repo before changing anything;
- check Git;
- check installed .NET SDK;
- target .NET 10 LTS when available;
- check Node/npm;
- check FFmpeg;
- check Python only because local TTS may need it later;
- check Ollama availability;
- create solution/repo foundations;
- create README and DAY_STATE;
- create safe `.gitignore`;
- run first backend build/test.

Teach:
- solution vs project,
- why .NET 10 LTS,
- repository boundaries,
- why environment verification matters.

Do NOT:
- set up Remotion,
- integrate Ollama,
- build TTS,
- generate a video.

Acceptance:
- backend skeleton builds,
- tests run,
- repo documentation exists.

---

## Day 2 — Domain model and SQLite persistence

Goal:
Represent video jobs properly.

Do:
- domain entities/enums;
- EF Core SQLite infrastructure;
- migrations;
- VideoJob persistence;
- basic repository/service;
- tests for state transitions.

Teach:
- entity,
- enum,
- EF Core,
- migration,
- persistence boundary.

Acceptance:
- create/read a VideoJob in local SQLite.

---

## Day 3 — Local LLM provider foundation

Goal:
Connect ASP.NET Core to Ollama without paid APIs.

Do:
- inspect local hardware and installed Ollama models;
- choose a practical free local model;
- `ILlmProvider`;
- Ollama HTTP adapter;
- health check;
- clear error when Ollama/model is unavailable;
- no structured scene generation yet beyond a small connectivity test.

Teach:
- interface,
- adapter,
- DI,
- HttpClientFactory,
- options/config.

Acceptance:
- API can send a harmless prompt to local Ollama and receive text.

---

## Day 4 — Fitness Shorts script generator

Goal:
Turn a fitness topic into an English Short narration.

Do:
- prompt template;
- input DTO;
- output model;
- safety/length constraints;
- generation service;
- tests around validation and prompt assembly;
- endpoint or service demo.

Teach:
- DTO vs entity,
- prompt as application logic,
- validation,
- keeping AI behind a boundary.

Acceptance:
- topic -> concise English narration locally.

---

## Day 5 — Structured scene planner

Goal:
Turn a script into validated scene JSON.

Do:
- VideoPlan schema;
- Scene models/enums;
- allowed animations and scene types;
- JSON structured generation;
- parse/validation;
- limited repair retry;
- save plan JSON.

Teach:
- schema contracts,
- why untrusted LLM output must be validated,
- deterministic boundaries around AI.

Acceptance:
- script -> valid 6–10 scene VideoPlan.

---

## Day 6 — Asset manifest and original placeholder mascot

Goal:
Create a replaceable visual asset system.

Do:
- asset metadata schema;
- asset repository;
- manifest JSON;
- original simple SVG mascot placeholders;
- initial exercise/equipment/prop assets;
- validate referenced asset IDs.

Do not copy Yellow Dude.

Teach:
- asset IDs vs physical files,
- indirection,
- why reusable assets make automation scalable.

Acceptance:
- planner can only choose known assets.

---

## Day 7 — Remotion renderer setup

Goal:
Render the first programmatic vertical test video.

Do:
- renderer Node/TypeScript project;
- Remotion setup;
- strict TypeScript;
- one composition;
- 1080x1920, 30fps;
- render a simple local MP4.

Teach:
- why Remotion lives in JS/React,
- composition,
- frame-based rendering,
- separation from ASP.NET backend.

Acceptance:
- renderer creates a basic MP4 from static test props.

---

## Day 8 — Scene renderer

Goal:
Render a list of scenes from JSON.

Do:
- shared render schema or generated contract;
- SceneRenderer;
- basic scene layouts;
- load local assets safely;
- deterministic scene order/timing.

Teach:
- renderer contract,
- component architecture,
- why backend should not own visual implementation.

Acceptance:
- sample plan JSON renders as multiple distinct scenes.

---

## Day 9 — Animation primitives

Goal:
Make scenes visually alive.

Implement controlled animations:
- fade,
- slide,
- pop,
- shake,
- punch zoom,
- bounce,
- pulse.

Teach:
- easing,
- interpolation,
- restraint in motion design.

Acceptance:
- JSON animation ID changes scene behavior.

---

## Day 10 — Caption system

Goal:
Add mobile-readable captions.

Do:
- phrase-level captions first;
- safe margins;
- line/chunk rules;
- emphasis animation;
- configurable caption style.

Teach:
- subtitle readability,
- timing,
- why phrase-level V1 is acceptable.

Acceptance:
- captions render correctly over the sample video.

---

## Day 11 — Local Kokoro TTS

Goal:
Generate narration locally with no paid API.

Do:
- set up isolated local TTS tooling;
- `ITtsProvider`;
- Kokoro adapter/CLI integration;
- generate English WAV;
- configuration for voice;
- graceful missing-engine errors.

Teach:
- process integration,
- provider abstraction,
- why local inference has hardware trade-offs.

Acceptance:
- narration text -> local audio file.

---

## Day 12 — Audio duration and timing

Goal:
Synchronize visuals with narration.

Do:
- inspect audio duration;
- establish scene duration allocation;
- reliable caption chunks;
- avoid timing drift;
- tests for timing logic.

Teach:
- media metadata,
- timing math,
- deterministic render plans.

Acceptance:
- visual timeline length matches narration sensibly.

---

## Day 13 — ASP.NET to Remotion integration

Goal:
Backend can trigger a real render.

Do:
- `IRenderer`;
- local Remotion process adapter;
- safe process arguments;
- input JSON/output path conventions;
- capture stdout/stderr;
- timeout/cancellation.

Teach:
- child processes,
- safe command execution,
- process result handling.

Acceptance:
- API/service -> renderer -> MP4.

---

## Day 14 — Background job queue

Goal:
Generation should not block HTTP request lifetime.

Do:
- local in-process queue;
- BackgroundService;
- persisted statuses;
- progress/failure transitions;
- one render at a time initially.

Teach:
- background workers,
- queues,
- request/response vs long-running tasks.

Acceptance:
- POST creates job quickly; background worker finishes it.

---

## Day 15 — First full end-to-end Short

Goal:
Topic -> final video automatically.

Use:
`Why Your Pull-Ups Aren't Increasing`

Pipeline:
- job,
- script,
- scenes,
- assets,
- TTS,
- timing,
- render,
- final MP4.

Do not move on until an actual video is produced or the blocker is fully diagnosed.

Acceptance:
- playable final.mp4 exists.

---

## Day 16 — Visual quality pass

Goal:
Make the output look intentional rather than like a coding demo.

Improve:
- spacing,
- typography,
- backgrounds,
- asset scale,
- hierarchy,
- transitions,
- hook scene energy.

Teach:
- component polish vs business logic.

Acceptance:
- before/after render comparison.

---

## Day 17 — Music and SFX

Goal:
Add controlled sound design.

Do:
- local licensed/free SFX slots;
- optional background music;
- volume defaults;
- fade in/out;
- SFX mapped by controlled IDs.

Acceptance:
- speech remains clear and sound effects are not overwhelming.

---

## Day 18 — Fitness-specific scene templates

Goal:
Increase content variety.

Add templates such as:
- MythVsFact
- ExerciseDemo
- Comparison
- MuscleHighlight
- Progress
- Recovery
- List

Acceptance:
- one plan can visibly mix several template types.

---

## Day 19 — Anti-repetition/originality controls

Goal:
Automation must not become low-value template spam.

Do:
- scene repetition limits;
- vary hook layouts;
- asset reuse constraints inside one video;
- duplicated-script phrase checks where practical;
- require a unique takeaway;
- preserve human review.

Acceptance:
- five generated plans show meaningful structural variation.

---

## Day 20 — Asset library expansion tools

Goal:
Make future art replacement easy.

Do:
- manifest validation command;
- add/replace asset workflow;
- preview/index utilities where useful;
- document naming rules.

Acceptance:
- adding a new asset does not require changing core business logic.

---

## Day 21 — Minimal local dashboard

Goal:
Stop relying only on Swagger/terminal.

Build the simplest useful UI:
- topic input,
- optional script input,
- generate button,
- recent jobs,
- status,
- output path/open action where practical.

Keep it local.

Acceptance:
- user can start a job from the UI.

---

## Day 22 — Asset inspection/selection UI

Goal:
Allow limited human control.

Do:
- view known asset IDs/categories;
- optionally override a scene asset before rerender;
- do not build a full editor.

Acceptance:
- user can change one known scene asset safely.

---

## Day 23 — Plan edit and rerender

Goal:
Cheap human correction without regenerating everything.

Do:
- edit permitted plan fields;
- validate changes;
- rerender using existing narration where valid;
- optionally regenerate narration when text changes.

Acceptance:
- change caption/asset/animation -> rerender.

---

## Day 24 — Backend tests

Goal:
Make the pipeline trustworthy.

Prioritize:
- job transitions,
- schema validation,
- bad Ollama response,
- missing asset,
- TTS/process failure,
- renderer failure,
- path safety,
- API validation.

Acceptance:
- meaningful automated test suite passes.

---

## Day 25 — Renderer/schema tests

Goal:
Prevent visual input contracts from silently breaking.

Do:
- schema parsing tests;
- duration/order tests;
- composition smoke render or equivalent practical checks.

Acceptance:
- invalid render input fails early and clearly.

---

## Day 26 — Logging, retries and recovery

Goal:
Make debugging easy.

Do:
- structured logs;
- per-job log context;
- limited retry policies where appropriate;
- startup handling of interrupted jobs;
- clear error summaries.

Acceptance:
- intentionally failing a dependency results in useful diagnostics.

---

## Day 27 — Performance pass

Goal:
Reduce local generation time without changing quality.

Measure first.

Possible improvements:
- cache stable assets,
- avoid repeated model/TTS initialization when possible,
- control render concurrency,
- skip unchanged stages on rerender.

Acceptance:
- record baseline and improved generation/render timing.

---

## Day 28 — One-click creator workflow

Goal:
Make daily usage easy.

Do:
- simple start scripts;
- local prerequisite checks;
- Generate flow;
- output organization;
- final review checklist.

Do NOT auto-upload.

Acceptance:
- user can go from startup to generated video with minimal commands.

---

## Day 29 — Generate five real sample Shorts

Goal:
Validate the system on varied topics.

Suggested topics:
1. Why Your Pull-Ups Aren't Increasing
2. Is Running Killing Your Gains?
3. Why Soreness Doesn't Mean Muscle Growth
4. Can You Build Muscle With Push-Ups?
5. Why Rest Days Make You Stronger

Review:
- script quality,
- factual wording,
- visual variety,
- narration,
- timing,
- captions,
- repetition.

Fix systemic issues found.

Acceptance:
- five playable distinct Shorts.

---

## Day 30 — V1 finalization

Goal:
A usable local product.

Do:
- final README,
- architecture docs,
- setup instructions,
- troubleshooting,
- configuration documentation,
- license/asset notes,
- clean repo,
- run all tests,
- perform one fresh end-to-end generation.

Then propose, but DO NOT implement without approval:
Phase 2 options:
- stronger paid LLM,
- premium TTS,
- professional mascot assets,
- AI image generation for missing assets,
- cloud rendering,
- YouTube publishing integration,
- analytics,
- SaaS conversion.

Acceptance:
- V1 definition in CLAUDE.md is satisfied.
