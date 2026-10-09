import React from "react";
import { AssetView, Sign, Stage, WallText, type TemplateProps } from "./shared";
import { pickVariant } from "./variant";
import { wallText } from "./wall";

// The first second decides whether the viewer stays: the mascot fills the screen, the hook
// is painted big on the wall, and a question gets a giant "?" stamped next to the mascot.
// Per plan (variant.ts) the lettering leans the other way and the "?" switches sides, so
// the openings of a channel don't all look the same.
export const HookTemplate: React.FC<TemplateProps> = ({ scene, frame, palette, assetId, assetSvgById, animation, variantSeed }) => {
  const flipped = pickVariant(variantSeed, 2) === 1;
  const slogan = wallText(scene.onScreenText, 6);
  const asks = (scene.narration ?? "").trim().endsWith("?") || (scene.onScreenText ?? "").trim().endsWith("?");
  return (
    <Stage
      animation={animation}
      wall={slogan ? <WallText text={slogan} color={palette.accent} frame={frame} size={150} maxLines={2} tilt={flipped ? 3 : -4} /> : null}
    >
      {(box) => (
        <>
          <AssetView svg={assetSvgById[assetId]} box={box} />
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
