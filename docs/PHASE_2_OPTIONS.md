# Phase 2 options (proposed, not built)

Nothing here is implemented. V1 stays zero-cost and local until you decide otherwise.
Each option lists what it would fix (with evidence from V1), what it costs, and the catch.

## A. Still free - fixes for what V1's own review found

| # | Option | Why (evidence) | Effort |
|---|---|---|---|
| A1 | **A larger local model** (e.g. an 7-8B instruct model in Ollama) | The 3B model invents on-screen numbers (25 cases in 15 videos, now blocked in code but costing re-plans), mislabels scenes ("Biceps" in a running video), writes generic takeaways, and the running video barely answered its question. A bigger model is a config change (`Ollama:Model`); measure speed vs. quality on the 5 sample topics first. | 1 day (measure + tune prompts) |
| A2 | **More drawings**: chest, hamstrings, calves, shoulders highlights; running, lunge, deadlift, dip poses | AnatomyHighlight falls back to the mascot when the muscle has no drawing; running/lunge videos have no matching exercise art. The asset tool adds them without code changes. | 1-2 days of drawing |
| A3 | **Pin Chrome's render processes to full speed** | An API that has run for a while sometimes renders 2-3x slower; Chrome (grandchild processes) is still under Windows' throttling heuristics (Day 29). Apply the same opt-out to every process in the Job Object. | 0.5 day + a long-running measurement |
| A4 | **Delete / archive jobs** in the dashboard | Measurement and test runs left ~40 plan-only drafts and duplicate videos in the list. | 0.5 day |
| A5 | **Word-level captions** | V1 captions are phrase-timed (CLAUDE.md section 12 allowed this first). Kokoro can give word timestamps; karaoke-style emphasis is a retention feature. | 2 days |
| A6 | **Script quality pass**: a second local "editor" prompt that checks the script answers the title's question and has one concrete takeaway | Weak takeaways and off-question scripts were the main content issues in the 20 sample videos. | 1-2 days |

## B. Paid - only if the channel performs

| # | Option | What it improves | Cost (order of magnitude) | Catch |
|---|---|---|---|---|
| B1 | **Stronger hosted LLM** (behind the existing `ILlmProvider`) | Script accuracy, real takeaways, fewer re-plans; plans would pass the same schema and rules. | cents per video | Needs an API key and billing; scripts leave the PC. |
| B2 | **Premium TTS** (behind `ITtsProvider`) | More natural, expressive narration. | cents to dimes per video | Voice licensing terms; never a cloned real person (CLAUDE.md section 9). |
| B3 | **Professional mascot and asset pack** | The biggest visible quality jump; the asset system swaps art without code changes. | one-off design fee | Must stay original (no look-alike of another channel's character). |
| B4 | **AI image generation for missing assets** | Fills gaps (A2) quickly. | per image | Style consistency, licensing of outputs; generate into the catalog once, review, then reuse - not per scene. |
| B5 | **Cloud rendering** (behind `IRenderer`) | Frees the PC, parallel renders. | per render-minute | Remotion's license: free for individuals/≤3 employees; check terms for cloud use. Upload of plans/audio. |
| B6 | **YouTube publishing integration** | Saves the upload step. | free API, quota-limited | Must keep the human review gate (CLAUDE.md section 4); OAuth and channel-safety risks. |
| B7 | **Analytics** (views, retention per scene type / hook layout) | Learn which templates and hooks work. | free API | Needs B6's auth; data stays useful only with enough videos. |
| B8 | **SaaS conversion** | Other creators could use it. | large | Accounts, billing, hosting, moderation - and Remotion's license forbids selling a derivative of Remotion itself and requires a company license above 3 employees. A different product, not a feature. |

## Recommendation

Start with **A1 + A2** (free, and they target the issues the V1 review actually found), then
A3/A4 for comfort. Consider **B3** (assets) before any paid AI service: it improves every
video at a one-off cost. B6/B7 only after a few dozen videos are published by hand.
