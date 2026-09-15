# PRODUCT_SPEC.md

## What this is

A zero-cost, local-first, automated faceless fitness Shorts generator.

Input: a fitness topic (or an optional pre-written script).
Output: a polished 9:16 MP4 Short — original mascot visuals, scene changes,
motion, captions, local voice-over, sound effects, and background music —
with as little manual editing as possible.

Full rules for how this is built live in `CLAUDE.md`. This file describes
*what* the product is; `CLAUDE.md` governs *how* we work on it.

## Who it's for

A single local creator (the project owner) producing fitness-education
Shorts without paying for any API or cloud service.

## V1 scope

In scope:
- topic or script -> local LLM script generation (Ollama);
- structured, schema-validated scene planning;
- reusable original SVG/PNG mascot and exercise assets (no AI image
  generation per scene in V1);
- local English TTS narration (Kokoro);
- word/phrase-level captions;
- scene animation via a controlled set of primitives;
- background music and SFX from local, rights-clear sources;
- Remotion + FFmpeg rendering to `output/<job-id>/final.mp4`;
- manual human review before publishing anywhere.

Explicitly out of scope for V1 (see `CLAUDE.md` section 20):
YouTube auto-upload, accounts/payments, multi-tenant SaaS, cloud rendering,
mobile app, and any paid LLM/TTS/image API.

## Non-negotiable constraints

- **Zero cost to run locally.** No paid API keys required for the default
  runtime.
- **Original visual identity.** An original athletic mascot; must not copy
  another creator's mascot, art style, scripts, or branding (must not be
  yellow, must not imitate Yellow Dude).
- **No auto-upload.** A human always reviews the final MP4 before it goes
  anywhere.
- **Safe fitness content.** Exercise technique, training principles,
  calisthenics, gym basics, running/performance basics, recovery, and
  general nutrition education only — no medical or treatment claims.

## Definition of V1 done

The user can start local prerequisites, submit a topic such as
`Why Your Pull-Ups Aren't Increasing` from Swagger or a local UI, and the
system automatically produces a playable `output/<job-id>/final.mp4` —
script, scene plan, assets, narration, captions, animation, and render all
generated locally, with no paid runtime service used. See `CLAUDE.md`
section 19 for the full acceptance list.

## How we get there

Delivery is day-gated per `docs/ROADMAP_30_DAYS.md`, tracked in
`docs/DAY_STATE.md`. One day is implemented and approved at a time.
