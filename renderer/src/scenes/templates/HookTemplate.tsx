import React from "react";
import { Easing, interpolate } from "remotion";
import { GenericTemplate } from "./GenericTemplate";
import { AssetView, Column, HEADLINE_FONT, type TemplateProps } from "./shared";
import { pickVariant } from "./variant";

const HOOK_VARIANTS = 3;

// Three different openings, chosen per plan (variant.ts): the hook is the most
// repeated moment across a channel, so it's where sameness would show first.
//   0 - burst:  big headline over the visual with an accent burst (Day 16)
//   1 - card:   headline on a tilted accent card that slaps in, visual below
//   2 - split:  oversized left-aligned headline, visual tilted in the corner
export const HookTemplate: React.FC<TemplateProps> = (props) => {
  const variant = pickVariant(props.variantSeed, HOOK_VARIANTS);
  if (variant === 1) {
    return <CardHook {...props} />;
  }
  if (variant === 2) {
    return <SplitHook {...props} />;
  }
  return <GenericTemplate {...props} />;
};

const slap = (frame: number) =>
  interpolate(frame, [0, 9], [0, 1], { extrapolateLeft: "clamp", extrapolateRight: "clamp", easing: Easing.out(Easing.back(1.8)) });

const CardHook: React.FC<TemplateProps> = ({ scene, frame, palette, assetId, assetSvgById, animation }) => {
  const progress = slap(frame);
  return (
    <Column animation={animation}>
      <div
        style={{
          marginTop: 40,
          padding: "36px 44px",
          maxWidth: 900,
          borderRadius: 30,
          backgroundColor: palette.accent,
          color: "#0F172A",
          fontFamily: HEADLINE_FONT,
          fontSize: 100,
          lineHeight: 1.02,
          textTransform: "uppercase",
          textAlign: "center",
          boxShadow: "0 24px 50px rgba(0,0,0,0.35)",
          transform: `rotate(${interpolate(progress, [0, 1], [-12, -3])}deg) scale(${interpolate(progress, [0, 1], [1.4, 1])})`,
        }}
      >
        {scene.onScreenText ?? scene.narration}
      </div>
      <AssetView svg={assetSvgById[assetId]} size={540} style={{ marginTop: 70 }} />
    </Column>
  );
};

const SplitHook: React.FC<TemplateProps> = ({ scene, frame, palette, assetId, assetSvgById, animation }) => {
  const progress = slap(frame);
  return (
    <Column animation={animation}>
      <div style={{ width: 900, marginTop: 20 }}>
        <div style={{ width: 120, height: 14, borderRadius: 7, backgroundColor: palette.accent, marginBottom: 28, transform: `scaleX(${progress})`, transformOrigin: "left" }} />
        <div
          style={{
            color: "#FFFFFF",
            fontFamily: HEADLINE_FONT,
            fontSize: 124,
            lineHeight: 0.98,
            textTransform: "uppercase",
            textAlign: "left",
            textShadow: "0 10px 28px rgba(0,0,0,0.35)",
            transform: `translateX(${(1 - progress) * -80}px)`,
          }}
        >
          {scene.onScreenText ?? scene.narration}
        </div>
      </div>
      <AssetView
        svg={assetSvgById[assetId]}
        size={560}
        style={{ alignSelf: "flex-end", marginTop: 30, transform: `rotate(${interpolate(progress, [0, 1], [18, 6])}deg)` }}
      />
    </Column>
  );
};
