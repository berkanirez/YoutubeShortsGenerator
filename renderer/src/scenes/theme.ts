import type { Scene, SceneType } from "../schemas/videoPlan";

export type ScenePalette = {
  // Flat page color - no gradient.
  background: string;
  // A shade darker than the background: the static diagonal stripe and flat shapes.
  stripe: string;
  // The gym floor the characters stand on, and its front edge.
  floor: string;
  floorEdge: string;
  // Strong team color: wall lettering, numbers, signs.
  accent: string;
};

// One dark gym-grey for every scene: the mascot's red, tan and the bright lettering pop on
// it, and the screen reads as one place rather than a slideshow of colors. The scene types
// still differ by their accent (wall lettering, signs, the wipe's edge).
const GREY = { background: "#2F3033", stripe: "#383A3E", floor: "#232427", floorEdge: "#4A4C52" };

export const PALETTE_BY_SCENE_TYPE: Record<SceneType, ScenePalette> = {
  Hook: { ...GREY, accent: "#EF4444" },
  ExerciseDemo: { ...GREY, accent: "#FACC15" },
  Comparison: { ...GREY, accent: "#FB923C" },
  MythVsFact: { ...GREY, accent: "#EF4444" },
  AnatomyHighlight: { ...GREY, accent: "#EF4444" },
  Progress: { ...GREY, accent: "#4ADE80" },
  List: { ...GREY, accent: "#FACC15" },
  Recovery: { ...GREY, accent: "#7DD3FC" },
  Outro: { ...GREY, accent: "#EF4444" },
};

// Every scene gets a visual. When the plan picked no asset (the Day 15 hook
// was an empty screen), the original mascot stands in, in a pose that fits
// the beat. These ids must exist in assets/manifests/assets.json.
export const FALLBACK_ASSET_BY_SCENE_TYPE: Record<SceneType, string> = {
  Hook: "mascot-thinking",
  ExerciseDemo: "mascot-idle",
  Comparison: "mascot-thinking",
  MythVsFact: "mascot-thinking",
  AnatomyHighlight: "mascot-idle",
  Progress: "mascot-happy",
  List: "mascot-idle",
  Recovery: "mascot-tired",
  Outro: "mascot-happy",
};

export function displayAssetId(scene: Scene): string {
  return scene.assetIds[0] ?? FALLBACK_ASSET_BY_SCENE_TYPE[scene.type];
}
