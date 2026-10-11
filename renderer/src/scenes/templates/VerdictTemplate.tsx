import React from "react";
import { poseAt } from "./poses";
import { AssetView, GREEN, RED, Sign, Stage, WallText, type TemplateProps } from "./shared";
import { spokenFrames } from "./staging";
import { wallText } from "./wall";

// Two beats with one switch, told by signs instead of cards: first the wrong idea (red word,
// big red X stamped on the visual), then - when the narration turns - the right one (green
// word, green check). The full sentences are in the captions.
// MythVsFact paints MYTH / FACT; Comparison paints its own short texts when they fit
// on the wall ("Half reps" / "Full range"), else WRONG / RIGHT.
export const VerdictTemplate: React.FC<TemplateProps> = (props) => {
  const { scene, frame, fps, durationInFrames, assetId, assetSvgById, animation } = props;
  const words =
    scene.type === "MythVsFact"
      ? ["MYTH", "FACT"]
      : [wallText(scene.onScreenText, 3) ?? "WRONG", wallText(scene.secondaryText, 3) ?? "RIGHT"];
  const spoken = spokenFrames(scene.spokenDurationSeconds, fps, durationInFrames);
  const turn = Math.round(spoken * 0.4);
  const right = frame >= turn;
  const local = right ? frame - turn : frame;
  // The mascot's own verdict on the same beat: "stop" becomes a thumbs-up (poses.ts).
  const pose = poseAt(scene.type, assetId, frame, spoken, assetSvgById);

  return (
    <Stage
      animation={animation}
      // key: the second word paints on fresh instead of swapping its letters in place.
      wall={<WallText key={right ? "right" : "wrong"} text={right ? words[1] : words[0]} color={right ? GREEN : RED} frame={local} />}
    >
      {(box) => (
        <>
          <AssetView svg={assetSvgById[pose.id]} box={box} />
          <Sign
            key={right ? "check" : "cross"}
            kind={right ? "check" : "cross"}
            frame={right ? local : local - 6}
            color={right ? GREEN : RED}
            size={380}
            style={{ right: 10, top: box.height * 0.16 }}
          />
        </>
      )}
    </Stage>
  );
};
