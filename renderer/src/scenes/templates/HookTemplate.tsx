import React from "react";
import { poseAt } from "./poses";
import { AssetView, Sign, Stage, WallText, type TemplateProps } from "./shared";
import { spokenFrames } from "./staging";
import { pickVariant } from "./variant";
import { wallText } from "./wall";

// The first second decides whether the viewer stays: the mascot fills the screen,
// the hook is painted big on the wall, a question gets a giant "?" stamped next to the
// mascot, and the mascot reacts (thinking -> pointing at the viewer). Per video (variant)
// the lettering leans the other way and the "?" switches sides.
export const HookTemplate: React.FC<TemplateProps> = (props) => {
  const { scene, frame, fps, durationInFrames, palette, assetId, assetSvgById, animation, variantSeed } = props;
  const flipped = pickVariant(variantSeed, 2) === 1;
  const pose = poseAt(scene.type, assetId, frame, spokenFrames(scene.spokenDurationSeconds, fps, durationInFrames), assetSvgById);
  const slogan = wallText(scene.onScreenText, 6);
  const asks = (scene.narration ?? "").trim().endsWith("?") || (scene.onScreenText ?? "").trim().endsWith("?");
  return (
    <Stage
      animation={animation}
      wall={slogan ? <WallText text={slogan} color={palette.accent} frame={frame} size={140} maxLines={2} tilt={flipped ? 3 : -4} /> : null}
    >
      {(box) => (
        <>
          <AssetView svg={assetSvgById[pose.id]} box={box} />
          {asks ? (
            <Sign
              kind="question"
              frame={frame - 6}
              color={palette.accent}
              size={400}
              style={flipped ? { left: 0, top: box.height * 0.2 } : { right: 0, top: box.height * 0.2 }}
            />
          ) : null}
        </>
      )}
    </Stage>
  );
};
