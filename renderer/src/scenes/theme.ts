import type { Scene, SceneType } from "../schemas/videoPlan";

export type ScenePalette = {
  // Two-stop gradient, darker at the bottom where the captions sit.
  from: string;
  to: string;
  // Decorative glow / badge color.
  accent: string;
};

// One palette per scene type: consecutive scenes of different types read as a
// change of topic even before the text changes. Deep, saturated colors keep
// white text and captions readable; the teal/orange accents are the mascot's.
export const PALETTE_BY_SCENE_TYPE: Record<SceneType, ScenePalette> = {
  Hook: { from: "#0F766E", to: "#042F2E", accent: "#FB923C" },
  ExerciseDemo: { from: "#1E293B", to: "#0B1120", accent: "#2DD4BF" },
  Comparison: { from: "#6D28D9", to: "#2E1065", accent: "#FBBF24" },
  MythVsFact: { from: "#B45309", to: "#431407", accent: "#FDE68A" },
  AnatomyHighlight: { from: "#1D4ED8", to: "#172554", accent: "#FB923C" },
  Progress: { from: "#15803D", to: "#052E16", accent: "#FDE047" },
  List: { from: "#4338CA", to: "#1E1B4B", accent: "#2DD4BF" },
  Recovery: { from: "#0369A1", to: "#082F49", accent: "#A5F3FC" },
  Outro: { from: "#0F766E", to: "#042F2E", accent: "#FB923C" },
};

// Small label above the headline that tells the viewer what kind of beat this
// is. Hook and Outro have none: the hook's headline must hit alone, and the
// outro is a closing card, not a "section".
export const BADGE_BY_SCENE_TYPE: Record<SceneType, string | null> = {
  Hook: null,
  ExerciseDemo: "Technique",
  Comparison: "Compare",
  MythVsFact: "Myth vs fact",
  AnatomyHighlight: "Muscles",
  Progress: "Progress",
  List: "Key points",
  Recovery: "Recovery",
  Outro: null,
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
