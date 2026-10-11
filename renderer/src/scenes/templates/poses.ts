import type { SceneType } from "../../schemas/videoPlan";

// The mascot reacts within a scene instead of holding one pose: the "stop" of a myth turns
// into a thumbs-up when the fact lands, a thinking hook points at the viewer, the outro goes
// from happy to a thumbs-up. One change per scene at most - more reads as flicker.
const REACTIONS: Partial<Record<SceneType, Record<string, string>>> = {
  MythVsFact: {
    "mascot-stop": "mascot-thumbsup",
    "mascot-thinking": "mascot-thumbsup",
    "mascot-shocked": "mascot-thumbsup",
    "mascot-idle": "mascot-thumbsup",
    "mascot-facepalm": "mascot-thumbsup",
    "mascot-confused": "mascot-thumbsup",
    "mascot-laughing": "mascot-thumbsup",
  },
  Comparison: {
    "mascot-stop": "mascot-thumbsup",
    "mascot-thinking": "mascot-thumbsup",
    "mascot-pointing": "mascot-thumbsup",
    "mascot-shrug": "mascot-thumbsup",
    "mascot-confused": "mascot-thumbsup",
  },
  Hook: {
    "mascot-thinking": "mascot-pointing",
    "mascot-shocked": "mascot-pointing",
    "mascot-stop": "mascot-pointing",
    "mascot-flex": "mascot-pointing",
    "mascot-pointing": "mascot-flex",
    "mascot-confused": "mascot-idea",
    "mascot-facepalm": "mascot-pointing",
    "mascot-mind-blown": "mascot-pointing",
    "mascot-crossed-arms": "mascot-pointing",
    "mascot-idea": "mascot-pointing",
  },
  Outro: {
    "mascot-happy": "mascot-thumbsup",
    "mascot-thumbsup": "mascot-happy",
    "mascot-flex": "mascot-thumbsup",
    "mascot-pointing": "mascot-thumbsup",
    "mascot-wave": "mascot-thumbsup",
    "mascot-celebrate": "mascot-thumbsup",
    "mascot-crossed-arms": "mascot-thumbsup",
  },
};

// When in the scene's speech the reaction happens (MythVsFact/Comparison use the verdict turn).
const REACTION_AT: Partial<Record<SceneType, number>> = {
  MythVsFact: 0.4,
  Comparison: 0.4,
  Hook: 0.6,
  Outro: 0.45,
};

export function reactionPose(sceneType: SceneType, assetId: string): string | null {
  return REACTIONS[sceneType]?.[assetId] ?? null;
}

/** The pose to show at `frame`, and the frame it changed at (null before / without a change). */
export function poseAt(
  sceneType: SceneType,
  assetId: string,
  frame: number,
  spokenFrames: number,
  available: Record<string, string>,
): { id: string; swapFrame: number | null } {
  const reaction = reactionPose(sceneType, assetId);
  const at = REACTION_AT[sceneType];
  if (!reaction || at === undefined || available[reaction] === undefined) {
    return { id: assetId, swapFrame: null };
  }
  const swapFrame = Math.round(spokenFrames * at);
  return frame >= swapFrame ? { id: reaction, swapFrame } : { id: assetId, swapFrame: null };
}
