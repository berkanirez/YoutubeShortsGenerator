// Local dashboard for the Shorts Generator API (same origin, so no CORS and no config).
// Plain JS on purpose: no build step, nothing to install - open http://localhost:5007.
//
// Safety: topics, scripts, plans and errors are text the user or the local LLM wrote, so
// they are only ever put on the page with textContent, never innerHTML.
"use strict";

const STAGES = ["Created", "Planning", "Narrating", "Rendering", "Completed"];
const POLL_MS = 2500;

const $ = (selector, root = document) => root.querySelector(selector);
let selectedId = null;
let lastDetailKey = "";

async function api(path, options = {}) {
  const response = await fetch(path, {
    headers: { "Content-Type": "application/json" },
    ...options,
  });
  const text = await response.text();
  let body = null;
  try {
    body = text ? JSON.parse(text) : null;
  } catch {
    body = text;
  }
  if (!response.ok) {
    const message = body && body.error ? body.error : `HTTP ${response.status}`;
    throw new Error(message);
  }
  return body;
}

// ---- health -------------------------------------------------------------------------

async function refreshHealth() {
  const checks = [
    ["LLM (Ollama)", "/api/llm/health", (b) => b.isReachable && b.isModelAvailable],
    ["Voice (Kokoro)", "/api/tts/health", (b) => b.isReady],
    ["Renderer (Remotion)", "/api/renderer/health", (b) => b.isReady],
  ];
  const box = $("#health");
  box.replaceChildren(
    ...(await Promise.all(
      checks.map(async ([label, path, isOk]) => {
        const chip = document.createElement("span");
        let ok = false;
        try {
          ok = isOk(await api(path));
        } catch {
          ok = false;
        }
        chip.className = ok ? "ok" : "down";
        chip.textContent = `${ok ? "●" : "○"} ${label}`;
        chip.title = ok ? "ready" : `not ready - check ${path}`;
        return chip;
      }),
    )),
  );
}

// ---- create --------------------------------------------------------------------------

$("#create-form").addEventListener("submit", async (event) => {
  event.preventDefault();
  const button = $("#generate");
  const message = $("#form-message");
  const topic = $("#topic").value.trim();
  const sourceScript = $("#script").value.trim() || null;
  button.disabled = true;
  message.className = "message";
  message.textContent = "Queuing...";
  try {
    const job = await api("/api/videos", { method: "POST", body: JSON.stringify({ topic, sourceScript }) });
    message.textContent = "Queued. It takes about a minute; you can watch the progress here.";
    $("#topic").value = "";
    $("#script").value = "";
    selectedId = job.id;
    await refreshJobs();
  } catch (error) {
    message.className = "message error";
    message.textContent = error.message;
  } finally {
    button.disabled = false;
  }
});

// ---- list ----------------------------------------------------------------------------

// What the badge says. An in-between status is only "in progress" while the worker
// actually has the job (queued or running); otherwise it's paused - created by hand, or
// interrupted by a restart - and the Continue button resumes it.
function statusLabel(job) {
  const terminal = job.status === "Completed" || job.status === "Failed";
  if (job.status === "Completed" && job.isReviewed && !job.isRunning) {
    return "Reviewed";
  }
  if (terminal || job.isRunning) {
    return job.status;
  }
  return job.isQueued ? "Queued" : "Paused";
}

async function refreshJobs() {
  let jobs;
  try {
    jobs = await api("/api/videos?take=30");
  } catch {
    return;
  }
  const list = $("#jobs");
  list.replaceChildren(
    ...jobs.map((job) => {
      const item = document.createElement("li");
      item.className = job.id === selectedId ? "selected" : "";
      const badge = document.createElement("span");
      badge.className = `badge ${statusLabel(job)}`;
      badge.textContent = statusLabel(job);
      const topic = document.createElement("span");
      topic.className = "topic";
      topic.textContent = job.topic;
      topic.title = job.topic;
      const time = document.createElement("span");
      time.className = "time";
      time.textContent = new Date(job.createdAt).toLocaleString();
      item.append(badge, topic, time);
      item.addEventListener("click", () => {
        selectedId = job.id;
        lastDetailKey = "";
        refreshJobs();
      });
      return item;
    }),
  );
  if (!selectedId && jobs.length > 0) {
    selectedId = jobs[0].id;
  }
  const selected = jobs.find((j) => j.id === selectedId);
  if (selected) {
    await renderDetail(selected);
  }
}

// ---- review sign-off (Day 28) ----------------------------------------------------------

