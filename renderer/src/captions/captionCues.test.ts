import { describe, expect, it } from "vitest";
import { buildCaptionCues } from "./captionCues";
import type { ChunkingRules } from "./chunking";

const RULES: ChunkingRules = { maxWordsPerChunk: 4, maxCharsPerChunk: 18 };
const NARRATION = "If you're stuck at the same pull-up count, it's probably your form.";

describe("buildCaptionCues", () => {
  it("covers the whole scene with no gaps or overlaps", () => {
    const cues = buildCaptionCues(NARRATION, null, 120, RULES);

    expect(cues.length).toBeGreaterThan(1);
    expect(cues[0].startFrame).toBe(0);
    expect(cues[cues.length - 1].endFrame).toBe(120);
    for (let i = 1; i < cues.length; i++) {
      expect(cues[i].startFrame).toBe(cues[i - 1].endFrame);
    }
  });

  it("gives every cue a positive duration", () => {
    for (const cue of buildCaptionCues(NARRATION, null, 120, RULES)) {
      expect(cue.endFrame).toBeGreaterThan(cue.startFrame);
    }
  });

  it("gives longer chunks more time", () => {
    const cues = buildCaptionCues("Go. Keep your shoulders completely down.", null, 90, RULES);
    const durations = cues.map((cue) => cue.endFrame - cue.startFrame);
    expect(durations[0]).toBeLessThan(durations[1]);
  });

  it("ends exactly on the scene's last frame for awkward durations (no rounding drift)", () => {
    for (const duration of [31, 97, 151, 179]) {
      const cues = buildCaptionCues(NARRATION, null, duration, RULES);
      expect(cues[cues.length - 1].endFrame).toBe(duration);
      const total = cues.reduce((sum, cue) => sum + (cue.endFrame - cue.startFrame), 0);
      expect(total).toBe(duration);
    }
  });

  it("returns no cues for a scene without narration (e.g. a silent Outro)", () => {
    expect(buildCaptionCues(null, null, 150, RULES)).toEqual([]);
    expect(buildCaptionCues("   ", null, 150, RULES)).toEqual([]);
  });

  it("drops cues rather than producing zero-length ones when frames run out", () => {
    const cues = buildCaptionCues("One. Two. Three. Four. Five.", null, 3, RULES);
    expect(cues.length).toBeLessThanOrEqual(3);
    expect(cues[cues.length - 1].endFrame).toBe(3);
    for (const cue of cues) {
      expect(cue.endFrame).toBeGreaterThan(cue.startFrame);
    }
  });

  it("spreads cues over the spoken frames only, then holds the last cue to the scene end", () => {
    // 150-frame scene, but speech only lasts 90 frames (the rest is the pause).
    const cues = buildCaptionCues(NARRATION, null, 150, RULES, 90);

    const secondToLast = cues[cues.length - 2];
    expect(secondToLast.endFrame).toBeLessThan(90);
    expect(cues[cues.length - 1].endFrame).toBe(150);
    for (let i = 1; i < cues.length; i++) {
      expect(cues[i].startFrame).toBe(cues[i - 1].endFrame);
    }
  });

  it("ignores spoken frames that are missing, zero, or longer than the scene", () => {
    const withoutSpoken = buildCaptionCues(NARRATION, null, 120, RULES);
    expect(buildCaptionCues(NARRATION, null, 120, RULES, 0)).toEqual(withoutSpoken);
    expect(buildCaptionCues(NARRATION, null, 120, RULES, 500)).toEqual(withoutSpoken);
  });

  it("carries emphasis through to the cue words", () => {
    const cues = buildCaptionCues("Keep your core tight and avoid swinging.", "core tight", 150, RULES);
    const emphasized = cues.flatMap((cue) => cue.words.filter((word) => word.emphasized).map((word) => word.text));
    expect(emphasized).toEqual(["core", "tight"]);
  });
});
