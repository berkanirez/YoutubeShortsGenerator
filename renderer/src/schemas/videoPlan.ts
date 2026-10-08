import { z } from "zod";

// Mirrors ShortsGenerator.Domain's controlled vocabularies exactly (CLAUDE.md
// sections 7/11/13). SceneType keeps the backend's PascalCase JSON spelling;
// AnimationId/SfxId use camelCase, because that's how the backend's custom
// JsonCamelCaseEnumConverterAttribute actually serializes them (see Day 5/6
// code notes) - this file has to match the real wire format, not a guess.
export const SceneTypeSchema = z.enum([
  "Hook",
  "ExerciseDemo",
  "Comparison",
  "MythVsFact",
  "AnatomyHighlight",
  "Progress",
  "List",
  "Recovery",
  "Outro",
]);

export const AnimationIdSchema = z.enum([
  "fade",
  "slide",
  "pop",
  "punchZoom",
  "shake",
  "bounce",
  "pulse",
  "counter",
  "highlight",
]);

export const SfxIdSchema = z.enum([
  "pop",
  "whoosh",
  "error",
  "success",
  "impact",
  "click",
]);

// Matches ShortsGenerator.Domain.Scene exactly, field for field.
export const SceneSchema = z.object({
  id: z.string().min(1),
  order: z.number().int().positive(),
  type: SceneTypeSchema,
  durationSeconds: z.number().positive(),
  narration: z.string().nullable(),
  onScreenText: z.string().nullable(),
  assetIds: z.array(z.string()),
  animation: AnimationIdSchema,
  sfx: SfxIdSchema.nullable(),
  background: z.string().nullable(),
  emphasis: z.string().nullable(),
  // Set once narration exists (Day 12): how much of the scene, from its start, is speech.
  // Optional so hand-written/pre-narration plans (like samples/) still validate.
  spokenDurationSeconds: z.number().positive().nullable().optional(),
  // Day 18 template fields. Optional for the same reason: older/hand-written
  // plans don't have them, and every template falls back gracefully without.
  secondaryText: z.string().nullable().optional(),
  items: z.array(z.string()).max(3).nullable().optional(),
});

// The only frame rate this project renders at (Root.tsx, Domain.VideoFormat).
export const PLAN_FPS = 30;

// Speech may exceed its scene by less than one frame: both come from the same audio and are
// rounded to frames separately (Day 12).
const SPOKEN_TOLERANCE_SECONDS = 1 / PLAN_FPS;

// Matches ShortsGenerator.Domain.VideoPlan, field for field (the backend's extra fields -
// takeaway, reviewWarnings, narrationOutdated - are review data the renderer doesn't need;
// Zod drops unknown keys). The cross-field rules below are each field-valid-on-its-own
// mistakes that would otherwise surface deep inside Remotion with no scene named.
export const VideoPlanSchema = z
  .object({
    title: z.string().min(1),
    hook: z.string().min(1),
    targetDurationSeconds: z.number().positive(),
    narrationText: z.string().min(1),
    scenes: z.array(SceneSchema).min(1),
    style: z.string().nullable(),
    captionStyle: z.string().nullable(),
    musicMood: z.string().nullable(),
  })
  .superRefine((plan, ctx) => {
    const seenIds = new Set<string>();
    const seenOrders = new Set<number>();
    plan.scenes.forEach((scene, index) => {
      const at = (field: string) => ["scenes", index, field];

      // Ids are React keys and the edit API's address for a scene.
      if (seenIds.has(scene.id)) {
        ctx.addIssue({ code: "custom", path: at("id"), message: `duplicate scene id '${scene.id}'` });
      }
      seenIds.add(scene.id);

      if (seenOrders.has(scene.order) || scene.order > plan.scenes.length) {
        ctx.addIssue({
          code: "custom",
          path: at("order"),
          message: `orders must be exactly 1..${plan.scenes.length}, each once (got ${scene.order})`,
        });
      }
      seenOrders.add(scene.order);

      // <Series.Sequence durationInFrames={0}> throws inside Remotion without saying which scene.
      if (Math.round(scene.durationSeconds * PLAN_FPS) < 1) {
        ctx.addIssue({
          code: "custom",
          path: at("durationSeconds"),
          message: `${scene.durationSeconds}s is shorter than one frame at ${PLAN_FPS} fps`,
        });
      }

      // Captions are spread over the spoken part inside the scene; the rest would be cut off.
      if (scene.spokenDurationSeconds != null &&
          scene.spokenDurationSeconds > scene.durationSeconds + SPOKEN_TOLERANCE_SECONDS) {
        ctx.addIssue({
          code: "custom",
          path: at("spokenDurationSeconds"),
          message: `speech (${scene.spokenDurationSeconds}s) is longer than the scene (${scene.durationSeconds}s)`,
        });
      }
    });
  });

// Validates incoming plan JSON and, if it's invalid, throws a plain Error whose message
// says exactly which field is wrong, on ONE line. Both details matter, found by testing
// a broken plan through the real CLI: Remotion runs calculateMetadata in headless Chrome
// and (1) forwards a thrown ZodError only as its class name, and (2) prints only the
// first line of any error message - the backend's error report would get neither.
export function parseVideoPlan(json: unknown, source: string): VideoPlan {
  const result = VideoPlanSchema.safeParse(json);
  if (!result.success) {
    const issues = result.error.issues
      .map((issue) => `${formatPath(issue.path)}: ${issue.message}`)
      .join("; ");
    throw new Error(`Invalid plan JSON (${source}): ${issues}`);
  }
  return result.data;
}

// ["scenes", 1, "animation"] -> "scenes[1].animation"
function formatPath(path: readonly PropertyKey[]): string {
  if (path.length === 0) {
    return "(root)";
  }
  return path
    .map((segment, index) =>
      typeof segment === "number" ? `[${segment}]` : `${index === 0 ? "" : "."}${String(segment)}`,
    )
    .join("");
}

export type SceneType = z.infer<typeof SceneTypeSchema>;
export type AnimationId = z.infer<typeof AnimationIdSchema>;
export type SfxId = z.infer<typeof SfxIdSchema>;
export type Scene = z.infer<typeof SceneSchema>;
export type VideoPlan = z.infer<typeof VideoPlanSchema>;
