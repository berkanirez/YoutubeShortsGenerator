import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";
import { parseVideoPlan } from "../schemas/videoPlan";
import { orderedSceneFrames, secondsToFrames } from "./timing";

const plan = () =>
  parseVideoPlan(JSON.parse(readFileSync(new URL("../../../samples/contract-plan.json", import.meta.url), "utf8")), "contract");

describe("secondsToFrames", () => {
  it("rounds to the nearest frame", () => {
    expect(secondsToFrames(1, 30)).toBe(30);
    expect(secondsToFrames(2.39, 30)).toBe(72);
    expect(secondsToFrames(2.3833, 30)).toBe(71);
    expect(secondsToFrames(0.016, 30)).toBe(0);
  });
});

describe("orderedSceneFrames", () => {
  it("plays scenes by order, not by array position", () => {
    const scenes = [...plan().scenes].reverse();

    const ordered = orderedSceneFrames(scenes, 30);

    expect(ordered.map((s) => s.scene.order)).toEqual(ordered.map((_, i) => i + 1));
  });

  it("starts each scene exactly where the previous one ended (no gaps, no overlap)", () => {
    const ordered = orderedSceneFrames(plan().scenes, 30);

    ordered.forEach((entry, i) => {
      expect(entry.durationInFrames).toBeGreaterThan(0);
      expect(entry.fromFrame).toBe(i === 0 ? 0 : ordered[i - 1].fromFrame + ordered[i - 1].durationInFrames);
    });
  });

  it("totals what the backend's audio timing computed (frame-aligned seconds sum to whole frames)", () => {
    const p = plan();
    const ordered = orderedSceneFrames(p.scenes, 30);
    const last = ordered[ordered.length - 1];
    const totalSeconds = p.scenes.reduce((sum, s) => sum + s.durationSeconds, 0);

    expect(last.fromFrame + last.durationInFrames).toBe(Math.round(totalSeconds * 30));
  });
});
