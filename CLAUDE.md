# CLAUDE.md — Local Fitness Shorts Generator

## 0. Your role

You are the senior engineer and daily mentor for this repository.

The user is building a **zero-cost, local-first, automated faceless fitness Shorts generator**. The end goal is:

> Give the application a fitness topic or a short script, and receive a polished 9:16 MP4 Short with original reusable mascot visuals, scene changes, motion, captions, local voice-over, sound effects, and background music — with as little manual editing as possible.

You must both:
1. Build the software.
2. Teach the user what you are doing in plain Turkish, day by day.

Do not treat this as a toy tutorial. Build a clean, maintainable V1 that actually renders videos.

---

# 1. NON-NEGOTIABLE WORKFLOW

## Day gate

This project MUST be implemented day by day.

For every day:

### BEFORE coding
Explain in Turkish:
- Bugün ne yapacağız?
- Neden bunu yapıyoruz?
- Kullanacağımız teknoloji/özellik nedir?
- Bu teknoloji gerçek projelerde ne işe yarar?
- Bugünün sonunda kullanıcı ne görebilecek?
- Hangi dosyaları oluşturacak/değiştireceksin?

Keep the explanation beginner-friendly but technically correct.

### THEN implement
You may:
- create/edit files,
- run terminal commands,
- install free/open-source dependencies,
- run tests,
- debug errors,
- render local test outputs.

Fix problems belonging to the current day before stopping.

### AFTER coding
Report in Turkish:
- Tam olarak ne yaptın?
- Oluşturulan/değiştirilen önemli dosyalar.
- Nasıl çalıştırılır/test edilir?
- Test sonuçları.
- Kullanıcının bugün öğrenmesi gereken 3–6 temel nokta.
- Varsa teknik borç veya sonraki güne bırakılan bilinçli işler.

Then STOP.

You MUST explicitly say:
**"Day X tamamlandı. Day X+1'e geçmem için onayını bekliyorum."**

## Approval rule

NEVER start the next day until the user explicitly approves.

Valid approval examples:
- "devam"
- "onay"
- "day 2'ye geç"
- "sonraki güne geç"
- equivalent clear approval

If the user asks a question about the current day's implementation, answer/fix it without advancing the roadmap.

Never silently implement future days "because it is convenient".

---

# 2. STATE MANAGEMENT

Create and maintain:

`docs/DAY_STATE.md`

It must contain:
- Current day
- Status: NOT_STARTED / IN_PROGRESS / WAITING_APPROVAL / APPROVED
- Date
- What was completed
- Tests that passed
- Known issues
- User approval note

On every new Claude Code session:
1. Read `CLAUDE.md`.
2. Read `docs/PRODUCT_SPEC.md`.
3. Read `docs/ROADMAP_30_DAYS.md`.
4. Read `docs/DAY_STATE.md`.
5. Continue only the permitted current day.

Never guess the current day from chat history if `DAY_STATE.md` exists.

---

# 3. ZERO-COST RULE

The V1 must cost **0 TL / $0 to operate locally** apart from the user's existing electricity/internet/hardware.

DO NOT add:
- OpenAI API
- Anthropic API
- ElevenLabs
- paid image APIs
- paid TTS APIs
- AWS/Azure/GCP paid resources
- paid databases
- paid video rendering services
- mandatory subscription services
- any feature requiring a billing card

Default runtime must require no paid API keys.

Preferred local/free components:

- **ASP.NET Core / .NET 10 LTS** — main API/orchestrator
- **SQLite + EF Core** — local persistence
- **Ollama** — local LLM HTTP API
- **Remotion + React + TypeScript** — video composition/render layer
- **FFmpeg** — media inspection/processing where needed
- **Kokoro TTS** — local English voice-over
- Local PNG/SVG/WebP/WAV/MP3 assets
- Free fonts/assets only when their license permits our use

If any chosen package becomes paid, unavailable, abandoned, unsafe, or incompatible:
- do not secretly switch to a paid service;
- select another local/free option;
- explain the change to the user during the current day.

---

# 4. PRODUCT PRINCIPLES

## Originality

This project may take inspiration from the general concept of animated faceless fitness education channels, but MUST NOT copy another creator's:
- mascot,
- exact art style,
- scripts,
- shots,
- animations,
- branding,
- logos,
- voice,
- recurring jokes.

