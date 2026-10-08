import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";
import { parseVideoPlan } from "./videoPlan";

const readJson = (relative: string): Record<string, unknown> =>
  JSON.parse(readFileSync(new URL(relative, import.meta.url), "utf8"));
const samplePlan = () => readJson("../../../samples/sample-plan.json");
const scenesOf = (plan: Record<string, unknown>) => plan.scenes as Record<string, unknown>[];

const errorOf = (fn: () => unknown): Error => {
  try {
    fn();
  } catch (caught) {
    return caught as Error;
  }
  throw new Error("expected a throw");
};

describe("parseVideoPlan", () => {
  it("accepts the hand-written sample plans", () => {
    expect(parseVideoPlan(samplePlan(), "sample").scenes).toHaveLength(6);
    expect(parseVideoPlan(readJson("../../../samples/template-showcase-plan.json"), "showcase").scenes.length).toBeGreaterThan(0);
    expect(parseVideoPlan(readJson("../../../samples/animation-showcase-plan.json"), "animations").scenes.length).toBeGreaterThan(0);
  });

  // samples/contract-plan.json is written by the .NET side (PlanContractTests) from a real
  // VideoPlan with every field set; if either side renames or retypes a field, one of the
  // two tests fails instead of the next render.
  it("accepts the exact JSON the backend produces (contract fixture)", () => {
    const plan = parseVideoPlan(readJson("../../../samples/contract-plan.json"), "contract");

    const myth = plan.scenes.find((s) => s.type === "MythVsFact");
    expect(myth?.secondaryText).toBeTruthy();
    expect(plan.scenes.some((s) => s.items && s.items.length > 0)).toBe(true);
    expect(plan.scenes.every((s) => s.spokenDurationSeconds === undefined || s.spokenDurationSeconds === null || s.spokenDurationSeconds > 0)).toBe(true);
    expect(plan.scenes.some((s) => s.sfx !== null)).toBe(true);
    expect(plan.scenes.some((s) => s.animation === "punchZoom")).toBe(true);
  });

  it("throws a plain one-line Error naming the bad field and the allowed values", () => {
    const plan = samplePlan();
    scenesOf(plan)[1].animation = "explode";

    const error = errorOf(() => parseVideoPlan(plan, "jobs/x/plan.json"));

    expect(error.constructor).toBe(Error);
    expect(error.message).toContain("jobs/x/plan.json");
    expect(error.message).toContain("scenes[1].animation");
    expect(error.message).toContain("punchZoom");
    // Remotion's CLI only prints the first line of an error message.
    expect(error.message).not.toContain("\n");
  });

  it("reports every invalid field, not just the first", () => {
    const plan = samplePlan();
    scenesOf(plan)[0].durationSeconds = -1;
    scenesOf(plan)[2].type = "Dance";

    expect(() => parseVideoPlan(plan, "p")).toThrow(/scenes\[0\]\.durationSeconds.*scenes\[2\]\.type/);
  });

  it.each([
    ["plan is not an object", null, /\(root\)/],
    ["no scenes", { scenes: [] }, /scenes/],
    ["missing title", { title: undefined }, /title/],
  ])("rejects: %s", (_, change, pattern) => {
    const plan = change === null ? null : { ...samplePlan(), ...change };
    expect(() => parseVideoPlan(plan, "p")).toThrow(pattern);
  });

  // Cross-field rules: each field is fine on its own, the combination isn't.
  it("rejects duplicate scene ids (they're React keys and the edit API's address)", () => {
    const plan = samplePlan();
    scenesOf(plan)[3].id = scenesOf(plan)[1].id;

    expect(() => parseVideoPlan(plan, "p")).toThrow(/scenes\[3\]\.id: duplicate scene id/);
  });

  it("rejects orders that aren't exactly 1..n (a gap or a repeat makes the sequence ambiguous)", () => {
    const repeated = samplePlan();
    scenesOf(repeated)[2].order = 2;
    expect(() => parseVideoPlan(repeated, "p")).toThrow(/scenes\[2\]\.order/);

    const gap = samplePlan();
    scenesOf(gap).forEach((scene, i) => (scene.order = i === 5 ? 9 : i + 1));
    expect(() => parseVideoPlan(gap, "p")).toThrow(/order/);
  });

  it("accepts scenes listed out of order as long as the orders are 1..n", () => {
    const plan = samplePlan();
    scenesOf(plan).reverse();

    expect(parseVideoPlan(plan, "p").scenes).toHaveLength(6);
  });

  it("rejects a scene too short to last a single frame at 30 fps", () => {
    const plan = samplePlan();
    scenesOf(plan)[4].durationSeconds = 0.01;

    const error = errorOf(() => parseVideoPlan(plan, "p"));
    expect(error.message).toMatch(/scenes\[4\]\.durationSeconds/);
    expect(error.message).toMatch(/frame/);
  });

  it("rejects speech longer than its scene (the end of the captions would be cut off)", () => {
    const plan = samplePlan();
    const scene = scenesOf(plan)[1];
    scene.spokenDurationSeconds = (scene.durationSeconds as number) + 0.5;

    expect(() => parseVideoPlan(plan, "p")).toThrow(/scenes\[1\]\.spokenDurationSeconds/);
  });

  it("tolerates speech equal to the scene up to frame rounding", () => {
    const plan = samplePlan();
    const scene = scenesOf(plan)[1];
    scene.spokenDurationSeconds = (scene.durationSeconds as number) + 0.01;

    expect(() => parseVideoPlan(plan, "p")).not.toThrow();
  });

  it("rejects more than 3 items (the List template has room for 3)", () => {
    const plan = samplePlan();
    scenesOf(plan)[2].items = ["a", "b", "c", "d"];

    expect(() => parseVideoPlan(plan, "p")).toThrow(/scenes\[2\]\.items/);
  });
});
