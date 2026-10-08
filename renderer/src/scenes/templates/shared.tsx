import React from "react";
import type { AnimationResult } from "../../animations/primitives";
import type { Scene } from "../../schemas/videoPlan";
import type { ScenePalette } from "../theme";

export const HEADLINE_FONT = "Arial Black, Arial, sans-serif";
export const BODY_FONT = "Arial, sans-serif";
export const RED = "#F87171";
export const GREEN = "#4ADE80";

// Everything a template needs to draw one scene. `frame` is already offset for
// the background reveal; `animation` is the scene's entrance (Day 9).
export type TemplateProps = {
  scene: Scene;
  frame: number;
  fps: number;
  durationInFrames: number;
  palette: ScenePalette;
  badge: string | null;
  assetId: string;
  assetSvgById: Record<string, string>;
  animation: AnimationResult;
  // Stable per plan (variant.ts); templates with layout variants pick from it.
  variantSeed: number;
};

export const Badge: React.FC<{ text: string; palette: ScenePalette }> = ({ text, palette }) => (
  <div
    style={{
      padding: "10px 28px",
      borderRadius: 999,
      backgroundColor: `${palette.accent}26`,
      border: `3px solid ${palette.accent}`,
      color: palette.accent,
      fontFamily: BODY_FONT,
      fontWeight: 800,
      fontSize: 34,
      letterSpacing: 4,
      textTransform: "uppercase",
      marginBottom: 28,
    }}
  >
    {text}
  </div>
);

export const Headline: React.FC<{ text: string; size?: number; style?: React.CSSProperties }> = ({
  text,
  size = 84,
  style,
}) => (
  <div
    style={{
      color: "#FFFFFF",
      fontFamily: HEADLINE_FONT,
      fontSize: size,
      lineHeight: 1.05,
      textTransform: "uppercase",
      textAlign: "center",
      maxWidth: 900,
      textShadow: "0 8px 24px rgba(0, 0, 0, 0.35)",
      ...style,
    }}
  >
    {text}
  </div>
);

export const AssetView: React.FC<{
  svg: string | undefined;
  size: number;
  style?: React.CSSProperties;
  shadow?: boolean;
}> = ({ svg, size, style, shadow = true }) => (
  <div style={{ position: "relative", width: size, height: size, ...style }}>
    {shadow ? (
      // A soft floor shadow grounds the asset instead of leaving it floating.
      <div
        style={{
          position: "absolute",
          left: "18%",
          right: "18%",
          bottom: -10,
          height: size * 0.07,
          borderRadius: "50%",
          background: "radial-gradient(ellipse, rgba(0,0,0,0.35) 0%, transparent 70%)",
        }}
      />
    ) : null}
    {svg ? (
      <div
        style={{ position: "absolute", inset: 0, display: "flex", alignItems: "center", justifyContent: "center" }}
        // Safe: this markup comes from our own hand-authored SVG files under
        // assets/, resolved and read by assetManifest.ts - never from the LLM
        // or any other untrusted source.
        dangerouslySetInnerHTML={{ __html: svg }}
      />
    ) : null}
  </div>
);

// A labelled text card (MYTH / FACT, wrong / right).
export const Card: React.FC<{
  label: string;
  text: string;
  color: string;
  width?: number;
  style?: React.CSSProperties;
  children?: React.ReactNode;
}> = ({ label, text, color, width = 860, style, children }) => (
  <div
    style={{
      position: "relative",
      width,
      padding: "26px 34px 32px",
      borderRadius: 28,
      backgroundColor: "rgba(15, 23, 42, 0.55)",
      border: `5px solid ${color}`,
      boxShadow: "0 18px 40px rgba(0,0,0,0.3)",
      ...style,
    }}
  >
    <div
      style={{
        color,
        fontFamily: BODY_FONT,
        fontWeight: 900,
        fontSize: 36,
        letterSpacing: 6,
        textTransform: "uppercase",
        marginBottom: 10,
      }}
    >
      {label}
    </div>
    <div
      style={{
        position: "relative",
        color: "#FFFFFF",
        fontFamily: HEADLINE_FONT,
        fontSize: 62,
        lineHeight: 1.08,
        textTransform: "uppercase",
      }}
    >
      {text}
      {children}
    </div>
  </div>
);

// The content column every template lays out in: inside the Shorts safe area,
// carrying the scene's entrance animation (Day 9) as one group.
export const Column: React.FC<{ animation: AnimationResult; children: React.ReactNode; gap?: number }> = ({
  animation,
  children,
  gap = 0,
}) => (
  <div
    style={{
      position: "absolute",
      inset: 0,
      paddingTop: 250,
      paddingLeft: 90,
      paddingRight: 90,
      display: "flex",
      flexDirection: "column",
      alignItems: "center",
      gap,
      ...animation.style,
    }}
  >
    {children}
  </div>
);
