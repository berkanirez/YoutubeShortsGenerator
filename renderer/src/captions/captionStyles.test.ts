import { describe, expect, it } from "vitest";
import { CAPTION_STYLES, resolveCaptionStyle } from "./captionStyles";

describe("resolveCaptionStyle", () => {
  it("returns the named preset", () => {
    expect(resolveCaptionStyle("boxed")).toBe(CAPTION_STYLES.boxed);
  });

  it("falls back to the default for null or unknown ids", () => {
    expect(resolveCaptionStyle(null)).toBe(CAPTION_STYLES.bold);
    expect(resolveCaptionStyle("neon-rainbow")).toBe(CAPTION_STYLES.bold);
  });

  it("does not treat inherited object keys as style ids", () => {
    expect(resolveCaptionStyle("toString")).toBe(CAPTION_STYLES.bold);
  });
});
