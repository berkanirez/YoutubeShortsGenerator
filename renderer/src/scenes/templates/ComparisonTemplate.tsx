import React from "react";
import { GenericTemplate } from "./GenericTemplate";
import { AssetView, Badge, Card, Column, GREEN, RED, type TemplateProps } from "./shared";
import { revealAt, spokenFrames } from "./staging";

// Wrong way vs. right way, side by side; the right way arrives second, when
// the narration gets to it.
export const ComparisonTemplate: React.FC<TemplateProps> = (props) => {
  const { scene, frame, fps, durationInFrames, palette, badge, assetId, assetSvgById, animation } = props;
  if (!scene.onScreenText || !scene.secondaryText) {
    return <GenericTemplate {...props} />;
  }

  const spoken = spokenFrames(scene.spokenDurationSeconds, fps, durationInFrames);
  const right = revealAt(frame, Math.round(spoken * 0.35), 8);
  const cardStyle: React.CSSProperties = { minHeight: 330 };

  return (
    <Column animation={animation}>
      {badge ? <Badge text={badge} palette={palette} /> : null}
      <div style={{ display: "flex", gap: 28, alignItems: "stretch" }}>
        <Card label="✗ Wrong" text={scene.onScreenText} color={RED} width={430} style={cardStyle} />
        <Card
          label="✓ Right"
          text={scene.secondaryText}
          color={GREEN}
          width={430}
          style={{ ...cardStyle, opacity: right, transform: `translateX(${(1 - right) * 80}px)` }}
        />
      </div>
      <AssetView svg={assetSvgById[assetId]} size={380} style={{ marginTop: 50 }} />
    </Column>
  );
};