// The checklist from review.md as tick boxes. Every change is saved; once all are ticked the
// video counts as reviewed - for this render only (a re-render makes the review stale).
// Nothing is uploaded: publishing stays the user's own step.
async function renderReviewChecks(job, detail) {
  const state = await api(`/api/videos/${job.id}/review-state`);
  const list = $('[data-field="review-checks"]', detail);
  const verdict = $('[data-field="review-verdict"]', detail);
  const show = (s) => {
    verdict.textContent = s.stale
      ? "The video was re-rendered after your review - check it again."
      : s.items.every((i) => i.checked)
        ? "Reviewed - ready for you to publish (the app never uploads)."
        : `${s.items.filter((i) => i.checked).length} of ${s.items.length} checked.`;
    const reviewed = s.items.every((i) => i.checked) && !s.stale;
    verdict.className = reviewed ? "verdict ok" : "verdict";
    // Updated in place - redrawing the detail would restart the video being reviewed.
    const badge = $('[data-field="status"]', detail);
    badge.textContent = reviewed ? "Reviewed" : "Completed";
    badge.className = `badge ${badge.textContent}`;
  };
  list.replaceChildren(
    ...state.items.map((item, index) => {
      const label = document.createElement("label");
      label.className = "inline check";
      const box = document.createElement("input");
      box.type = "checkbox";
      box.checked = item.checked && !state.stale;
      box.dataset.index = String(index);
      const text = document.createElement("span");
      text.textContent = item.text;
      label.append(box, text);
      box.addEventListener("change", async () => {
        const checked = [...list.querySelectorAll("input")].map((b) => b.checked);
        try {
          show(await api(`/api/videos/${job.id}/review-state`, {
            method: "PUT",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ checked }),
          }));
          // No forced redraw: it would restart the video the reviewer is watching. The list
          // badge picks up isReviewed on its next poll.
        } catch (error) {
          box.checked = !box.checked;
          alert(error.message);
        }
      });
      return label;
    }),
  );
  show(state);
}

// ---- detail --------------------------------------------------------------------------

