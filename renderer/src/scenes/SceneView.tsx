import React from "react";
import { AbsoluteFill, useCurrentFrame, useVideoConfig } from "remotion";
import { getAnimation } from "../animations/primitives";
import type { Scene } from "../schemas/videoPlan";
import { SceneBackground } from "./SceneBackground";
import { TEMPLATE_BY_SCENE_TYPE } from "./templates";
import { displayAssetId, PALETTE_BY_SCENE_TYPE, type ScenePalette } from "./theme";

// Content starts entering just after the background starts opening, so the
// transition and the entrance read as one movement. Kept to 2 frames: a longer
// delay (tried 4) left a visibly empty screen at every cut.
const CONTENT_DELAY_FRAMES = 2;

export type SceneViewProps = {
  scene: Scene;
  index: number;
  previousPalette: ScenePalette | null;
  assetSvgById: Record<string, string>;
  durationInFrames: number;
  variantSeed: number;
};

// Background + transition here; everything inside the scene is its type's
// template (scenes/templates/, Day 18). Captions are a separate layer
// (CaptionRenderer), so no template can ever cover or move them.
export const SceneView: React.FC<SceneViewProps> = ({
  scene,
  index,
  previousPalette,
  assetSvgById,
  durationInFrames,
  variantSeed,
}) => {
  // Inside a <Series.Sequence>, useCurrentFrame() starts at 0 for each scene.
  const frame = useCurrentFrame();
  const { fps } = useVideoConfig();
  const palette = PALETTE_BY_SCENE_TYPE[scene.type];
  const contentFrame = Math.max(0, frame - (previousPalette ? CONTENT_DELAY_FRAMES : 0));
  const Template = TEMPLATE_BY_SCENE_TYPE[scene.type];

  return (
    <AbsoluteFill>
      <SceneBackground palette={palette} previous={previousPalette} seed={index} wipeStyle={Math.floor(variantSeed / 2)} />
      <Template
        scene={scene}
        frame={contentFrame}
        fps={fps}
        durationInFrames={durationInFrames}
        palette={palette}
        assetId={displayAssetId(scene)}
        assetSvgById={assetSvgById}
        animation={getAnimation(scene.animation, { frame: contentFrame, fps, durationInFrames })}
        variantSeed={variantSeed}
      />
    </AbsoluteFill>
  );
};
