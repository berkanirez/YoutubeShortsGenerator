import React from "react";
import { AbsoluteFill, Freeze, Html5Audio, interpolate, staticFile, useCurrentFrame, useVideoConfig, type CalculateMetadataFunction } from "remotion";
import { AUDIO_MIX, musicVolumeAt, resolveMusicPath } from "../audio/audioLibrary";
import { resolveAssetSvgs, resolveOptionalAssetSvgs } from "../assets/assetManifest";
import { poseCounterpart } from "../scenes/templates/staging";
import { reactionPose } from "../scenes/templates/poses";
import { cycleIdsFor } from "../scenes/shots/cycle";
import { planSeed } from "../scenes/templates/variant";
import { resolveCaptionStyle } from "../captions/captionStyles";
import { parseVideoPlan, PLAN_FPS, type VideoPlan } from "../schemas/videoPlan";
import { SceneRenderer } from "../scenes/SceneRenderer";
import { displayAssetId } from "../scenes/theme";
import { CLOSING_SECONDS, orderedSceneFrames, totalFrames, videoTimeline } from "../scenes/timing";
import { SceneView } from "../scenes/SceneView";

// What the composition is given before calculateMetadata runs - a path to a
// plan.json file and (once narration exists) its narration.wav, both relative to
// public/ (as staticFile() expects).
export type ShortCompositionInputProps = {
  planPath: string;
  narrationPath: string | null;
};

// What the React component actually receives, after calculateMetadata has
// loaded, validated and enriched the input - this is the "backend should not
// own visual implementation" boundary made concrete: the component only ever
// sees already-validated data, never raw JSON or file paths.
export type ShortCompositionProps = ShortCompositionInputProps & {
  plan: VideoPlan;
  assetSvgById: Record<string, string>;
};

// Satisfies Composition's defaultProps typing before calculateMetadata ever
// runs (Remotion validates defaultProps against the component's full prop
// type). calculateMetadata always replaces this with the real, validated
// plan - Root.tsx's defaultProps.planPath is what actually gets loaded.
export const PLACEHOLDER_PLAN: VideoPlan = {
  title: "Placeholder",
  hook: "Placeholder",
  targetDurationSeconds: 1,
  narrationText: "Placeholder.",
  scenes: [
    {
      id: "placeholder",
      order: 1,
      type: "Hook",
      durationSeconds: 1,
      narration: "Placeholder.",
      onScreenText: null,
      assetIds: [],
      animation: "fade",
      sfx: null,
      background: null,
      emphasis: null,
    },
  ],
  style: null,
  captionStyle: null,
  musicMood: null,
};

export const calculateMetadata: CalculateMetadataFunction<
  ShortCompositionProps
