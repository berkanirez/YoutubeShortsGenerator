import { describe, expect, it } from "vitest";
import { cycleFrames, cycleIdsFor, onceFrame, repFrame } from "./cycle";
import { goalCount, stopwatch } from "./goal";

describe("four-frame animations", () => {
  it("find the frames of a pose's exercise only when all four are loaded", () => {
    const art = { "pushup-f1": "a", "pushup-f2": "b", "pushup-f3": "c", "pushup-f4": "d", "squat-f1": "e" };
    expect(cycleFrames(art, "pushup-top")).toEqual(["pushup-f1", "pushup-f2", "pushup-f3", "pushup-f4"]);
    expect(cycleFrames(art, "pushup-bottom")).toEqual(cycleFrames(art, "pushup-top"));
    expect(cycleFrames(art, "squat-top")).toBeNull();
  });

  it("ask for each exercise's frames once", () => {
    expect(cycleIdsFor(["pushup-top", "pushup-bottom", "plank"])).toEqual([
      "pushup-f1", "pushup-f2", "pushup-f3", "pushup-f4", "plank-f1", "plank-f2", "plank-f3", "plank-f4",
    ]);
  });

  it("play a rep forth and back, then repeat", () => {
    // 1.6 s at 30 fps = 48 frames, six steps of 8.
    const steps = Array.from({ length: 7 }, (_, i) => repFrame(i * 8, 30));
    expect(steps).toEqual([0, 1, 2, 3, 2, 1, 0]);
  });

  it("play a gesture once and hold its last frame", () => {
    expect([0, 4, 7, 10, 13, 100].map((f) => onceFrame(f))).toEqual([0, 0, 1, 2, 3, 3]);
  });
});

describe("level goal counter", () => {
  it("counts up to the goal by 80% of the speech and holds", () => {
    expect(goalCount(0, 100, 60)).toBe(0);
    expect(goalCount(80, 100, 60)).toBe(60);
    expect(goalCount(99, 100, 60)).toBe(60);
    expect(goalCount(40, 100, 60)).toBeGreaterThan(0);
  });

  it("shows seconds as a stopwatch", () => {
    expect([45, 60, 90].map(stopwatch)).toEqual(["0:45", "1:00", "1:30"]);
  });
});
