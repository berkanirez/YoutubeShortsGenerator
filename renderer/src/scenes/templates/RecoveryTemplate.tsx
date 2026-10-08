import React from "react";
import { AssetView, Badge, BODY_FONT, Column, Headline, type TemplateProps } from "./shared";
import { itemStartFrame, revealAt, spokenFrames } from "./staging";

// Calm by design: a slow breathing circle (~4s per breath) behind the visual,
// and tips as soft chips rather than numbered bullets.
export const RecoveryTemplate: React.FC<TemplateProps> = ({
  scene,
  frame,
  fps,
  durationInFrames,
  palette,
  badge,
  assetId,
  assetSvgById,
  animation,
}) => {
  const breath = (Math.sin((frame / fps) * ((2 * Math.PI) / 4) - Math.PI / 2) + 1) / 2;
  const items = scene.items ?? [];
  const spoken = spokenFrames(scene.spokenDurationSeconds, fps, durationInFrames);

  return (
    <Column animation={animation}>
      {badge ? <Badge text={badge} palette={palette} /> : null}
      {scene.onScreenText ? <Headline text={scene.onScreenText} size={76} /> : null}
      <div style={{ position: "relative", width: 520, height: 520, marginTop: 40 }}>
        <div
          style={{
            position: "absolute",
            inset: 0,
            borderRadius: "50%",
            background: `radial-gradient(circle, ${palette.accent}55 0%, ${palette.accent}00 70%)`,
            transform: `scale(${0.85 + breath * 0.25})`,
          }}
        />
        <AssetView svg={assetSvgById[assetId]} size={520} />
      </div>
      {items.length > 0 ? (
        <div style={{ display: "flex", flexWrap: "wrap", justifyContent: "center", gap: 18, marginTop: 34, maxWidth: 900 }}>
          {items.map((item, index) => (
            <div
              key={`${index}-${item}`}
              style={{
                padding: "14px 28px",
                borderRadius: 999,
                backgroundColor: `${palette.accent}33`,
                border: `3px solid ${palette.accent}`,
                color: "#FFFFFF",
                fontFamily: BODY_FONT,
                fontWeight: 800,
                fontSize: 40,
                opacity: revealAt(frame, itemStartFrame(index, items.length, spoken), 10),
              }}
            >
              {item}
            </div>
          ))}
        </div>
      ) : null}
    </Column>
  );
};
