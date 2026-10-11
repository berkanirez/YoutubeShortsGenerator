import React from "react";
import { AbsoluteFill, Freeze, type CalculateMetadataFunction } from "remotion";
import { SceneView } from "../scenes/SceneView";
import { fitBlockFontSize } from "../scenes/templates/fit";
import { WALL_FONT } from "../scenes/templates/shared";
import { planSeed } from "../scenes/templates/variant";
import { orderedSceneFrames } from "../scenes/timing";
import { calculateMetadata, type ShortCompositionProps } from "./ShortComposition";

// The Short's cover (cover.png next to final.mp4, uploadable as the thumbnail): the hook
// scene once it has settled, with the video's title painted big across the upper half - the
// feed shows this before a single frame plays, so it must read in a glance.
// The hook's whip-in has finished and its signs have landed by now.
const SETTLED_FRAME = 24;

export const calculateCoverMetadata: CalculateMetadataFunction<ShortCompositionProps> = async (options) => {
  const metadata = await calculateMetadata(options);
  // Long enough to reach SETTLED_FRAME: Remotion clamps every frame (a <Freeze> too) to the
  // composition's length, so a 1-frame cover froze the hook at frame 0 - before anything popped in.
  // The still is taken at frame 0; the <Freeze> below shows the settled hook.
  return { ...metadata, durationInFrames: SETTLED_FRAME + 1 };
};

export const ShortCover: React.FC<ShortCompositionProps> = ({ plan, assetSvgById }) => {
  const first = orderedSceneFrames(plan.scenes, 30)[0];
  const title = plan.title.toUpperCase();
  return (
    <AbsoluteFill style={{ backgroundColor: "#2F3033" }}>
      {first ? (
        // Its own wall words would fight the title: dropped, and the picture moved below the title.
        <AbsoluteFill style={{ transform: "translateY(150px)" }}>
        <Freeze frame={Math.min(SETTLED_FRAME, first.durationInFrames - 1)}>
          <SceneView
            scene={{ ...first.scene, onScreenText: null }}
            index={0}
            previousPalette={null}
            assetSvgById={assetSvgById}
            durationInFrames={first.durationInFrames}
            variantSeed={plan.variant ?? planSeed(plan.title, plan.narrationText)}
            camera={false}
          />
        </Freeze>
        </AbsoluteFill>
      ) : null}
      <AbsoluteFill style={{ background: "linear-gradient(180deg, rgba(0,0,0,0.55) 0%, rgba(0,0,0,0) 45%)" }} />
      <div
        style={{
          position: "absolute",
          top: 230,
          left: 60,
          right: 60,
          textAlign: "center",
          color: "#FACC15",
          fontFamily: WALL_FONT,
          fontSize: fitBlockFontSize(title, 960 * 1.2, 170, 80, 3),
          lineHeight: 0.98,
          letterSpacing: 2,
          WebkitTextStroke: "10px #111111",
          paintOrder: "stroke fill",
          textShadow: "10px 10px 0 rgba(0,0,0,0.55)",
          transform: "rotate(-3deg)",
        }}
      >
        {title}
      </div>
    </AbsoluteFill>
  );
};
