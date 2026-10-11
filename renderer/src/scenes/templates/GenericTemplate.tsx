import React from "react";
import { poseAt } from "./poses";
import { AssetView, Stage, WallText, type TemplateProps } from "./shared";
import { spokenFrames } from "./staging";
import { wallText } from "./wall";

// Outro, and the fallback for any template whose extra data is missing: the visual as
// big as the frame allows, with the scene's slogan on the wall if it is short enough. The
// mascot reacts once (poses.ts), e.g. happy -> thumbs-up at the outro.
export const GenericTemplate: React.FC<TemplateProps> = (props) => {
  const { scene, frame, fps, durationInFrames, palette, assetId, assetSvgById, animation } = props;
  const slogan = wallText(scene.onScreenText);
  const text = slogan && animation.formatText ? animation.formatText(slogan) : slogan;
  const pose = poseAt(scene.type, assetId, frame, spokenFrames(scene.spokenDurationSeconds, fps, durationInFrames), assetSvgById);
  return (
    <Stage animation={animation} wall={text ? <WallText text={text} color={palette.accent} frame={frame} /> : null}>
      {(box) => <AssetView svg={assetSvgById[pose.id]} box={box} />}
    </Stage>
  );
};
