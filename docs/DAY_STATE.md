# DAY_STATE.md

Current day: 25
Status: APPROVED (blanket approval — see below; continuing to Day 26)
Date: 2026-10-08

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
- Queue is in memory; interrupted jobs aren't recovered yet (Day 26). Two
  real interrupted jobs exist from Day 21 testing ("How Slow Negatives
  Build Strength" in Created, one "Why Tempo Reps..." in Planning).
- Audio quality was verified by measurement, not by ear.
- Failed jobs can't be edited or retried; a failed re-render of a Completed
  job marks it Failed although the old final.mp4 is still on disk (Day 26).

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
