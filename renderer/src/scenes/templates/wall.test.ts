import { describe, expect, it } from "vitest";
import { currentItem, wallText } from "./wall";

describe("wallText", () => {
  it("keeps a short slogan", () => {
    expect(wallText("  Full range  ")).toBe("Full range");
  });

  it("leaves a sentence to the captions", () => {
    expect(wallText("Knees should be slightly in front of toes")).toBeNull();
  });

  it("allows a longer limit where the template asks for one", () => {
    expect(wallText("Stuck at five pull-ups?", 4)).toBe("Stuck at five pull-ups?");
    expect(wallText("Are you stuck at five pull-ups?", 4)).toBeNull();
    expect(wallText("Are you stuck at five pull-ups?", 6)).toBe("Are you stuck at five pull-ups?");
  });

  it("treats missing text as nothing to paint", () => {
    expect(wallText(null)).toBeNull();
    expect(wallText("   ")).toBeNull();
  });
});

describe("currentItem", () => {
  it("shows the item whose turn has started", () => {
    const starts = [0, 30, 60];
    expect(currentItem(0, starts)).toBe(0);
    expect(currentItem(29, starts)).toBe(0);
    expect(currentItem(30, starts)).toBe(1);
    expect(currentItem(500, starts)).toBe(2);
  });
});
