import React from "react";
import { AbsoluteFill, Easing, interpolate } from "remotion";

// The camera moves, the character doesn't: every move is a change of framing on the whole
// shot (a fast push-in with motion blur, a slow push, a pan, an impact punch, a close-up).
// The earlier squash/sway of the picture itself read as "jelly"; this is the cinematography
// the reference channels use. Off when the plan's style is "static".
export type CameraMove = "whipIn" | "pushIn" | "pan" | "punch" | "closeUp" | "none";

const clamp = { extrapolateLeft: "clamp", extrapolateRight: "clamp" } as const;

export function cameraStyle(move: CameraMove, frame: number, durationInFrames: number): React.CSSProperties {
  const progress = interpolate(frame, [0, Math.max(1, durationInFrames)], [0, 1], clamp);
  switch (move) {
    case "whipIn": {
      const t = interpolate(frame, [0, 9], [0, 1], { ...clamp, easing: Easing.out(Easing.cubic) });
      const blur = interpolate(frame, [0, 7], [16, 0], clamp);
      return {
        transform: `scale(${(1.7 - 0.7 * t) * (1 + progress * 0.04)})`,
        filter: blur > 0.2 ? `blur(${blur.toFixed(1)}px)` : undefined,
        transformOrigin: "50% 45%",
      };
    }
    case "pushIn":
      return { transform: `scale(${1 + progress * 0.07})`, transformOrigin: "50% 55%" };
    case "pan":
      return { transform: `translateX(${interpolate(progress, [0, 1], [60, -60])}px) scale(1.06)` };
    case "punch": {
      const kick = interpolate(frame, [0, 3, 9], [1.14, 1.0, 1.0], { ...clamp, easing: Easing.out(Easing.cubic) });
      return { transform: `scale(${kick + progress * 0.04})`, transformOrigin: "50% 40%" };
    }
    case "closeUp":
      // Upper body big, head kept below the lettering at the top.
      return { transform: `translateY(240px) scale(${1.6 + progress * 0.06})`, transformOrigin: "50% 20%" };
    default:
      return {};
  }
}

export const Camera: React.FC<{
  move: CameraMove;
  frame: number;
  durationInFrames: number;
  enabled: boolean;
  children: React.ReactNode;
}> = ({ move, frame, durationInFrames, enabled, children }) => (
  <AbsoluteFill style={enabled ? cameraStyle(move, frame, durationInFrames) : undefined}>{children}</AbsoluteFill>
);
