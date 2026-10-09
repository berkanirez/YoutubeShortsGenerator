import { describe, expect, it } from "vitest";
import { estimateLines, fitBlockFontSize, fitFontSize } from "./fit";

describe("fitBlockFontSize", () => {
  // The real Day 29 texts.
  const longHook = "Lifting weights shouldn't leave you feeling like a failure.";
  const longProgress = "3 sets of 8-12 reps can challenge your muscles and stimulate growth";

  it("keeps a short headline at its full size", () => {
    expect(fitBlockFontSize("Stuck", 900, 124, 60, 3)).toBe(124);
    expect(fitBlockFontSize("Rest days are key", 900, 124, 60, 3)).toBe(124);
  });

  it("shrinks a long hook until it takes at most 3 lines", () => {
    const size = fitBlockFontSize(longHook, 900, 124, 48, 3);
    expect(size).toBeLessThan(124);
    expect(estimateLines(longHook, 900, size)).toBeLessThanOrEqual(3);
  });

  it("shrinks a long progress label to 2 lines, but not below the minimum", () => {
    const size = fitBlockFontSize(longProgress, 760, 64, 30, 2);
    expect(size).toBeGreaterThanOrEqual(30);
    expect(estimateLines(longProgress, 760, size)).toBeLessThanOrEqual(2);
  });

  it("is deterministic (same text, same size - required for frame-by-frame rendering)", () => {
    expect(fitBlockFontSize(longHook, 900, 124, 60, 3)).toBe(fitBlockFontSize(longHook, 900, 124, 60, 3));
  });
});

describe("estimateLines", () => {
  it("wraps by words", () => {
    expect(estimateLines("aaaa bbbb", 100, 20)).toBe(2);   // 8 chars per line
    expect(estimateLines("aaa bbb", 100, 20)).toBe(1);
  });
});

// A 430px Comparison card: 430 - 2 x 34 padding - 2 x 5 border = 352px for text.
const CARD_TEXT_WIDTH = 352;

describe("fitFontSize", () => {
  it("keeps the base size when every word fits", () => {
    expect(fitFontSize("Weak core", CARD_TEXT_WIDTH, 62, 34)).toBe(62);
  });

  it("shrinks for the long words found in the Day 29 videos, until they fit", () => {
    for (const word of ["Overtraining", "Insufficient recovery"]) {
      const size = fitFontSize(word, CARD_TEXT_WIDTH, 62, 34);
      const longest = Math.max(...word.split(" ").map((w) => w.length));
      expect(size).toBeLessThan(62);
      expect(longest * size * 0.6).toBeLessThanOrEqual(CARD_TEXT_WIDTH);
    }
  });

  it("never goes below the readable minimum", () => {
    expect(fitFontSize("Supercalifragilisticexpialidocious", CARD_TEXT_WIDTH, 62, 34)).toBe(34);
  });

  it("is driven by the longest word, not the total length", () => {
    expect(fitFontSize("a b c d e f g h i j k l", CARD_TEXT_WIDTH, 62, 34)).toBe(62);
  });
});
