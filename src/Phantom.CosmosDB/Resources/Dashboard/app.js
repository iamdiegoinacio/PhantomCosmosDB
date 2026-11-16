const BASE = "__PHANTOM_BASE_PATH__";
let cachedLogs = [];
let overviewData = null;

function toast(msg) {
  const el = document.getElementById("toast");
  el.textContent = msg;
  el.style.display = "block";
  setTimeout(() => { el.style.display = "none"; }, 2600);
}

async function api(path, opts = {}) {
  const res = await fetch(BASE + path, opts);
  const text = await res.text();
  let data = text;
  try { data = JSON.parse(text); } catch {}
  if (!res.ok) throw { status: res.status, data };
  return data;
}

function switchTab(name, btn) {
  document.querySelectorAll(".tab").forEach(t => t.classList.remove("active"));
  document.querySelectorAll("nav button").forEach(b => b.classList.remove("active"));
  document.getElementById("tab-" + name).classList.add("active");
  btn.classList.add("active");
  if (name === "logs") loadLogs();
  if (name === "config") loadConfig();
  if (name === "chaos") loadChaos();
}

async function loadOverview() {
  overviewData = await api("/api/overview");
  document.getElementById("header-db").textContent = "Database: " + overviewData.databaseName;
  document.getElementById("ov-db").textContent = overviewData.databaseName;
  document.getElementById("ov-containers").textContent = overviewData.containerCount;
  document.getElementById("ov-items").textContent = overviewData.totalItems;
  document.getElementById("ov-chaos").textContent = overviewData.chaosActive ? "ATIVO" : "Off";

  const chaosPill = document.getElementById("chaos-pill");
  chaosPill.textContent = overviewData.chaosActive ? "Chaos: ATIVO" : "Chaos: Desligado";
  chaosPill.className = overviewData.chaosActive ? "pill chaos" : "pill";

  const grid = document.getElementById("containers-grid");
  grid.innerHTML = "";
  const select = document.getElementById("exp-container");
  const current = select.value;
  select.innerHTML = "";

  for (const c of overviewData.containers) {
    const opt = document.createElement("option");
    opt.value = c.name;
    opt.textContent = `${c.name} (${c.itemCount})`;
    select.appendChild(opt);

    const card = document.createElement("div");
    card.className = "card";
    card.innerHTML = `
      <div class="row space-between">
        <strong style="font-size:16px">${c.name}</strong>
        <span class="pill">${c.itemCount} itens</span>
      </div>
      <div class="mt">
        <span class="tag">PK: ${c.partitionKeyPaths.join(", ")}</span>
        <span class="tag">${c.throughput} RU/s</span>
        <span class="tag">Index: ${c.indexingMode}</span>
        ${c.defaultTtl !== null && c.defaultTtl !== undefined ? `<span class="tag">TTL: ${c.defaultTtl}</span>` : ""}
        ${c.compositeIndexCount > 0 ? `<span class="tag">Composite: ${c.compositeIndexCount}</span>` : ""}
        ${c.uniqueKeys.length > 0 ? `<span class="tag">UniqueKeys: ${c.uniqueKeys.length}</span>` : ""}
        ${c.computedProperties.length > 0 ? `<span class="tag">Computed: ${c.computedProperties.length}</span>` : ""}
      </div>
    `;
    grid.appendChild(card);
  }

  if (current && overviewData.containers.some(c => c.name === current)) {
    select.value = current;
  }
}

async function loadContainerItems() {
  const name = document.getElementById("exp-container").value;
  if (!name) return;
  document.getElementById("exp-sql").value = "SELECT TOP 50 * FROM c";
  await runQuery();
}

async function runQuery() {
  const name = document.getElementById("exp-container").value;
  const sql = document.getElementById("exp-sql").value;
  const enableScan = document.getElementById("exp-scan").checked;
  const errBox = document.getElementById("exp-error");
  errBox.style.display = "none";

  try {
    const res = await api(`/api/containers/${encodeURIComponent(name)}/query`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ sql, enableScan })
    });
    document.getElementById("exp-count").textContent = res.count;
    document.getElementById("exp-ru").textContent = `RU: ${res.totalRu}`;
    renderQueryResults(name, res.items);
  } catch (e) {
    errBox.textContent = `Erro (${e.status}): ${e.data?.error || JSON.stringify(e.data)}`;
    errBox.style.display = "block";
  }
}

function renderQueryResults(containerName, items) {
  const box = document.getElementById("exp-results");
  box.innerHTML = "";
  const cMeta = overviewData?.containers?.find(c => c.name === containerName);
  const pkPaths = cMeta?.partitionKeyPaths || ["/id"];

  for (const item of items) {
    const wrap = document.createElement("div");
    wrap.className = "result-item";

    const header = document.createElement("div");
    header.className = "row space-between result-item-header";
    const idLabel = document.createElement("span");
    idLabel.className = "result-item-id";
    idLabel.textContent = item.id ? `id: ${item.id}` : "projeção";
    header.appendChild(idLabel);

    if (item.id) {
      const delBtn = document.createElement("button");
      delBtn.className = "btn danger sm";
      delBtn.textContent = "Excluir";
      delBtn.onclick = () => deleteItem(containerName, item, pkPaths);
      header.appendChild(delBtn);
    }

    const pre = document.createElement("pre");
    pre.textContent = JSON.stringify(item, null, 2);
    wrap.appendChild(header);
    wrap.appendChild(pre);
    box.appendChild(wrap);
  }
}

