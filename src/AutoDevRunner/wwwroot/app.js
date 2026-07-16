// ---- tiny helpers ----
const $ = (s, el = document) => el.querySelector(s);
const app = $("#app");
const esc = (s) => (s ?? "").toString().replace(/[&<>"]/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" }[c]));
const fmt = (d) => d ? new Date(d).toLocaleString() : "—";
const badge = (s) => `<span class="badge ${esc(s)}">${esc(s ?? "—")}</span>`;

async function api(path, opts) {
  const res = await fetch("/api" + path, {
    headers: { "Content-Type": "application/json" },
    ...opts
  });
  if (!res.ok) {
    let msg = res.statusText;
    try { const j = await res.json(); msg = j.detail || j.title || JSON.stringify(j); } catch { try { msg = await res.text(); } catch {} }
    throw new Error(msg || ("HTTP " + res.status));
  }
  if (res.status === 204) return null;
  const ct = res.headers.get("content-type") || "";
  return ct.includes("json") ? res.json() : res.text();
}

function toast(msg, isErr) {
  const t = document.createElement("div");
  t.className = "toast" + (isErr ? " err" : "");
  t.textContent = msg;
  document.body.appendChild(t);
  setTimeout(() => t.remove(), 3500);
}

// ---- routing ----
let current = "overview";
const views = {};
let runEventStream = null;

function closeRunEventStream() {
  if (runEventStream) runEventStream.close();
  runEventStream = null;
}

$("#nav").addEventListener("click", (e) => {
  const b = e.target.closest("button[data-view]");
  if (!b) return;
  navigate(b.dataset.view);
});
$("#refresh").addEventListener("click", () => render());

function navigate(view, arg) {
  closeRunEventStream();
  current = view;
  [...$("#nav").children].forEach(b => b.classList.toggle("active", b.dataset.view === view));
  render(arg);
}

function startRunEventStream(id) {
  closeRunEventStream();
  const output = $("#live-events");
  if (!output) return;

  const source = new EventSource(`/api/runs/${id}/events`);
  runEventStream = source;
  const append = (kind, message, timestamp) => {
    const time = timestamp ? new Date(timestamp).toLocaleTimeString() : new Date().toLocaleTimeString();
    const lines = output.textContent.split("\n").filter(Boolean);
    lines.push(`${time} [${kind}] ${message}`);
    output.textContent = lines.slice(-500).join("\n") + "\n";
    output.scrollTop = output.scrollHeight;
  };
  const handle = (e) => {
    try {
      const event = JSON.parse(e.data);
      append(event.kind || e.type, event.message || "", event.timestamp);
      if (event.terminal) {
        closeRunEventStream();
        setTimeout(() => { if (current === "runDetail") views.runDetail(id); }, 500);
      }
    } catch (err) {
      append("error", "Invalid live event: " + err.message);
    }
  };
  ["lifecycle", "heartbeat", "agent", "command", "file", "tool", "error", "provider-complete", "terminal"]
    .forEach(kind => source.addEventListener(kind, handle));
  source.onerror = () => {
    if (source.readyState === EventSource.CLOSED)
      append("stream", "Live stream closed.");
  };
}

async function render(arg) {
  app.innerHTML = "Loading…";
  try {
    await views[current](arg);
  } catch (e) {
    app.innerHTML = `<p class="muted">Error: ${esc(e.message)}</p>`;
  }
}

// ---- Overview ----
views.overview = async () => {
  const o = await api("/overview");
  app.innerHTML = `
    <h1>Overview</h1>
    <div class="cards">
      <div class="card"><div class="label">Projects</div><div class="value">${o.totalProjects}</div></div>
      <div class="card"><div class="label">Enabled</div><div class="value">${o.enabledProjects}</div></div>
      <div class="card"><div class="label">Paused</div><div class="value">${o.pausedProjects}</div></div>
      <div class="card"><div class="label">Running now</div><div class="value">${o.runningProjects}</div></div>
    </div>
    <h2>Last run</h2>
    ${o.lastRun ? `
      <div class="detail-grid">
        <div class="k">Project</div><div><a data-run="${o.lastRun.id}">${esc(o.lastRun.projectName)}</a></div>
        <div class="k">Provider</div><div>${esc(o.lastRun.provider)}</div>
        <div class="k">Status</div><div>${badge(o.lastRun.status)}</div>
        <div class="k">When</div><div>${fmt(o.lastRun.startedAt)}</div>
        <div class="k">Usage</div><div>${esc(o.lastUsage || "—")}</div>
        ${o.lastError ? `<div class="k">Last error</div><div class="muted">${esc(o.lastError)}</div>` : ""}
      </div>` : `<p class="muted">No runs yet.</p>`}
    <h2>Providers</h2>
    ${providerTable(o.providers)}
  `;
  app.querySelectorAll("[data-run]").forEach(a => a.onclick = () => navigate("runDetail", +a.dataset.run));
};

function providerTable(providers) {
  return `<table><thead><tr><th>Provider</th><th>Enabled</th><th>Last success</th><th>Last auth error</th><th>Last quota limit</th><th>Usage</th></tr></thead><tbody>
    ${providers.map(p => `<tr>
      <td>${esc(p.provider)}</td>
      <td>${p.enabled ? badge("on") : badge("off")}</td>
      <td>${fmt(p.lastSuccessAt)}</td>
      <td>${fmt(p.lastAuthErrorAt)}</td>
      <td>${fmt(p.lastQuotaLimitAt)}${p.lastQuotaResetHint ? ` <span class="muted">(${esc(p.lastQuotaResetHint)})</span>` : ""}</td>
      <td>${esc(p.lastKnownUsage || "—")}</td>
    </tr>`).join("")}
  </tbody></table>`;
}

