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
