import React from "react";
import { GenericTemplate } from "./GenericTemplate";
import { AssetView, Badge, BODY_FONT, Column, Headline, HEADLINE_FONT, type TemplateProps } from "./shared";
import { itemStartFrame, revealAt, spokenFrames } from "./staging";

// Numbered points that appear one by one while they're being talked about.
export const ListTemplate: React.FC<TemplateProps> = (props) => {
  const { scene, frame, fps, durationInFrames, palette, badge, assetId, assetSvgById, animation } = props;
  const items = scene.items ?? [];
  if (items.length < 2) {
    return <GenericTemplate {...props} />;
  }

  const spoken = spokenFrames(scene.spokenDurationSeconds, fps, durationInFrames);

  return (
    <Column animation={animation}>
      {badge ? <Badge text={badge} palette={palette} /> : null}
      {scene.onScreenText ? <Headline text={scene.onScreenText} size={72} style={{ marginBottom: 34 }} /> : null}
      <div style={{ display: "flex", flexDirection: "column", gap: 22, width: 860 }}>
        {items.map((item, index) => {
          const shown = revealAt(frame, itemStartFrame(index, items.length, spoken), 8);
          return (
            <div
              key={`${index}-${item}`}
              style={{
                display: "flex",
                alignItems: "center",
                gap: 28,
                padding: "20px 28px",
                borderRadius: 24,
                backgroundColor: "rgba(15, 23, 42, 0.5)",
                opacity: shown,
                transform: `translateX(${(1 - shown) * -60}px)`,
              }}
            >
              <div
                style={{
                  flex: "0 0 auto",
                  width: 76,
                  height: 76,
                  borderRadius: "50%",
                  backgroundColor: palette.accent,
                  color: "#0F172A",
                  fontFamily: HEADLINE_FONT,
                  fontSize: 42,
                  display: "flex",
                  alignItems: "center",
                  justifyContent: "center",
                }}
              >
                {index + 1}
              </div>
              <div style={{ color: "#FFFFFF", fontFamily: BODY_FONT, fontWeight: 800, fontSize: 54 }}>{item}</div>
            </div>
          );
        })}
      </div>
      <AssetView svg={assetSvgById[assetId]} size={300} style={{ marginTop: 40 }} />
    </Column>
  );
};