// ---- Projects ----
views.projects = async () => {
  const projects = await api("/projects");
  app.innerHTML = `
    <div class="toolbar"><h1>Projects</h1><button class="primary" id="new">+ New project</button></div>
    ${projects.length === 0 ? `<p class="muted">No projects yet. Click “New project”.</p>` : `
    <table><thead><tr>
      <th>Name</th><th>Status</th><th>Priority</th><th>Last run</th><th>Provider</th><th>Branch</th><th>Task</th><th></th>
    </tr></thead><tbody>
      ${projects.map(p => `<tr>
        <td><a data-id="${p.id}">${esc(p.name)}</a><div class="muted mono">${esc(p.repoPath)}</div></td>
        <td>${p.isRunning ? badge("Running") : (p.enabled ? (p.paused ? badge("Paused") : badge("on")) : badge("off"))}
            ${p.lastRunStatus ? " " + badge(p.lastRunStatus) : ""}</td>
        <td>${p.priority}</td>
        <td>${fmt(p.lastRunAt)}</td>
        <td>${esc(p.lastProvider || "—")}</td>
        <td class="mono">${esc(p.currentBranch || "—")}</td>
        <td class="muted">${esc((p.currentTask || "—").slice(0, 40))}</td>
        <td class="actions">
          <button class="sm" data-act="run" data-id="${p.id}">Run</button>
          <button class="sm" data-act="${p.paused ? "resume" : "pause"}" data-id="${p.id}">${p.paused ? "Resume" : "Pause"}</button>
          <button class="sm" data-act="${p.enabled ? "disable" : "enable"}" data-id="${p.id}">${p.enabled ? "Disable" : "Enable"}</button>
          <button class="sm" data-act="edit" data-id="${p.id}">Edit</button>
          <button class="sm danger" data-act="delete" data-id="${p.id}">Delete</button>
        </td>
      </tr>`).join("")}
    </tbody></table>`}
  `;
  $("#new").onclick = () => openProjectModal();
  app.querySelectorAll("a[data-id]").forEach(a => a.onclick = () => navigate("projectDetail", +a.dataset.id));
  app.querySelectorAll("button[data-act]").forEach(b => b.onclick = () => projectAction(b.dataset.act, +b.dataset.id));
};

async function projectAction(act, id) {
  try {
    if (act === "edit") { const d = await api(`/projects/${id}`); return openProjectModal(d.project, d.brief ? d.brief.content : ""); }
    if (act === "delete") {
      if (!confirm("Delete this project and its run history?")) return;
      await api(`/projects/${id}`, { method: "DELETE" });
      toast("Deleted.");
      return render();
    }
    if (act === "run") {
      await api(`/projects/${id}/run`, { method: "POST" });
      toast("Run started.");
    } else {
      await api(`/projects/${id}/${act}`, { method: "POST" });
      toast(act + " ok");
    }
    render();
  } catch (e) { toast(e.message, true); }
}

// ---- lifecycle / artifact helpers ----
const LIFECYCLE = ["Idea","Candidate","Planned","Running","Validated","Committed","Reported","Learned"];
function lifecycleTrack(stage) {
  if (stage === "Failed") return `<span class="badge Failed">Failed</span>`;
  const at = LIFECYCLE.indexOf(stage);
  return `<div class="track">${LIFECYCLE.map((s,i) =>
    `<span class="step ${i <= at && at >= 0 ? "done" : ""}" title="${esc(s)}">${esc(s)}</span>`).join("<span class=\"sep\">›</span>")}</div>`;
}
function nextTasksList(tasks) {
  return (tasks && tasks.length)
    ? `<ul class="next-tasks">${tasks.map(t => `<li>${esc(t)}</li>`).join("")}</ul>`
    : "—";
}
function riskBadge(risk) {
  const cls = risk === "Risky" ? "off" : (risk === "Safe" ? "on" : "");
  return `<span class="badge ${cls}">${esc(risk || "—")}</span>`;
}
const PREVIEWABLE = new Set(["Frame","SpriteSheet","Gif","Image"]);
function artifactGallery(projectId, artifacts) {
  if (!artifacts || artifacts.length === 0) return `<p class="muted">No artifacts generated yet.</p>`;
  return `<div class="artifacts">${artifacts.map(a => {
    const url = `/api/projects/${projectId}/artifact?path=${encodeURIComponent(a.path)}`;
    const media = PREVIEWABLE.has(a.kind)
      ? `<img class="art-img" src="${url}" alt="${esc(a.path)}" loading="lazy">`
      : `<div class="art-icon">${a.kind === "AnimationManifest" ? "🎬" : (a.kind === "Report" ? "📄" : "📦")}</div>`;
    return `<figure class="art-card">
      <a href="${url}" target="_blank" rel="noopener">${media}</a>
      <figcaption>
        <span class="badge">${esc(a.kind)}</span>
        <span class="mono art-path" title="${esc(a.path)}">${esc(a.path.split("/").pop())}</span>
        ${a.skillId ? `<span class="muted mono">$${esc(a.skillId)}</span>` : ""}
      </figcaption>
    </figure>`;
  }).join("")}</div>`;
}

