import type { ArtAnchor } from "../assets/art";

// Where things go on the 1080x1920 frame.
export const FRAME_WIDTH = 1080;

// The gym floor: characters stand exactly on this line, so they don't float. Low on the
// frame so the mascot can be big; the feet may sit behind the captions / the Shorts title.
export const FLOOR_Y = 1700;

// The wall lettering (at most 2 lines, ~250px) and the visual below it. Without lettering the
// visual takes the whole height above the floor.
export const WALL_TOP = 200;
export const VISUAL_TOP_WITH_WALL = 455;
export const VISUAL_TOP_ALONE = 200;

export type Box = { width: number; height: number };

export type Placement = {
  left: number;
  top: number;
  width: number;
  height: number;
  // The point motion pivots around (feet, the bar, or the middle), relative to left/top.
  originX: number;
  originY: number;
};

/**
 * Where a picture goes inside the visual's box, using the figure's own bounds (not the
 * picture's transparent margin): a standing figure fills the box height with its feet
 * exactly on the box's bottom edge (the floor), a hanging one hangs from the top edge, an
 * icon sits in the middle at a modest size.
 */
export function placeArt(
  image: { width: number; height: number; bounds: readonly [number, number, number, number] },
  anchor: ArtAnchor,
  box: Box,
): Placement {
  const [x0, y0, x1, y1] = image.bounds;
  const bw = Math.max(1, x1 - x0);
  const bh = Math.max(1, y1 - y0);
  const centerX = x0 + bw / 2;
  const fit = (wShare: number, hShare: number) => Math.min((box.width * wShare) / bw, (box.height * hShare) / bh);

  const s = anchor === "bottom" ? fit(0.96, 1) : anchor === "top" ? fit(0.96, 0.94) : fit(0.78, 0.72);
  const left = box.width / 2 - centerX * s;
  const top =
    anchor === "bottom" ? box.height - y1 * s : anchor === "top" ? -y0 * s : box.height * 0.45 - (y0 + bh / 2) * s;
  const originY = anchor === "bottom" ? y1 * s : anchor === "top" ? y0 * s : (y0 + bh / 2) * s;
  return { left, top, width: image.width * s, height: image.height * s, originX: centerX * s, originY };
}
