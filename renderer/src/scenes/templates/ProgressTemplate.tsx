import React from "react";
import { Easing, interpolate } from "remotion";
import { GenericTemplate } from "./GenericTemplate";
import { AssetView, Badge, BODY_FONT, Column, HEADLINE_FONT, type TemplateProps } from "./shared";
import { revealAt, splitLeadingNumber } from "./staging";

// A number worth remembering, counted up big, with a bar that fills as it counts.
export const ProgressTemplate: React.FC<TemplateProps> = (props) => {
  const { scene, frame, fps, palette, badge, assetId, assetSvgById, animation } = props;
  const parts = scene.onScreenText ? splitLeadingNumber(scene.onScreenText) : null;
  if (!parts) {
    return <GenericTemplate {...props} />;
  }

  const countFrames = Math.round(fps * 1.2);
  const progress = interpolate(frame, [0, countFrames], [0, 1], {
    extrapolateLeft: "clamp",
    extrapolateRight: "clamp",
    easing: Easing.out(Easing.cubic),
  });
  const label = [parts.before, parts.after].filter((s) => s.length > 0).join(" ");
  const unlock = revealAt(frame, countFrames, 8);

  return (
    <Column animation={animation}>
      {badge ? <Badge text={badge} palette={palette} /> : null}
      <div
        style={{
          color: palette.accent,
          fontFamily: HEADLINE_FONT,
          fontSize: 260,
          lineHeight: 1,
          textShadow: "0 12px 30px rgba(0,0,0,0.35)",
          // Fixed-width digits: the number doesn't jitter sideways as it counts
          // (the Day 9 counter's known issue).
          fontVariantNumeric: "tabular-nums",
        }}
      >
        {Math.round(progress * parts.value)}
      </div>
      {label ? (
        <div style={{ color: "#FFFFFF", fontFamily: HEADLINE_FONT, fontSize: 64, textTransform: "uppercase", textAlign: "center" }}>
          {label}
        </div>
      ) : null}
      <div style={{ width: 760, height: 34, borderRadius: 17, backgroundColor: "rgba(255,255,255,0.18)", marginTop: 36 }}>
        <div style={{ width: `${progress * 100}%`, height: "100%", borderRadius: 17, backgroundColor: palette.accent }} />
      </div>
      {scene.secondaryText ? (
        <div
          style={{
            marginTop: 30,
            color: "#FFFFFF",
            fontFamily: BODY_FONT,
            fontWeight: 800,
            fontSize: 50,
            opacity: unlock,
          }}
        >
          → {scene.secondaryText}
        </div>
      ) : null}
      <AssetView svg={assetSvgById[assetId]} size={300} style={{ marginTop: 30 }} />
    </Column>
  );
};