function pct(value) {
  return `${Math.round((value || 0) * 100)}%`;
}

function money(value) {
  return `$${Number(value || 0).toFixed(4)}`;
}

function metricsPanel(m) {
  if (!m) return `<p class="muted">No metrics available yet.</p>`;
  const trend = m.trend || [];
  return `
    <div class="cards">
      <div class="card"><div class="label">Runs</div><div class="value">${m.runCount || 0}</div></div>
      <div class="card"><div class="label">Success rate</div><div class="value">${pct(m.successRate)}</div></div>
      <div class="card"><div class="label">Cost</div><div class="value">${money(m.totalCostUsd)}</div></div>
      <div class="card"><div class="label">Tokens</div><div class="value">${((m.totalInputTokens || 0) + (m.totalOutputTokens || 0)).toLocaleString()}</div></div>
      <div class="card"><div class="label">Resume saved</div><div class="value">${(m.estimatedTokensSavedByResume || 0).toLocaleString()}</div></div>
    </div>
    ${trend.length ? `<table style="margin-top:12px"><thead><tr><th>Run</th><th>Status</th><th>Tier</th><th>Tokens</th><th>Cost</th><th>Resumed</th></tr></thead><tbody>
      ${trend.map(t => `<tr>
        <td>#${t.runId}</td>
        <td>${badge(t.status)}</td>
        <td>${esc(t.tier || "—")}</td>
        <td>${((t.inputTokens || 0) + (t.outputTokens || 0)).toLocaleString()}</td>
        <td>${money(t.costUsd)}</td>
        <td>${t.resumed ? "yes" : "no"}</td>
      </tr>`).join("")}
    </tbody></table>` : `<p class="muted">No trend data yet.</p>`}
  `;
}

function learningPanel(l) {
  if (!l) return `<p class="muted">No learning state available yet.</p>`;
  const failing = l.repeatedFailingTasks || [];
  const changes = l.settingChanges || [];
  return `
    <div class="cards">
      <div class="card"><div class="label">Total runs</div><div class="value">${l.totalRuns || 0}</div></div>
      <div class="card"><div class="label">Successes</div><div class="value">${l.successes || 0}</div></div>
      <div class="card"><div class="label">Failures</div><div class="value">${l.failures || 0}</div></div>
      <div class="card"><div class="label">Not verified</div><div class="value">${l.notVerified || 0}</div></div>
      <div class="card"><div class="label">Rolling rate</div><div class="value">${pct(l.rollingSuccessRate)}</div></div>
    </div>
    <h3>Repeated failing tasks</h3>
    ${failing.length ? `<table><thead><tr><th>Task</th><th>Attempts</th><th>Failures</th><th>Last outcome</th><th>Last attempt</th></tr></thead><tbody>
      ${failing.map(t => `<tr>
        <td>${esc(t.taskTitle)}</td>
        <td>${t.attempts}</td>
        <td>${t.failures}</td>
        <td>${esc(t.lastOutcome)}</td>
        <td>${fmt(t.lastAttemptAt)}</td>
      </tr>`).join("")}
    </tbody></table>` : `<p class="muted">No repeated failures recorded.</p>`}
    <h3>Setting changes</h3>
    ${changes.length ? `<table><thead><tr><th>Key</th><th>Old</th><th>New</th><th>Source</th><th>When</th></tr></thead><tbody>
      ${changes.map(c => `<tr>
        <td>${esc(c.key)}</td>
        <td class="mono">${esc(c.oldValue || "—")}</td>
        <td class="mono">${esc(c.newValue || "—")}</td>
        <td>${esc(c.source)}</td>
        <td>${fmt(c.createdAt)}</td>
      </tr>`).join("")}
    </tbody></table>` : `<p class="muted">No AI setting changes recorded.</p>`}
  `;
}

