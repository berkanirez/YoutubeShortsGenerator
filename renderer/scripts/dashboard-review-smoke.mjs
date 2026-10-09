// Day 28 UI check in a real headless browser: tick the review checklist of a finished video
// while it plays; it must end "Reviewed", the list badge must follow, and the video must not
// restart while the boxes are ticked.
//   node scripts/dashboard-review-smoke.mjs <job-id> ../output/dashboard-smoke
import fs from "node:fs";
import path from "node:path";
import { openBrowser } from "@remotion/renderer";

const [jobId, outDir = "../output/dashboard-smoke"] = process.argv.slice(2);
fs.mkdirSync(outDir, { recursive: true });
const browser = await openBrowser("chrome");
try {
  const page = await browser.newPage({ context: () => null, logLevel: "error", indent: false, pageIndex: 0, onBrowserLog: null, onLog: () => undefined });
  await page.setViewport({ width: 1280, height: 1400, deviceScaleFactor: 1 });
  await page.goto({ url: "http://localhost:5007", timeout: 30000 });
  const waitFor = async (what, fn, ms, ...args) => {
    const t0 = Date.now();
    while (Date.now() - t0 < ms) {
      const v = await page.evaluate(fn, ...args);
      if (v) return v;
      await new Promise((r) => setTimeout(r, 300));
    }
    throw new Error(`Timed out waiting for: ${what}`);
  };

  // Select the job (its topic is shown; match on the id via the API's ordering instead).
  const topic = await page.evaluate(async (id) => (await (await fetch(`/api/videos/${id}`)).json()).topic, jobId);
  await waitFor("the job in the list", (t) => {
    const li = [...document.querySelectorAll("#jobs li")].find((x) => x.querySelector(".topic").textContent === t);
    if (li) li.click();
    return !!li;
  }, 15000, topic);
  await waitFor("the review checklist", () => {
    const box = document.querySelector('[data-field="review-box"]');
    if (box && !box.hidden) { box.open = true; return document.querySelectorAll('[data-field="review-checks"] input').length > 0; }
    return false;
  }, 20000);

  // Play from 5s (muted: headless autoplay rules), then tick every box.
  await waitFor("video metadata", () => (document.querySelector("#detail video")?.readyState ?? 0) >= 1, 30000);
  await page.evaluate(() => { const v = document.querySelector("#detail video"); v.muted = true; v.currentTime = 5; return v.play(); });
  const boxes = await page.evaluate(() => document.querySelectorAll('[data-field="review-checks"] input').length);
  for (let i = 0; i < boxes; i++) {
    await page.evaluate((index) => document.querySelectorAll('[data-field="review-checks"] input')[index].click(), i);
    await new Promise((r) => setTimeout(r, 400));
  }
  const verdict = await waitFor("the reviewed verdict", () => {
    const v = document.querySelector('[data-field="review-verdict"]').textContent;
    return v.startsWith("Reviewed") ? v : null;
  }, 10000);
  console.log(`ticked ${boxes} boxes -> "${verdict}"`);

  // Wait through a few polls: the list badge follows, the detail is not rebuilt.
  await new Promise((r) => setTimeout(r, 6000));
  const after = await page.evaluate((t) => {
    const li = [...document.querySelectorAll("#jobs li")].find((x) => x.querySelector(".topic").textContent === t);
    const v = document.querySelector("#detail video");
    return { listBadge: li.querySelector(".badge").textContent, detailBadge: document.querySelector("#detail .detail-head .badge").textContent, time: v.currentTime, paused: v.paused };
  }, topic);
  console.log(`list badge "${after.listBadge}", detail badge "${after.detailBadge}", video at ${after.time.toFixed(1)}s (paused=${after.paused})`);
  const r = await page._client().send("Page.captureScreenshot", { format: "png" });
  fs.writeFileSync(path.join(outDir, "9-reviewed.png"), Buffer.from(r.data ?? r.value?.data, "base64"));
  console.log("screenshot 9-reviewed.png");
  if (after.listBadge !== "Reviewed" || after.time < 5) process.exitCode = 1;
} finally {
  await browser.close({ silent: true });
}
