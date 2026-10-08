import React from "react";
import { Easing, interpolate } from "remotion";
import { AssetView, Badge, Column, Headline, type TemplateProps } from "./shared";

// Hook, Outro, and the fallback for any template whose extra data is missing
// (a MythVsFact without a fact still renders as a clean card). This is the
// Day 16 layout: badge -> headline -> big visual.
export const GenericTemplate: React.FC<TemplateProps> = ({
  scene,
  frame,
  palette,
  badge,
  assetId,
  assetSvgById,
  animation,
}) => {
  const isHook = scene.type === "Hook";
  const headline =
    scene.onScreenText && animation.formatText ? animation.formatText(scene.onScreenText) : scene.onScreenText;

  // Hook energy: a burst of the accent color behind the visual that blooms in
  // with the punch and then breathes slowly.
  const burst = isHook
    ? interpolate(frame, [0, 10], [0.4, 1], {
        extrapolateLeft: "clamp",
        extrapolateRight: "clamp",
        easing: Easing.out(Easing.back(2)),
      }) *
      (1 + Math.sin(frame / 12) * 0.04)
    : 0;

  return (
    <Column animation={animation}>
      {badge ? <Badge text={badge} palette={palette} /> : null}
      {headline ? <Headline text={headline} size={isHook ? 108 : 84} /> : null}
      <div style={{ position: "relative", marginTop: 50 }}>
        {isHook ? (
          <div
            style={{
              position: "absolute",
              inset: -120,
              borderRadius: "50%",
              background: `radial-gradient(circle, ${palette.accent}66 0%, ${palette.accent}00 62%)`,
              transform: `scale(${burst})`,
            }}
          />
        ) : null}
        <AssetView svg={assetSvgById[assetId]} size={600} />
      </div>
    </Column>
  );
};
