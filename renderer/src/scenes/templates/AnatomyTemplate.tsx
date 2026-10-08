import React from "react";
import { AssetView, Badge, Column, Headline, type TemplateProps } from "./shared";

// Muscle focus: the diagram large, with a slow pulsing ring that says "look here".
export const AnatomyTemplate: React.FC<TemplateProps> = ({
  scene,
  frame,
  fps,
  palette,
  badge,
  assetId,
  assetSvgById,
  animation,
}) => {
  const beat = (Math.sin((frame / fps) * Math.PI * 1.6) + 1) / 2; // ~0.8 pulses per second
  return (
    <Column animation={animation}>
      {badge ? <Badge text={badge} palette={palette} /> : null}
      {scene.onScreenText ? <Headline text={scene.onScreenText} /> : null}
      <div style={{ position: "relative", marginTop: 50, width: 640, height: 640 }}>
        <div
          style={{
            position: "absolute",
            inset: 40,
            borderRadius: "50%",
            border: `8px solid ${palette.accent}`,
            opacity: 0.25 + beat * 0.5,
            transform: `scale(${0.92 + beat * 0.08})`,
            boxShadow: `0 0 ${30 + beat * 40}px ${palette.accent}`,
          }}
        />
        <AssetView svg={assetSvgById[assetId]} size={640} shadow={false} />
      </div>
    </Column>
  );
};
