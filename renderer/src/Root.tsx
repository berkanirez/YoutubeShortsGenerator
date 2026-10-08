import React from "react";
import { Composition } from "remotion";
import {
  calculateMetadata,
  PLACEHOLDER_PLAN,
  ShortComposition,
} from "./compositions/ShortComposition";

export const RemotionRoot: React.FC = () => {
  return (
    <Composition
      id="ShortComposition"
      component={ShortComposition}
      // Overridden by calculateMetadata once the real plan.json is loaded -
      // this is just a safe fallback so the Composition is valid before that.
      durationInFrames={150}
      fps={30}
      width={1080}
      height={1920}
      defaultProps={{
        planPath: "samples/sample-plan.json",
        narrationPath: null,
        plan: PLACEHOLDER_PLAN,
        assetSvgById: {},
      }}
      calculateMetadata={calculateMetadata}
    />
  );
};
