import { staticFile } from "remotion";
import { z } from "zod";
import { imageArt, isRasterPath } from "./art";

// A tiny, dependency-free stand-in for Node's path.posix.normalize/join:
// this file is bundled into the browser context Remotion renders in (see Day
// 8 code notes), where Node built-ins like "node:path" aren't available at
// all - not even for pure, pathname-string-only logic like this.
function joinAndNormalizePosix(baseDir: string, relativePath: string): string {
  const stack: string[] = [];
  for (const part of `${baseDir}/${relativePath}`.split("/")) {
    if (part === "" || part === ".") {
      continue;
    }
    if (part === "..") {
      if (stack.length === 0) {
        throw new Error(`Path '${relativePath}' escapes the '${baseDir}' directory.`);
      }
      stack.pop();
    } else {
      stack.push(part);
    }
  }
  return stack.join("/");
}

// Mirrors ShortsGenerator.Domain.AssetMetadata's shape closely enough for what
// the renderer needs - it only ever reads id/filePath from this file, it
// doesn't need category/tags/compatibleSceneTypes/orientation/anchor today.
const AssetManifestEntrySchema = z.object({
  id: z.string().min(1),
  filePath: z.string().min(1),
});

const AssetManifestSchema = z.array(AssetManifestEntrySchema);

// calculateMetadata and composition code run inside a headless Chrome
// instance Remotion manages, not raw Node.js - there is no fs module there.
// staticFile() + fetch() is Remotion's actual supported way to read local
// files in both Studio preview and rendering (see Day 8 code notes for why
// an earlier fs-based attempt failed at bundle time). The manifest and SVGs
// read here are synced from the repo's real assets/ into public/assets/ by
// scripts/sync-public-assets.mjs - same files the .NET backend reads, just a
// mirrored copy Remotion's local server can serve over HTTP.
const MANIFEST_STATIC_PATH = "assets/manifests/assets.json";

let cachedSvgByIdPromise: Promise<Map<string, string>> | null = null;

async function loadManifest(): Promise<Map<string, string>> {
  if (cachedSvgByIdPromise) {
    return cachedSvgByIdPromise;
  }

  cachedSvgByIdPromise = (async () => {
    const manifestUrl = staticFile(MANIFEST_STATIC_PATH);
    const manifestResponse = await fetch(manifestUrl);
    if (!manifestResponse.ok) {
      throw new Error(
        `Asset manifest not found at '${manifestUrl}' (HTTP ${manifestResponse.status}). ` +
          "Did you run 'npm run sync-assets'?",
      );
    }

    const entries = AssetManifestSchema.parse(await manifestResponse.json());

    const svgById = new Map<string, string>();
    for (const entry of entries) {
      // The manifest's filePath is relative to the manifest file itself (e.g.
      // "../mascot/mascot-idle.svg"), exactly like the .NET side resolves it -
      // see JsonAssetRepository's Day 6 code notes. Re-root it under
      // public/assets/ using the same posix-style join staticFile() expects.
      // joinAndNormalizePosix itself throws if this would escape assets/.
      const relativeFromManifestDir = joinAndNormalizePosix("manifests", entry.filePath);

      const assetUrl = staticFile(`assets/${relativeFromManifestDir}`);
      // PNG art is only checked for existence here and shown by URL (AssetView uses
      // Remotion's <Img>, which holds the frame until the image has loaded).
      const assetResponse = await fetch(assetUrl, isRasterPath(entry.filePath) ? { method: "HEAD" } : undefined);
      if (!assetResponse.ok) {
        throw new Error(
          `Asset '${entry.id}' points to a missing file: '${assetUrl}' (HTTP ${assetResponse.status}).`,
        );
      }

      svgById.set(entry.id, isRasterPath(entry.filePath) ? imageArt(assetUrl) : await assetResponse.text());
    }

    return svgById;
  })();

  return cachedSvgByIdPromise;
}

// Like resolveAssetSvgs, but silently skips ids that aren't in the catalog -
// for *optional* extras the renderer itself derives (e.g. the other pose of an
// exercise pair), never for ids that came from the plan.
export async function resolveOptionalAssetSvgs(
  assetIds: readonly string[],
): Promise<Record<string, string>> {
  const svgById = await loadManifest();
  const result: Record<string, string> = {};
  for (const id of assetIds) {
    const svg = svgById.get(id);
    if (svg !== undefined) {
      result[id] = svg;
    }
  }
  return result;
}

// Returns raw SVG markup for every requested asset id. Throws a clear error
// (never silently skips) if an id referenced by a scene isn't in the
// catalog - the same "known assets only" guarantee the backend's
// VideoPlanValidator already enforces before this JSON is ever produced.
export async function resolveAssetSvgs(
  assetIds: readonly string[],
): Promise<Record<string, string>> {
  const svgById = await loadManifest();
  const result: Record<string, string> = {};

  for (const id of assetIds) {
    const svg = svgById.get(id);
    if (svg === undefined) {
      throw new Error(`Unknown asset id '${id}' - it is not in the asset manifest.`);
    }
    result[id] = svg;
  }

  return result;
}
