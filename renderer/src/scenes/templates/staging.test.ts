import { describe, expect, it } from "vitest";
import { itemStartFrame, poseCounterpart, repPose, revealAt, splitLeadingNumber, spokenFrames } from "./staging";

describe("spokenFrames", () => {
  it("uses the spoken duration, clamped to the scene", () => {
    expect(spokenFrames(2, 30, 90)).toBe(60);
    expect(spokenFrames(10, 30, 90)).toBe(90);
    expect(spokenFrames(null, 30, 90)).toBe(90);
  });
});

describe("revealAt", () => {
  it("is 0 before the start, 1 after the reveal, and monotonic in between", () => {
    expect(revealAt(5, 10)).toBe(0);
    expect(revealAt(18, 10)).toBe(1);
    expect(revealAt(12, 10)).toBeLessThan(revealAt(14, 10));
  });
});

describe("itemStartFrame", () => {
  it("spreads items over the first 80% of the speech, first item immediately", () => {
    expect([0, 1, 2].map((i) => itemStartFrame(i, 3, 90))).toEqual([0, 24, 48]);
    expect(itemStartFrame(0, 1, 90)).toBe(0);
  });
});

describe("poseCounterpart", () => {
  it("pairs -top and -bottom poses and ignores everything else", () => {
    expect(poseCounterpart("pullup-top")).toBe("pullup-bottom");
    expect(poseCounterpart("pushup-bottom")).toBe("pushup-top");
    expect(poseCounterpart("dumbbell")).toBeNull();
  });
});

describe("repPose", () => {
  it("starts at the bottom, reaches the top at half a rep, and counts reps", () => {
    expect(repPose(0, 30).top).toBe(0);
    expect(repPose(21, 30).top).toBe(1); // half of 1.4s at 30fps
    expect(repPose(0, 30).reps).toBe(0);
    expect(repPose(21, 30).reps).toBe(1);
    expect(repPose(42 + 21, 30).reps).toBe(2);
  });
});

describe("splitLeadingNumber", () => {
  it("splits out the first number", () => {
    expect(splitLeadingNumber("10 clean reps")).toEqual({ before: "", value: 10, after: "clean reps" });
    expect(splitLeadingNumber("Add 5 kg")).toEqual({ before: "Add", value: 5, after: "kg" });
    expect(splitLeadingNumber("No number")).toBeNull();
  });
});
