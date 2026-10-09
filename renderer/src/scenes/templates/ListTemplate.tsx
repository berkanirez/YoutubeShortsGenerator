import React from "react";
import { GenericTemplate } from "./GenericTemplate";
import { AssetView, Stage, WallText, type TemplateProps } from "./shared";
import { itemStartFrame, spokenFrames } from "./staging";
import { currentItem, wallText } from "./wall";

// Tips take turns on the wall, one at a time while each is being said, instead of a
// stacked paragraph of bullets. List numbers them ("#2 SLEEP MORE"); Recovery doesn't.
export const ListTemplate: React.FC<TemplateProps> = (props) => {
  const { scene, frame, fps, durationInFrames, palette, assetId, assetSvgById, animation } = props;
  const items = (scene.items ?? []).map((item) => wallText(item));
  if (items.length < 2) {
    return <GenericTemplate {...props} />;
  }

  const spoken = spokenFrames(scene.spokenDurationSeconds, fps, durationInFrames);
  const starts = items.map((_, index) => itemStartFrame(index, items.length, spoken));
  const index = currentItem(frame, starts);
  const item = items[index];
  const numbered = scene.type === "List";
  const text = item ? (numbered ? `#${index + 1} ${item}` : item) : null;

  return (
    <Stage
      animation={animation}
      wall={
        text ? (
          <WallText key={index} text={text} color={palette.accent} frame={frame - starts[index]} tilt={index % 2 === 0 ? -4 : 3} />
        ) : null
      }
    >
      {(box) => <AssetView svg={assetSvgById[assetId]} box={box} />}
    </Stage>
  );
};
