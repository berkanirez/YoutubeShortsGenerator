# DAY_STATE.md

Current day: 30 (final)
Status: WAITING_APPROVAL — V1 done; Phase 2 is proposed only (docs/PHASE_2_OPTIONS.md)
and is NOT to be built without the user's explicit approval.
Date: 2026-10-11

Post-V1 polish (user requests, 2026-10-09/10 — not a roadmap day):
- Voice am_puck (speed 1.0; a 0.85 slowdown was tried and reverted at the user's request).
- Own mascot + 37 PNG artworks (exercise pairs incl. dip/curl/deadlift, lunge, running,
  10 muscle highlights, icons, 10 mascot poses); catalog 40 assets, 0 warnings.
  prepare_art.py gained --pair (split + floor-aligned) and --keep-holes.
- Hook: HookRule (first sentence <=14 words, speaks to "you", no slow openers) as a soft
  retry; the hook sentence gets the first scene to itself.
- Theme: flat pale backgrounds + static diagonal stripe, dark text, slanted sports
  badges, diagonal wipe transition; no gradients/glows (read as a tech/AI channel).
- Visual-first layout: the visual fills the frame (1000-1120 px), no cards/badges/bubbles.
  Text = a 1-4 word slogan painted on the wall (Impact, tilted, paints on); longer text is
  left to the captions (wall.ts). Signs instead of words: red X -> green check
  (MythVsFact/Comparison share VerdictTemplate), giant "?" on question hooks, painted rep
  count; List/Recovery tips take turns on the wall one at a time. Prompt: onScreenText 1-3 words.
- Dark gym-grey background for every scene (accent per type; accent slash on the wipe).
  Art re-cut tight to the figure (prepare_art.py, longer side 1280) and fitted bottom-aligned
  in a full-width box reaching y=1680: the mascot fills most of the screen.
- Closing: 2 s after the last word (timing.ts CLOSING_SECONDS) - slow push-in, music fades,
  last 0.6 s eases to black; captions stay on the narrated part.
- HD: all art upscaled 4x with Real-ESRGAN anime (local, free, GPU) and prepared at 2048 px;
  render JPEG quality 95 / CRF 15 (crisp outlines). Binary + HD cache gitignored.
- Art automation: docs/ART_AUTOMATION_PLAN.md rewritten (gaps filled during generation);
  plan only, waiting for the owner's 3 decisions (provider, pre-review use, limits).
- Pictures-needed loop (art still made by hand): ArtGapDetector decides each scene's subject
  from its own words (assets/manifests/subjects.json, 67 subjects); missing art -> job status
  WaitingForArt + art-requests.md with ready prompts; dashboard upload / assets/incoming +
  continue.cmd; ArtImporter = Real-ESRGAN + prepare_art + catalog add; catalog reloads live.
  Verified: forearms -> forearms-highlight + barbell requested (not biceps); plank re-import
  end to end (17 s import, video completed; catalog restored afterwards).
- Storytelling: StoryFormats (Journey, Countdown, InsideTheBody, Levels, MythStory) picked from
  the topic; length reminder last in the prompt (first try overshot to 168 words).
- Music: old-school boom-bap loops synthesized (tools/audio/generate_audio_assets.py: kick,
  roomy snare, swung hats, syncopated bass, Am9-Dm9-Fmaj7-E7 Rhodes chops, vinyl crackle;
  16 bars with variation; upbeat 92 BPM, calm 80 BPM; RMS -16 dBFS). Music gain 0.16 ->
  measured 17.4 dB under the voice. SFX unchanged.
- Bigger, grounded, alive mascot: art placed by its measured figure bounds (no transparent
  margin), feet exactly on a drawn floor (FLOOR_Y 1700, layout.ts); anchors bottom/top/center
  (hanging poses hang from the bar, icons centered); drop shadow removed. Transform motion
  (squash/sway/bob) was tried and removed - the owner found it "jelly-like"; real animation
  will come from drawn frames (sprite flipbook, pilot pending). Mid-scene pose reactions stay
  (poses.ts: myth stop -> thumbs-up, hook thinking -> pointing, outro happy -> thumbs-up).
- Variety: VisualDirector picks mascot expressions, hook animation and look set (variant:
  hook layout x 3 wipe styles) least-recently-used over the last 5 finished videos;
  story format rotates the same way (detected from recent scripts); a subject's picture in
  at most 2 scenes per video.
- Art library push: subjects.json 94 entries (+10 icons, +17 mascot expressions as kind
  MascotPose, joined to VisualDirector pools / poses.ts reactions once imported). AssetTool
  `prompts` writes docs/ART_PROMPTS_MISSING.md (70 ready prompts, same builder as the videos);
  AssetTool `import` imports every matching file in assets/incoming (HD + cut-out + catalog).
- Sheets: ArtSheetPlanner groups missing pictures into sheets (characters 4 in a row, exercises
  2x2/1x3, icons 3x3, exercise pairs 2 rows) -> 70 pictures in 20 prompts; split_sheet.py cuts
  them at the emptiest gaps; AssetTool import cuts sheets (assets/incoming/sheets.json) then
  imports. Verified with a fake sheet end to end (catalog restored afterwards).
- Owner's art batch imported (2026-10-11): 83 new pictures (catalog 123, 0 missing) from 19
  sheets + singles. split_sheet.py: --pairs (column-first, keeps drawn heights so bar/floor stay
  registered) and edge-fragment cleanup; mascot-mind-blown fragment removed. Animation test
  frames saved as assets/incoming/pushup-cycle.png, mascot-point-anim.png (not used yet).
- Fixes from the 5-video review: scene prompt example "Full range" was copied into every hook
  (examples removed); AssetMatcher counted "muscle-up" as "up" (push-ups -> muscle-up) and
  named a subject when its words merely occurred - now whole tags / phrases only.
- Review videos: b7ee963a, 9b2a4a44, 45ad4fd2, ccd39551, d8bc92b9 (first batch fe3050d1,
  7302558a, dfe30c0e, 8955df5c, eb08fce0 has the "Full range" hook bug).
- Owner verdict on the 5 review videos: too similar, mascot always front-on, text carries the
  message, weak hooks. Research written: docs/VIDEO_QUALITY_RESEARCH.md (reference channels
  analysed frame by frame from YouTube storyboards; proposal: storyboard-first beats, shot
  library, hook engine, wrong-form + 4-frame art). Waiting for the owner's decisions; not built.
- Storyboard videos, phase 1 built (owner: "do all your recommendations"): countdown-of-mistakes
  and levels formats; the LLM fills a storyboard (mistake ids from subjects.json / fixed ladders
  with benchmarks), StoryboardComposer makes the scenes, every scene has a shot with a visual
  sign (13 shots in renderer/src/scenes/shots: hooks STOP/number/shock/which-one, kinetic word,
  wrong-vs-right split, mistake reaction, fix demo with ring + arrow, zoom magnifier, lineup grid,
  level meter, payoff spotlight, CTA close-up); camera moves (whip-in, push, punch, close-up);
  hook shot rotates per video; captions smaller (60px). Myth / "what happens" topics keep the
  classic path. 24 wrong-form subjects added (kind Mistake); until their art exists a mistake is a
  reaction shot and the art gate asks for it. Prompts: docs/ART_PROMPTS_MISSING.md (6 sheets).
- Trial videos: 84ccf435 (push-up mistakes), cafd641b (pull-up levels), 29ca27b8 (squat
  mistakes), 8d8ccac8 (core levels), da7944ab (pull-ups, general topic -> countdown).
- 2026-10-11 later: all 24 wrong-form drawings imported (catalog 147, 0 missing); clean_holes.py
  removed left-over white between legs/arms (27 characters + 4 icon handles) and runs on every
  character import; match_facing.py mirrors a wrong-form drawing to face like its right pose.
  Countdown numbering now 1 -> N (owner), most important last. Storyboard countdown only for
  exercise-technique topics; fat loss / diet / sleep etc. go classic with TopicBriefs (curated
  points the script must build on). A named exercise's countdown uses only its own mistakes
  (2 is fine).
- Owner storyboard scripts: a source script starting with "FORMAT: mistakes|levels" (written in
  ChatGPT with docs/CHATGPT_SCRIPT_PROMPT.md, regenerated by `AssetTool prompts`) is parsed by
  StoryboardScript and drives the storyboard shots directly (no LLM); "other" items keep their own
  labels; checked at job creation. generate.ps1 -ScriptFile was broken (PS 5.1 Get-Content -Raw
  serialized as an object) - fixed. Wrong-form art is tagged and hidden from the classic planner;
  lifestyle topics pick each scene's picture from its own words (ArtGapDetector lifestyle).
  Verified: e9c6f8f6 (squat, owner script, 120 s, no LLM), 7c5d7019 (shredded, on-topic visuals).
- Human-like voice (2026-10-11, owner: "talks non-stop like a machine"): SpeechDelivery plans
  each scene's speech from the scenes (never the LLM): "Number two." / "Level two." and the
  level's exercise said on their own with a beat, a longer breath after the hook (0.55 s) and
  after each point, hook + close slightly slower, the scene's key phrase stressed
  (KokoroTts:Emphasis = Pause (default: a comma before it) | Stress (slower + louder) | None).
  kokoro_tts.py accepts segments as parts {text, speed, gain, pauseAfter}. The stressed phrase:
  the level's benchmark ("20 clean push-ups"), else the on-screen label as the sentence says it,
  also in other word forms ("FLARED ELBOWS" -> "elbows flare"); a name already said alone is
  never stressed again. Lines ending on a comma no longer give ",." (Sentence).
  Samples: output/voice-samples/delivery-before.wav, -after-comma.wav, -after.wav (Stress).
- Floor band removed (flat grey + stripe only). Levels: the hook hides the top level as a black
  silhouette with "?"; each level gets a level-up jingle (sfx levelUp) + white flash and a live
  benchmark counter (stopwatch / reps, reaches the goal at 80% of the speech). The closing
  dissolves into the first frame (seamless loop). cover.png (title over the settled hook) is
  rendered next to final.mp4 (ShortCover; a 1-frame composition froze at frame 0 - fixed).
- 4-frame animations: subjects kind Cycle (13 exercises + mascot-point gesture, 2x2 sprite sheet,
  split + aligned on one floor/bar by ArtImporter); the renderer plays <stem>-f1..f4 as a
  ping-pong flipbook wherever the exercise's pair would alternate, and the pointing gesture once
  in the CTA. All 14 animations imported (hanging frames align on the bar row, not the head); catalog 203, 0 missing.
  Verified: 9fc8dd9e (core levels), 415766ce (push-up mistakes), 32dea218 (push-up levels:
  flipbook + counter + cover).
- Tests: .NET 550 + renderer 85 passing; real videos 2e960fbb, df1b42a4, 183c8f45, 4120db68,
  3db61c0c (plank). Waiting for art: f8f17f7c (forearms), 57d41f96 (dead hang).

Completed (Day 30 — V1 finalization):
- Docs: README (rewritten), ARCHITECTURE (real), CONFIGURATION,
  TROUBLESHOOTING, LICENSES (Remotion: free for individuals / <=3
  employees, no selling derivatives; phonemizer/espeak-ng GPL-3.0),
  PHASE_2_OPTIONS.
- Repo: `.remotion/` (282 MB downloaded Chrome) ignored.
- Fixed: dashboard smoke script followed an older same-titled job; the
  topic-exercise rule now also applies when the model chose no asset.
- V1 definition (CLAUDE.md section 19) run through the dashboard after the
  last change: "Why Your Pull-Ups Aren't Increasing" -> Completed in 83s,
  1080x1920 h264+aac 31.2s, output/269fe16e-051a-428f-a269-1966defb9a37/final.mp4,
  no paid service.

Tests (Day 30):
- `dotnet build` 0 warnings; `dotnet test` 413/413; renderer 64/64, lint
  clean, smoke OK.

Completed (Day 29 — five real sample Shorts):
- Five roadmap topics generated 4 times (20 videos), reviewed with contact
  sheets (`tools/workflow/contact-sheet.ps1`, `generate-batch.ps1`).
  Final five: 1080x1920 h264 30fps + AAC, 33-43s, distinct titles.
- Systemic fixes:
  - `AssetMatcher`: generic tag words ignored ("top", "muscle"...),
    hyphenated words match joined tags ("pull-ups" ~ "pullup"), topic as
    second clue, ExerciseDemo shows the topic's exercise, an anatomy scene
    never shows an unrelated muscle (mascot). Tests on the REAL catalog
    (the fake one had hidden the "pullup" bug).
  - `AssetMatcher.Spread`: asset repetition repaired in code (all 6 run-1
    plan rejections; 0 in the 10 later videos).
  - `Domain/OnScreenNumbers`: no number on screen that the scene's narration
    doesn't say (25 found, e.g. "60 minutes of rest = 1% muscle growth");
    soft rule + hard strip with a review warning. 0 left in the final five.
  - Prompt: Progress example removed ("10 clean reps" copied 3/5),
    placeholders in <...> (half-copy "max Form").
  - Renderer `fit.ts`: longest-word and line-count fitting for cards,
    headlines, hooks, progress labels.
  - `Processes/FullSpeed`: API and its children opt out of Windows
    efficiency mode (61s -> 25s render from a minimized window).

Tests (Day 29):
- `dotnet test` 412/412; renderer 64/64, lint clean, smoke OK.

Completed (Day 28 — one-click creator workflow):
- `start.cmd` (tools/workflow/start.ps1 + prereqs.ps1): checks .NET, Node,
  renderer packages, Kokoro, Ollama + model, disk; offers the cheap fixes
  (npm ci, TTS setup, start Ollama, pull model); starts the API; opens the
  dashboard. `-CheckOnly`, `-Yes`, `-NoBrowser`.
- `generate.cmd "topic"`: one command -> starts the API if needed, prints
  stage progress, video path and the checklist.
- Review sign-off: `ReviewChecklist.HumanChecks` (one list), `ReviewService`
  (review-state.json, pinned to the render; stale after a re-render),
  `GET/PUT /api/videos/{id}/review-state`, dashboard tick boxes (in place,
  the video keeps playing), "Reviewed" badge. Never uploads.
- `output/INDEX.md` (`OutputIndexService`): finished videos by date/title,
  length, reviewed, relative links; updated after every job and review.
- Fixed: `pause` reset start.cmd's exit code.

Tests (Day 28):
- `dotnet test` 394/394. Browser: 7 boxes ticked while playing -> Reviewed,
  video kept playing (test sign-off undone afterwards).
- Acceptance: API stopped -> `generate.cmd "Why Rest Days Make You
  Stronger"` -> Completed in 88s.

Completed (Day 27 — performance pass, measured first):
- Baseline 57.6s (script 10.7 incl. 6.6s model load, plan 11.7, TTS 10.4,
  render 24.0) -> 48.0s same topic (-17%); script 10.7 -> 2.4s.
- Ollama timing logged per call; `tools/perf/measure-render.ps1`,
  `tools/perf/measure-planning.ps1`.
- Ollama BaseUrl 127.0.0.1 (localhost cost ~2s/video: IPv6 tried first,
  Ollama listens on IPv4 only); `Ollama:KeepAlive` 15m.
- Unchanged re-render skipped via input fingerprint (plan, narration,
  renderer/src, lock file, assets) -> 24.2s -> 0.5s. `Remotion:Concurrency`
  option (measured table in RemotionOptions).
- Plans compared only with Completed videos (drafts made planning slower as
  they piled up): 1.5-2.0 -> 1.0 attempts. Rejected by measurement: listing
  used openings in the prompt (2.83 attempts). Not done by decision:
  TTS || plan (~5s, big restructure).
- Found: PC entered modern standby mid-measurement (keep-awake had stopped
  with the previous session) -> restarted, measurement repeated.

Tests (Day 27):
- `dotnet test` 387/387.

Completed (Day 26 — logging, retries, recovery):
- `VideoJob.PendingWork` + `InterruptionCount` (migration JobPendingWork);
  `VideoJobWorkKind` moved to Domain. `VideoJobScheduler` saves the intent
  before enqueueing; every enqueue site uses it. PendingWork = queued *or*
  running: fixes double-queue / edit-during-renarration race.
- Pipeline clears PendingWork when work ends (not on shutdown); Narrating
  with an untimed plan re-runs narration (was: rendered stale audio).
- `JobRecoveryService` + `JobRecoveryHostedService`: resumes pending jobs at
  startup (Rendering -> Narrating), gives up after 3 interruptions.
- Retry: `POST /api/videos/{id}/retry` + dashboard button; resumes at the
  last stage valid on disk.
- `JobLogFileLoggerProvider`: output/<job-id>/job.log via the JobId scope.
- Ollama: one retry for connection refused / 503 only.
- Fixed: orphaned Remotion/Chrome after an API crash -> Windows Job Object
  with KILL_ON_JOB_CLOSE (`Processes/KillOnExitJob`).

Tests (Day 26):
- `dotnet test` 377/377 (Domain 116, Application 128, Infrastructure 107,
  Api 26); renderer 55/55.
- Real: wrong Ollama port -> Failed with actionable message + job.log ->
  Retry -> killed mid-render 3x (render nodes 1 -> 0 after each crash;
  3rd interruption -> Failed, poison guard) -> Retry -> Completed.

Completed (Day 25 — renderer/schema tests):
- Zod cross-field rules: unique scene ids, orders exactly 1..n, every scene
  ≥1 frame at 30 fps, speech ≤ scene (+1 frame). Checked against all 21 real
  plans in output/: 0 rejected.
- `timing.orderedSceneFrames`: single timeline used by calculateMetadata and
  SceneRenderer.
- Contract test: `samples/contract-plan.json` written by .NET
  (`PlanContractTests`, UPDATE_CONTRACT=1 regenerates) and parsed by Vitest.
- `npm run smoke` (`renderer/scripts/smoke-render.mjs`): bundle once, still
  per scene type (9, all distinct), full 0.25-scale render, broken plan
  refused naming scenes[3].id. ~23s.

Tests (Day 25):
- renderer 55/55, lint clean; `dotnet test` 344/344; smoke OK.

Completed (Day 24 — backend tests):
- Risk map `docs/TESTING.md` (8 roadmap risks -> guarding tests); every new
  test seen red (written first, or fix temporarily reverted).
- Full state-machine table test (6 statuses x 5 transitions).
- Bugs found and fixed:
  1. Ollama HTTP 500 / non-JSON body / HttpClient timeout surfaced as raw
     exceptions -> `LlmUnavailableException` with Ollama's own message.
  2. `plan.json` save truncated before writing -> atomic temp + move.
  3. `plan.json` containing `null` read as "no plan" (would silently
     re-plan) / corrupt JSON -> `InvalidDataException` with fix hint.
  4. No global error handler: Development returned stack traces / HTML ->
     `ErrorHandling.UseJsonErrors` ({error}, 400 for bad bodies).
  5. `ScriptValidator` substring match rejected "secure your grip" ->
     whole-word regex.
  6. Topic unbounded (SQLite ignores HasMaxLength) -> 200 chars; own script
     validated at job creation (400 immediately instead of a failed job).
- `RenderService.FindPlanProblemsAsync`: assets missing from the catalog are
  refused before rendering (409 from /render and /apply-edits, job stays
  editable).

Tests (Day 24):
- `dotnet test` 342/342 (Domain 108, Application 114, Infrastructure 96, Api 24).
- Manual API checks: malformed body, short script, long topic, corrupt plan.

Completed (Day 23 — plan edit and rerender):
- `PlanEditService.UpdateSceneAsync` (`SceneEdit`: null = keep, "" = clear)
  for on-screen/second text, items, animation, sfx, emphasis, type,
  narration; Hook/Outro can't be retyped or created; narration edits pass
  the script safety filter, rebuild `NarrationText` and set
  `VideoPlan.NarrationOutdated`. `UpdateSettingsAsync` (caption style via
  new `Domain/CaptionStyleId`, music mood) — whitelists only.
- `VideoJob.MarkNarrating` also from Completed; `NarrationService` clears
  the flag; `RenderService` refuses a plan with outdated narration.
- Queue kind `Renarrate` (Narration then Render).
- API: `PUT /api/videos/{id}/scenes/{sceneId}`, `PUT /api/videos/{id}/settings`,
  `POST /api/videos/{id}/apply-edits` (Renarrate or Render, decided by the plan).
- Dashboard: scene editor dialog, caption/music selects, narration notice;
  Re-render uses apply-edits.
- `renderer/scripts/dashboard-plan-edit-smoke.mjs`.

Tests (Day 23):
- `dotnet test` 283/283 (Domain 72, Application 112, Infrastructure 84, Api 15).
- Browser: visual edit + boxed captions -> render only (narration.wav
  untouched); narration edit -> notice -> new voice-over (28.72s) + render,
  notice gone.

Completed (Day 22 — asset inspection / selection UI):
- `Application/PlanEditService.ChangeSceneAssetAsync`: catalog ids only,
  re-validated with `VideoPlanValidator` (new `checkDurations: false` for
  audio-timed plans), refused while queued/rendering/failed, variety
  warnings recomputed; job status unchanged.
- API: `PUT /api/videos/{id}/scenes/{sceneId}/asset`,
  `GET /api/assets/{id}/svg` (image/svg+xml + CSP default-src 'none' +
  nosniff), `VideoJobResponse.VideoOutdated` (plan.json newer than final.mp4).
- Dashboard: asset picker dialog (previews via <img>, compatible-only
  filter, search), "Change" per scene, outdated notice, "Browse assets".
- Fixed: prompt placeholders copied into plans ("max 5 words" as Hook
  text) — placeholders defined once and treated as missing.
- `renderer/scripts/dashboard-edit-smoke.mjs` (real browser edit test).

Tests (Day 22):
- `dotnet test` 272/272.
- Browser: scene 2 asset sleep -> mascot-idle, outdated notice, re-render,
  notice gone.

Completed (Day 21 — minimal local dashboard):
- `web/` (plain HTML/CSS/JS, no build step) served by the API at `/`
  (`Api/Endpoints/DashboardEndpoints.cs`: `UseDashboard`, `Dashboard:Directory`).
- UI: topic + optional own script, Generate, recent jobs (polling 2.5s),
  detail with stage chips, in-page video player, Open folder / Continue /
  Re-render, review checklist, scene plan, service health chips. All
  user/LLM text set via textContent (no innerHTML).
- New endpoints: `GET /api/videos/{id}/video` (range requests),
  `GET /api/videos/{id}/review`, `POST /api/videos/{id}/open-folder`
  (loopback callers only; explorer.exe via ArgumentList).
- `VideoJobResponse.IsRunning` (worker's current job): in-between statuses
  that are neither queued nor running show as "Paused" (were misleadingly
  shown as in progress).
- `renderer/scripts/dashboard-smoke.mjs`: real headless-Chrome UI test.

Tests (Day 21):
- `dotnet test` 263/263 (Api 11).
- Browser smoke test: Generate -> queued in ~1s -> Completed in 63s ->
  video element 1080x1920, 29.4s.

Completed (Day 20 — asset library tools):
- `tools/ShortsGenerator.AssetTool` (in the solution): `validate`, `add`,
  `replace`, `index`; logic in Infrastructure (`AssetManifestValidator`,
  `AssetCatalogEditor` all-or-nothing with rollback, `AssetIndexPage`).
- SVG safety gate (assets are inlined into the renderer page): no script,
  event handlers, javascript:, foreignObject/iframe/embed/object, external
  URLs; viewBox required; ≤200 kB. Required fallback mascots enforced;
  orphan files and half pose pairs warned.
- `docs/ASSETS.md`: naming, drawing, metadata rules and the workflow.
- 10 new original assets added via the tool (quads/glutes/lats/core
  highlights, squat-top/bottom registered pair, plank, sleep, meal,
  calendar) -> 22 assets. Fixed pull-up pose registration (Day 18 debt).
- `Application/AssetMatcher`: swaps a chosen asset whose tags don't fit
  the scene text for a compatible one that does (fixed biceps-on-core).
- Prompt no longer contains literal example card texts (model copied one);
  `NarrationSegmenter` split pieces ≥5 words.

Tests (Day 20):
- `dotnet test` 259/259; `AssetTool validate` OK (22 assets, 0 warnings);
  renderer 40/40 (unchanged).
- Real runs: new assets picked without code changes (quads-highlight,
  squat-bottom, plank, sleep); matcher fix verified (core-highlight).

Completed (Day 19 — anti-repetition / originality):
- Hard vs soft rules: variety issues are retried for 2 attempts, then the
  plan is accepted with `VideoPlan.ReviewWarnings` instead of failing.
- `Domain/PlanVariationRules` (type ≤2x, ≥3 distinct middle types, no
  consecutive same type/asset, asset ≤2x; `CheckAgainstEarlier`: scenes 2-4
  type order must differ from the last 20 plans).
- `Domain/TextSimilarity` (5-word shingle overlap); generated scripts
  sharing >25% phrases with any of the last 30 scripts are rewritten.
- `VideoPlan.Takeaway` (required in the LLM schema) checked against earlier
  takeaways (3-word shingles ≥60% or normalized-equal -> soft issue).
- Prompt template guide rotated per job (deterministic) to break the
  model's list-order bias (found by measurement).
- Renderer `HookTemplate`: 3 hook layouts (burst / card / split) picked by
  an FNV-1a hash of the plan (`variant.ts`).
- `review.md` per finished video (`ReviewChecklist` + `IJobArtifactWriter`,
  plain-file-name only): automatic checks, warnings, scene table, human
  checklist, script. V1 still never uploads.

Tests (Day 19):
- `dotnet test` 237/237; renderer 40/40, lint clean.
- 10 real plans (script+plan only), all completed. Run 1: 5/5 distinct
  sequences but scene 3 = AnatomyHighlight in 5/5 -> fix -> run 2: scene 2/3
  types vary across videos; soft rules triggered and fixed or accepted
  with warnings as designed; 0% script phrase overlap.

Completed (Day 18 — fitness scene templates):
- Plan contract: `Scene.SecondaryText` + `Scene.Items` (optional); in the
  LLM schema (required-but-nullable), prompt explains per-type usage with
  examples; code strips card-label prefixes ("Fact: ...") and drops
  label-only text ("Wrong way").
- Renderer `scenes/templates/`: exhaustive `TEMPLATE_BY_SCENE_TYPE`;
  MythVsFact (strike-through myth, stamped fact), Comparison (✗/✓ cards),
  List (staged numbered items), Progress (big tabular-nums counter + bar),
  AnatomyHighlight (pulsing ring), ExerciseDemo (pose-pair rep animation
  with rep counter), Recovery (breathing circle + chips); every template
  falls back to the generic layout when its data is missing.
- Fixed: caption flicker (cue entrance started at opacity 0 -> one empty
  frame at every cue change; now starts at 0.5).
- `samples/template-showcase-plan.json`; images in `docs/images/day-18/`.

Tests (Day 18):
- `dotnet test` 208/208; renderer 38/38, lint clean.
- Real runs: "Can You Build Muscle With Push-Ups?" (61s, 7 scenes, 6
  types), "Why Soreness Doesn't Mean Muscle Growth" (60s, 8 scenes, 7
  types, clean myth/fact text).

Completed (Day 17 — music and SFX):
- Original audio, synthesized (no downloads, no license question):
  `tools/audio/generate_audio_assets.py` (deterministic) ->
  `assets/sfx/{pop,whoosh,click,impact,success,error}.wav`,
  `assets/music/{upbeat,calm}.wav` (22.05 kHz loops).
- `Domain/MusicMood` (upbeat | calm); the LLM picks it via the schema enum;
  code keeps at most 3 sound effects per video.
- Renderer `audio/audioLibrary.ts` (paths; mix: voice 1 / sfx 0.35 /
  music 0.06; music fade in 1s / out 1.5s); music looped under the video,
  each scene's sfx plays on its cut.
- `tools/audio/measure_mix.py`: speech measured 16.2 dB above the music on
  a real render (11.9 dB at the first setting of 0.10 -> recalibrated),
  peak -4 dBFS (no clipping).

Tests (Day 17):
- `dotnet test` 201/201 (Domain 57, Application 76, Infrastructure 61, Api 7).
- Renderer `npm test` 32/32, lint clean.
- Real run "Why Rest Days Make You Stronger": Completed in 57s, plan on
  attempt 1, musicMood calm, one sfx (whoosh).

Earlier days (summary — details in docs/daily-code-notes/day-NN.md):
- Day 16: visual pass — themed gradients, badges, big headlines, 600px
  visuals, mascot fallback, circle-reveal transitions without overlap
  (sync preserved), counter-without-number -> pop.
- Day 15: first automatic Short; narration split in code
  (NarrationSegmenter), LLM picks visuals only, Ollama structured outputs.
- Day 14: Channel + BackgroundService queue, resumable pipeline.
- Day 13: Remotion renderer adapter (node directly, staging, atomic output).
- Day 12: audio-timed scenes (segment synthesis, WAV header reader).
- Day 11: local Kokoro TTS, ProcessRunner, content-root paths.
- Day 10: phrase-level captions.

Known issues:
- The model sometimes misuses fields (e.g. a Comparison title in the
  "wrong" card) or writes generic takeaways — surfaced in review.md.
- Placeholder art is simple; `replace` swaps drawings without touching
  plans or code when better art exists.
- Two jobs interrupted on Day 21 (before PendingWork existed) are not
  auto-resumed; "Continue" resumes them by hand.
- Orphan-process protection (Job Object) is Windows-only.
- Day 29: an API instance that has run for a while sometimes renders ~2-3x
  slower (Chrome grandchildren still subject to Windows' throttling
  heuristics); restarting the API fixes it. Root cause not proven.
- The 3B model writes some on-screen labels that don't fit the narration and
  keeps inventing numbers (removed automatically, but costs re-plans).
- Day 27/29 measurement runs left ~40 plan-only draft jobs ("Paused") and
  several duplicate sample videos in the dashboard/output; not deleted (ask).
- Audio quality was verified by measurement, not by ear.

User approval:
- Day 1 approved ("geçebilirsin") on 2026-09-15.
- Day 2 approved ("diğer güne geçelim") on 2026-10-06.
- Day 3 approved ("geçebilirsin") on 2026-10-06.
- Day 4 approved ("geçebilirsin") on 2026-10-06.
- Day 5 approved ("geçebilirsin") on 2026-10-08.
- Day 6 approved ("geçebilirsin") on 2026-10-08.
- Day 7 approved ("geçebilirsin") on 2026-10-08.
- Day 8 approved ("geçebilirsin") on 2026-10-08.
- Day 9 approved ("geç") on 2026-10-08.
- 2026-10-08, during Day 10: blanket approval for all remaining days —
  "devam edelim, direkt tüm günleri sırayla yap sen bana sorma". Days are
  still done strictly in order with full per-day deliverables, but without
  stopping for per-day approval. The user also asked work to continue while
  they're away; a temporary keep-awake process (SetThreadExecutionState,
  system + display, no system settings changed, 12h max) runs meanwhile.
