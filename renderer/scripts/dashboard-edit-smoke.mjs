// Day 22 UI check in a real headless browser: open a finished video, change one scene's
// visual through the asset picker, confirm the "outdated" notice, re-render, confirm it's gone.
//   node scripts/dashboard-edit-smoke.mjs "<topic of a Completed job>" ../output/dashboard-smoke
import fs from "node:fs";
import path from "node:path";
import { openBrowser } from "@remotion/renderer";

const [topic, outDir = "../output/dashboard-smoke"] = process.argv.slice(2);
fs.mkdirSync(outDir, { recursive: true });
const browser = await openBrowser("chrome");
try {
  const page = await browser.newPage({ context: () => null, logLevel: "error", indent: false, pageIndex: 0, onBrowserLog: null, onLog: () => undefined });
  await page.setViewport({ width: 1280, height: 1100, deviceScaleFactor: 1 });
  await page.goto({ url: "http://localhost:5007", timeout: 30000 });
  const shot = async (name) => {
    const r = await page._client().send("Page.captureScreenshot", { format: "png" });
    fs.writeFileSync(path.join(outDir, name), Buffer.from(r.data ?? r.value?.data, "base64"));
    console.log(`screenshot ${name}`);
  };
  const waitFor = async (what, fn, ms, ...args) => {
    const t0 = Date.now();
    while (Date.now() - t0 < ms) {
      const v = await page.evaluate(fn, ...args);
      if (v) return v;
      await new Promise((r) => setTimeout(r, 500));
    }
    throw new Error(`Timed out waiting for: ${what}`);
  };

  // Select the job and open its scene plan.
  await waitFor("the job", (t) => {
    const li = [...document.querySelectorAll("#jobs li")].find((x) => x.querySelector(".topic").textContent === t);
    if (li) li.click();
    return !!li;
  }, 15000, topic);
  await waitFor("the plan table", () => {
    const box = document.querySelector('[data-field="plan-box"]');
    if (box && !box.hidden) { box.open = true; return document.querySelectorAll('[data-field="scenes"] tr').length > 0; }
    return false;
  }, 15000);

  const before = await page.evaluate(() => document.querySelectorAll('[data-field="scenes"] tr')[1].children[3].textContent);
  await page.evaluate(() => document.querySelectorAll('[data-field="scenes"] tr')[1].querySelector("button").click());
  await waitFor("the picker", () => document.querySelector("#asset-picker").open && document.querySelectorAll("#asset-picker .asset").length > 0, 10000);
  await new Promise((r) => setTimeout(r, 800)); // let the previews load for the screenshot
  await shot("3-picker.png");

  // Pick the first compatible asset that isn't the current one.
  const picked = await page.evaluate(() => {
    const card = [...document.querySelectorAll("#asset-picker .asset")].find((c) => !c.classList.contains("current"));
    const id = card.querySelector(".name").textContent;
    card.click();
    return id;
  });
  await waitFor("the outdated notice", () => !document.querySelector('[data-field="outdated"]').hidden, 15000);
  const after = await page.evaluate(() => document.querySelectorAll('[data-field="scenes"] tr')[1].children[3].textContent);
  console.log(`scene 2 asset: ${before} -> ${after} (picked ${picked}); outdated notice shown`);
  await shot("4-outdated.png");

  await page.evaluate(() => document.querySelector('[data-action="render"]').click());
  await waitFor("re-render finished", () => {
    const badge = document.querySelector("#detail .detail-head .badge")?.textContent;
    return badge === "Completed" && document.querySelector('[data-field="outdated"]').hidden;
  }, 300000);
  console.log("re-rendered; outdated notice gone");
  await shot("5-rerendered.png");
} finally {
  await browser.close({ silent: true });
}
