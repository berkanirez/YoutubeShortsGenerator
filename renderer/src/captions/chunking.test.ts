import { describe, expect, it } from "vitest";
import { chunkWords, markEmphasis, normalizeWord, splitWords, type ChunkingRules } from "./chunking";

const RULES: ChunkingRules = { maxWordsPerChunk: 4, maxCharsPerChunk: 18 };

function chunkTexts(narration: string, rules: ChunkingRules = RULES): string[] {
  const words = markEmphasis(splitWords(narration), null);
  return chunkWords(words, rules).map((chunk) => chunk.map((word) => word.text).join(" "));
}

describe("normalizeWord", () => {
  it("strips surrounding punctuation but keeps apostrophes and hyphens", () => {
    expect(normalizeWord("Lats,")).toBe("lats");
    expect(normalizeWord("you're")).toBe("you're");
    expect(normalizeWord("pull-up")).toBe("pull-up");
    expect(normalizeWord("\"Chin.\"")).toBe("chin");
  });
});

describe("chunkWords", () => {
  it("breaks after clause-ending punctuation", () => {
    expect(chunkTexts("Your lats, not your arms, should do most of the work.")).toEqual([
      "Your lats,",
      "not your arms,",
      "should do most of",
      "the work.",
    ]);
  });

  it("never exceeds the word or character limit", () => {
    const narration =
      "If you're stuck at the same pull-up count for months it's probably your form and not your strength";
    for (const chunk of chunkTexts(narration)) {
      expect(chunk.split(" ").length).toBeLessThanOrEqual(RULES.maxWordsPerChunk);
      // A single word longer than the limit is allowed to stand alone.
      if (chunk.includes(" ")) {
        expect(chunk.length).toBeLessThanOrEqual(RULES.maxCharsPerChunk);
      }
    }
  });

  it("keeps every word, in order", () => {
    const narration = "Keep your core tight and avoid swinging.";
    expect(chunkTexts(narration).join(" ")).toBe(narration);
  });

  it("merges a trailing orphan word into the previous chunk when it fits", () => {
    // Without the orphan rule: ["Track your progress", "weekly"].
    expect(chunkTexts("Track your progress weekly", { maxWordsPerChunk: 3, maxCharsPerChunk: 30 })).toEqual([
      "Track your progress weekly",
    ]);
  });

  it("does not merge an orphan across a sentence boundary", () => {
    expect(chunkTexts("Rest well. Recover.")).toEqual(["Rest well.", "Recover."]);
  });

  it("puts an over-long single word in its own chunk instead of dropping it", () => {
    expect(chunkTexts("Supercalifragilisticexpialidocious form")).toEqual([
      "Supercalifragilisticexpialidocious",
      "form",
    ]);
  });
});

describe("markEmphasis", () => {
  it("marks the matching phrase ignoring case and punctuation", () => {
    const words = markEmphasis(splitWords("Keep your Core, tight and avoid swinging."), "core tight");
    expect(words.filter((word) => word.emphasized).map((word) => word.text)).toEqual(["Core,", "tight"]);
  });

  it("marks only the first occurrence", () => {
    const words = markEmphasis(splitWords("Reps matter. More reps win."), "reps");
    expect(words.map((word) => word.emphasized)).toEqual([true, false, false, false, false]);
  });

  it("ignores an emphasis phrase that isn't in the narration", () => {
    const words = markEmphasis(splitWords("Keep your core tight."), "glutes");
    expect(words.some((word) => word.emphasized)).toBe(false);
  });

  it("keeps the emphasis when the phrase is split across chunks", () => {
    const words = markEmphasis(splitWords("Add weight once you can do ten clean reps."), "ten clean reps");
    const chunks = chunkWords(words, RULES);
    const emphasizedPerChunk = chunks.map((chunk) =>
      chunk.filter((word) => word.emphasized).map((word) => word.text),
    );
    expect(emphasizedPerChunk.flat()).toEqual(["ten", "clean", "reps."]);
  });
});
