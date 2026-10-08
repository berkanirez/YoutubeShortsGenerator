# Testing

```
dotnet test                 # backend: Domain, Application, Infrastructure, Api (no Ollama/Kokoro/Remotion needed)
cd renderer && npm test     # renderer: captions, schema, timing, audio mix, templates (Vitest)
cd renderer && npm run smoke  # real headless-Chrome render of every scene type + a broken plan (~25s)
```

## The plan.json contract

`samples/contract-plan.json` is written by the backend (`PlanContractTests`, real
`FileVideoPlanStore` serializer, every scene type and optional field) and parsed by the
renderer (`videoPlan.test.ts`, and `npm run smoke` renders it). Change `VideoPlan`/`Scene`
and the .NET test fails with the diff; if the change is intended, regenerate the fixture and
see whether the renderer still accepts it:

```
$env:UPDATE_CONTRACT="1"; dotnet test --filter PlanContractTests; $env:UPDATE_CONTRACT=$null
cd renderer; npm test
```

Backend tests never call the real engines: `ILlmProvider`, `ITtsProvider`, `IRenderer`,
`IProcessRunner` and `HttpMessageHandler` are replaced by fakes, and API tests run the real
app in memory (`WebApplicationFactory`) with the job processor swapped out. Real-engine runs
are done by hand (and by `renderer/scripts/*-smoke.mjs` against a running API).

## Risk map

Where a bug would be expensive, and which tests guard it.

| Risk | Guarded by |
|---|---|
| Job status transitions | `VideoJobTests.StateMachine_AllowsExactlyTheDocumentedTransitions` (every status × every transition, 30 cases), pipeline/render/narration tests for "never stuck in a transient state" |
| Plan schema / validation | `VideoPlanValidatorTests`, `VisualPlanSchemaTests`, `PlanJsonExtractorTests`, `ScenePlanningServiceTests` (retries, repair prompts, placeholders), renderer `videoPlan.test.ts` (fields + cross-field: unique ids, orders 1..n, ≥1 frame, speech ≤ scene), `PlanContractTests` |
| Render timing | renderer `timing.test.ts` (by order, back to back, total = backend seconds), `captionCues.test.ts`, smoke render (duration = sum of scenes) |
| Bad Ollama response | `OllamaLlmProviderTests`: unreachable, model missing (404), server error (500 + Ollama's own message), non-JSON body, HttpClient timeout vs. real cancellation; malformed/off-schema JSON in `ScenePlanningServiceTests` |
| Missing asset | startup manifest check (`JsonAssetRepositoryTests`), only catalog ids in plans/edits, `RenderServiceTests` + API test: a plan whose asset left the catalog is refused before rendering, naming the scene |
| TTS / process failure | `KokoroTtsProviderTests` (exit codes, crash, garbage output, timeout, WAV/segment mismatch), `ProcessRunnerTests` (no shell, BOM-less stdin, timeout/cancel kill the tree) |
| Renderer failure | `RemotionRendererTests` (real Remotion error text, no partial mp4, timeout, node missing), `RenderServiceTests` (Failed with reason, cancelled) |
| Path safety | `FileJobArtifactWriterTests` (plain file names only), `FileVideoPlanStoreTests` (job dir under the root, absolute root, atomic save, corrupt plan.json), `RemotionRendererTests`/`KokoroTtsProviderTests` (relative/non-mp4 outputs rejected), `AssetToolingTests` (SVG gate, paths) |
| API validation | `VideoJobEndpointTests`: empty/over-long topic, too-short own script, malformed JSON body (400, no stack trace), unknown ids (404), wrong state (409), unknown enum values (422 + allowed values), corrupt plan file (JSON error, no stack trace), open-folder local-only |

## Proving a test can fail

A new test is only trusted after it was seen red: either written before the fix, or by
temporarily reverting the fix (Day 24 did this for the medical-phrase word boundary and the
API error handler — see `docs/daily-code-notes/day-24.md`).
