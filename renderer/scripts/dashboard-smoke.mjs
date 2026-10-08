// Drives the local dashboard in a real (headless) browser - the one Remotion already ships,
// so no extra install: open it, type a topic, press Generate, wait for the video, and save
// screenshots. Usage (API running on :5007):
//   node scripts/dashboard-smoke.mjs "Why Tempo Reps Build More Muscle" ../output/dashboard-smoke
import fs from "node:fs";
import path from "node:path";
import { openBrowser } from "@remotion/renderer";

const [topic = "Why Tempo Reps Build More Muscle", outDir = "../output/dashboard-smoke"] = process.argv.slice(2);
const base = "http://localhost:5007";
fs.mkdirSync(outDir, { recursive: true });

const browser = await openBrowser("chrome");
try {
  const page = await browser.newPage({
    context: () => null,
    logLevel: "error",
    indent: false,
    pageIndex: 0,
    onBrowserLog: null,
    onLog: () => undefined,
  });
  await page.setViewport({ width: 1280, height: 1000, deviceScaleFactor: 1 });
  await page.goto({ url: base, timeout: 30000 });

  const shot = async (name) => {
    // Remotion's CDP session wraps protocol results as { value: <result>, ... }.
    const response = await page._client().send("Page.captureScreenshot", { format: "png" });
    const data = response.data ?? response.value?.data;
    fs.writeFileSync(path.join(outDir, name), Buffer.from(data, "base64"));
    console.log(`screenshot ${name}`);
  };
  // predicate runs inside the page, so values it needs must be passed as arguments
  // (a closure over a Node variable would be undefined there).
  const waitFor = async (description, predicate, timeoutMs, ...args) => {
    const started = Date.now();
    while (Date.now() - started < timeoutMs) {
      const value = await page.evaluate(predicate, ...args);
      if (value) {
        return value;
      }
      await new Promise((r) => setTimeout(r, 1000));
    }
    throw new Error(`Timed out waiting for: ${description}`);
  };

  await waitFor("the form", () => !!document.querySelector("#create-form"), 15000);
  if (topic === "--screenshot-only") {
    await waitFor("the job list", () => document.querySelectorAll("#jobs li").length > 0, 15000);
    await new Promise((r) => setTimeout(r, 1500));
    await shot("0-overview.png");
    process.exit(0);
  }
  const submittedAt = Date.now();
  await page.evaluate((t) => {
    document.querySelector("#topic").value = t;
    document.querySelector("#generate").click();
  }, topic);

  const message = await waitFor(
    "the queued message",
    () => (document.querySelector("#form-message")?.textContent.startsWith("Queued") ? document.querySelector("#form-message").textContent : null),
    15000,
  );
  console.log(`after ${Date.now() - submittedAt} ms: "${message}"`);
  await waitFor("the new job in the list", (t) => document.querySelector("#jobs li.selected .topic")?.textContent === t, 15000, topic);
  await shot("1-queued.png");

  const status = await waitFor(
    "Completed or Failed",
    () => {
      const badge = document.querySelector("#detail .detail-head .badge")?.textContent;
      return badge === "Completed" || badge === "Failed" ? badge : null;
    },
    300000,
  );
  console.log(`job finished after ${Math.round((Date.now() - submittedAt) / 1000)} s: ${status}`);
  await waitFor("the video element to load", () => {
    const video = document.querySelector("#detail video");
    return !video || video.readyState >= 1;
  }, 30000);
  const videoInfo = await page.evaluate(() => {
    const video = document.querySelector("#detail video");
    return video ? { duration: video.duration, width: video.videoWidth, height: video.videoHeight } : null;
  });
  console.log(`video element: ${JSON.stringify(videoInfo)}`);
  await shot("2-completed.png");
} finally {
  await browser.close({ silent: true });
}
