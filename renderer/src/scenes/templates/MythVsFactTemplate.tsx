import React from "react";
import { interpolate } from "remotion";
import { GenericTemplate } from "./GenericTemplate";
import { AssetView, Badge, Card, Column, GREEN, RED, type TemplateProps } from "./shared";
import { revealAt, spokenFrames } from "./staging";

// The myth is shown, struck through while it's being debunked, and the fact
// lands like a stamp - the structure of the sentence made visible.
export const MythVsFactTemplate: React.FC<TemplateProps> = (props) => {
  const { scene, frame, fps, durationInFrames, palette, badge, assetId, assetSvgById, animation } = props;
  if (!scene.onScreenText || !scene.secondaryText) {
    return <GenericTemplate {...props} />;
  }

  const spoken = spokenFrames(scene.spokenDurationSeconds, fps, durationInFrames);
  const strikeStart = Math.round(spoken * 0.3);
  const factStart = Math.round(spoken * 0.4);
  const strike = revealAt(frame, strikeStart, 10);
  const fact = revealAt(frame, factStart, 7);

  return (
    <Column animation={animation}>
      {badge ? <Badge text={badge} palette={palette} /> : null}
      <Card label="Myth" text={scene.onScreenText} color={RED} style={{ opacity: 1 - strike * 0.35 }}>
        <div
          style={{
            position: "absolute",
            left: -8,
            top: "50%",
            height: 10,
            width: `${strike * 104}%`,
            borderRadius: 5,
            backgroundColor: RED,
            transform: "rotate(-2deg)",
          }}
        />
      </Card>
      <Card
        label="Fact"
        text={scene.secondaryText}
        color={GREEN}
        style={{
          marginTop: 36,
          opacity: fact,
          transform: `scale(${interpolate(fact, [0, 1], [1.25, 1])}) rotate(${interpolate(fact, [0, 1], [-6, -1.5])}deg)`,
        }}
      />
      <AssetView svg={assetSvgById[assetId]} size={300} style={{ marginTop: 40 }} />
    </Column>
  );
};