// ---- Project detail ----
views.projectDetail = async (id) => {
  const [d, goal, lifecycle, artifacts, metrics, prompt, learning] = await Promise.all([
    api(`/projects/${id}`),
    api(`/projects/${id}/goal`).catch(() => null),
    api(`/projects/${id}/lifecycle?take=8`).catch(() => []),
    api(`/projects/${id}/artifacts`).catch(() => []),
    api(`/projects/${id}/metrics?take=12`).catch(() => null),
    api(`/projects/${id}/prompt`).catch(() => null),
    api(`/projects/${id}/learning`).catch(() => null)
  ]);
  const p = d.project;
  const latestMeta = (lifecycle && lifecycle[0]) || null;
  app.innerHTML = `
    <div class="toolbar">
      <h1>${esc(p.name)}</h1>
      <div>
        <button class="sm" data-act="run" data-id="${p.id}">Run now</button>
        <button class="sm" data-act="edit" data-id="${p.id}">Edit</button>
        <button class="sm danger" id="del">Delete</button>
      </div>
    </div>
    <div class="detail-grid">
      <div class="k">Repo</div><div class="mono">${esc(p.repoPath)}</div>
      <div class="k">Target platform</div><div>${p.projectType ? esc(p.projectType) : `<span class="muted">— (not enforced)</span>`}</div>
      <div class="k">AI may evolve brief</div><div>${p.allowAiEditBrief ? badge("on") : badge("off")}</div>
      <div class="k">AI may tune settings</div><div>${p.allowAiEditSettings ? badge("on") : badge("off")}</div>
      <div class="k">Enabled</div><div>${p.enabled ? badge("on") : badge("off")} ${p.paused ? badge("Paused") : ""}</div>
      <div class="k">Priority</div><div>${p.priority}</div>
      <div class="k">Providers</div><div>${esc(p.providerPriority)}</div>
      <div class="k">Validation</div><div class="mono">${esc(p.validationCommand || "—")}</div>
      <div class="k">Auto commit / push</div><div>${p.autoCommit ? "commit" : "no commit"} / ${p.autoPush ? "push" : "no push"}</div>
      <div class="k">Allow main</div><div>${p.allowRunOnMainBranch ? "yes" : "no"}</div>
      <div class="k">Max minutes</div><div>${p.maxRunMinutes}</div>
      <div class="k">Current branch</div><div class="mono">${esc(p.currentBranch || "—")}</div>
      <div class="k">Current task</div><div>${esc(p.currentTask || "—")}</div>
      <div class="k">Last status</div><div>${p.lastRunStatus ? badge(p.lastRunStatus) : "—"} <span class="muted">${fmt(p.lastRunAt)}</span></div>
      ${p.lastError ? `<div class="k">Last error</div><div class="muted">${esc(p.lastError)}</div>` : ""}
    </div>

    <h2>Project goal <span class="muted">${goal && goal.hasGoal ? badge("on") : badge("off")}</span></h2>
    ${goal && goal.hasGoal
      ? `<pre class="goal">${esc((goal.goal || "").slice(0, 1200))}</pre>`
      : `<p class="muted">No <span class="mono">.ai-runner/PROJECT_GOAL.md</span> yet. Add one so runs are goal-directed.</p>`}
    ${goal ? `<div class="goal-files">${Object.entries(goal.files).map(([f,ok]) =>
        `<span class="badge ${ok ? "on" : "off"}">${esc(f)}</span>`).join(" ")}</div>` : ""}

    <h2>Brief <span class="muted">${d.brief ? `v${d.brief.version} · ${esc(d.brief.author)} · ${fmt(d.brief.createdAt)}` : badge("off")}</span>
        ${d.brief ? `<button class="sm" id="briefHistory" style="margin-left:8px">History</button>` : ""}</h2>
    ${d.brief
      ? `<pre class="goal">${esc((d.brief.content || "").slice(0, 2000))}</pre>`
      : `<p class="muted">No brief stored yet. Click <strong>Edit</strong> to write one (or it will be seeded from the brief file on the first run).</p>`}
    <div id="briefHistoryBox"></div>

    <h2>Run metrics</h2>
    ${metricsPanel(metrics)}

    <h2>Prompt directives <span class="muted">${prompt ? `v${prompt.version} · ${esc(prompt.author)} · ${fmt(prompt.createdAt)}` : badge("off")}</span></h2>
    <div class="field">
      <textarea id="promptDirectivesEdit" rows="8" placeholder="Project-specific standing directives for future runs.">${esc(prompt?.content || "")}</textarea>
      <div style="margin-top:8px">
        <button class="sm" id="savePromptDirectives">Save directives</button>
        <button class="sm" id="promptHistory">History</button>
      </div>
    </div>
    <div id="promptHistoryBox"></div>

    <h2>Learning state</h2>
    ${learningPanel(learning)}

    <h2>Task lifecycle</h2>
    ${latestMeta ? `
      <div class="detail-grid">
        <div class="k">Latest task</div><div>${esc(latestMeta.task || "—")}${latestMeta.taskSource ? ` <span class="muted">(${esc(latestMeta.taskSource)})</span>` : ""}</div>
        <div class="k">Stage</div><div>${lifecycleTrack(latestMeta.stage)}</div>
        <div class="k">Risk</div><div>${riskBadge(latestMeta.risk)}${(latestMeta.riskReasons||[]).length ? ` <span class="muted">${esc(latestMeta.riskReasons.join("; "))}</span>` : ""}</div>
        <div class="k">Skills</div><div>${(latestMeta.skills||[]).map(s => `<span class="mono">$${esc(s.id)}</span>`).join(", ") || "—"}</div>
        <div class="k">Next suggested</div><div>${nextTasksList(latestMeta.nextSuggestedTasks)}</div>
      </div>` : `<p class="muted">No lifecycle data yet (runs will populate it).</p>`}

    <h2>Generated artifacts</h2>
    ${artifactGallery(id, artifacts)}

    <h2>Notes</h2>
    <div class="field">
      <textarea id="notes" placeholder="Your notes…">${esc(p.notes || "")}</textarea>
      <button class="sm" id="saveNotes" style="margin-top:8px">Save notes</button>
    </div>

    <h2>Latest summary</h2>
    <pre>${esc(p.lastSummary || "(none)")}</pre>

    <h2>Recent runs</h2>
    ${runsTable(d.recentRuns)}
  `;
  app.querySelectorAll("button[data-act]").forEach(b => b.onclick = () => projectAction(b.dataset.act, +b.dataset.id));
  app.querySelectorAll("a[data-run]").forEach(a => a.onclick = () => navigate("runDetail", +a.dataset.run));
  $("#saveNotes").onclick = async () => {
    try { await api(`/projects/${id}`, { method: "PUT", body: JSON.stringify({ notes: $("#notes").value }) }); toast("Notes saved."); }
    catch (e) { toast(e.message, true); }
  };
  $("#del").onclick = async () => {
    if (!confirm("Delete this project and its run history?")) return;
    try { await api(`/projects/${id}`, { method: "DELETE" }); toast("Deleted."); navigate("projects"); }
    catch (e) { toast(e.message, true); }
  };
  const bh = $("#briefHistory");
  if (bh) bh.onclick = async () => {
    const box = $("#briefHistoryBox");
    if (box.innerHTML) { box.innerHTML = ""; return; }
    try {
      const versions = await api(`/projects/${id}/briefs`);
      box.innerHTML = versions.map(v =>
        `<details><summary>v${v.version} · <strong>${esc(v.author)}</strong> · ${fmt(v.createdAt)}${v.note ? ` · <span class="muted">${esc(v.note)}</span>` : ""}</summary><pre>${esc(v.content)}</pre></details>`
      ).join("") || `<p class="muted">No history.</p>`;
    } catch (e) { toast(e.message, true); }
  };
  $("#savePromptDirectives").onclick = async () => {
    try {
      await api(`/projects/${id}/prompt`, {
        method: "PUT",
        body: JSON.stringify({ content: $("#promptDirectivesEdit").value, note: "edited in dashboard" })
      });
      toast("Prompt directives saved.");
      render();
    } catch (e) { toast(e.message, true); }
  };
  $("#promptHistory").onclick = async () => {
    const box = $("#promptHistoryBox");
    if (box.innerHTML) { box.innerHTML = ""; return; }
    try {
      const versions = await api(`/projects/${id}/prompt/history`);
      box.innerHTML = versions.map(v =>
        `<details><summary>v${v.version} · <strong>${esc(v.author)}</strong> · ${fmt(v.createdAt)}${v.note ? ` · <span class="muted">${esc(v.note)}</span>` : ""} <button class="sm" data-prompt-revert="${v.version}">Revert</button></summary><pre>${esc(v.content)}</pre></details>`
      ).join("") || `<p class="muted">No history.</p>`;
      box.querySelectorAll("button[data-prompt-revert]").forEach(b => b.onclick = async (ev) => {
        ev.preventDefault();
        try {
          await api(`/projects/${id}/prompt/revert/${b.dataset.promptRevert}`, { method: "POST" });
          toast("Prompt directives reverted.");
          render();
        } catch (e) { toast(e.message, true); }
      });
    } catch (e) { toast(e.message, true); }
  };
};

