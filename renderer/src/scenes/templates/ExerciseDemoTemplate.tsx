import React from "react";
import { GenericTemplate } from "./GenericTemplate";
import { AssetView, Badge, BODY_FONT, Column, Headline, type TemplateProps } from "./shared";
import { poseCounterpart, repPose } from "./staging";

// Exercise assets come in pose pairs (pullup-top / pullup-bottom). Alternating
// between them with a short crossfade turns two still drawings into a rep - the
// cheapest possible "animation rig", and it counts the reps as it goes.
export const ExerciseDemoTemplate: React.FC<TemplateProps> = (props) => {
  const { scene, frame, fps, palette, badge, assetId, assetSvgById, animation } = props;
  const partner = poseCounterpart(assetId);
  const partnerSvg = partner ? assetSvgById[partner] : undefined;
  if (!partner || !partnerSvg) {
    return <GenericTemplate {...props} />;
  }

  const topId = assetId.endsWith("-top") ? assetId : partner;
  const bottomId = topId === assetId ? partner : assetId;
  const { top, reps } = repPose(frame, fps);

  return (
    <Column animation={animation}>
      {badge ? <Badge text={badge} palette={palette} /> : null}
      {scene.onScreenText ? <Headline text={scene.onScreenText} /> : null}
      <div style={{ position: "relative", width: 600, height: 600, marginTop: 50 }}>
        <AssetView svg={assetSvgById[bottomId]} size={600} style={{ position: "absolute", opacity: 1 - top }} />
        <AssetView svg={assetSvgById[topId]} size={600} style={{ position: "absolute", opacity: top }} />
        <div
          style={{
            position: "absolute",
            right: -20,
            top: 10,
            padding: "8px 20px",
            borderRadius: 999,
            backgroundColor: palette.accent,
            color: "#0F172A",
            fontFamily: BODY_FONT,
            fontWeight: 900,
            fontSize: 34,
            fontVariantNumeric: "tabular-nums",
          }}
        >
          REP {reps}
        </div>
      </div>
    </Column>
  );
};