async function renderDetail(job) {
  // Rebuild only when something visible changed, so the video doesn't restart every poll.
  const key = `${job.id}|${job.status}|${job.isQueued}|${job.isRunning}|${job.videoOutdated}|${job.updatedAt}|${planVersion}`;
  if (key === lastDetailKey) {
    return;
  }
  lastDetailKey = key;

  const view = $("#detail-template").content.cloneNode(true);
  const field = (name) => $(`[data-field="${name}"]`, view);

  field("topic").textContent = job.topic;
  field("status").textContent = statusLabel(job);
  field("status").className = `badge ${statusLabel(job)}`;

  const current = STAGES.indexOf(job.status);
  field("stages").replaceChildren(
    ...STAGES.map((stage, index) => {
      const li = document.createElement("li");
      li.textContent = stage;
      if (job.status !== "Failed") {
        li.className = index < current || job.status === "Completed" ? "done" : index === current ? "current" : "";
      }
      return li;
    }),
  );

  if (job.errorMessage) {
    field("error").hidden = false;
    field("error").textContent = job.errorMessage;
    field("log-hint").hidden = false;
  }
  field("outdated").hidden = !job.videoOutdated;

  field("path").textContent = job.finalVideoPath ?? "(not rendered yet)";

  if (job.status === "Completed") {
    field("player").hidden = false;
    // ?v= changes after a re-render, so the browser doesn't show a cached old video.
    $("video", field("player")).src = `/api/videos/${job.id}/video?v=${encodeURIComponent(job.updatedAt)}`;
  }

  const actions = {
    "open-folder": () => api(`/api/videos/${job.id}/open-folder`, { method: "POST" }),
    continue: () => api(`/api/videos/${job.id}/generate`, { method: "POST" }),
    // apply-edits decides: plain re-render, or regenerate the voice-over first (Day 23).
    render: () => api(`/api/videos/${job.id}/apply-edits`, { method: "POST" }),
    // Back to the last stage whose output is still valid, then queued (Day 26).
    retry: () => api(`/api/videos/${job.id}/retry`, { method: "POST" }),
  };
  const terminal = job.status === "Completed" || job.status === "Failed";
  $('[data-action="retry"]', view).hidden = job.status !== "Failed" || job.isQueued;
  $('[data-action="continue"]', view).hidden = terminal || job.isQueued || job.isRunning;
  $('[data-action="render"]', view).hidden =
    !(job.status === "Completed" || (job.status === "Narrating" && !job.isRunning)) || job.isQueued;
  for (const [name, run] of Object.entries(actions)) {
    $(`[data-action="${name}"]`, view).addEventListener("click", async (event) => {
      event.target.disabled = true;
      try {
        await run();
        lastDetailKey = "";
        await refreshJobs();
      } catch (error) {
        alert(error.message);
      } finally {
        event.target.disabled = false;
      }
    });
  }

  const detail = $("#detail");
  // Keep the plan/review sections open across a refresh (e.g. right after editing a scene).
  const wasOpen = {
    plan: $('[data-field="plan-box"]', detail)?.open ?? false,
    review: $('[data-field="review-box"]', detail)?.open ?? false,
  };
  detail.replaceChildren(view);
  $('[data-field="plan-box"]', detail).open = wasOpen.plan;
  $('[data-field="review-box"]', detail).open = wasOpen.review;

  // Optional extras, filled in after the main view is on screen.
  try {
    const review = await api(`/api/videos/${job.id}/review`);
    const box = $('[data-field="review-box"]', detail);
    box.hidden = false;
    $('[data-field="review"]', detail).textContent = review;
    if (job.status === "Completed") {
      await renderReviewChecks(job, detail);
    }
  } catch {
    /* no review yet */
  }
  try {
    const plan = await api(`/api/videos/${job.id}/plan`);
    const editable = !job.isQueued && !job.isRunning && job.status !== "Failed";
    $('[data-field="narration-outdated"]', detail).hidden = !plan.narrationOutdated;
    for (const [fieldName, key] of [["caption-style", "captionStyle"], ["music-mood", "musicMood"]]) {
      const select = $(`[data-field="${fieldName}"]`, detail);
      select.value = plan[key] ?? select.options[0].value;
      select.disabled = !editable;
      select.onchange = async () => {
        try {
          await api(`/api/videos/${job.id}/settings`, { method: "PUT", body: JSON.stringify({ [key]: select.value }) });
          planVersion++;
          lastDetailKey = "";
          await refreshJobs();
        } catch (error) {
          alert(error.message);
        }
      };
    }
    const rows = [...plan.scenes]
      .sort((a, b) => a.order - b.order)
      .map((scene) => {
        const tr = document.createElement("tr");
        const onScreen = [scene.onScreenText, scene.secondaryText].filter(Boolean).join(" / ");
        for (const value of [scene.order, scene.type, onScreen, scene.assetIds.join(", ") || "-", scene.durationSeconds.toFixed(1)]) {
          const td = document.createElement("td");
          td.textContent = value;
          tr.append(td);
        }
        // Editing is allowed when nothing is processing the job (the API enforces it too).
        const actionCell = document.createElement("td");
        if (editable) {
          const change = document.createElement("button");
          change.type = "button";
          change.textContent = "Change";
          change.title = "Change the visual";
          change.addEventListener("click", () => openAssetPicker({ job, scene }));
          const edit = document.createElement("button");
          edit.type = "button";
          edit.textContent = "Edit";
          edit.title = "Edit text, animation, sound, narration";
          edit.addEventListener("click", () => openSceneEditor({ job, scene }));
          actionCell.append(change, edit);
        }
        tr.append(actionCell);
        return tr;
      });
    $('[data-field="scenes"]', detail).replaceChildren(...rows);
    $('[data-field="plan-box"]', detail).hidden = false;
  } catch {
    /* no plan yet */
  }
}

// ---- asset picker (Day 22) -------------------------------------------------------------

let assetCatalog = null;
let planVersion = 0; // bumps after an edit so the detail view reloads the plan

async function openAssetPicker(target) {
  const dialog = $("#asset-picker");
  const field = (name) => $(`[data-field="${name}"]`, dialog);
  assetCatalog ??= await api("/api/assets");

  field("picker-title").textContent = target
    ? `Scene ${target.scene.order} (${target.scene.type}): pick a visual`
    : `Asset catalog (${assetCatalog.length})`;
  field("picker-message").textContent = target ? "" : "Browse only - to change a scene's visual, use Change in a video's scene plan.";
  field("picker-message").className = "message";
  field("picker-compatible").parentElement.hidden = !target;
  field("picker-search").value = "";

  const draw = () => {
    const query = field("picker-search").value.trim().toLowerCase();
    const onlyCompatible = target && field("picker-compatible").checked;
    const visible = assetCatalog.filter((asset) => {
      if (onlyCompatible && !asset.compatibleSceneTypes.includes(target.scene.type)) {
        return false;
      }
      const haystack = [asset.id, asset.category, ...asset.tags].join(" ").toLowerCase();
      return !query || haystack.includes(query);
    });
    field("picker-grid").replaceChildren(
      ...visible.map((asset) => {
        const card = document.createElement(target ? "button" : "div");
        card.className = "asset" + (target && target.scene.assetIds.includes(asset.id) ? " current" : "");
        const img = document.createElement("img");
        // <img>, not inline markup: the browser never runs scripts inside an SVG loaded this way.
        img.src = `/api/assets/${encodeURIComponent(asset.id)}/svg`;
        img.alt = asset.id;
        img.loading = "lazy";
        const name = document.createElement("div");
        name.className = "name";
        name.textContent = asset.id;
        const meta = document.createElement("div");
        meta.className = "meta";
        meta.textContent = `${asset.category} · ${asset.tags.join(", ")}`;
        card.append(img, name, meta);
        if (target) {
          card.type = "button";
          card.addEventListener("click", () => chooseAsset(target, asset.id));
        }
        return card;
      }),
    );
  };

  field("picker-search").oninput = draw;
  field("picker-compatible").onchange = draw;
  draw();
  dialog.showModal();
}

