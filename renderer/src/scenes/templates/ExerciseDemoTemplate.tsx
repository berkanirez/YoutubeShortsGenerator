import React from "react";
import { GenericTemplate } from "./GenericTemplate";
import { imageInfo } from "../../assets/art";
import { AssetView, Stage, WALL_FONT, WallText, type TemplateProps } from "./shared";
import { poseCounterpart, repPose } from "./staging";
import { wallText } from "./wall";

// Exercise assets come in pose pairs (pullup-top / pullup-bottom). Alternating
// between them with a short crossfade turns two still drawings into a rep - the
// cheapest possible "animation rig", and it counts the reps as it goes.
export const ExerciseDemoTemplate: React.FC<TemplateProps> = (props) => {
  const { scene, frame, fps, palette, assetId, assetSvgById, animation } = props;
  const partner = poseCounterpart(assetId);
  const partnerSvg = partner ? assetSvgById[partner] : undefined;
  if (!partner || !partnerSvg) {
    return <GenericTemplate {...props} />;
  }

  const topId = assetId.endsWith("-top") ? assetId : partner;
  const bottomId = topId === assetId ? partner : assetId;
  const { top, reps } = repPose(frame, fps);
  const slogan = wallText(scene.onScreenText);
  const a = imageInfo(assetSvgById[topId] ?? "")?.bounds;
  const b = imageInfo(assetSvgById[bottomId] ?? "")?.bounds;
  const union = a && b ? ([Math.min(a[0], b[0]), Math.min(a[1], b[1]), Math.max(a[2], b[2]), Math.max(a[3], b[3])] as const) : undefined;

  return (
    <Stage animation={animation} wall={slogan ? <WallText text={slogan} color={palette.accent} frame={frame} /> : null}>
      {(box) => (
        <>
          <AssetView svg={assetSvgById[bottomId]} box={box} bounds={union} style={{ position: "absolute", opacity: 1 - top }} />
          <AssetView svg={assetSvgById[topId]} box={box} bounds={union} style={{ position: "absolute", opacity: top }} />
          {reps > 0 ? (
            // The rep count as a painted scoreboard number, not a label.
            <div
              style={{
                position: "absolute",
                right: 40,
                bottom: box.height * 0.1,
                color: palette.accent,
                fontFamily: WALL_FONT,
                fontSize: 210,
                lineHeight: 0.85,
                textAlign: "center",
                textShadow: "6px 6px 0 rgba(0, 0, 0, 0.45)",
                transform: "rotate(-6deg)",
                fontVariantNumeric: "tabular-nums",
              }}
            >
              {reps}
              <div style={{ fontSize: 62, letterSpacing: 4 }}>REPS</div>
            </div>
          ) : null}
        </>
      )}
    </Stage>
  );
};