// ---- Runs ----
views.runs = async () => {
  const runs = await api("/runs?take=100");
  app.innerHTML = `<h1>Runs</h1>${runsTable(runs)}`;
  app.querySelectorAll("a[data-run]").forEach(a => a.onclick = () => navigate("runDetail", +a.dataset.run));
};

function runsTable(runs) {
  if (!runs || runs.length === 0) return `<p class="muted">No runs.</p>`;
  return `<table><thead><tr>
    <th>#</th><th>Project</th><th>Provider</th><th>Status</th><th>Started</th><th>Finished</th><th>Reason</th><th>Usage</th><th></th>
  </tr></thead><tbody>
    ${runs.map(r => `<tr>
      <td>${r.id}</td>
      <td>${esc(r.projectName)}</td>
      <td>${esc(r.provider)}</td>
      <td>${badge(r.status)}</td>
      <td>${fmt(r.startedAt)}</td>
      <td>${fmt(r.finishedAt)}</td>
      <td class="muted">${esc((r.reason || "").slice(0, 50))}</td>
      <td>${esc(r.usage || "—")}</td>
      <td><a data-run="${r.id}">view</a></td>
    </tr>`).join("")}
  </tbody></table>`;
}

// ---- Run detail ----
views.runDetail = async (id) => {
  closeRunEventStream();
  const d = await api(`/runs/${id}`);
  const r = d.run;
  const isLive = r.status === "Running" || r.status === "Pending";
  app.innerHTML = `
    <div class="toolbar"><h1>Run #${r.id} — ${esc(r.projectName)}</h1><button class="ghost" id="back">← Runs</button></div>
    <div class="detail-grid">
      <div class="k">Provider</div><div>${esc(r.provider)}</div>
      <div class="k">Status</div><div>${badge(r.status)}</div>
      <div class="k">Branch</div><div class="mono">${esc(r.branch || "—")}</div>
      <div class="k">Started</div><div>${fmt(r.startedAt)}</div>
      <div class="k">Finished</div><div>${fmt(r.finishedAt)}</div>
      <div class="k">Usage</div><div>${esc(r.usage || "—")}</div>
      <div class="k">Email sent</div><div>${r.emailSent ? "yes" : "no"}</div>
      <div class="k">Commit</div><div class="mono">${esc(r.commitSha || "—")}</div>
      <div class="k">Validation</div><div>${d.validationRun ? (d.validationPassed ? badge("Success") : badge("Failed")) : "not run"}</div>
      ${r.reason ? `<div class="k">Reason</div><div class="muted">${esc(r.reason)}</div>` : ""}
      ${d.meta ? `
        <div class="k">Lifecycle</div><div>${lifecycleTrack(d.meta.stage)}</div>
        <div class="k">Task</div><div>${esc(d.meta.task || "—")}${d.meta.taskSource ? ` <span class="muted">(${esc(d.meta.taskSource)})</span>` : ""}</div>
        <div class="k">Risk</div><div>${riskBadge(d.meta.risk)}${(d.meta.riskReasons||[]).length ? ` <span class="muted">${esc(d.meta.riskReasons.join("; "))}</span>` : ""}</div>
        <div class="k">Skills</div><div>${(d.meta.skills||[]).map(s => `<span class="mono">$${esc(s.id)}</span> <span class="muted">(${(s.matchedKeywords||[]).map(esc).join(", ")})</span>`).join("<br>") || "—"}</div>
        <div class="k">Next suggested</div><div>${nextTasksList(d.meta.nextSuggestedTasks)}</div>
        ${(d.meta.memoryUpdates||[]).length ? `<div class="k">Memory updated</div><div class="muted">${d.meta.memoryUpdates.map(esc).join(", ")}</div>` : ""}` : ""}
    </div>
    ${d.meta && d.meta.artifacts && d.meta.artifacts.length ? `<h2>Generated artifacts</h2>${artifactGallery(r.projectId, d.meta.artifacts.map(a => ({path:a.path, kind:a.kind, skillId:a.skillId})))}` : ""}
    <h2>Summary</h2><pre>${esc(d.summary || "(none)")}</pre>
    ${d.creativePlan ? `<details><summary><strong>Creative plan</strong> (from the knowledge-base planner)</summary><pre>${esc(d.creativePlan)}</pre></details>` : ""}
    ${d.prompt ? `<details><summary><strong>Prompt sent to the provider</strong> (${d.prompt.length.toLocaleString()} chars)</summary><pre>${esc(d.prompt)}</pre></details>` : ""}
    <h2>Changed files</h2><pre>${esc(d.changedFiles || "(none)")}</pre>
    ${d.validationOutput ? `<h2>Validation output</h2><pre>${esc(d.validationOutput)}</pre>` : ""}
    ${isLive ? `<h2>Live activity <span class="live-dot">●</span></h2>
      <pre id="live-events" class="live-events">Connecting to run stream…\n</pre>` : ""}
    <h2>Log <button class="sm" id="loadlog">Load full log</button></h2>
    <pre id="log" class="muted">Click “Load full log”.</pre>
  `;
  $("#back").onclick = () => navigate("runs");
  $("#loadlog").onclick = async () => {
    try { const l = await api(`/runs/${id}/log`); $("#log").textContent = l.log; $("#log").classList.remove("muted"); }
    catch (e) { toast(e.message, true); }
  };
  if (isLive) startRunEventStream(id);
};

