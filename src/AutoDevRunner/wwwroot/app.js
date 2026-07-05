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

$("#nav").addEventListener("click", (e) => {
  const b = e.target.closest("button[data-view]");
  if (!b) return;
  navigate(b.dataset.view);
});
$("#refresh").addEventListener("click", () => render());

function navigate(view, arg) {
  current = view;
  [...$("#nav").children].forEach(b => b.classList.toggle("active", b.dataset.view === view));
  render(arg);
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
    if (act === "edit") { const d = await api(`/projects/${id}`); return openProjectModal(d.project); }
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

// ---- Project detail ----
views.projectDetail = async (id) => {
  const d = await api(`/projects/${id}`);
  const p = d.project;
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
      <div class="k">Brief</div><div class="mono">${esc(p.briefPath)}</div>
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
  const d = await api(`/runs/${id}`);
  const r = d.run;
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
    </div>
    <h2>Summary</h2><pre>${esc(d.summary || "(none)")}</pre>
    <h2>Changed files</h2><pre>${esc(d.changedFiles || "(none)")}</pre>
    ${d.validationOutput ? `<h2>Validation output</h2><pre>${esc(d.validationOutput)}</pre>` : ""}
    <h2>Log <button class="sm" id="loadlog">Load full log</button></h2>
    <pre id="log" class="muted">Click “Load full log”.</pre>
  `;
  $("#back").onclick = () => navigate("runs");
  $("#loadlog").onclick = async () => {
    try { const l = await api(`/runs/${id}/log`); $("#log").textContent = l.log; $("#log").classList.remove("muted"); }
    catch (e) { toast(e.message, true); }
  };
};

// ---- Providers ----
views.providers = async () => {
  const providers = await api("/providers");
  app.innerHTML = `<h1>Providers</h1>${providerTable(providers)}`;
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
function openProjectModal(p) {
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
    <div class="field"><label>Brief file</label><input type="text" id="f-brief" value="${esc(p.briefPath || "ai-autonomous.md")}"></div>
    <div class="grid2">
      <div class="field"><label>Priority</label><input type="number" id="f-prio" value="${p.priority ?? 0}"></div>
      <div class="field"><label>Max run minutes</label><input type="number" id="f-max" value="${p.maxRunMinutes ?? 30}"></div>
    </div>
    <div class="field"><label>Provider priority</label><input type="text" id="f-prov" value="${esc(p.providerPriority || "Codex,Claude")}"></div>
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
      priority: +$("#f-prio").value,
      maxRunMinutes: +$("#f-max").value,
      providerPriority: $("#f-prov").value.trim(),
      validationCommand: $("#f-val").value.trim(),
      autoCommit: $("#f-commit").checked,
      autoPush: $("#f-push").checked,
      allowRunOnMainBranch: $("#f-main").checked,
      enabled: $("#f-enabled").checked,
      notes: $("#f-notes").value
    };
    if (!body.name || !body.repoPath) { toast("Name and repo path are required.", true); return; }
    try {
      if (isEdit) await api(`/projects/${p.id}`, { method: "PUT", body: JSON.stringify(body) });
      else await api(`/projects`, { method: "POST", body: JSON.stringify(body) });
      modal.classList.add("hidden");
      toast("Saved.");
      render();
    } catch (e) { toast(e.message, true); }
  };
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