> = async ({ props }) => {
  const planUrl = staticFile(props.planPath);
  const response = await fetch(planUrl);
  if (!response.ok) {
    throw new Error(
      `Plan JSON not found at '${planUrl}' (HTTP ${response.status}). Did you run 'npm run sync-assets'?`,
    );
  }

  // Runtime validation of incoming render JSON (CLAUDE.md section 22): the
  // backend's TypeScript-side contract isn't "whatever shape the .NET code
  // happens to produce today" - it's this Zod schema. If the two drift apart,
  // this throws a clear error here instead of a React component crashing
  // deep inside JSX with a cryptic message.
  const plan = parseVideoPlan(await response.json(), props.planPath);

  // Each scene shows one visual: its own asset, or a mascot fallback (theme.ts).
  const allAssetIds = [...new Set(plan.scenes.map(displayAssetId))];
  // Plus the other pose of any exercise pair (pullup-top -> pullup-bottom), so the
  // ExerciseDemo template can animate a rep. Optional: a missing partner just
  // means a still image, never a failed render.
  const partnerIds = allAssetIds.map(poseCounterpart).filter((id): id is string => id !== null);
  // And the pose the mascot reacts with mid-scene (poses.ts), when the catalog has it.
  const reactionIds = plan.scenes
    .map((scene) => reactionPose(scene.type, displayAssetId(scene)))
    .filter((id): id is string => id !== null);
  // Storyboard shots show more than one picture (the wrong form, the other pose, a lineup).
  const shotIds = plan.scenes.flatMap((scene) => (scene.shot ? [...scene.assetIds, ...(scene.altAssetIds ?? [])] : []));
  // And their four-frame animations, plus the pointing gesture, when the catalog has them.
  const frameIds = [...cycleIdsFor([...allAssetIds, ...partnerIds, ...shotIds]), ...cycleIdsFor(["mascot-point"])];
  const assetSvgById = {
    ...(await resolveOptionalAssetSvgs([...partnerIds, ...reactionIds, ...shotIds, ...frameIds])),
    ...(await resolveAssetSvgs(allAssetIds)),
  };

  // Same function the <Series> in SceneRenderer is built from, so the video's length is
  // exactly the sum of its scenes plus the closing. PLAN_FPS matches the fps in Root.tsx.
  const durationInFrames = totalFrames(videoTimeline(plan.scenes, PLAN_FPS));

  return {
    durationInFrames,
    props: {
      ...props,
      plan,
      assetSvgById,
    },
  };
};

export const ShortComposition: React.FC<ShortCompositionProps> = ({
  plan,
  assetSvgById,
  narrationPath,
}) => {
  const frame = useCurrentFrame();
  const { fps, durationInFrames } = useVideoConfig();
  // The closing (timing.ts): after the last word the picture slowly pushes in, then over the
  // final half second dissolves into the video's own first frame - so when a Short replays,
  // the loop is seamless (replays count as strong interest). It used to fade to black.
  const closingStart = durationInFrames - Math.round(CLOSING_SECONDS * fps);
  const push = interpolate(frame, [closingStart, durationInFrames], [1, 1.06], { extrapolateLeft: "clamp", extrapolateRight: "clamp" });
  const loop = interpolate(frame, [durationInFrames - Math.round(fps * 0.6), durationInFrames - 1], [0, 1], {
    extrapolateLeft: "clamp",
    extrapolateRight: "clamp",
  });
  const variantSeed = plan.variant ?? planSeed(plan.title, plan.narrationText);
  const firstScene = orderedSceneFrames(plan.scenes, fps)[0];
  return (
    <>
      <AbsoluteFill style={{ transform: `scale(${push})` }}>
        <SceneRenderer
          scenes={plan.scenes}
          assetSvgById={assetSvgById}
          captionStyle={resolveCaptionStyle(plan.captionStyle)}
          variantSeed={variantSeed}
          camera={plan.style !== "static"}
        />
      </AbsoluteFill>
      {loop > 0 && firstScene ? (
        <AbsoluteFill style={{ opacity: loop }}>
          <Freeze frame={0}>
            <SceneView
              scene={firstScene.scene}
              index={0}
              previousPalette={null}
              assetSvgById={assetSvgById}
              durationInFrames={firstScene.durationInFrames}
              variantSeed={variantSeed}
              camera={plan.style !== "static"}
            />
          </Freeze>
        </AbsoluteFill>
      ) : null}
      {/* One continuous track from frame 0: the plan's scene durations were
          computed from this same file (Day 12), so scene N starts exactly when
          its sentence does - no per-scene audio offsets needed. */}
      {narrationPath ? <Html5Audio src={staticFile(narrationPath)} volume={AUDIO_MIX.voice} /> : null}
      {/* Background music under the whole video, looped (the loops are shorter
          than a Short), faded in/out and kept far below the voice. */}
      <Html5Audio
        src={staticFile(resolveMusicPath(plan.musicMood))}
        loop
        volume={(frame) => musicVolumeAt(frame, fps, durationInFrames)}
      />
    </>
  );
};