// ---- Providers ----
views.providers = async () => {
  const providers = await api("/providers");
  app.innerHTML = `<h1>Providers</h1>${providerTable(providers)}`;
};

// ---- Skills (global skill store) ----
views.skills = async () => {
  const [skills, log] = await Promise.all([api("/skills"), api("/skills/log")]);
  app.innerHTML = `
    <div class="toolbar"><h1>Skills</h1><button class="sm" id="reload">Reload from disk</button></div>
    <p class="muted">Global skills shared across all projects. AutoDev auto-selects a skill when a task
      matches its trigger keywords, and injects it explicitly into the run prompt.</p>
    ${skills.length === 0 ? `<p class="muted">No skills found. Check <span class="mono">AutoDevSkills/</span>.</p>` : `
    <table><thead><tr>
      <th>Skill</th><th>Version</th><th>Status</th><th>Triggers</th><th></th>
    </tr></thead><tbody>
      ${skills.map(s => `<tr>
        <td><strong>${esc(s.name)}</strong> <span class="mono muted">$${esc(s.id)}</span>
            <div class="muted">${esc(s.description)}</div>
            <div class="muted mono" style="font-size:11px">${esc(s.sourcePath)}</div></td>
        <td>${esc(s.version || "—")}</td>
        <td>${s.enabled ? badge("on") : badge("off")}</td>
        <td class="muted">${(s.triggers || []).map(esc).join(", ")}</td>
        <td class="actions">
          <button class="sm" data-skill="${esc(s.id)}" data-act="${s.enabled ? "disable" : "enable"}">${s.enabled ? "Disable" : "Enable"}</button>
        </td>
      </tr>`).join("")}
    </tbody></table>`}
    <h2>Recent skill selections</h2>
    ${log.length === 0 ? `<p class="muted">No skill has been selected for a run yet.</p>` : `
    <table><thead><tr><th>When</th><th>Skill</th><th>Project</th><th>Matched keywords</th><th>Task</th></tr></thead><tbody>
      ${log.map(l => `<tr>
        <td>${fmt(l.selectedAt)}</td>
        <td class="mono">${esc(l.skillId)}</td>
        <td>${esc(l.projectName)}</td>
        <td class="muted">${(l.matchedKeywords || []).map(esc).join(", ")}</td>
        <td class="muted">${esc((l.task || "—").slice(0, 60))}</td>
      </tr>`).join("")}
    </tbody></table>`}
  `;
  $("#reload").onclick = async () => {
    try { const r = await api("/skills/reload", { method: "POST" }); toast(`Reloaded ${r.count} skill(s).`); render(); }
    catch (e) { toast(e.message, true); }
  };
  app.querySelectorAll("button[data-skill]").forEach(b => b.onclick = async () => {
    try { await api(`/skills/${encodeURIComponent(b.dataset.skill)}/${b.dataset.act}`, { method: "POST" }); toast("Updated."); render(); }
    catch (e) { toast(e.message, true); }
  });
};

