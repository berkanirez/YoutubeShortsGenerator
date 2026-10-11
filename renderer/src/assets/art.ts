// An asset's "art" as it travels through the composition props (assetSvgById): either the raw
// SVG markup (hand-drawn assets, inlined) or, for PNG art, a marker plus the file's
// staticFile() URL - and, once measured, the picture's size, where the figure actually is
// inside it (its opaque bounds) and how it is pinned (anchor). Kept as one string so every
// template and the props schema stay as they were; AssetView is the only place that reads it.
const IMAGE_PREFIX = "image:";

export const RASTER_EXTENSIONS = [".png"] as const;

// Feet on the floor (characters), the bar at the top (hanging poses), or centered (icons).
export type ArtAnchor = "bottom" | "top" | "center";

export type ImageMeta = {
  width: number;
  height: number;
  // The opaque figure inside the picture: [left, top, right, bottom] in image pixels.
  bounds: [number, number, number, number];
  anchor: ArtAnchor;
};

export type ImageInfo = ImageMeta & { src: string };

export function isRasterPath(filePath: string): boolean {
  const lower = filePath.toLowerCase();
  return RASTER_EXTENSIONS.some((ext) => lower.endsWith(ext));
}

export function parseAnchor(value: string | null | undefined): ArtAnchor {
  return value === "top" || value === "center" ? value : "bottom";
}

export function imageArt(url: string, meta?: ImageMeta): string {
  if (!meta) {
    return IMAGE_PREFIX + url;
  }
  const [x0, y0, x1, y1] = meta.bounds.map((v) => Math.round(v));
  return `${IMAGE_PREFIX}${url}#${meta.width},${meta.height},${x0},${y0},${x1},${y1},${meta.anchor}`;
}

// The image URL for raster art, or null for SVG markup.
export function imageSrc(art: string): string | null {
  if (!art.startsWith(IMAGE_PREFIX)) {
    return null;
  }
  const hash = art.lastIndexOf("#");
  return art.slice(IMAGE_PREFIX.length, hash > IMAGE_PREFIX.length ? hash : undefined);
}

// Size, figure bounds and anchor of measured raster art; null for SVG or unmeasured art.
export function imageInfo(art: string): ImageInfo | null {
  const src = imageSrc(art);
  const hash = art.lastIndexOf("#");
  if (src === null || hash <= IMAGE_PREFIX.length) {
    return null;
  }
  const parts = art.slice(hash + 1).split(",");
  const numbers = parts.slice(0, 6).map(Number);
  if (parts.length !== 7 || numbers.some((n) => !Number.isFinite(n)) || numbers[0] <= 0 || numbers[1] <= 0) {
    return null;
  }
  const [width, height, x0, y0, x1, y1] = numbers;
  return { src, width, height, bounds: [x0, y0, x1, y1], anchor: parseAnchor(parts[6]) };
}
