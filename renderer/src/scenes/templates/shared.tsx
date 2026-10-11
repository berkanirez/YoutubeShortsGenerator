import React from "react";
import { Easing, Img, interpolate } from "remotion";
import { imageInfo, imageSrc } from "../../assets/art";
import { FLOOR_Y, FRAME_WIDTH, placeArt, VISUAL_TOP_ALONE, VISUAL_TOP_WITH_WALL, WALL_TOP, type Box } from "../layout";
import type { AnimationResult } from "../../animations/primitives";
import type { Scene } from "../../schemas/videoPlan";
import type { ScenePalette } from "../theme";
import { fitBlockFontSize } from "./fit";

// Condensed, heavy, ships with Windows: reads like lettering painted on a gym wall.
export const WALL_FONT = "Impact, 'Arial Black', sans-serif";
export const RED = "#EF4444";
export const GREEN = "#22C55E";

const clamp = { extrapolateLeft: "clamp", extrapolateRight: "clamp" } as const;

// Everything a template needs to draw one scene. `frame` is already offset for
// the background reveal; `animation` is the scene's entrance (Day 9).
export type TemplateProps = {
  scene: Scene;
  frame: number;
  fps: number;
  durationInFrames: number;
  palette: ScenePalette;
  assetId: string;
  assetSvgById: Record<string, string>;
  animation: AnimationResult;
  // Stable per plan (variant.ts); templates with layout variants pick from it.
  variantSeed: number;
};

const WALL_WIDTH = 940;

// A slogan painted on the wall behind the visual: big condensed letters, slightly tilted,
// with a flat offset shadow - no bubble, no card. It "paints on" left to right.
export const WallText: React.FC<{
  text: string;
  color: string;
  frame: number;
  size?: number;
  maxLines?: number;
  tilt?: number;
}> = ({ text, color, frame, size = 130, maxLines = 2, tilt = -4 }) => {
  const paint = interpolate(frame, [0, 9], [0, 100], { ...clamp, easing: Easing.out(Easing.cubic) });
  return (
    <div
      style={{
        width: WALL_WIDTH,
        color,
        fontFamily: WALL_FONT,
        // fit.ts is calibrated on Arial Black; Impact is ~20% narrower per letter.
        fontSize: fitBlockFontSize(text, WALL_WIDTH * 1.2, size, 64, maxLines),
        lineHeight: 0.95,
        letterSpacing: 2,
        textTransform: "uppercase",
        textAlign: "center",
        textShadow: "7px 7px 0 rgba(0, 0, 0, 0.45)",
        transform: `rotate(${tilt}deg)`,
        clipPath: `inset(-20% ${100 - paint}% -20% -5%)`,
      }}
    >
      {text}
    </div>
  );
};

export type SignKind = "check" | "cross" | "question";

// A big plain mark that says it without words, stamped on with a little overshoot.
// A thick white under-stroke keeps it readable on top of the art.
export const Sign: React.FC<{ kind: SignKind; frame: number; color: string; size?: number; style?: React.CSSProperties }> = ({
  kind,
  frame,
  color,
  size = 300,
  style,
}) => {
  if (frame < 0) {
    return null;
  }
  const stamp = interpolate(frame, [0, 8], [0, 1], { ...clamp, easing: Easing.out(Easing.back(2)) });
  const stroke = (d: string) => (
    <>
      <path d={d} fill="none" stroke="#FFFFFF" strokeWidth={84} strokeLinecap="round" strokeLinejoin="round" />
      <path d={d} fill="none" stroke={color} strokeWidth={54} strokeLinecap="round" strokeLinejoin="round" />
    </>
  );
  return (
    <svg
      viewBox="0 0 300 300"
      width={size}
      height={size}
      style={{
        position: "absolute",
        overflow: "visible",
        opacity: Math.min(1, stamp * 1.5),
        transform: `scale(${interpolate(stamp, [0, 1], [1.7, 1])}) rotate(${interpolate(stamp, [0, 1], [-20, -6])}deg)`,
        ...style,
      }}
    >
      {kind === "check" ? stroke("M55 160 L120 225 L250 80") : null}
      {kind === "cross" ? stroke("M70 70 L230 230 M230 70 L70 230") : null}
      {kind === "question" ? (
        <text
          x="150"
          y="265"
          textAnchor="middle"
          fontFamily={WALL_FONT}
          fontSize={300}
          fill={color}
          stroke="#FFFFFF"
          strokeWidth={14}
          paintOrder="stroke"
        >
          ?
        </text>
      ) : null}
    </svg>
  );
};

// Measured PNG art is placed by its figure's bounds (feet on the floor, see layout.ts). The
// picture itself is never wobbled (squash/sway read as jelly, not animation): movement comes
// from changing drawn poses. No drop shadow: on the flat grey wall it read as a dirty smudge, and the
// floor line already grounds the figure.
export const AssetView: React.FC<{
  svg: string | undefined;
  box: Box;
  style?: React.CSSProperties;
  // Place by these figure bounds instead of the picture's own (both poses of a pair use their
  // union, so the floor/bar stays put when the rep flips between them).
  bounds?: readonly [number, number, number, number];
}> = ({ svg, box, style, bounds }) => {
  const info = svg ? imageInfo(svg) : null;
  const src = svg ? imageSrc(svg) : null;
  const frameStyle: React.CSSProperties = { position: "relative", width: box.width, height: box.height, ...style };

  if (info) {
    const p = placeArt(bounds ? { ...info, bounds } : info, info.anchor, box);
    return (
      <div style={frameStyle}>
        <div
          style={{
            position: "absolute",
            left: p.left,
            top: p.top,
            width: p.width,
            height: p.height,
          }}
        >
          {/* <Img> delays the frame until the image is decoded: never a blank picture. */}
          <Img src={info.src} style={{ width: "100%", height: "100%" }} />
        </div>
      </div>
    );
  }

  return (
    <div style={frameStyle}>
      {src ? (
        <Img
          src={src}
          style={{ position: "absolute", inset: 0, width: "100%", height: "100%", objectFit: "contain", objectPosition: "50% 100%" }}
        />
      ) : svg ? (
        <div
          style={{ position: "absolute", inset: 0, display: "flex", alignItems: "flex-end", justifyContent: "center" }}
          // Safe: this markup comes from our own hand-authored SVG files under
          // assets/, resolved and read by assetManifest.ts - never from the LLM
          // or any other untrusted source.
          dangerouslySetInnerHTML={{ __html: svg }}
        />
      ) : null}
    </div>
  );
};

// Every template's layout: the slogan on the wall at the top, the visual as big as the frame
// allows below it, standing on the floor (layout.ts), and anything stamped on the visual
// (signs, counters) positioned inside `children`'s box.
export const Stage: React.FC<{
  animation: AnimationResult;
  wall: React.ReactNode;
  children: (box: Box) => React.ReactNode;
}> = ({ animation, wall, children }) => {
  const top = wall ? VISUAL_TOP_WITH_WALL : VISUAL_TOP_ALONE;
  const box = { width: FRAME_WIDTH, height: FLOOR_Y - top };
  return (
    <>
      {wall ? (
        <div style={{ position: "absolute", top: WALL_TOP, left: 0, right: 0, display: "flex", justifyContent: "center" }}>
          {wall}
        </div>
      ) : null}
      <div style={{ position: "absolute", left: 0, top, width: box.width, height: box.height, ...animation.style }}>
        {children(box)}
      </div>
    </>
  );
};

export type { Box } from "../layout";
