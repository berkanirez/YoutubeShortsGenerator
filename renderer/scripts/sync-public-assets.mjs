// Remotion serves local files during both Studio preview and rendering only
// from public/ (via staticFile() + fetch) - calculateMetadata and composition
// code run inside a headless Chrome instance, not raw Node.js, so fs.readFile
// on an arbitrary repo path simply doesn't work there (see Day 8 code notes).
// This script copies the repo's real assets/ and samples/ (the single source
// of truth, also read by the .NET backend) into public/ as a disposable,
// regenerable mirror. Run before `npm run dev` / rendering.
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const rendererRoot = path.resolve(__dirname, "..");
const repoRoot = path.resolve(rendererRoot, "..");

function syncDir(name) {
  const source = path.join(repoRoot, name);
  const destination = path.join(rendererRoot, "public", name);

  fs.rmSync(destination, { recursive: true, force: true });
  fs.cpSync(source, destination, { recursive: true });
  console.log(`Synced ${name}/ -> renderer/public/${name}/`);
}

syncDir("assets");
syncDir("samples");
