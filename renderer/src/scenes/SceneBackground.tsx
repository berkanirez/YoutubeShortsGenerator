import React from "react";
import { AbsoluteFill, Easing, interpolate, useCurrentFrame } from "remotion";
import type { ScenePalette } from "./theme";

// How long the new scene's background takes to open over the previous one.
// circle(N%) is relative to the frame's diagonal, so with ease-out a 9-frame
// reveal already covered the whole screen by frame 2 - invisible. 12 frames
// with ease-in-out makes the circle actually readable as a transition.
export const REVEAL_FRAMES = 12;

const gradient = (palette: ScenePalette) => `linear-gradient(170deg, ${palette.from} 0%, ${palette.to} 100%)`;

const Layer: React.FC<{ palette: ScenePalette; frame: number; seed: number }> = ({ palette, frame, seed }) => {
  // Two soft accent glows that drift a few pixels over the scene - enough that
  // the frame never looks frozen, too little to pull attention from the content.
  // Pure functions of frame + seed, so renders stay deterministic.
  const drift = Math.sin(frame / 45 + seed) * 30;
  return (
    <AbsoluteFill style={{ background: gradient(palette), overflow: "hidden" }}>
      <div
        style={{
          position: "absolute",
          width: 900,
          height: 900,
          left: -300 + drift,
          top: 180,
          borderRadius: "50%",
          background: `radial-gradient(circle, ${palette.accent}33 0%, transparent 65%)`,
        }}
      />
      <div
        style={{
          position: "absolute",
          width: 700,
          height: 700,
          right: -260 - drift,
          top: 900,
          borderRadius: "50%",
          background: `radial-gradient(circle, ${palette.accent}22 0%, transparent 65%)`,
        }}
      />
    </AbsoluteFill>
  );
};

export type SceneBackgroundProps = {
  palette: ScenePalette;
  // The previous scene's palette; null for the first scene.
  previous: ScenePalette | null;
  seed: number;
};

// The scene transition. Remotion's <TransitionSeries> overlaps neighbouring
// scenes, which shortens the video - and the scene lengths are exactly the
// narration's timing (Day 12), so an overlap would pull the visuals out of sync
// with the voice. Instead, each scene paints the previous scene's background
// underneath and opens its own over it as an expanding circle: a visible
// transition that takes no time away from either scene.
export const SceneBackground: React.FC<SceneBackgroundProps> = ({ palette, previous, seed }) => {
  const frame = useCurrentFrame();
  const radius = interpolate(frame, [0, REVEAL_FRAMES], [0, 150], {
    extrapolateLeft: "clamp",
    extrapolateRight: "clamp",
    easing: Easing.inOut(Easing.cubic),
  });

  return (
    <AbsoluteFill>
      {previous && radius < 150 ? <Layer palette={previous} frame={frame} seed={seed - 1} /> : null}
      <AbsoluteFill style={{ clipPath: previous ? `circle(${radius}% at 50% 45%)` : undefined }}>
        <Layer palette={palette} frame={frame} seed={seed} />
      </AbsoluteFill>
    </AbsoluteFill>
  );
};
