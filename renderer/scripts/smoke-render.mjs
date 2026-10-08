// Day 25 smoke render: the composition really renders, in real headless Chrome, for every
// scene type - and a broken plan is refused before anything is drawn, with a message that
// names the field. Uses samples/contract-plan.json (written by the .NET contract test).
//   npm run smoke            (writes to out/smoke/)
import crypto from "node:crypto";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { bundle } from "@remotion/bundler";
import { getVideoMetadata, renderMedia, renderStill, selectComposition } from "@remotion/renderer";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const outDir = path.join(root, "out", "smoke");
fs.mkdirSync(outDir, { recursive: true });
const planPath = "samples/contract-plan.json";
const plan = JSON.parse(fs.readFileSync(path.join(root, "public", planPath), "utf8"));
const failures = [];
const check = (ok, what) => {
  console.log(`${ok ? "ok  " : "FAIL"} ${what}`);
  if (!ok) failures.push(what);
};

// The bundle copies public/ when it's built, so the broken plan (last check) is written first.
const badDir = path.join(root, "public", "jobs", "smoke-invalid");
fs.mkdirSync(badDir, { recursive: true });
const bad = structuredClone(plan);
bad.scenes[3].id = bad.scenes[1].id;
fs.writeFileSync(path.join(badDir, "plan.json"), JSON.stringify(bad));

const t0 = Date.now();
const serveUrl = await bundle({ entryPoint: path.join(root, "src", "index.ts") });
fs.rmSync(badDir, { recursive: true, force: true });
console.log(`bundled in ${((Date.now() - t0) / 1000).toFixed(1)}s`);

const inputProps = { planPath, narrationPath: null };
const composition = await selectComposition({ serveUrl, id: "ShortComposition", inputProps });
const expectedFrames = plan.scenes.reduce((sum, s) => sum + Math.round(s.durationSeconds * 30), 0);
check(composition.width === 1080 && composition.height === 1920 && composition.fps === 30, "1080x1920 @ 30 fps");
check(composition.durationInFrames === expectedFrames, `duration ${composition.durationInFrames} frames = sum of scenes (${expectedFrames})`);

// One still from the middle of every scene: each scene type's template must draw something,
// and something different from the others.
const hashes = new Map();
let from = 0;
for (const scene of [...plan.scenes].sort((a, b) => a.order - b.order)) {
  const frames = Math.round(scene.durationSeconds * 30);
  const frame = from + Math.floor(frames * 0.6);
  from += frames;
  const output = path.join(outDir, `${scene.order}-${scene.type}.png`);
  await renderStill({ serveUrl, composition, inputProps, frame, output, scale: 0.25 });
  const hash = crypto.createHash("sha1").update(fs.readFileSync(output)).digest("hex");
  check(!hashes.has(hash), `scene ${scene.order} ${scene.type} renders (frame ${frame}) and differs from the others`);
  hashes.set(hash, scene.type);
}

// The whole video, small: exercises every Sequence, caption cue, sfx and the music track.
const video = path.join(outDir, "smoke.mp4");
const r0 = Date.now();
await renderMedia({ serveUrl, composition, inputProps, codec: "h264", outputLocation: video, scale: 0.25 });
const meta = await getVideoMetadata(video);
check(Math.abs(meta.durationInSeconds - expectedFrames / 30) < 0.1, `full render ${meta.durationInSeconds.toFixed(2)}s in ${((Date.now() - r0) / 1000).toFixed(1)}s`);

// A plan that is valid field by field but has a duplicate scene id: must fail in
// calculateMetadata, before rendering, naming the field.
try {
  await selectComposition({ serveUrl, id: "ShortComposition", inputProps: { planPath: "jobs/smoke-invalid/plan.json", narrationPath: null } });
  check(false, "invalid plan is refused");
} catch (error) {
  const firstLine = String(error.message).split("\n")[0];
  console.log(`     -> ${firstLine}`);
  check(/scenes\[3\]\.id: duplicate scene id/.test(firstLine), "invalid plan is refused before rendering, naming scenes[3].id");
}

console.log(failures.length === 0 ? "SMOKE OK" : `SMOKE FAILED (${failures.length})`);
process.exit(failures.length === 0 ? 0 : 1);
