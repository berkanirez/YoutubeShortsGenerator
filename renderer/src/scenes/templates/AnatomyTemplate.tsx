import React from "react";
import { AssetView, Stage, WallText, type TemplateProps } from "./shared";
import { wallText } from "./wall";

// Muscle focus: the red-highlighted body as big as the frame allows - the red IS the sign -
// with the muscle's name on the wall. A slow, slight "breath" keeps the still drawing alive.
export const AnatomyTemplate: React.FC<TemplateProps> = ({ scene, frame, fps, palette, assetId, assetSvgById, animation }) => {
  const breath = 1 + Math.sin((frame / fps) * Math.PI * 1.2) * 0.012;
  const slogan = wallText(scene.onScreenText);
  return (
    <Stage animation={animation} wall={slogan ? <WallText text={slogan} color={palette.accent} frame={frame} /> : null}>
      {(box) => <AssetView svg={assetSvgById[assetId]} box={box} shadow={false} style={{ transform: `scale(${breath})` }} />}
    </Stage>
  );
};
