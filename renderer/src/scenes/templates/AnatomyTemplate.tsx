import React from "react";
import { AssetView, Stage, WallText, type TemplateProps } from "./shared";
import { wallText } from "./wall";

// Muscle focus: the red-highlighted body as big as the frame allows - the red IS the sign -
// with the muscle's name on the wall.
export const AnatomyTemplate: React.FC<TemplateProps> = (props) => {
  const { scene, frame, palette, assetId, assetSvgById, animation } = props;
  const slogan = wallText(scene.onScreenText);
  return (
    <Stage animation={animation} wall={slogan ? <WallText text={slogan} color={palette.accent} frame={frame} /> : null}>
      {(box) => <AssetView svg={assetSvgById[assetId]} box={box} />}
    </Stage>
  );
};
