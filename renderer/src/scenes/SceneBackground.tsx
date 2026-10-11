import React from "react";
import { AbsoluteFill, Easing, interpolate, useCurrentFrame } from "remotion";
import { FLOOR_Y, FRAME_WIDTH } from "./layout";
import type { ScenePalette } from "./theme";

// How long the new scene's background takes to sweep over the previous one.
export const REVEAL_FRAMES = 10;

// The diagonal wipes' edge is slanted by this many pixels (top runs ahead of the bottom).
const SLANT = 300;
const FRAME_HEIGHT = 1920;

// A flat gym wall with one static diagonal stripe, and the floor the characters stand on:
// a darker band with a lighter front edge at FLOOR_Y. Nothing drifts or glows (the earlier
// gradient + moving glow blobs read as a tech/AI channel). `seed` mirrors the stripe on odd
// scenes so consecutive scenes don't look identical.
const Layer: React.FC<{ palette: ScenePalette; seed: number }> = ({ palette, seed }) => (
  <AbsoluteFill style={{ backgroundColor: palette.background, overflow: "hidden" }}>
    <div style={{ position: "absolute", left: 0, top: 0, width: FRAME_WIDTH, height: FLOOR_Y, overflow: "hidden" }}>
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
    </div>
    <div style={{ position: "absolute", left: 0, right: 0, top: FLOOR_Y, bottom: 0, backgroundColor: palette.floor }} />
    <div style={{ position: "absolute", left: 0, right: 0, top: FLOOR_Y - 3, height: 6, backgroundColor: palette.floorEdge }} />
  </AbsoluteFill>
);

export type SceneBackgroundProps = {
  palette: ScenePalette;
  // The previous scene's palette; null for the first scene.
  previous: ScenePalette | null;
  seed: number;
  // Which wipe this video uses (from its variant): 0 diagonal left->right, 1 diagonal
  // right->left, 2 a shutter rising from the floor.
  wipeStyle: number;
};

// The clip of the new layer and of the accent edge for wipe `style` at progress p (0..1).
function wipe(style: number, p: number): { reveal: string; edge: string } {
  if (style === 2) {
    const y = interpolate(p, [0, 1], [FRAME_HEIGHT, -40]);
    return {
      reveal: `polygon(0 ${y}px, 100% ${y}px, 100% 100%, 0 100%)`,
      edge: `polygon(0 ${y - 40}px, 100% ${y - 40}px, 100% ${y}px, 0 ${y}px)`,
    };
  }
  const x = interpolate(p, [0, 1], [-SLANT, FRAME_WIDTH]);
  if (style === 1) {
    // Mirrored: the new scene comes in from the right.
    const m = (v: number) => FRAME_WIDTH - v;
    return {
      reveal: `polygon(${m(x + SLANT)}px 0, 100% 0, 100% 100%, ${m(x)}px 100%)`,
      edge: `polygon(${m(x + SLANT)}px 0, ${m(x + SLANT - 40)}px 0, ${m(x - 40)}px 100%, ${m(x)}px 100%)`,
    };
  }
  return {
    reveal: `polygon(0 0, ${x + SLANT}px 0, ${x}px 100%, 0 100%)`,
    edge: `polygon(${x + SLANT - 40}px 0, ${x + SLANT}px 0, ${x}px 100%, ${x - 40}px 100%)`,
  };
}

// The scene transition. Remotion's <TransitionSeries> overlaps neighbouring
// scenes, which shortens the video - and the scene lengths are exactly the
// narration's timing (Day 12), so an overlap would pull the visuals out of sync
// with the voice. Instead, each scene paints the previous scene's background
// underneath and sweeps its own over it with a hard edge: a quick, sporty wipe that
// takes no time away from either scene. The edge is a bright accent slash - with
// every scene on the same grey, the cut would otherwise be invisible.
export const SceneBackground: React.FC<SceneBackgroundProps> = ({ palette, previous, seed, wipeStyle }) => {
  const frame = useCurrentFrame();
  const p = interpolate(frame, [0, REVEAL_FRAMES], [0, 1], {
    extrapolateLeft: "clamp",
    extrapolateRight: "clamp",
    easing: Easing.inOut(Easing.cubic),
  });
  const wiping = previous !== null && frame < REVEAL_FRAMES;
  const clip = wipe(wipeStyle % 3, p);

  return (
    <AbsoluteFill>
      {wiping ? <Layer palette={previous} seed={seed - 1} /> : null}
      <AbsoluteFill style={{ clipPath: wiping ? clip.reveal : undefined }}>
        <Layer palette={palette} seed={seed} />
      </AbsoluteFill>
      {wiping ? <AbsoluteFill style={{ backgroundColor: palette.accent, clipPath: clip.edge }} /> : null}
    </AbsoluteFill>
  );
};
