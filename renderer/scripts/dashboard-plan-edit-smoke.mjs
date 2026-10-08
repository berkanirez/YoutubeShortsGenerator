// Day 23 UI check in a real headless browser:
//   1. edit scene 2's on-screen text + animation, switch captions to "boxed", re-render;
//   2. edit scene 2's narration text, confirm the "voice-over will be regenerated" notice,
//      re-render (renarrate + render).
// After each, the player is seeked into scene 2 and screenshotted.
//   node scripts/dashboard-plan-edit-smoke.mjs "<topic of a Completed job>" ../output/dashboard-smoke
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
  const openPlan = () =>
    waitFor("the plan table", () => {
      const box = document.querySelector('[data-field="plan-box"]');
      if (box && !box.hidden) { box.open = true; return document.querySelectorAll('[data-field="scenes"] tr').length > 1; }
      return false;
    }, 20000);
  const editScene2 = async (changes) => {
    await page.evaluate(() => document.querySelectorAll('[data-field="scenes"] tr')[1].querySelectorAll("button")[1].click());
    await waitFor("the editor", () => document.querySelector("#scene-editor").open, 5000);
    await page.evaluate((c) => {
      for (const [name, value] of Object.entries(c)) document.querySelector(`[data-field="e-${name}"]`).value = value;
      document.querySelector('[data-field="editor-form"] button[type="submit"]').click();
    }, changes);
    await waitFor("the editor to close", () => !document.querySelector("#scene-editor").open, 10000);
  };
  const rerenderAndWait = async () => {
    await page.evaluate(() => document.querySelector('[data-action="render"]').click());
    await waitFor("processing to start", () => document.querySelector("#detail .detail-head .badge")?.textContent !== "Completed", 15000);
    await waitFor("Completed again", () => document.querySelector("#detail .detail-head .badge")?.textContent === "Completed", 400000);
  };
  const seekIntoScene2 = async () => {
    const t = await page.evaluate(() => {
      const rows = [...document.querySelectorAll('[data-field="scenes"] tr')];
      const s1 = Number(rows[0].children[4].textContent);
      const s2 = Number(rows[1].children[4].textContent);
      return s1 + s2 * 0.75;
    });
    await waitFor("video metadata", () => (document.querySelector("#detail video")?.readyState ?? 0) >= 1, 30000);
    await page.evaluate((time) => { const v = document.querySelector("#detail video"); v.currentTime = time; }, t);
    await waitFor("the seek", () => { const v = document.querySelector("#detail video"); return !v.seeking && v.readyState >= 2; }, 20000);
    await new Promise((r) => setTimeout(r, 500));
  };

  await waitFor("the job", (t) => {
    const li = [...document.querySelectorAll("#jobs li")].find((x) => x.querySelector(".topic").textContent === t);
    if (li) li.click();
    return !!li;
  }, 15000, topic);
  await openPlan();

  // 1) visuals only
  await editScene2({ onScreenText: "Edited by hand", animation: "shake" });
  await openPlan();
  await page.evaluate(() => { const s = document.querySelector('[data-field="caption-style"]'); s.value = "boxed"; s.dispatchEvent(new Event("change")); });
  await waitFor("the outdated notice", () => !document.querySelector('[data-field="outdated"]').hidden, 15000);
  const row = await page.evaluate(() => document.querySelectorAll('[data-field="scenes"] tr')[1].children[2].textContent);
  console.log(`scene 2 on screen after edit: "${row}"; captions = ${await page.evaluate(() => document.querySelector('[data-field="caption-style"]').value)}`);
  await rerenderAndWait();
  await openPlan();
  await seekIntoScene2();
  await shot("6-visual-edit-rendered.png");

  // 2) narration text
  await editScene2({ narration: "Slow, controlled reps keep tension on the muscle the whole time." });
  await openPlan();
  await waitFor("the narration notice", () => !document.querySelector('[data-field="narration-outdated"]').hidden, 15000);
  console.log("narration notice shown");
  await shot("7-narration-edited.png");
  await rerenderAndWait();
  await openPlan();
  const notice = await page.evaluate(() => document.querySelector('[data-field="narration-outdated"]').hidden);
  console.log(`re-narrated + re-rendered; narration notice hidden = ${notice}`);
  await seekIntoScene2();
  await shot("8-narration-rendered.png");
} finally {
  await browser.close({ silent: true });
}
