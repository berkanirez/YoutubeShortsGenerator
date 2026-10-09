# Art automation plan - missing visuals generated while the video is made

Status: **plan only, not built.** Three decisions below need the channel owner's answer first.

## Goal

Today a scene can only show art that is already in the catalog (40 PNGs, all made by hand).
When a video needs something the catalog doesn't have (a kettlebell swing, a hip-thrust, a
foam roller) the matcher falls back to a generic mascot pose. The goal is that the pipeline
notices the gap, generates the missing art in the channel's style, cleans it up and uses it
in that same video - and that every generated piece then stays in the catalog, so the
library grows and later videos need fewer new images.

## Where it goes in the pipeline

```
Script -> Scene plan -> [NEW] Illustrating -> Narration -> Render -> Completed
                         |
                         | runs in parallel with narration (TTS takes ~10 s,
                         | an image ~20-90 s), so the video waits only for the slower one
```

1. **Gap detection (after the scene plan, in code).**
   The plan's visual step gets one extra, optional field per scene:
   `neededVisual: { kind, subject, poses }`.
   - `kind` must be one of `exercise-pair | exercise | muscle | icon | mascot-pose`.
   - `subject` must be 2-4 lowercase words, letters/spaces/hyphens only ("kettlebell swing").

   Code checks the field and keeps a gap only when all of these hold:
   - the kind and subject pass the whitelist and the format rule;
   - the catalog has no asset whose first tag (the subject) matches;
   - the scene type benefits from a specific visual (ExerciseDemo, AnatomyHighlight, List/Recovery icons);
   - the video is still under the per-video limit (default 2 new images).

   Everything else keeps today's matcher result.
2. **Prompt building (in code, never by the LLM).** The image prompt is assembled from fixed
   blocks - the CHARACTER block from `docs/ART_PROMPTS_READY.md`, a per-kind template
   (pair, single pose, muscle highlight, icon) and the validated `subject`. The LLM's text
   never goes into the image prompt verbatim (CLAUDE.md section 18: LLM output is data).
3. **Generation** behind `IImageGenerator.GenerateAsync(prompt, referenceImages, size, ct)`
   (CLAUDE.md section 21). The reference images are `assets/mascot/mascot-idle.png`, plus one
   catalog image of the same kind.
4. **Automatic quality gate.** 2 candidates per gap; the best one that passes wins:
   - mostly white background;
   - one figure, or two for a pair, split cleanly at an empty column;
   - mascot colors present (red hair/shorts, grey sleeves) for character art;
   - an aspect ratio that suits the kind.

   If both fail: one retry, then the mascot fallback. **The video never fails because of art.**
5. **Post-processing (already exists):** Real-ESRGAN 4x -> `prepare_art.py --size 2048`
   (`--pair` / `--keep-holes` by kind) -> `AssetCatalogEditor.Add`. The tags come from the
   subject and the kind, and the new asset is marked `"review": "pending"`.
6. **Use and record.** The scene's `assetIds` point to the new asset, the plan is saved and
   the render goes on. The job log and `review.md` list every generated image, so the
   pre-publish review covers it.
7. **Review in the dashboard.** A "New art" page shows pending assets with Approve /
   Regenerate / Reject:
   - **Approve:** the asset becomes a normal catalog asset.
   - **Reject:** the asset is deleted and the affected videos are re-rendered with the fallback.

   Until it is approved, a pending asset is used only by the video that created it, never
   picked for other videos.

## Decision 1 - which image generator

| | A. Paid API (OpenAI image API) | B. Local, free (ComfyUI + Stable Diffusion) |
|---|---|---|
| Cost | Per image: roughly a few cents (medium quality) to ~$0.25 (high). Check the current price list before building. | $0 (electricity) |
| Character consistency | Good: takes reference images; the existing art came from this kind of model | Weak without a trained character LoRA; training one needs more than the laptop's 4 GB VRAM (a free Colab session could do it, once) |
| Speed on this laptop | ~20-60 s per image (network) | ~1-3 min per image (RTX 3050 4 GB, SD 1.5 anime model, low-VRAM mode) |
| Rejects | Few | Many: extra fingers, wrong sleeves, a different face |
| Rule | **Breaks the zero-cost rule (CLAUDE.md section 3)**; needs the owner's explicit approval, a key in `OPENAI_API_KEY` only, and a budget cap | Keeps V1's zero-cost rule |

Recommendation: **A, with hard caps**, kept switchable to B through `IImageGenerator`.
- **Caps:**
  - max 2 new images per video;
  - a daily and a monthly spending limit (generation stops and the fallback is used once the limit is reached);
  - a dry-run mode that only logs what it would generate.
- **Cost falls over time:** every image is generated once and then reused. After ~150-200 assets most scenes hit the catalog, and the spend falls toward zero.
- **Ballpark:** 30 new images a month ≈ $1-8.

Option B is worth a one-hour spike first if a $0 operation matters more than quality:
1. Install ComfyUI.
2. Generate 5 test images.
3. Compare them with the current art.

## Decision 2 - may a generated image appear before a human has seen it?

- **Yes, flagged (recommended):** the video uses it right away, and it is listed in
  `review.md` and on the "New art" page. The owner reviews every MP4 before publishing anyway (CLAUDE.md section 4).
- **No:** the first video still uses the fallback; the new image is generated in the background, and
  re-rendering after approval swaps it in. Safer, but every new subject needs one extra render.

## Decision 3 - limits

- New images per video (default 2).
- Daily and monthly budget (only for option A).
- Which kinds are allowed (default: all five; `mascot-pose` could be left out to keep the mascot's look under manual control).

## Build order (once the decisions are made)

1. **Gap detection + `IImageGenerator` with a fake generator** that returns a fixture PNG.
   - The plan schema gets `neededVisual`; whitelist and validation in code.
   - Tests: no gap when the catalog has the subject; the limit per video; unsafe subjects rejected.
2. **Post-processing chain in .NET:**
   - a process runner around Real-ESRGAN and `prepare_art.py` (`ProcessStartInfo.ArgumentList`, paths under known folders);
   - the pending status in the manifest and the validator;
   - the quality gate.
3. **Real generator adapter:**
   - option A: an HTTP client, a key from the environment, cost accounting in SQLite, the caps;
   - option B: the ComfyUI local HTTP API.
4. **Pipeline stage "Illustrating"** in parallel with narration; recovery after a crash, like the other stages.
5. **Dashboard "New art" page** (approve / regenerate / reject + re-render).
6. **Measurement:** 10 real videos.
   - Measure: the gap rate, the images generated, accepted vs rejected, the spend, and the extra time per video.
   - Then tune the limits.

## What stays the same

- No automatic upload; the owner reviews every video.
- The voice, script, plan and render stay local and free; only option A's image calls go out.
- All art stays the channel owner's own character.
- Real-ESRGAN (local, free) handles HD for both hand-made and generated art.
