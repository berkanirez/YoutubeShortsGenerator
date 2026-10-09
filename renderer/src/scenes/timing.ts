export function secondsToFrames(seconds: number, fps: number): number {
  return Math.round(seconds * fps);
}

type Timed = { order: number; durationSeconds: number };

export type SceneFrames<T extends Timed> = {
  scene: T;
  fromFrame: number;
  durationInFrames: number;
};

// The one place a plan's scenes become frames: played by `order` (not array position),
// back to back. calculateMetadata (total length) and SceneRenderer (the <Series>) both use
// it, so the video's length and its scene boundaries can't disagree.
export function orderedSceneFrames<T extends Timed>(scenes: readonly T[], fps: number): SceneFrames<T>[] {
  const ordered = [...scenes].sort((a, b) => a.order - b.order);
  let fromFrame = 0;
  return ordered.map((scene) => {
    const entry = { scene, fromFrame, durationInFrames: secondsToFrames(scene.durationSeconds, fps) };
    fromFrame += entry.durationInFrames;
    return entry;
  });
}

// After the last word the outro holds while the music fades out and the picture eases into
// black, instead of the video stopping dead on the final syllable.
export const CLOSING_SECONDS = 2;

export type TimelineEntry<T extends Timed> = SceneFrames<T> & {
  // Frames added after the scene's own narration (only the last scene has any).
  closingFrames: number;
};

// The video's timeline: the scenes back to back, the last one extended by the closing.
export function videoTimeline<T extends Timed>(scenes: readonly T[], fps: number): TimelineEntry<T>[] {
  const ordered = orderedSceneFrames(scenes, fps);
  const closing = secondsToFrames(CLOSING_SECONDS, fps);
  return ordered.map((entry, index) =>
    index === ordered.length - 1
      ? { ...entry, durationInFrames: entry.durationInFrames + closing, closingFrames: closing }
      : { ...entry, closingFrames: 0 },
  );
}

export function totalFrames(timeline: readonly SceneFrames<Timed>[]): number {
  const last = timeline[timeline.length - 1];
  return last ? last.fromFrame + last.durationInFrames : 0;
}