function extractPkParam(item, pkPaths) {
  const resolvePath = (obj, path) => {
    const parts = path.replace(/^\//, "").split("/");
    let cur = obj;
    for (const p of parts) {
      if (cur == null) return null;
      cur = cur[p];
    }
    return cur;
  };
  if (pkPaths.length > 1) {
    return JSON.stringify(pkPaths.map(p => resolvePath(item, p)));
  }
  return resolvePath(item, pkPaths[0]) ?? item.id;
}

async function deleteItem(containerName, item, pkPaths) {
  const pk = extractPkParam(item, pkPaths);
  await api(`/api/containers/${encodeURIComponent(containerName)}/items/${encodeURIComponent(item.id)}?pk=${encodeURIComponent(pk)}`, {
    method: "DELETE"
  });
  toast(`Item '${item.id}' removido.`);
  await refreshAll();
}

async function upsertItem() {
  const name = document.getElementById("exp-container").value;
  const raw = document.getElementById("exp-upsert").value;
  try {
    JSON.parse(raw);
    await api(`/api/containers/${encodeURIComponent(name)}/items`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: raw
    });
    toast("Item salvo com sucesso.");
    await refreshAll();
  } catch (e) {
    toast("Erro no JSON/Upsert: " + (e.data?.error || e.message));
  }
}

const chaosBoolFields = [
  "EnableRandomThrottling", "SimulateHighLatency",
  "Simulate429_TooManyRequests", "Simulate503_ServiceUnavailable",
  "Simulate408_RequestTimeout", "Simulate403_Forbidden",
  "Simulate401_Unauthorized", "Simulate409_Conflict",
  "Simulate413_EntityTooLarge", "Simulate412_PreconditionFailed",
  "Simulate400_BadRequest"
];
const chaosNumFields = ["ThrottlingProbability", "MinLatencyMs", "MaxLatencyMs", "RetryAfterMs"];

async function loadChaos() {
  const cfg = await api("/api/chaos");
  for (const f of chaosBoolFields) {
    const el = document.getElementById("ch-" + f);
    if (el) el.checked = !!cfg[f];
  }
  for (const f of chaosNumFields) {
    const el = document.getElementById("ch-" + f);
    if (el) el.value = cfg[f] ?? 0;
  }
}

async function applyChaos() {
  const payload = {};
  for (const f of chaosBoolFields) payload[f] = document.getElementById("ch-" + f).checked;
  for (const f of chaosNumFields) payload[f] = Number(document.getElementById("ch-" + f).value);
  await api("/api/chaos", {
    method: "PUT",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(payload)
  });
  toast("Chaos Engineering atualizado em memória!");
  await loadOverview();
}

async function disableAllChaos() {
  for (const f of chaosBoolFields) document.getElementById("ch-" + f).checked = false;
  await applyChaos();
}

async function loadLogs() {
  const res = await api("/api/logs?take=200");
  cachedLogs = res.items || [];
  renderLogs();
}

function renderLogs() {
  const q = (document.getElementById("log-filter").value || "").toLowerCase();
  const tbody = document.getElementById("logs-body");
  tbody.innerHTML = "";
  for (const entry of cachedLogs) {
    const text = `${entry.method} ${entry.path} ${entry.containerName || ""} ${entry.sqlQuery || ""} ${entry.statusCode}`.toLowerCase();
    if (q && !text.includes(q)) continue;
    const scClass = entry.statusCode >= 500 ? "status-5xx" : entry.statusCode >= 400 ? "status-4xx" : "status-2xx";
    const tr = document.createElement("tr");
    tr.innerHTML = `
      <td class="mono-cell">${new Date(entry.timestampUtc).toISOString().substring(11, 23)}</td>
      <td><span class="tag">${entry.method}</span></td>
      <td class="${scClass}">${entry.statusCode}${entry.subStatusCode ? ` (${entry.subStatusCode})` : ""} ${entry.isChaosInjected ? '<span class="pill chaos">CHAOS</span>' : ''}</td>
      <td>${entry.containerName || "-"}</td>
      <td class="mono-cell">${entry.requestCharge}</td>
      <td class="mono-cell">${entry.durationMs}ms</td>
      <td class="mono-cell">${entry.path}${entry.sqlQuery ? `<div class="sql-cell">${entry.sqlQuery}</div>` : ""}</td>
    `;
    tbody.appendChild(tr);
  }
}

async function clearLogs() {
  await api("/api/logs", { method: "DELETE" });
  cachedLogs = [];
  renderLogs();
  toast("Logs limpos.");
}

async function loadConfig() {
  const res = await api("/api/config");
  document.getElementById("cfg-path").textContent = res.path + (res.exists ? "" : " (será criado ao salvar)");
  document.getElementById("cfg-editor").value = res.json;
}

function formatConfigJson() {
  const ed = document.getElementById("cfg-editor");
  try {
    ed.value = JSON.stringify(JSON.parse(ed.value), null, 2);
  } catch (e) {
    toast("JSON inválido: " + e.message);
  }
}

async function saveConfigJson() {
  const json = document.getElementById("cfg-editor").value;
  try {
    JSON.parse(json);
    const res = await api("/api/config", {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ json })
    });
    toast("Salvo em: " + res.path);
    await loadChaos();
    await loadOverview();
  } catch (e) {
    toast("Erro ao salvar: " + (e.data?.error || e.message));
  }
}

async function resetSeed() {
  await api("/api/seed/reset", { method: "POST" });
  toast("Containers limpos e re-populados!");
  await refreshAll();
}

async function refreshAll() {
  await loadOverview();
  await runQuery();
  await loadChaos();
}

document.getElementById("exp-sql").addEventListener("keydown", e => {
  if (e.ctrlKey && e.key === "Enter") runQuery();
});

setInterval(() => {
  if (document.getElementById("tab-logs").classList.contains("active") &&
      document.getElementById("log-auto").checked) {
    loadLogs();
  }
}, 1500);

(async () => {
  await refreshAll();
  await loadLogs();
  await loadConfig();
})();
