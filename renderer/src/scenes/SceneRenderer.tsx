import React from "react";
import { Html5Audio, Series, staticFile, useVideoConfig } from "remotion";
import { AUDIO_MIX, SFX_PATHS } from "../audio/audioLibrary";
import { CaptionRenderer } from "../captions/CaptionRenderer";
import type { CaptionStyle } from "../captions/captionStyles";
import type { Scene } from "../schemas/videoPlan";
import { SceneView } from "./SceneView";
import { PALETTE_BY_SCENE_TYPE } from "./theme";
import { secondsToFrames, videoTimeline } from "./timing";

export type SceneRendererProps = {
  scenes: readonly Scene[];
  assetSvgById: Record<string, string>;
  captionStyle: CaptionStyle;
  variantSeed: number;
  camera?: boolean;
};

// Deterministic scene order/timing (CLAUDE.md Day 8 "Do"): scenes are sorted
// by `order` once, then handed to Remotion's own <Series> primitive, which
// plays each child for exactly its given duration and never overlaps them -
// there is no hand-rolled "which scene is active at this frame" math here.
export const SceneRenderer: React.FC<SceneRendererProps> = ({
  scenes,
  assetSvgById,
  captionStyle,
  variantSeed,
  camera = true,
}) => {
  const { fps } = useVideoConfig();
  const timeline = videoTimeline(scenes, fps);

  return (
    <Series>
      {timeline.map(({ scene, durationInFrames, closingFrames }, index) => {
        const previous = index > 0 ? timeline[index - 1].scene : null;
        return (
          <Series.Sequence key={scene.id} durationInFrames={durationInFrames}>
            <SceneView
              scene={scene}
              index={index}
              previousPalette={previous ? PALETTE_BY_SCENE_TYPE[previous.type] : null}
              assetSvgById={assetSvgById}
              durationInFrames={durationInFrames}
              variantSeed={variantSeed}
              camera={camera}
            />
            {/* A sibling layer, not inside SceneView's animated content group:
                a scene's shake/slide must never move the captions. Living in
                the same Sequence means captions are timed by the same scene
                boundaries, so they can't drift into the next scene. */}
            <CaptionRenderer
              narration={scene.narration}
              emphasis={scene.emphasis}
              // The closing is silent: captions are timed on the narrated part only.
              durationInFrames={durationInFrames - closingFrames}
              spokenFrames={
                scene.spokenDurationSeconds
                  ? secondsToFrames(scene.spokenDurationSeconds, fps)
                  : undefined
              }
              captionStyle={captionStyle}
            />
            {/* The scene's sound effect lands with the scene's entrance (frame 0
                of its Sequence), i.e. on the cut it belongs to. */}
            {scene.sfx ? (
              <Html5Audio src={staticFile(SFX_PATHS[scene.sfx])} volume={AUDIO_MIX.sfx} />
            ) : null}
          </Series.Sequence>
        );
      })}
    </Series>
  );
};
