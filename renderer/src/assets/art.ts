// An asset's "art" as it travels through the composition props (assetSvgById): either the raw
// SVG markup (hand-drawn assets, inlined) or, for PNG art, a marker plus the file's
// staticFile() URL. Kept as one string so every template and the props schema stay as they
// were; AssetView is the only place that tells the two apart.
const IMAGE_PREFIX = "image:";

export const RASTER_EXTENSIONS = [".png"] as const;

export function isRasterPath(filePath: string): boolean {
  const lower = filePath.toLowerCase();
  return RASTER_EXTENSIONS.some((ext) => lower.endsWith(ext));
}

export function imageArt(url: string): string {
  return IMAGE_PREFIX + url;
}

// The image URL for raster art, or null for SVG markup.
export function imageSrc(art: string): string | null {
  return art.startsWith(IMAGE_PREFIX) ? art.slice(IMAGE_PREFIX.length) : null;
}
