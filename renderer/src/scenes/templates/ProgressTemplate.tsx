import React from "react";
import { Easing, interpolate } from "remotion";
import { GenericTemplate } from "./GenericTemplate";
import { AssetView, Stage, WallText, type TemplateProps } from "./shared";
import { splitLeadingNumber } from "./staging";
import { wallText } from "./wall";

// A number worth remembering, counted up and painted on the wall like a scoreboard.
export const ProgressTemplate: React.FC<TemplateProps> = (props) => {
  const { scene, frame, fps, palette, assetId, assetSvgById, animation } = props;
  const parts = scene.onScreenText ? splitLeadingNumber(scene.onScreenText) : null;
  if (!parts) {
    return <GenericTemplate {...props} />;
  }

  const progress = interpolate(frame, [0, Math.round(fps * 1.2)], [0, 1], {
    extrapolateLeft: "clamp",
    extrapolateRight: "clamp",
    easing: Easing.out(Easing.cubic),
  });
  const number = `${parts.before}${Math.round(progress * parts.value)}`;
  // The unit stays only while the whole thing is still a slogan ("8 WEEKS", "+1 REP").
  const unit = wallText(parts.after, 2);

  return (
    <Stage
      animation={animation}
      wall={<WallText text={unit ? `${number} ${unit}` : number} color={palette.accent} frame={frame} size={190} maxLines={2} />}
    >
      {(box) => <AssetView svg={assetSvgById[assetId]} box={box} />}
    </Stage>
  );
};