// ---- Settings (read-only config view) ----
views.settings = async () => {
  const s = await api("/settings");
  app.innerHTML = `
    <h1>Settings</h1>
    <p class="muted">Scheduler, email and provider commands are configured in <span class="mono">appsettings.json</span>. Per-project settings are editable from the Projects screen.</p>
    <h2>Scheduler</h2>
    <div class="detail-grid">
      <div class="k">Enabled</div><div>${s.scheduler.enabled}</div>
      <div class="k">Interval (hours)</div><div>${s.scheduler.intervalHours}</div>
      <div class="k">Run on startup</div><div>${s.scheduler.runOnStartup}</div>
    </div>
    <h2>Email (EmailJS)</h2>
    <div class="detail-grid">
      <div class="k">Enabled</div><div>${s.email.enabled}</div>
      <div class="k">To</div><div>${esc(s.email.to || "—")}</div>
      <div class="k">Service id</div><div>${esc(s.email.serviceId || "—")}</div>
    </div>
    <h2>Providers</h2>
    <div class="detail-grid">
      <div class="k">Codex</div><div>${s.providers.codex.enabled ? "enabled" : "disabled"} <span class="mono">(${esc(s.providers.codex.command)})</span></div>
      <div class="k">Claude</div><div>${s.providers.claude.enabled ? "enabled" : "disabled"} <span class="mono">(${esc(s.providers.claude.command)})</span></div>
    </div>
  `;
};

// ---- Project modal ----
const modal = $("#modal");
function openProjectModal(p, briefContent) {
  const isEdit = !!p;
  p = p || {};
  $("#modal-title").textContent = isEdit ? `Edit ${p.name}` : "New project";
  $("#modal-body").innerHTML = `
    <div class="field"><label>Name *</label><input type="text" id="f-name" value="${esc(p.name || "")}"></div>
    <div class="field"><label>Repo path *</label>
      <div class="pathpick">
        <input type="text" id="f-repo" value="${esc(p.repoPath || "")}" placeholder="C:\\path\\to\\repo">
        <button type="button" id="f-repo-browse" class="ghost">Browse…</button>
      </div>
      <div id="folder-browser" class="folder-browser hidden"></div>
    </div>
    <div class="field"><label>Brief / product prompt</label>
      <textarea id="f-brief-content" rows="8" placeholder="Describe the product goal the AI should build toward. Stored in the database and versioned — every edit keeps history.">${esc(briefContent || "")}</textarea>
      <div class="muted" style="font-size:11px">Stored in the DB (versioned). The planner always uses the latest version.</div>
    </div>
    <div class="field row"><input type="checkbox" id="f-aiedit" ${p.allowAiEditBrief ? "checked" : ""}><label>Allow AI to evolve this brief (saved as new versions)</label></div>
    <div class="field row"><input type="checkbox" id="f-aisettings" ${p.allowAiEditSettings ? "checked" : ""}><label>Allow AI to tune validation/provider/time settings</label></div>
    <details style="margin-bottom:12px"><summary class="muted">Advanced: seed file (fallback)</summary>
      <div class="field"><label>Brief file path</label><input type="text" id="f-brief" value="${esc(p.briefPath || "ai-autonomous.md")}">
      <div class="muted" style="font-size:11px">Only used to seed version 1 if no brief is stored yet.</div></div>
    </details>
    <div class="field"><label>Target platform / stack</label>
      <input type="text" id="f-type" value="${esc(p.projectType || "")}" placeholder="e.g. Android (Kotlin/Jetpack Compose)">
      <div class="muted" style="font-size:11px">Enforced as a hard constraint so the agent can't reimplement on another stack (e.g. HTML web). Leave blank to let it infer.</div>
    </div>
    <div class="grid2">
      <div class="field"><label>Priority</label><input type="number" id="f-prio" value="${p.priority ?? 0}"></div>
      <div class="field"><label>Max run minutes</label><input type="number" id="f-max" value="${p.maxRunMinutes ?? 30}"></div>
    </div>
    <div class="field"><label>Provider priority</label>
      <div id="f-prov-list" class="prov-list"></div>
      <div class="muted" style="font-size:11px">Tick a provider to allow it for this project; ↑/↓ sets the order tried (top = first).</div>
    </div>
    <div class="field"><label>Validation command</label><input type="text" id="f-val" value="${esc(p.validationCommand || "")}" placeholder="dotnet build"></div>
    <div class="grid2">
      <div class="field row"><input type="checkbox" id="f-commit" ${p.autoCommit ?? true ? "checked" : ""}><label>Auto commit</label></div>
      <div class="field row"><input type="checkbox" id="f-push" ${p.autoPush ? "checked" : ""}><label>Auto push</label></div>
      <div class="field row"><input type="checkbox" id="f-main" ${p.allowRunOnMainBranch ? "checked" : ""}><label>Allow run on main</label></div>
      <div class="field row"><input type="checkbox" id="f-enabled" ${(p.enabled ?? true) ? "checked" : ""}><label>Enabled</label></div>
    </div>
    <div class="field"><label>Notes</label><textarea id="f-notes">${esc(p.notes || "")}</textarea></div>
  `;
  modal.classList.remove("hidden");
  const readProviderPriority = initProviderPriority(p.providerPriority || "Codex,Claude");
  $("#f-repo-browse").onclick = () => {
    const fb = $("#folder-browser");
    if (fb.classList.contains("hidden")) openFolderBrowser($("#f-repo").value.trim());
    else fb.classList.add("hidden");
  };
  $("#modal-cancel").onclick = () => modal.classList.add("hidden");
  $("#modal-save").onclick = async () => {
    const body = {
      name: $("#f-name").value.trim(),
      repoPath: $("#f-repo").value.trim(),
      briefPath: $("#f-brief").value.trim(),
      brief: $("#f-brief-content").value,
      projectType: $("#f-type").value.trim(),
      priority: +$("#f-prio").value,
      maxRunMinutes: +$("#f-max").value,
      providerPriority: readProviderPriority(),
      validationCommand: $("#f-val").value.trim(),
      autoCommit: $("#f-commit").checked,
      autoPush: $("#f-push").checked,
      allowRunOnMainBranch: $("#f-main").checked,
      allowAiEditBrief: $("#f-aiedit").checked,
      allowAiEditSettings: $("#f-aisettings").checked,
      enabled: $("#f-enabled").checked,
      notes: $("#f-notes").value
    };
    if (!body.name || !body.repoPath) { toast("Name and repo path are required.", true); return; }
    if (!body.providerPriority) { toast("Select at least one provider.", true); return; }
    try {
      if (isEdit) await api(`/projects/${p.id}`, { method: "PUT", body: JSON.stringify(body) });
      else await api(`/projects`, { method: "POST", body: JSON.stringify(body) });
      modal.classList.add("hidden");
      toast("Saved.");
      render();
    } catch (e) { toast(e.message, true); }
  };
}

