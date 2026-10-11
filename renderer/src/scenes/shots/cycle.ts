// Four-frame animations (assets <stem>-f1..f4, imported from a 2x2 sprite sheet and aligned on
// one floor): a rep played as a flipbook instead of two poses swapping.

/** pushup-top / pushup-bottom / pushup -> "pushup". */
export function cycleStem(assetId: string): string {
  return assetId.replace(/-(top|bottom)$/, "");
}

/** The frame ids of the asset's animation, when all four are loaded. */
export function cycleFrames(art: Record<string, string>, assetId: string): string[] | null {
  const stem = cycleStem(assetId);
  const ids = [1, 2, 3, 4].map((n) => `${stem}-f${n}`);
  return ids.every((id) => art[id] !== undefined) ? ids : null;
}

/** The ids to load for every asset a plan shows, so the renderer can animate them if they exist. */
export function cycleIdsFor(assetIds: readonly string[]): string[] {
  return [...new Set(assetIds.map(cycleStem))].flatMap((stem) => [1, 2, 3, 4].map((n) => `${stem}-f${n}`));
}

// One rep: 1 2 3 4 3 2 | 1 2 3 4 ... - a hard cut between frames, like hand-drawn animation.
const PING_PONG = [0, 1, 2, 3, 2, 1];

/** Which of the four frames shows at `frame` for a rep of `repSeconds`. */
export function repFrame(frame: number, fps: number, repSeconds = 1.6): number {
  const step = Math.max(2, Math.round((repSeconds * fps) / PING_PONG.length));
  return PING_PONG[Math.floor(Math.max(0, frame) / step) % PING_PONG.length];
}

/** A gesture played once (f1 -> f4) from `start`, then held on its last frame. */
export function onceFrame(frame: number, start = 4, step = 3): number {
  return Math.min(3, Math.max(0, Math.floor((frame - start) / step)));
}