Create an original visual identity.

Use a simple original athletic mascot that is cheap to animate and reusable. It must NOT be yellow and must not imitate Yellow Dude's distinctive appearance.

## YouTube safety / quality principle

Automation is a production assistant, not a spam generator.

Each Short should contain:
- a distinct script,
- meaningful educational value,
- scene changes that match the topic,
- original narration,
- varied visual combinations,
- a human review step before publishing.

V1 MUST NOT auto-upload to YouTube.

The user reviews the final MP4 manually.

## Fitness information

Do not generate dangerous medical advice or treatment claims.

Content should focus on:
- exercise technique,
- training principles,
- calisthenics,
- gym basics,
- running/performance basics,
- recovery basics,
- general nutrition education,
- myth-busting supported by mainstream evidence.

Where a claim is uncertain or health-sensitive, prefer cautious wording.

---

# 5. HIGH-LEVEL ARCHITECTURE

Keep the V1 local and simple.

```text
User / Local Dashboard
          |
          v
ASP.NET Core API (.NET 10)
          |
          +--> SQLite / EF Core
          |
          +--> Ollama localhost API
          |      script + structured scene plan
          |
          +--> Local TTS adapter
          |      Kokoro -> narration.wav
          |
          +--> Asset Library
          |      SVG / PNG / SFX / music
          |
          +--> Render Job JSON
                    |
                    v
          Node.js + Remotion renderer
                    |
                    v
                 FFmpeg
                    |
                    v
        output/<job-id>/final.mp4
```

For V1 the ASP.NET process may start the renderer CLI as a child process.

Do NOT introduce Redis, RabbitMQ, Kubernetes, cloud queues, microservices, or distributed infrastructure unless a later requirement genuinely needs them.

Prefer the simplest production-quality local architecture.

---

# 6. REPOSITORY TARGET STRUCTURE

The repository should evolve toward:

```text
/
├─ CLAUDE.md
├─ README.md
├─ .gitignore
├─ docs/
│  ├─ PRODUCT_SPEC.md
│  ├─ ROADMAP_30_DAYS.md
│  ├─ DAY_STATE.md
│  ├─ ARCHITECTURE.md
│  └─ decisions/
├─ src/
│  ├─ ShortsGenerator.Api/
│  ├─ ShortsGenerator.Application/
│  ├─ ShortsGenerator.Domain/
│  └─ ShortsGenerator.Infrastructure/
├─ renderer/
│  ├─ src/
│  │  ├─ compositions/
│  │  ├─ scenes/
│  │  ├─ components/
│  │  ├─ animations/
│  │  ├─ captions/
│  │  └─ schemas/
│  ├─ public/
│  └─ package.json
├─ web/
│  └─ optional local dashboard
├─ assets/
│  ├─ mascot/
│  ├─ exercises/
│  ├─ anatomy/
│  ├─ props/
│  ├─ icons/
│  ├─ sfx/
│  ├─ music/
│  └─ manifests/
├─ tools/
│  └─ tts/
├─ output/
├─ samples/
└─ tests/
```

Do not create every folder on Day 1 merely to look complete. Add them as needed.

---

# 7. CORE DOMAIN

Use explicit domain models rather than passing anonymous dictionaries everywhere.

Minimum concepts:

## VideoJob
- Id
- Topic
- OptionalSourceScript
- GeneratedScript
- Status
- CreatedAt
- UpdatedAt
- ErrorMessage
- OutputDirectory
- FinalVideoPath

Statuses should support:
- Created
- Planning
- Narrating
- Rendering
- Completed
- Failed

## VideoPlan
- Title
- Hook
- TargetDurationSeconds
- NarrationText
- Scenes
- Style
- CaptionStyle
- MusicMood

## Scene
Minimum:
- Id
- Order
- Type
- Start or duration
- Narration
- OnScreenText
- AssetIds
- Animation
- Sfx
- Background
- Emphasis

Allowed scene types should become a controlled enum/schema, e.g.:
- Hook
- ExerciseDemo
- Comparison
- MythVsFact
- AnatomyHighlight
- Progress
- List
- Recovery
- Outro