async function chooseAsset(target, assetId) {
  const dialog = $("#asset-picker");
  const message = $('[data-field="picker-message"]', dialog);
  try {
    await api(`/api/videos/${target.job.id}/scenes/${encodeURIComponent(target.scene.id)}/asset`, {
      method: "PUT",
      body: JSON.stringify({ assetId }),
    });
    dialog.close();
    planVersion++;
    lastDetailKey = "";
    await refreshJobs();
  } catch (error) {
    message.className = "message error";
    message.textContent = error.message;
  }
}

$('[data-action="close-picker"]').addEventListener("click", () => $("#asset-picker").close());

// ---- scene editor (Day 23) -----------------------------------------------------------

// The controlled vocabularies (mirror of the backend enums; the API rejects anything else).
const ANIMATIONS = ["fade", "slide", "pop", "punchZoom", "shake", "bounce", "pulse", "counter", "highlight"];
const SFX = ["", "pop", "whoosh", "error", "success", "impact", "click"];
const MIDDLE_TYPES = ["ExerciseDemo", "Comparison", "MythVsFact", "AnatomyHighlight", "Progress", "List", "Recovery"];

function fillSelect(select, values, current, labelFor = (v) => v || "(none)") {
  select.replaceChildren(
    ...values.map((value) => {
      const option = document.createElement("option");
      option.value = value;
      option.textContent = labelFor(value);
      return option;
    }),
  );
  select.value = current;
}

function openSceneEditor(target) {
  const dialog = $("#scene-editor");
  const field = (name) => $(`[data-field="${name}"]`, dialog);
  const { scene } = target;
  const fixedType = scene.type === "Hook" || scene.type === "Outro";

  field("editor-title").textContent = `Edit scene ${scene.order} (${scene.type})`;
  fillSelect(field("e-type"), fixedType ? [scene.type] : MIDDLE_TYPES, scene.type);
  field("e-type").disabled = fixedType;
  fillSelect(field("e-animation"), ANIMATIONS, scene.animation);
  fillSelect(field("e-sfx"), SFX, scene.sfx ?? "");
  field("e-emphasis").value = scene.emphasis ?? "";
  field("e-onScreenText").value = scene.onScreenText ?? "";
  field("e-secondaryText").value = scene.secondaryText ?? "";
  field("e-items").value = (scene.items ?? []).join("\n");
  field("e-narration").value = scene.narration ?? "";
  field("editor-message").textContent = "";

  field("editor-form").onsubmit = async (event) => {
    event.preventDefault();
    // Send every field: unchanged values are no-ops on the server, "" clears.
    const body = {
      type: field("e-type").disabled ? null : field("e-type").value,
      animation: field("e-animation").value,
      sfx: field("e-sfx").value,
      emphasis: field("e-emphasis").value,
      onScreenText: field("e-onScreenText").value,
      secondaryText: field("e-secondaryText").value,
      items: field("e-items").value.split("\n"),
      narration: field("e-narration").value,
    };
    try {
      await api(`/api/videos/${target.job.id}/scenes/${encodeURIComponent(scene.id)}`, {
        method: "PUT",
        body: JSON.stringify(body),
      });
      dialog.close();
      planVersion++;
      lastDetailKey = "";
      await refreshJobs();
    } catch (error) {
      field("editor-message").className = "message error";
      field("editor-message").textContent = error.message;
    }
  };
  dialog.showModal();
}

$('[data-action="close-editor"]').addEventListener("click", () => $("#scene-editor").close());
$("#browse-assets").addEventListener("click", () => openAssetPicker(null));

refreshHealth();
refreshJobs();
setInterval(refreshJobs, POLL_MS);
setInterval(refreshHealth, 30000);
