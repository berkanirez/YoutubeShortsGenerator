import React from "react";
import { AssetView, Stage, WallText, type TemplateProps } from "./shared";
import { wallText } from "./wall";

// Outro, and the fallback for any template whose extra data is missing: the visual as
// big as the frame allows, with the scene's slogan on the wall if it is short enough.
export const GenericTemplate: React.FC<TemplateProps> = ({ scene, frame, palette, assetId, assetSvgById, animation }) => {
  const slogan = wallText(scene.onScreenText);
  const text = slogan && animation.formatText ? animation.formatText(slogan) : slogan;
  return (
    <Stage animation={animation} wall={text ? <WallText text={text} color={palette.accent} frame={frame} /> : null}>
      {(box) => <AssetView svg={assetSvgById[assetId]} box={box} />}
    </Stage>
  );
};