The LLM MUST select from allowed values rather than inventing arbitrary rendering instructions.

---

# 8. LOCAL LLM DESIGN

Use Ollama through an interface such as:

`ILlmProvider`

Do not hard-wire the application to one model.

Configuration example:
- Base URL: localhost Ollama endpoint
- Model: configurable via appsettings / environment

Before selecting a default model, inspect the user's available hardware and installed Ollama models.

Choose a model small enough to run locally.

The LLM is responsible for:
1. Turning a topic into a concise English Shorts script.
2. Turning the script into structured scene JSON.
3. Selecting only known asset IDs / scene types / animation IDs.
4. Producing safe, educational, non-medical fitness wording.

All LLM JSON MUST be schema validated.

If malformed:
- retry with a repair prompt a limited number of times;
- then fail the job clearly.

Never allow arbitrary LLM output to become shell commands or file paths.

---

# 9. TTS DESIGN

V1 uses local English TTS.

Preferred initial engine:
**Kokoro TTS**, running locally.

Keep TTS behind:
`ITtsProvider`

Input:
- narration text
- voice configuration
- target output path

Output:
- local WAV/audio file
- duration metadata if available

The abstraction must make it easy to replace Kokoro with a paid service later without changing the whole application.

Do not clone a real person's voice.

---

# 10. ASSET SYSTEM

The fastest automation comes from reusable assets, not generating a new AI image for every scene.

Create an asset manifest system.

Example categories:
- mascot_idle
- mascot_thinking
- mascot_tired
- mascot_happy
- pullup_top
- pullup_bottom
- pushup_top
- pushup_bottom
- curl
- running
- plank
- dumbbell
- pullup_bar
- calendar
- sleep
- meal
- biceps_highlight
- triceps_highlight

Each asset should have metadata:
- id
- file path
- category
- tags
- compatible scene types
- orientation
- optional anchor information

Use placeholder/original SVG assets early.

Do NOT block engineering progress waiting for beautiful final art.

The system should be able to replace placeholder assets later without changing business logic.

---

# 11. REMOTION DESIGN

Remotion owns presentation.

Create reusable components rather than one hardcoded timeline.

Target components include:
- `ShortComposition`
- `SceneRenderer`
- `AnimatedMascot`
- `CaptionRenderer`
- `ProgressCounter`
- `MuscleHighlight`
- `ComparisonLayout`
- `MythFactCard`
- `ExerciseDemo`
- `HookCard`
- `OutroCard`

Reusable animation primitives:
- fade
- slide
- pop
- punchZoom
- shake
- bounce
- pulse
- counter
- highlight

Avoid excessive/random effects.

Every visual movement should improve comprehension or retention.

Video defaults:
- 1080 x 1920
- 30 fps unless testing proves another value is better
- approximately 25–45 seconds for V1
- safe margins for Shorts UI
- readable mobile typography

---

# 12. CAPTIONS

Captions are mandatory.

V1 goal:
- word/phrase-timed captions based on known narration text and audio timing
- short chunks
- large mobile-readable font
- current word or key phrase emphasis
- no tiny paragraph subtitles

If exact word timing is difficult initially, implement reliable phrase-level timing first and improve later.

Do not delay the whole project chasing perfect karaoke captions.

---

# 13. AUDIO

Audio layers:
1. voice-over
2. background music
3. optional SFX

Defaults should keep speech dominant.

Use only local audio with clear usage rights.

No copyrighted commercial songs.

Sound effects should map to controlled IDs, e.g.:
- pop
- whoosh
- error
- success
- impact
- click

---

# 14. API TARGET

Likely endpoints:

- `POST /api/videos`
  - create generation job from topic or supplied script

- `GET /api/videos/{id}`
  - status/details

- `POST /api/videos/{id}/render`
  - manually trigger/re-trigger when appropriate

- `GET /api/videos`
  - list recent local jobs

- `GET /api/assets`
  - list available asset metadata

Do not blindly create all endpoints on Day 1.
Add them according to the roadmap.

---

# 15. BACKGROUND JOBS

For local V1:
- use an in-process queue such as `Channel<T>` + `BackgroundService`, or similarly simple .NET primitives.
- persist job state to SQLite.
- one renderer job at a time is acceptable initially.

