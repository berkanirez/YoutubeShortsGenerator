import type React from "react";
import { Easing, interpolate, spring } from "remotion";
import type { AnimationId } from "../schemas/videoPlan";

export type AnimationContext = {
  // Frame number local to the scene (0 = the scene's first frame), as
  // useCurrentFrame() returns it inside a <Series.Sequence>.
  frame: number;
  fps: number;
  durationInFrames: number;
};

export type AnimationResult = {
  style: React.CSSProperties;
  // Only "counter" uses this: it changes what the text says, not just where
  // it is, so it can't be expressed as a style alone.
  formatText?: (text: string) => string;
};

export type AnimationFn = (context: AnimationContext) => AnimationResult;

const clamp = { extrapolateLeft: "clamp", extrapolateRight: "clamp" } as const;

// Restraint (CLAUDE.md section 11): entrance motion finishes within half a
// second at 30fps, so the viewer spends the rest of the scene reading the
// content rather than watching it move. Only "pulse" keeps moving, and only
// by a few percent.
const ENTRANCE_FRAMES = 15;

const fadeIn = (frame: number, frames = ENTRANCE_FRAMES): number =>
  interpolate(frame, [0, frames], [0, 1], clamp);

export const fade: AnimationFn = ({ frame }) => ({
  style: { opacity: fadeIn(frame) },
});

export const slide: AnimationFn = ({ frame }) => {
  // Easing.out: starts fast, decelerates into place - reads as "arriving",
  // where linear motion would read as mechanical.
  const progress = interpolate(frame, [0, ENTRANCE_FRAMES], [0, 1], {
    ...clamp,
    easing: Easing.out(Easing.cubic),
  });
  return {
    style: {
      opacity: progress,
      transform: `translateY(${(1 - progress) * 120}px)`,
    },
  };
};

export const pop: AnimationFn = ({ frame, fps }) => {
  const progress = spring({ frame, fps, config: { damping: 10, stiffness: 180 } });
  return {
    style: {
      opacity: fadeIn(frame, 6),
      transform: `scale(${interpolate(progress, [0, 1], [0.6, 1])})`,
    },
  };
};

export const punchZoom: AnimationFn = ({ frame }) => {
  // Starts oversized and snaps down hard - a "hit" for hooks, over in ~0.25s.
  const scale = interpolate(frame, [0, 8], [1.35, 1], {
    ...clamp,
    easing: Easing.out(Easing.exp),
  });
  return { style: { opacity: fadeIn(frame, 4), transform: `scale(${scale})` } };
};

export const shake: AnimationFn = ({ frame }) => {
  // A pure function of the frame number, never Math.random(): every render of
  // the same plan must produce identical frames (deterministic renders).
  const decay = interpolate(frame, [0, 18], [1, 0], clamp);
  const offsetX = Math.sin(frame * 1.8) * 18 * decay;
  return { style: { opacity: fadeIn(frame, 4), transform: `translateX(${offsetX}px)` } };
};

export const bounce: AnimationFn = ({ frame, fps }) => {
  const progress = spring({ frame, fps, config: { damping: 7, mass: 0.8 } });
  return {
    style: {
      opacity: fadeIn(frame, 5),
      transform: `translateY(${interpolate(progress, [0, 1], [-160, 0])}px)`,
    },
  };
};

export const pulse: AnimationFn = ({ frame, fps }) => {
  // One gentle 3% breath per second for the whole scene. sin(0) = 0, so it
  // starts at exactly scale(1) - no jump on the first frame.
  const scale = 1 + Math.sin((frame / fps) * Math.PI * 2) * 0.03;
  return { style: { opacity: fadeIn(frame), transform: `scale(${scale})` } };
};

export const counter: AnimationFn = ({ frame, fps, durationInFrames }) => {
  const countFrames = Math.min(Math.round(fps * 1.2), durationInFrames);
  return {
    style: { opacity: fadeIn(frame, 6) },
    // Counts the first number in the text up from 0 ("10 clean reps" shows
    // 0, 1, 2 ... 10). Text with no number in it is left unchanged, so
    // "counter" on such a scene degrades to a plain fade rather than failing.
    formatText: (text) =>
      text.replace(/\d+/, (match) =>
        String(
          Math.round(
            interpolate(frame, [0, countFrames], [0, Number(match)], {
              ...clamp,
              easing: Easing.out(Easing.cubic),
            }),
          ),
        ),
      ),
  };
};

export const highlight: AnimationFn = ({ frame }) => {
  // Flares an orange glow (the mascot's accent color) and settles to a faint
  // one - draws the eye once, then gets out of the way.
  const glow = interpolate(frame, [0, 10, 25], [0, 28, 10], clamp);
  return {
    style: { opacity: fadeIn(frame), filter: `drop-shadow(0 0 ${glow}px #FB923C)` },
  };
};

// Record<AnimationId, ...> rather than a switch with a default branch: if an
// id is ever added to AnimationIdSchema without an implementation here,
// TypeScript refuses to compile this file instead of a scene silently
// rendering with no animation.
export const ANIMATIONS: Record<AnimationId, AnimationFn> = {
  fade,
  slide,
  pop,
  punchZoom,
  shake,
  bounce,
  pulse,
  counter,
  highlight,
};

export function getAnimation(id: AnimationId, context: AnimationContext): AnimationResult {
  return ANIMATIONS[id](context);
}
