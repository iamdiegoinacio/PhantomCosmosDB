"use strict";
const BASE = "__PHANTOM_BASE_PATH__";
let cachedLogs = [];
let overviewData = null;
let regionsCatalog = [];
function byId(id) {
    const el = document.getElementById(id);
    if (!el)
        throw new Error(`Element #${id} not found.`);
    return el;
}
function toast(msg) {
    const el = byId("toast");
    el.textContent = msg;
    el.style.display = "block";
    window.setTimeout(() => { el.style.display = "none"; }, 2600);
}
async function api(path, opts = {}) {
    const res = await fetch(BASE + path, opts);
    const text = await res.text();
    let data = text;
    try {
        data = JSON.parse(text);
    }
    catch { /* plain text */ }
    if (!res.ok)
        throw { status: res.status, data };
    return data;
}
function switchTab(name, btn) {
    document.querySelectorAll(".tab").forEach(t => t.classList.remove("active"));
    document.querySelectorAll("nav button").forEach(b => b.classList.remove("active"));
    byId("tab-" + name).classList.add("active");
    btn.classList.add("active");
    if (name === "logs")
        void loadLogs();
    if (name === "config")
        void loadConfig();
    if (name === "chaos")
        void loadChaos();
    if (name === "cost")
        void runCostSimulation();
}
async function loadRegions() {
    const res = await api("/api/cost/regions");
    regionsCatalog = res.regions;
    const populate = (selectId) => {
        const sel = byId(selectId);
        const prev = sel.value;
        sel.innerHTML = "";
        for (const r of regionsCatalog) {
            const opt = document.createElement("option");
            opt.value = r.id;
            opt.textContent = `${r.country} — ${r.regionName} ($${r.serverlessPerMillionRuUsd.toFixed(3)}/1M)`;
            sel.appendChild(opt);
        }
        if (prev && regionsCatalog.some(r => r.id === prev))
            sel.value = prev;
    };
    populate("header-region");
    populate("sim-region");
}
async function loadOverview() {
    overviewData = await api("/api/overview");
    byId("header-db").textContent = `Database: ${overviewData.databaseName} | Região: ${overviewData.activeRegion.country} (${overviewData.activeRegion.regionName})`;
    byId("ov-db").textContent = overviewData.databaseName;
    byId("ov-containers").textContent = String(overviewData.containerCount);
    byId("ov-items").textContent = String(overviewData.totalItems);
    byId("ov-chaos").textContent = overviewData.chaosActive ? "ATIVO" : "Off";
    byId("header-region").value = overviewData.costOptions.Region;
    byId("header-log-cost").checked = !!overviewData.costOptions.LogQueryCost;
    if (!byId("sim-region").value) {
        byId("sim-region").value = overviewData.costOptions.Region;
    }
    const chaosPill = byId("chaos-pill");
    chaosPill.textContent = overviewData.chaosActive ? "Chaos: ATIVO" : "Chaos: Desligado";
    chaosPill.className = overviewData.chaosActive ? "pill chaos" : "pill";
    const grid = byId("containers-grid");
    grid.innerHTML = "";
    const select = byId("exp-container");
    const simSelect = byId("sim-container-import");
    const current = select.value;
    select.innerHTML = "";
    simSelect.innerHTML = `<option value="">-- Selecionar container --</option>`;
    for (const c of overviewData.containers) {
        const opt = document.createElement("option");
        opt.value = c.name;
        opt.textContent = `${c.name} (${c.itemCount})`;
        select.appendChild(opt);
        const simOpt = document.createElement("option");
        simOpt.value = c.name;
        simOpt.textContent = `${c.name} (${c.itemCount} itens, ${c.indexingMode})`;
        simSelect.appendChild(simOpt);
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
async function updateCostSettings() {
    const region = byId("header-region").value;
    const logQueryCost = byId("header-log-cost").checked;
    await api("/api/cost/settings", {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ Region: region, LogQueryCost: logQueryCost })
    });
    toast(`Região: ${region} | LogQueryCost: ${logQueryCost ? "ON" : "OFF"}`);
    await loadOverview();
}
async function loadContainerItems() {
    const name = byId("exp-container").value;
    if (!name)
        return;
    byId("exp-sql").value = "SELECT TOP 50 * FROM c";
    await runQuery();
}
async function runQuery() {
    const name = byId("exp-container").value;
    if (!name)
        return;
    const sql = byId("exp-sql").value;
    const enableScan = byId("exp-scan").checked;
    const errBox = byId("exp-error");
    errBox.style.display = "none";
    try {
        const res = await api(`/api/containers/${encodeURIComponent(name)}/query`, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ sql, enableScan })
        });
        byId("exp-count").textContent = String(res.count);
        byId("exp-ru").textContent = `RU: ${res.totalRu}`;
        renderQueryResults(name, res.items);
    }
    catch (e) {
        const err = e;
        errBox.textContent = `Erro (${err.status ?? 500}): ${err.data?.error ?? JSON.stringify(err.data)}`;
        errBox.style.display = "block";
    }
}
function renderQueryResults(containerName, items) {
    const box = byId("exp-results");
    box.innerHTML = "";
    const cMeta = overviewData?.containers.find(c => c.name === containerName);
    const pkPaths = cMeta?.partitionKeyPaths ?? ["/id"];
    for (const item of items) {
        const wrap = document.createElement("div");
        wrap.className = "result-item";
        const header = document.createElement("div");
        header.className = "row space-between result-item-header";
        const idLabel = document.createElement("span");
        idLabel.className = "result-item-id";
        const itemId = typeof item.id === "string" ? item.id : undefined;
        idLabel.textContent = itemId ? `id: ${itemId}` : "projeção";
        header.appendChild(idLabel);
        if (itemId) {
            const delBtn = document.createElement("button");
            delBtn.className = "btn danger sm";
            delBtn.textContent = "Excluir";
            delBtn.onclick = () => void deleteItem(containerName, itemId, item, pkPaths);
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
            if (cur == null || typeof cur !== "object")
                return null;
            cur = cur[p];
        }
        return cur;
    };
    if (pkPaths.length > 1) {
        return JSON.stringify(pkPaths.map(p => resolvePath(item, p)));
    }
    const val = resolvePath(item, pkPaths[0]) ?? item.id;
    return String(val ?? "");
}
async function deleteItem(containerName, itemId, item, pkPaths) {
    const pk = extractPkParam(item, pkPaths);
    await api(`/api/containers/${encodeURIComponent(containerName)}/items/${encodeURIComponent(itemId)}?pk=${encodeURIComponent(pk)}`, {
        method: "DELETE"
    });
    toast(`Item '${itemId}' removido.`);
    await refreshAll();
}
async function upsertItem() {
    const name = byId("exp-container").value;
    const raw = byId("exp-upsert").value;
    try {
        JSON.parse(raw);
        await api(`/api/containers/${encodeURIComponent(name)}/items`, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: raw
        });
        toast("Item salvo com sucesso.");
        await refreshAll();
    }
    catch (e) {
        const err = e;
        toast("Erro no JSON/Upsert: " + (err.data?.error ?? err.message ?? "inválido"));
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
        if (el)
            el.checked = !!cfg[f];
    }
    for (const f of chaosNumFields) {
        const el = document.getElementById("ch-" + f);
        if (el)
            el.value = String(cfg[f] ?? 0);
    }
}
async function applyChaos() {
    const payload = {};
    for (const f of chaosBoolFields)
        payload[f] = byId("ch-" + f).checked;
    for (const f of chaosNumFields)
        payload[f] = Number(byId("ch-" + f).value);
    await api("/api/chaos", {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(payload)
    });
    toast("Chaos Engineering atualizado em memória!");
    await loadOverview();
}
async function disableAllChaos() {
    for (const f of chaosBoolFields)
        byId("ch-" + f).checked = false;
    await applyChaos();
}
async function loadLogs() {
    const res = await api("/api/logs?take=200");
    cachedLogs = res.items ?? [];
    renderLogs();
}
function renderLogs() {
    const q = (byId("log-filter").value || "").toLowerCase();
    const tbody = byId("logs-body");
    tbody.innerHTML = "";
    for (const entry of cachedLogs) {
        const text = `${entry.method} ${entry.path} ${entry.containerName ?? ""} ${entry.sqlQuery ?? ""} ${entry.statusCode}`.toLowerCase();
        if (q && !text.includes(q))
            continue;
        const scClass = entry.statusCode >= 500 ? "status-5xx" : entry.statusCode >= 400 ? "status-4xx" : "status-2xx";
        const tr = document.createElement("tr");
        tr.innerHTML = `
      <td class="mono-cell">${new Date(entry.timestampUtc).toISOString().substring(11, 23)}</td>
      <td><span class="tag">${entry.method}</span></td>
      <td class="${scClass}">${entry.statusCode}${entry.subStatusCode ? ` (${entry.subStatusCode})` : ""} ${entry.isChaosInjected ? '<span class="pill chaos">CHAOS</span>' : ""}</td>
      <td>${entry.containerName ?? "-"}</td>
      <td class="mono-cell">${entry.requestCharge}</td>
      <td class="mono-cell">$${entry.estimatedCostUsd.toFixed(6)}</td>
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
    byId("cfg-path").textContent = res.path + (res.exists ? "" : " (será criado ao salvar)");
    byId("cfg-editor").value = res.json;
}
function formatConfigJson() {
    const ed = byId("cfg-editor");
    try {
        ed.value = JSON.stringify(JSON.parse(ed.value), null, 2);
    }
    catch (e) {
        toast("JSON inválido: " + e.message);
    }
}
async function saveConfigJson() {
    const json = byId("cfg-editor").value;
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
    }
    catch (e) {
        const err = e;
        toast("Erro ao salvar: " + (err.data?.error ?? err.message ?? "erro"));
    }
}
function importContainerIntoSimulator() {
    const name = byId("sim-container-import").value;
    if (!name || !overviewData)
        return;
    const c = overviewData.containers.find(x => x.name === name);
    if (!c)
        return;
    byId("sim-total-items").value = String(Math.max(1000, c.itemCount));
    byId("sim-pk-paths").value = c.partitionKeyPaths.join(", ");
    byId("sim-indexing-mode").value = c.indexingMode;
    byId("sim-prov-override").value = String(c.throughput);
    void runCostSimulation();
}
function applyCostPreset(preset) {
    if (preset === "efficient") {
        byId("sim-sql").value = "SELECT TOP 100 c.id, c.total, c.status FROM c WHERE c.tenantId = @tenantId AND c.status = 'paid' ORDER BY c.createdAt DESC";
        byId("sim-total-items").value = "100000";
        byId("sim-returned-items").value = "100";
        byId("sim-doc-kb").value = "1";
        byId("sim-partitions").value = "1";
        byId("sim-pk-paths").value = "/tenantId";
        byId("sim-indexing-mode").value = "Consistent";
        byId("sim-queries-month").value = "1000000";
        byId("sim-storage-gb").value = "10";
    }
    else if (preset === "scan") {
        byId("sim-sql").value = "SELECT * FROM c WHERE CONTAINS(UPPER(c.nome), 'MOUSE')";
        byId("sim-total-items").value = "50000";
        byId("sim-returned-items").value = "500";
        byId("sim-doc-kb").value = "16";
        byId("sim-partitions").value = "4";
        byId("sim-pk-paths").value = "/categoria";
        byId("sim-indexing-mode").value = "None";
        byId("sim-queries-month").value = "500000";
        byId("sim-storage-gb").value = "20";
    }
    else {
        byId("sim-sql").value = "SELECT * FROM c WHERE c.status = 'paid'";
        byId("sim-total-items").value = "250000";
        byId("sim-returned-items").value = "1000";
        byId("sim-doc-kb").value = "4";
        byId("sim-partitions").value = "8";
        byId("sim-pk-paths").value = "/tenantId";
        byId("sim-indexing-mode").value = "Consistent";
        byId("sim-queries-month").value = "1000000";
        byId("sim-storage-gb").value = "15";
    }
    void runCostSimulation();
}
async function runCostSimulation() {
    const pkPaths = byId("sim-pk-paths").value
        .split(",")
        .map(s => s.trim())
        .filter(Boolean);
    const payload = {
        region: byId("sim-region").value || "brazil-south",
        sql: byId("sim-sql").value,
        totalItemsInContainer: Number(byId("sim-total-items").value),
        returnedItemsCount: Number(byId("sim-returned-items").value),
        avgDocumentSizeKb: Number(byId("sim-doc-kb").value),
        physicalPartitions: Number(byId("sim-partitions").value),
        partitionKeyPaths: pkPaths.length > 0 ? pkPaths : ["/id"],
        indexingMode: byId("sim-indexing-mode").value,
        queriesPerMonth: Number(byId("sim-queries-month").value),
        storageGb: Number(byId("sim-storage-gb").value),
        provisionedRuOverride: Number(byId("sim-prov-override").value),
        consistencyLevel: byId("sim-consistency").value,
        applyFreeTier: byId("sim-free-tier").checked,
        availabilityZones: byId("sim-az").checked
    };
    const res = await api("/api/cost/simulate", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(payload)
    });
    byId("sim-ru-exec").textContent = `${res.ruBreakdown.totalRuPerExecution} RU`;
    byId("sim-monthly-mru").textContent = `${res.totalMonthlyMillionRus}M RUs`;
    byId("sim-breakeven").textContent = `${res.breakEvenMillionRusFor400Ru}M RUs`;
    byId("sim-vs-eastus").textContent = `${res.regionalServerlessDiffPercentVsBaseline >= 0 ? "+" : ""}${res.regionalServerlessDiffPercentVsBaseline}%`;
    byId("sim-serverless-usd").textContent = `$${res.serverless.totalUsd.toFixed(2)}`;
    byId("sim-serverless-det").textContent = `Proc: $${res.serverless.processingUsd.toFixed(2)} | Storage: $${res.serverless.storageUsd.toFixed(2)}`;
    byId("sim-prov-usd").textContent = `$${res.provisioned.totalUsd.toFixed(2)}`;
    byId("sim-prov-det").textContent = `${res.provisioned.notes} | Proc: $${res.provisioned.processingUsd.toFixed(2)}`;
    byId("sim-auto-usd").textContent = `$${res.autoscale.totalUsd.toFixed(2)} – $${(res.autoscale.maxTotalUsd ?? res.autoscale.totalUsd).toFixed(2)}`;
    byId("sim-auto-det").textContent = res.autoscale.notes;
    byId("sim-baseline-usd").textContent = `$${res.baselineServerless.totalUsd.toFixed(2)} (Serverless) / $${res.baselineProvisioned.totalUsd.toFixed(2)} (Prov.)`;
    const bd = res.ruBreakdown;
    byId("sim-breakdown").innerHTML = `
    <span class="tag">Base: ${bd.baseQueryRu} RU</span>
    <span class="tag">Doc Read (${payload.avgDocumentSizeKb} KB): ${bd.documentReadRuUnit} RU</span>
    <span class="tag">Scan/Retorno: ${bd.resultScanRu} RU</span>
    <span class="tag">Cross-Partition: ${bd.crossPartitionOverheadRu} RU</span>
    <span class="tag">Penalty Índice: ${bd.indexScanPenaltyRu} RU</span>
    <span class="tag">Consistência: ${bd.consistencyMultiplier}x</span>
  `;
    const warnBox = byId("sim-warnings");
    warnBox.innerHTML = res.warnings.length === 0
        ? `<span class="pill ok">Query otimizada: nenhum alerta estrutural detectado.</span>`
        : res.warnings.map(w => `<div class="warn-item">${w}</div>`).join("");
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
byId("exp-sql").addEventListener("keydown", (e) => {
    if (e.ctrlKey && e.key === "Enter")
        void runQuery();
});
window.setInterval(() => {
    if (byId("tab-logs").classList.contains("active") &&
        byId("log-auto").checked) {
        void loadLogs();
    }
}, 1500);
Object.assign(window, {
    switchTab,
    refreshAll,
    resetSeed,
    updateCostSettings,
    loadContainerItems,
    runQuery,
    upsertItem,
    applyChaos,
    disableAllChaos,
    loadLogs,
    renderLogs,
    clearLogs,
    formatConfigJson,
    saveConfigJson,
    importContainerIntoSimulator,
    applyCostPreset,
    runCostSimulation
});
void (async () => {
    await loadRegions();
    await refreshAll();
    await loadLogs();
    await loadConfig();
    await runCostSimulation();
})();
