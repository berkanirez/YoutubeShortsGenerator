import React from "react";
import { AbsoluteFill, Easing, interpolate, useCurrentFrame } from "remotion";
import type { ScenePalette } from "./theme";

// How long the new scene's background takes to sweep over the previous one.
export const REVEAL_FRAMES = 10;

// The wipe's edge is slanted by this many pixels (top runs ahead of the bottom).
const SLANT = 300;

// A flat page with one static diagonal stripe, like a team jersey or a gym wall
// marking. Nothing drifts or glows: the earlier gradient + moving glow blobs made
// the videos look like a tech/AI channel. `seed` mirrors the stripe on odd scenes
// so consecutive scenes don't look identical.
const Layer: React.FC<{ palette: ScenePalette; seed: number }> = ({ palette, seed }) => (
  <AbsoluteFill style={{ backgroundColor: palette.background, overflow: "hidden" }}>
    <div
      style={{
        position: "absolute",
        width: 2600,
        height: 300,
        left: -760,
        top: 1150,
        backgroundColor: palette.stripe,
        transform: `rotate(${seed % 2 === 0 ? -14 : 14}deg)`,
      }}
    />
  </AbsoluteFill>
);

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
// underneath and sweeps its own over it with a hard diagonal edge: a quick,
// sporty wipe that takes no time away from either scene.
export const SceneBackground: React.FC<SceneBackgroundProps> = ({ palette, previous, seed }) => {
  const frame = useCurrentFrame();
  const edge = interpolate(frame, [0, REVEAL_FRAMES], [-SLANT, 1080], {
    extrapolateLeft: "clamp",
    extrapolateRight: "clamp",
    easing: Easing.inOut(Easing.cubic),
  });
  const wiping = previous !== null && frame < REVEAL_FRAMES;

  return (
    <AbsoluteFill>
      {wiping ? <Layer palette={previous} seed={seed - 1} /> : null}
      <AbsoluteFill
        style={{
          clipPath: wiping ? `polygon(0 0, ${edge + SLANT}px 0, ${edge}px 100%, 0 100%)` : undefined,
        }}
      >
        <Layer palette={palette} seed={seed} />
      </AbsoluteFill>
      {wiping ? (
        // The wipe's edge as a bright accent slash: with every scene on the same grey, the
        // cut would otherwise be invisible.
        <AbsoluteFill
          style={{
            backgroundColor: palette.accent,
            clipPath: `polygon(${edge + SLANT - 40}px 0, ${edge + SLANT}px 0, ${edge}px 100%, ${edge - 40}px 100%)`,
          }}
        />
      ) : null}
    </AbsoluteFill>
  );
};
