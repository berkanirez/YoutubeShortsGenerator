import { describe, expect, it } from "vitest";
import { pickVariant, planSeed } from "./variant";

describe("planSeed", () => {
  it("is stable for the same plan", () => {
    expect(planSeed("Fix Your Pull-Ups", "Narration.")).toBe(planSeed("Fix Your Pull-Ups", "Narration."));
  });

  it("spreads different plans over all hook variants", () => {
    const variants = new Set(
      Array.from({ length: 30 }, (_, i) => pickVariant(planSeed(`Title ${i}`, `Narration ${i}`), 3)),
    );
    expect(variants).toEqual(new Set([0, 1, 2]));
  });
});
