"use strict";
var PhantomDashboard;
(function (PhantomDashboard) {
    var Infrastructure;
    (function (Infrastructure) {
        function byId(id) {
            const el = document.getElementById(id);
            if (!el)
                throw new Error(`Element #${id} not found.`);
            return el;
        }
        Infrastructure.byId = byId;
        function toast(msg) {
            const el = byId("toast");
            el.textContent = msg;
            el.style.display = "block";
            window.setTimeout(() => { el.style.display = "none"; }, 2600);
        }
        Infrastructure.toast = toast;
        function extractPartitionKeyParam(item, pkPaths) {
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
        Infrastructure.extractPartitionKeyParam = extractPartitionKeyParam;
    })(Infrastructure = PhantomDashboard.Infrastructure || (PhantomDashboard.Infrastructure = {}));
})(PhantomDashboard || (PhantomDashboard = {}));
var PhantomDashboard;
(function (PhantomDashboard) {
    var Infrastructure;
    (function (Infrastructure) {
        class DashboardApiClient {
            constructor() {
                this.basePath = "__PHANTOM_BASE_PATH__";
            }
            async request(path, opts = {}) {
                const res = await fetch(this.basePath + path, opts);
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
            get(path) {
                return this.request(path);
            }
            post(path, body) {
                return this.request(path, {
                    method: "POST",
                    headers: body !== undefined ? { "Content-Type": "application/json" } : undefined,
                    body: body !== undefined ? (typeof body === "string" ? body : JSON.stringify(body)) : undefined
                });
            }
            put(path, body) {
                return this.request(path, {
                    method: "PUT",
                    headers: { "Content-Type": "application/json" },
                    body: typeof body === "string" ? body : JSON.stringify(body)
                });
            }
            delete(path) {
                return this.request(path, { method: "DELETE" });
            }
        }
        Infrastructure.DashboardApiClient = DashboardApiClient;
    })(Infrastructure = PhantomDashboard.Infrastructure || (PhantomDashboard.Infrastructure = {}));
})(PhantomDashboard || (PhantomDashboard = {}));
var PhantomDashboard;
(function (PhantomDashboard) {
    var Features;
    (function (Features) {
        var byId = PhantomDashboard.Infrastructure.byId;
        var toast = PhantomDashboard.Infrastructure.toast;
        class OverviewController {
            constructor(api, onRefreshDependents) {
                this.api = api;
                this.onRefreshDependents = onRefreshDependents;
                this.latestOverview = null;
            }
            get currentOverview() {
                return this.latestOverview;
            }
            async loadOverview() {
                this.latestOverview = await this.api.get("/api/overview");
                const ov = this.latestOverview;
                byId("header-db").textContent =
                    `Database: ${ov.databaseName} | Região: ${ov.activeRegion.country} (${ov.activeRegion.regionName})`;
                byId("ov-db").textContent = ov.databaseName;
                byId("ov-containers").textContent = String(ov.containerCount);
                byId("ov-items").textContent = String(ov.totalItems);
                byId("ov-chaos").textContent = ov.chaosActive ? "ATIVO" : "Off";
                byId("header-region").value = ov.costOptions.Region;
                byId("header-log-cost").checked = !!ov.costOptions.LogQueryCost;
                const simRegion = byId("sim-region");
                if (!simRegion.value)
                    simRegion.value = ov.costOptions.Region;
                const chaosPill = byId("chaos-pill");
                chaosPill.textContent = ov.chaosActive ? "Chaos: ATIVO" : "Chaos: Desligado";
                chaosPill.className = ov.chaosActive ? "pill chaos" : "pill";
                this.renderContainers(ov);
            }
            async resetSeed() {
                await this.api.post("/api/seed/reset");
                toast("Containers limpos e re-populados!");
                await this.loadOverview();
                await this.onRefreshDependents();
            }
            renderContainers(ov) {
                const grid = byId("containers-grid");
                grid.innerHTML = "";
                const select = byId("exp-container");
                const simSelect = byId("sim-container-import");
                const current = select.value;
                select.innerHTML = "";
                simSelect.innerHTML = `<option value="">-- Selecionar container --</option>`;
                for (const c of ov.containers) {
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
                if (current && ov.containers.some(c => c.name === current)) {
                    select.value = current;
                }
            }
        }
        Features.OverviewController = OverviewController;
    })(Features = PhantomDashboard.Features || (PhantomDashboard.Features = {}));
})(PhantomDashboard || (PhantomDashboard = {}));
var PhantomDashboard;
(function (PhantomDashboard) {
    var Features;
    (function (Features) {
        var byId = PhantomDashboard.Infrastructure.byId;
        var toast = PhantomDashboard.Infrastructure.toast;
        var extractPartitionKeyParam = PhantomDashboard.Infrastructure.extractPartitionKeyParam;
        class ExplorerController {
            constructor(api, getOverview, onMutated) {
                this.api = api;
                this.getOverview = getOverview;
                this.onMutated = onMutated;
            }
            async loadContainerItems() {
                const name = byId("exp-container").value;
                if (!name)
                    return;
                byId("exp-sql").value = "SELECT TOP 50 * FROM c";
                await this.runQuery();
            }
            async runQuery() {
                const name = byId("exp-container").value;
                if (!name)
                    return;
                const sql = byId("exp-sql").value;
                const enableScan = byId("exp-scan").checked;
                const errBox = byId("exp-error");
                errBox.style.display = "none";
                try {
                    const res = await this.api.post(`/api/containers/${encodeURIComponent(name)}/query`, { sql, enableScan });
                    byId("exp-count").textContent = String(res.count);
                    byId("exp-ru").textContent = `RU: ${res.totalRu}`;
                    this.renderQueryResults(name, res.items);
                }
                catch (e) {
                    const err = e;
                    errBox.textContent = `Erro (${err.status ?? 500}): ${err.data?.error ?? JSON.stringify(err.data)}`;
                    errBox.style.display = "block";
                }
            }
            async upsertItem() {
                const name = byId("exp-container").value;
                const raw = byId("exp-upsert").value;
                try {
                    JSON.parse(raw);
                    await this.api.post(`/api/containers/${encodeURIComponent(name)}/items`, raw);
                    toast("Item salvo com sucesso.");
                    await this.onMutated();
                }
                catch (e) {
                    const err = e;
                    toast("Erro no JSON/Upsert: " + (err.data?.error ?? err.message ?? "inválido"));
                }
            }
            async deleteItem(containerName, itemId, item, pkPaths) {
                const pk = extractPartitionKeyParam(item, pkPaths);
                await this.api.delete(`/api/containers/${encodeURIComponent(containerName)}/items/${encodeURIComponent(itemId)}?pk=${encodeURIComponent(pk)}`);
                toast(`Item '${itemId}' removido.`);
                await this.onMutated();
            }
            renderQueryResults(containerName, items) {
                const box = byId("exp-results");
                box.innerHTML = "";
                const cMeta = this.getOverview()?.containers.find(c => c.name === containerName);
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
                        delBtn.onclick = () => void this.deleteItem(containerName, itemId, item, pkPaths);
                        header.appendChild(delBtn);
                    }
                    const pre = document.createElement("pre");
                    pre.textContent = JSON.stringify(item, null, 2);
                    wrap.appendChild(header);
                    wrap.appendChild(pre);
                    box.appendChild(wrap);
                }
            }
        }
        Features.ExplorerController = ExplorerController;
    })(Features = PhantomDashboard.Features || (PhantomDashboard.Features = {}));
})(PhantomDashboard || (PhantomDashboard = {}));
var PhantomDashboard;
(function (PhantomDashboard) {
    var Features;
    (function (Features) {
        var byId = PhantomDashboard.Infrastructure.byId;
        var toast = PhantomDashboard.Infrastructure.toast;
        class CostSimulatorController {
            constructor(api, getOverview, onSettingsChanged) {
                this.api = api;
                this.getOverview = getOverview;
                this.onSettingsChanged = onSettingsChanged;
                this.regions = [];
            }
            async loadRegions() {
                const res = await this.api.get("/api/cost/regions");
                this.regions = res.regions;
                this.populateRegionSelect("header-region");
                this.populateRegionSelect("sim-region");
            }
            async updateCostSettings() {
                const region = byId("header-region").value;
                const logQueryCost = byId("header-log-cost").checked;
                await this.api.put("/api/cost/settings", { Region: region, LogQueryCost: logQueryCost });
                toast(`Região: ${region} | LogQueryCost: ${logQueryCost ? "ON" : "OFF"}`);
                await this.onSettingsChanged();
            }
            importContainer() {
                const name = byId("sim-container-import").value;
                const ov = this.getOverview();
                if (!name || !ov)
                    return;
                const c = ov.containers.find(x => x.name === name);
                if (!c)
                    return;
                byId("sim-total-items").value = String(Math.max(1000, c.itemCount));
                byId("sim-pk-paths").value = c.partitionKeyPaths.join(", ");
                byId("sim-indexing-mode").value = c.indexingMode;
                byId("sim-prov-override").value = String(c.throughput);
                void this.runSimulation();
            }
            applyPreset(preset) {
                if (preset === "efficient") {
                    byId("sim-sql").value =
                        "SELECT TOP 100 c.id, c.total, c.status FROM c WHERE c.tenantId = @tenantId AND c.status = 'paid' ORDER BY c.createdAt DESC";
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
                void this.runSimulation();
            }
            async runSimulation() {
                const pkPaths = byId("sim-pk-paths").value
                    .split(",")
                    .map(s => s.trim())
                    .filter(Boolean);
                const docKb = Number(byId("sim-doc-kb").value);
                const res = await this.api.post("/api/cost/simulate", {
                    region: byId("sim-region").value || "brazil-south",
                    sql: byId("sim-sql").value,
                    totalItemsInContainer: Number(byId("sim-total-items").value),
                    returnedItemsCount: Number(byId("sim-returned-items").value),
                    avgDocumentSizeKb: docKb,
                    physicalPartitions: Number(byId("sim-partitions").value),
                    partitionKeyPaths: pkPaths.length > 0 ? pkPaths : ["/id"],
                    indexingMode: byId("sim-indexing-mode").value,
                    queriesPerMonth: Number(byId("sim-queries-month").value),
                    storageGb: Number(byId("sim-storage-gb").value),
                    provisionedRuOverride: Number(byId("sim-prov-override").value),
                    consistencyLevel: byId("sim-consistency").value,
                    applyFreeTier: byId("sim-free-tier").checked,
                    availabilityZones: byId("sim-az").checked
                });
                this.renderSimulation(res, docKb);
            }
            populateRegionSelect(selectId) {
                const sel = byId(selectId);
                const prev = sel.value;
                sel.innerHTML = "";
                for (const r of this.regions) {
                    const opt = document.createElement("option");
                    opt.value = r.id;
                    opt.textContent = `${r.country} — ${r.regionName} ($${r.serverlessPerMillionRuUsd.toFixed(3)}/1M)`;
                    sel.appendChild(opt);
                }
                if (prev && this.regions.some(r => r.id === prev))
                    sel.value = prev;
            }
            renderSimulation(res, docKb) {
                byId("sim-ru-exec").textContent = `${res.ruBreakdown.totalRuPerExecution} RU`;
                byId("sim-monthly-mru").textContent = `${res.totalMonthlyMillionRus}M RUs`;
                byId("sim-breakeven").textContent = `${res.breakEvenMillionRusFor400Ru}M RUs`;
                const sign = res.regionalServerlessDiffPercentVsBaseline >= 0 ? "+" : "";
                byId("sim-vs-eastus").textContent = `${sign}${res.regionalServerlessDiffPercentVsBaseline}%`;
                byId("sim-serverless-usd").textContent = `$${res.serverless.totalUsd.toFixed(2)}`;
                byId("sim-serverless-det").textContent =
                    `Proc: $${res.serverless.processingUsd.toFixed(2)} | Storage: $${res.serverless.storageUsd.toFixed(2)}`;
                byId("sim-prov-usd").textContent = `$${res.provisioned.totalUsd.toFixed(2)}`;
                byId("sim-prov-det").textContent =
                    `${res.provisioned.notes} | Proc: $${res.provisioned.processingUsd.toFixed(2)}`;
                byId("sim-auto-usd").textContent =
                    `$${res.autoscale.totalUsd.toFixed(2)} – $${(res.autoscale.maxTotalUsd ?? res.autoscale.totalUsd).toFixed(2)}`;
                byId("sim-auto-det").textContent = res.autoscale.notes;
                byId("sim-baseline-usd").textContent =
                    `$${res.baselineServerless.totalUsd.toFixed(2)} (Serverless) / $${res.baselineProvisioned.totalUsd.toFixed(2)} (Prov.)`;
                const bd = res.ruBreakdown;
                byId("sim-breakdown").innerHTML = `
        <span class="tag">Base: ${bd.baseQueryRu} RU</span>
        <span class="tag">Doc Read (${docKb} KB): ${bd.documentReadRuUnit} RU</span>
        <span class="tag">Scan/Retorno: ${bd.resultScanRu} RU</span>
        <span class="tag">Cross-Partition: ${bd.crossPartitionOverheadRu} RU</span>
        <span class="tag">Penalty Índice: ${bd.indexScanPenaltyRu} RU</span>
        <span class="tag">Consistência: ${bd.consistencyMultiplier}x</span>
      `;
                byId("sim-warnings").innerHTML = res.warnings.length === 0
                    ? `<span class="pill ok">Query otimizada: nenhum alerta estrutural detectado.</span>`
                    : res.warnings.map(w => `<div class="warn-item">${w}</div>`).join("");
            }
        }
        Features.CostSimulatorController = CostSimulatorController;
    })(Features = PhantomDashboard.Features || (PhantomDashboard.Features = {}));
})(PhantomDashboard || (PhantomDashboard = {}));
var PhantomDashboard;
(function (PhantomDashboard) {
    var Features;
    (function (Features) {
        var byId = PhantomDashboard.Infrastructure.byId;
        var toast = PhantomDashboard.Infrastructure.toast;
        class ChaosController {
            constructor(api, onUpdated) {
                this.api = api;
                this.onUpdated = onUpdated;
            }
            async load() {
                const cfg = await this.api.get("/api/chaos");
                for (const f of ChaosController.BoolFields) {
                    const el = document.getElementById("ch-" + f);
                    if (el)
                        el.checked = !!cfg[f];
                }
                for (const f of ChaosController.NumFields) {
                    const el = document.getElementById("ch-" + f);
                    if (el)
                        el.value = String(cfg[f] ?? 0);
                }
            }
            async apply() {
                const payload = {};
                for (const f of ChaosController.BoolFields) {
                    payload[f] = byId("ch-" + f).checked;
                }
                for (const f of ChaosController.NumFields) {
                    payload[f] = Number(byId("ch-" + f).value);
                }
                await this.api.put("/api/chaos", payload);
                toast("Chaos Engineering atualizado em memória!");
                await this.onUpdated();
            }
            async disableAll() {
                for (const f of ChaosController.BoolFields) {
                    byId("ch-" + f).checked = false;
                }
                await this.apply();
            }
        }
        ChaosController.BoolFields = [
            "EnableRandomThrottling", "SimulateHighLatency",
            "Simulate429_TooManyRequests", "Simulate503_ServiceUnavailable",
            "Simulate408_RequestTimeout", "Simulate403_Forbidden",
            "Simulate401_Unauthorized", "Simulate409_Conflict",
            "Simulate413_EntityTooLarge", "Simulate412_PreconditionFailed",
            "Simulate400_BadRequest"
        ];
        ChaosController.NumFields = [
            "ThrottlingProbability", "MinLatencyMs", "MaxLatencyMs", "RetryAfterMs"
        ];
        Features.ChaosController = ChaosController;
    })(Features = PhantomDashboard.Features || (PhantomDashboard.Features = {}));
})(PhantomDashboard || (PhantomDashboard = {}));
var PhantomDashboard;
(function (PhantomDashboard) {
    var Features;
    (function (Features) {
        var byId = PhantomDashboard.Infrastructure.byId;
        var toast = PhantomDashboard.Infrastructure.toast;
        class LogsController {
            constructor(api) {
                this.api = api;
                this.cachedLogs = [];
            }
            async load() {
                const res = await this.api.get("/api/logs?take=200");
                this.cachedLogs = res.items ?? [];
                this.render();
            }
            render() {
                const q = (byId("log-filter").value || "").toLowerCase();
                const tbody = byId("logs-body");
                tbody.innerHTML = "";
                for (const entry of this.cachedLogs) {
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
            async clear() {
                await this.api.delete("/api/logs");
                this.cachedLogs = [];
                this.render();
                toast("Logs limpos.");
            }
        }
        Features.LogsController = LogsController;
    })(Features = PhantomDashboard.Features || (PhantomDashboard.Features = {}));
})(PhantomDashboard || (PhantomDashboard = {}));
var PhantomDashboard;
(function (PhantomDashboard) {
    var Features;
    (function (Features) {
        var byId = PhantomDashboard.Infrastructure.byId;
        var toast = PhantomDashboard.Infrastructure.toast;
        class ConfigController {
            constructor(api, onSaved) {
                this.api = api;
                this.onSaved = onSaved;
            }
            async load() {
                const res = await this.api.get("/api/config");
                byId("cfg-path").textContent = res.path + (res.exists ? "" : " (será criado ao salvar)");
                byId("cfg-editor").value = res.json;
            }
            format() {
                const ed = byId("cfg-editor");
                try {
                    ed.value = JSON.stringify(JSON.parse(ed.value), null, 2);
                }
                catch (e) {
                    toast("JSON inválido: " + e.message);
                }
            }
            async save() {
                const json = byId("cfg-editor").value;
                try {
                    JSON.parse(json);
                    const res = await this.api.put("/api/config", { json });
                    toast("Salvo em: " + res.path);
                    await this.onSaved();
                }
                catch (e) {
                    const err = e;
                    toast("Erro ao salvar: " + (err.data?.error ?? err.message ?? "erro"));
                }
            }
        }
        Features.ConfigController = ConfigController;
    })(Features = PhantomDashboard.Features || (PhantomDashboard.Features = {}));
})(PhantomDashboard || (PhantomDashboard = {}));
var PhantomDashboard;
(function (PhantomDashboard) {
    var DashboardApiClient = PhantomDashboard.Infrastructure.DashboardApiClient;
    var byId = PhantomDashboard.Infrastructure.byId;
    var OverviewController = PhantomDashboard.Features.OverviewController;
    var ExplorerController = PhantomDashboard.Features.ExplorerController;
    var CostSimulatorController = PhantomDashboard.Features.CostSimulatorController;
    var ChaosController = PhantomDashboard.Features.ChaosController;
    var LogsController = PhantomDashboard.Features.LogsController;
    var ConfigController = PhantomDashboard.Features.ConfigController;
    const api = new DashboardApiClient();
    const overview = new OverviewController(api, async () => {
        await explorer.runQuery();
        await chaos.load();
    });
    const explorer = new ExplorerController(api, () => overview.currentOverview, async () => {
        await refreshAll();
    });
    const cost = new CostSimulatorController(api, () => overview.currentOverview, async () => {
        await overview.loadOverview();
    });
    const chaos = new ChaosController(api, async () => {
        await overview.loadOverview();
    });
    const logs = new LogsController(api);
    const config = new ConfigController(api, async () => {
        await chaos.load();
        await overview.loadOverview();
    });
    async function refreshAll() {
        await overview.loadOverview();
        await explorer.runQuery();
        await chaos.load();
    }
    function switchTab(name, btn) {
        document.querySelectorAll(".tab").forEach(t => t.classList.remove("active"));
        document.querySelectorAll("nav button").forEach(b => b.classList.remove("active"));
        byId("tab-" + name).classList.add("active");
        btn.classList.add("active");
        if (name === "logs")
            void logs.load();
        if (name === "config")
            void config.load();
        if (name === "chaos")
            void chaos.load();
        if (name === "cost")
            void cost.runSimulation();
    }
    byId("exp-sql").addEventListener("keydown", (e) => {
        if (e.ctrlKey && e.key === "Enter")
            void explorer.runQuery();
    });
    window.setInterval(() => {
        if (byId("tab-logs").classList.contains("active") &&
            byId("log-auto").checked) {
            void logs.load();
        }
    }, 1500);
    Object.assign(window, {
        switchTab,
        refreshAll,
        resetSeed: () => overview.resetSeed(),
        updateCostSettings: () => cost.updateCostSettings(),
        loadContainerItems: () => explorer.loadContainerItems(),
        runQuery: () => explorer.runQuery(),
        upsertItem: () => explorer.upsertItem(),
        applyChaos: () => chaos.apply(),
        disableAllChaos: () => chaos.disableAll(),
        loadLogs: () => logs.load(),
        renderLogs: () => logs.render(),
        clearLogs: () => logs.clear(),
        formatConfigJson: () => config.format(),
        saveConfigJson: () => config.save(),
        importContainerIntoSimulator: () => cost.importContainer(),
        applyCostPreset: (p) => cost.applyPreset(p),
        runCostSimulation: () => cost.runSimulation()
    });
    void (async () => {
        await cost.loadRegions();
        await refreshAll();
        await logs.load();
        await config.load();
        await cost.runSimulation();
    })();
})(PhantomDashboard || (PhantomDashboard = {}));