// ---- Provider priority editor (checkbox = allowed, row order = try order) ----
// Serializes back to the same CSV the backend already parses ("Codex,Claude"),
// so no API or DB change is needed. Unknown tokens from old data are kept.
const KNOWN_PROVIDERS = ["Codex", "Claude"];
function initProviderPriority(csv) {
  const chosen = (csv || "").split(",").map(s => s.trim()).filter(Boolean)
    .map(s => KNOWN_PROVIDERS.find(k => k.toLowerCase() === s.toLowerCase()) || s);
  const rows = [...new Set([...chosen, ...KNOWN_PROVIDERS])]
    .map(kind => ({ kind, checked: chosen.some(c => c.toLowerCase() === kind.toLowerCase()) }));
  const list = $("#f-prov-list");

  const draw = () => {
    list.innerHTML = rows.map((r, i) => `
      <div class="prov-item${r.checked ? "" : " prov-off"}">
        <input type="checkbox" ${r.checked ? "checked" : ""} title="Allow ${esc(r.kind)}">
        <span class="prov-name">${i + 1}. ${esc(r.kind)}</span>
        <span class="prov-move">
          <button type="button" class="sm prov-up" ${i === 0 ? "disabled" : ""} title="Try earlier">↑</button>
          <button type="button" class="sm prov-down" ${i === rows.length - 1 ? "disabled" : ""} title="Try later">↓</button>
        </span>
      </div>`).join("");
    list.querySelectorAll(".prov-item").forEach((el, i) => {
      el.querySelector("input").onchange = e => { rows[i].checked = e.target.checked; draw(); };
      el.querySelector(".prov-up").onclick = () => { [rows[i - 1], rows[i]] = [rows[i], rows[i - 1]]; draw(); };
      el.querySelector(".prov-down").onclick = () => { [rows[i + 1], rows[i]] = [rows[i], rows[i + 1]]; draw(); };
    });
  };
  draw();
  return () => rows.filter(r => r.checked).map(r => r.kind).join(",");
}

// ---- Folder browser (repo path picker) ----
async function openFolderBrowser(startPath) {
  const fb = $("#folder-browser");
  fb.classList.remove("hidden");
  fb.innerHTML = `<div class="fb-empty muted">Loading…</div>`;
  try {
    await loadFolder(startPath);
  } catch {
    // Start path missing/inaccessible — fall back to the drive list.
    try { await loadFolder(""); } catch (e) { fb.innerHTML = `<div class="fb-empty muted">${esc(e.message)}</div>`; }
  }
}

async function loadFolder(path) {
  const fb = $("#folder-browser");
  const d = await api(`/fs/browse${path ? "?path=" + encodeURIComponent(path) : ""}`);
  const atDrives = !d.path;
  fb.innerHTML = `
    <div class="fb-head">
      <button type="button" class="sm" id="fb-up" ${d.parent === null && atDrives ? "disabled" : ""}>↑ Up</button>
      <span class="fb-path mono" title="${esc(d.path)}">${esc(d.path || "Drives")}</span>
      <button type="button" class="sm" id="fb-select" ${atDrives ? "disabled" : ""}>Select this folder</button>
    </div>
    <div class="fb-list">
      ${d.dirs.length
        ? d.dirs.map(x => `<div class="fb-item" data-path="${esc(x.path)}">📁 ${esc(x.name)}</div>`).join("")
        : `<div class="fb-empty muted">No subfolders</div>`}
    </div>
  `;
  $("#fb-up").onclick = () => loadFolder(d.parent ?? "").catch(e => toast(e.message, true));
  $("#fb-select").onclick = () => {
    $("#f-repo").value = d.path;
    fb.classList.add("hidden");
  };
  fb.querySelectorAll(".fb-item").forEach(el => {
    el.onclick = () => loadFolder(el.dataset.path).catch(e => toast(e.message, true));
  });
}

// ---- boot ----
render();
setInterval(() => { if (current === "overview" || current === "projects") render(); }, 15000);