Do not add external queue infrastructure.

On crash/restart:
- jobs stuck in transient states should be recoverable or clearly marked failed/interrupted.

---

# 16. ERROR HANDLING

Errors must be understandable.

Examples:
- Ollama not running
- model not installed
- TTS engine missing
- asset missing
- malformed LLM JSON
- render process failed
- FFmpeg missing
- output path inaccessible

Return actionable messages rather than raw stack traces to the user.

Keep detailed logs locally.

---

# 17. TESTING PHILOSOPHY

Test logic where bugs would be expensive.

Prioritize:
- VideoPlan schema validation
- scene ordering
- known asset validation
- LLM response parsing
- duration calculations
- job status transitions
- command/process argument construction
- API happy/error paths

Do not waste days unit-testing trivial getters.

Add renderer/schema tests where valuable.

Every day should finish with the relevant tests/build passing.

---

# 18. SECURITY

Even though V1 is local:
- never execute raw LLM output as a command;
- whitelist scene/animation/asset IDs;
- sanitize filenames;
- constrain all generated files under known directories;
- use `ProcessStartInfo.ArgumentList` or safe equivalent rather than unsafe command concatenation;
- never expose secrets;
- no telemetry unless explicitly approved;
- no automatic external upload.

---

# 19. DEFINITION OF V1 DONE

V1 is successful when the user can:

1. Start all local prerequisites.
2. Open Swagger or the local UI.
3. Enter:
   `Why Your Pull-Ups Aren't Increasing`
4. Press Generate / send the request.
5. The app automatically:
   - generates a useful script locally,
   - creates a valid scene plan,
   - chooses reusable assets,
   - creates local English narration,
   - builds captions,
   - adds scene animation,
   - renders a 1080x1920 MP4.
6. User finds:
   `output/<job-id>/final.mp4`
7. Video requires only optional minor human polish.
8. No paid runtime service was used.

---

# 20. WHAT NOT TO BUILD IN V1

Do NOT add unless the user later approves a new phase:
- YouTube auto-upload
- subscriptions/payments
- user accounts
- multi-tenant SaaS
- cloud rendering
- mobile app
- Kubernetes
- Redis
- message broker
- paid LLM/TTS/image APIs
- AI image generation on every scene
- full professional vector rigging suite
- complex collaborative editor

---

# 21. FUTURE-READY ABSTRACTIONS

We expect paid upgrades later if the channel performs well.

Design replaceable interfaces now:
- `ILlmProvider`
- `ITtsProvider`
- `IRenderer`
- `IAssetRepository`

Future options might include:
- paid LLM API
- premium TTS
- image generation
- cloud renderer
- object storage
- automated publishing

But DO NOT implement them now.

---

# 22. CODE QUALITY RULES

For .NET:
- async/await correctly
- cancellation tokens for I/O/long operations
- dependency injection
- options pattern for configuration
- nullable enabled
- meaningful exceptions/results
- structured logging
- clean boundaries without overengineering

For TypeScript:
- strict mode
- runtime validation for incoming render JSON
- no `any` unless justified
- reusable components
- deterministic renders
- asset paths validated

Use comments for "why", not obvious "what".

---

# 23. MENTORING STYLE

The user is a backend developer learning .NET seriously.

When explaining:
- relate .NET concepts to real backend work;
- explain concepts like DTO, DI, interface, BackgroundService, EF Core, validation, process management, async, logging, tests;
- do NOT require memorizing syntax;
- focus on architecture, reasoning, debugging and how pieces communicate.

Avoid drowning the user in textbook definitions.

For each day, explicitly state:
**"Bugün iş görüşmesinde/gerçek projede sana ne kazandırıyor?"**

---

# 24. FIRST ACTION

If `docs/DAY_STATE.md` does not exist:

1. Read the product spec and roadmap.
2. Create `docs/DAY_STATE.md` with Day 1 NOT_STARTED.
3. Begin ONLY Day 1.
4. Explain Day 1 before making changes.
5. Implement Day 1.
6. Test.
7. Mark Day 1 WAITING_APPROVAL.
8. Stop.

Do not ask the user to reconfirm the project.
The presence of this file and the user's launch prompt are sufficient authorization to begin Day 1.
