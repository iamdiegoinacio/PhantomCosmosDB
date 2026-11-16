interface ContainerOverview {
  name: string;
  itemCount: number;
  throughput: number;
  defaultTtl?: number | null;
  partitionKeyPaths: string[];
  indexingMode: string;
  automaticIndexing: boolean;
  compositeIndexCount: number;
  uniqueKeys: string[][];
  computedProperties: { name: string; query: string }[];
}

interface RegionPricing {
  id: string;
  country: string;
  regionName: string;
  flag: string;
  monthly1000RuProvisionedUsd: number;
  serverlessPerMillionRuUsd: number;
  storagePerGbMonthUsd: number;
  provisionedPer100RuPerHourUsd: number;
}

interface CostOptions {
  Region: string;
  BillingMode: string;
  LogQueryCost: boolean;
  ApplyFreeTier: boolean;
  ConsistencyLevel: string;
  AvailabilityZones: boolean;
  AdditionalRegionsCount: number;
  MultiRegionWrites: boolean;
}

interface OverviewPayload {
  databaseName: string;
  configFilePath?: string | null;
  totalItems: number;
  containerCount: number;
  chaosActive: boolean;
  costOptions: CostOptions;
  activeRegion: RegionPricing;
  containers: ContainerOverview[];
}

interface LogEntry {
  sequence: number;
  timestampUtc: string;
  method: string;
  path: string;
  containerName?: string | null;
  statusCode: number;
  subStatusCode: number;
  requestCharge: number;
  estimatedCostUsd: number;
  regionId: string;
  durationMs: number;
  sqlQuery?: string | null;
  isChaosInjected: boolean;
}

interface QueryCostBreakdown {
  baseQueryRu: number;
  documentReadRuUnit: number;
  resultScanRu: number;
  crossPartitionOverheadRu: number;
  indexScanPenaltyRu: number;
  consistencyMultiplier: number;
  totalRuPerExecution: number;
  isCrossPartition: boolean;
  isFullScan: boolean;
}

interface MonthlyCostEstimate {
  model: string;
  processingUsd: number;
  storageUsd: number;
  totalUsd: number;
  maxTotalUsd?: number | null;
  notes: string;
}

interface SimulationResult {
  region: RegionPricing;
  baselineRegion: RegionPricing;
  ruBreakdown: QueryCostBreakdown;
  totalMonthlyRus: number;
  totalMonthlyMillionRus: number;
  averageRuPerSecond: number;
  recommendedProvisionedRu: number;
  breakEvenMillionRusFor400Ru: number;
  regionalServerlessDiffPercentVsBaseline: number;
  regionalProvisionedDiffPercentVsBaseline: number;
  serverless: MonthlyCostEstimate;
  provisioned: MonthlyCostEstimate;
  autoscale: MonthlyCostEstimate;
  baselineServerless: MonthlyCostEstimate;
  baselineProvisioned: MonthlyCostEstimate;
  warnings: string[];
}

const BASE = "__PHANTOM_BASE_PATH__";
let cachedLogs: LogEntry[] = [];
let overviewData: OverviewPayload | null = null;
let regionsCatalog: RegionPricing[] = [];

function byId<T extends HTMLElement>(id: string): T {
  const el = document.getElementById(id);
  if (!el) throw new Error(`Element #${id} not found.`);
  return el as T;
}

function toast(msg: string): void {
  const el = byId<HTMLDivElement>("toast");
  el.textContent = msg;
  el.style.display = "block";
  window.setTimeout(() => { el.style.display = "none"; }, 2600);
}

async function api<T>(path: string, opts: RequestInit = {}): Promise<T> {
  const res = await fetch(BASE + path, opts);
  const text = await res.text();
  let data: unknown = text;
  try { data = JSON.parse(text); } catch { /* plain text */ }
  if (!res.ok) throw { status: res.status, data };
  return data as T;
}

function switchTab(name: string, btn: HTMLButtonElement): void {
  document.querySelectorAll(".tab").forEach(t => t.classList.remove("active"));
  document.querySelectorAll("nav button").forEach(b => b.classList.remove("active"));
  byId("tab-" + name).classList.add("active");
  btn.classList.add("active");
  if (name === "logs") void loadLogs();
  if (name === "config") void loadConfig();
  if (name === "chaos") void loadChaos();
  if (name === "cost") void runCostSimulation();
}

async function loadRegions(): Promise<void> {
  const res = await api<{ regions: RegionPricing[] }>("/api/cost/regions");
  regionsCatalog = res.regions;

  const populate = (selectId: string) => {
    const sel = byId<HTMLSelectElement>(selectId);
    const prev = sel.value;
    sel.innerHTML = "";
    for (const r of regionsCatalog) {
      const opt = document.createElement("option");
      opt.value = r.id;
      opt.textContent = `${r.country} — ${r.regionName} ($${r.serverlessPerMillionRuUsd.toFixed(3)}/1M)`;
      sel.appendChild(opt);
    }
    if (prev && regionsCatalog.some(r => r.id === prev)) sel.value = prev;
  };

  populate("header-region");
  populate("sim-region");
}

async function loadOverview(): Promise<void> {
  overviewData = await api<OverviewPayload>("/api/overview");
  byId("header-db").textContent = `Database: ${overviewData.databaseName} | Região: ${overviewData.activeRegion.country} (${overviewData.activeRegion.regionName})`;
  byId("ov-db").textContent = overviewData.databaseName;
  byId("ov-containers").textContent = String(overviewData.containerCount);
  byId("ov-items").textContent = String(overviewData.totalItems);
  byId("ov-chaos").textContent = overviewData.chaosActive ? "ATIVO" : "Off";

  byId<HTMLSelectElement>("header-region").value = overviewData.costOptions.Region;
  byId<HTMLInputElement>("header-log-cost").checked = !!overviewData.costOptions.LogQueryCost;
  if (!byId<HTMLSelectElement>("sim-region").value) {
    byId<HTMLSelectElement>("sim-region").value = overviewData.costOptions.Region;
  }

  const chaosPill = byId("chaos-pill");
  chaosPill.textContent = overviewData.chaosActive ? "Chaos: ATIVO" : "Chaos: Desligado";
  chaosPill.className = overviewData.chaosActive ? "pill chaos" : "pill";

  const grid = byId("containers-grid");
  grid.innerHTML = "";
  const select = byId<HTMLSelectElement>("exp-container");
  const simSelect = byId<HTMLSelectElement>("sim-container-import");
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

async function updateCostSettings(): Promise<void> {
  const region = byId<HTMLSelectElement>("header-region").value;
  const logQueryCost = byId<HTMLInputElement>("header-log-cost").checked;

  await api("/api/cost/settings", {
    method: "PUT",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ Region: region, LogQueryCost: logQueryCost })
  });

  toast(`Região: ${region} | LogQueryCost: ${logQueryCost ? "ON" : "OFF"}`);
  await loadOverview();
}

async function loadContainerItems(): Promise<void> {
  const name = byId<HTMLSelectElement>("exp-container").value;
  if (!name) return;
  byId<HTMLTextAreaElement>("exp-sql").value = "SELECT TOP 50 * FROM c";
  await runQuery();
}

async function runQuery(): Promise<void> {
  const name = byId<HTMLSelectElement>("exp-container").value;
  if (!name) return;
  const sql = byId<HTMLTextAreaElement>("exp-sql").value;
  const enableScan = byId<HTMLInputElement>("exp-scan").checked;
  const errBox = byId("exp-error");
  errBox.style.display = "none";

  try {
    const res = await api<{ count: number; totalRu: number; items: Record<string, unknown>[] }>(
      `/api/containers/${encodeURIComponent(name)}/query`,
      {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ sql, enableScan })
      }
    );
    byId("exp-count").textContent = String(res.count);
    byId("exp-ru").textContent = `RU: ${res.totalRu}`;
    renderQueryResults(name, res.items);
  } catch (e: unknown) {
    const err = e as { status?: number; data?: { error?: string } };
    errBox.textContent = `Erro (${err.status ?? 500}): ${err.data?.error ?? JSON.stringify(err.data)}`;
    errBox.style.display = "block";
  }
}

function renderQueryResults(containerName: string, items: Record<string, unknown>[]): void {
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

function extractPkParam(item: Record<string, unknown>, pkPaths: string[]): string {
  const resolvePath = (obj: Record<string, unknown>, path: string): unknown => {
    const parts = path.replace(/^\//, "").split("/");
    let cur: unknown = obj;
    for (const p of parts) {
      if (cur == null || typeof cur !== "object") return null;
      cur = (cur as Record<string, unknown>)[p];
    }
    return cur;
  };
  if (pkPaths.length > 1) {
    return JSON.stringify(pkPaths.map(p => resolvePath(item, p)));
  }
  const val = resolvePath(item, pkPaths[0]) ?? item.id;
  return String(val ?? "");
}

async function deleteItem(containerName: string, itemId: string, item: Record<string, unknown>, pkPaths: string[]): Promise<void> {
  const pk = extractPkParam(item, pkPaths);
  await api(`/api/containers/${encodeURIComponent(containerName)}/items/${encodeURIComponent(itemId)}?pk=${encodeURIComponent(pk)}`, {
    method: "DELETE"
  });
  toast(`Item '${itemId}' removido.`);
  await refreshAll();
}

async function upsertItem(): Promise<void> {
  const name = byId<HTMLSelectElement>("exp-container").value;
  const raw = byId<HTMLTextAreaElement>("exp-upsert").value;
  try {
    JSON.parse(raw);
    await api(`/api/containers/${encodeURIComponent(name)}/items`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: raw
    });
    toast("Item salvo com sucesso.");
    await refreshAll();
  } catch (e: unknown) {
    const err = e as { data?: { error?: string }; message?: string };
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

async function loadChaos(): Promise<void> {
  const cfg = await api<Record<string, unknown>>("/api/chaos");
  for (const f of chaosBoolFields) {
    const el = document.getElementById("ch-" + f) as HTMLInputElement | null;
    if (el) el.checked = !!cfg[f];
  }
  for (const f of chaosNumFields) {
    const el = document.getElementById("ch-" + f) as HTMLInputElement | null;
    if (el) el.value = String(cfg[f] ?? 0);
  }
}

async function applyChaos(): Promise<void> {
  const payload: Record<string, unknown> = {};
  for (const f of chaosBoolFields) payload[f] = byId<HTMLInputElement>("ch-" + f).checked;
  for (const f of chaosNumFields) payload[f] = Number(byId<HTMLInputElement>("ch-" + f).value);
  await api("/api/chaos", {
    method: "PUT",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(payload)
  });
  toast("Chaos Engineering atualizado em memória!");
  await loadOverview();
}

async function disableAllChaos(): Promise<void> {
  for (const f of chaosBoolFields) byId<HTMLInputElement>("ch-" + f).checked = false;
  await applyChaos();
}

async function loadLogs(): Promise<void> {
  const res = await api<{ items: LogEntry[] }>("/api/logs?take=200");
  cachedLogs = res.items ?? [];
  renderLogs();
}

function renderLogs(): void {
  const q = (byId<HTMLInputElement>("log-filter").value || "").toLowerCase();
  const tbody = byId("logs-body");
  tbody.innerHTML = "";
  for (const entry of cachedLogs) {
    const text = `${entry.method} ${entry.path} ${entry.containerName ?? ""} ${entry.sqlQuery ?? ""} ${entry.statusCode}`.toLowerCase();
    if (q && !text.includes(q)) continue;
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

async function clearLogs(): Promise<void> {
  await api("/api/logs", { method: "DELETE" });
  cachedLogs = [];
  renderLogs();
  toast("Logs limpos.");
}

async function loadConfig(): Promise<void> {
  const res = await api<{ path: string; exists: boolean; json: string }>("/api/config");
  byId("cfg-path").textContent = res.path + (res.exists ? "" : " (será criado ao salvar)");
  byId<HTMLTextAreaElement>("cfg-editor").value = res.json;
}

function formatConfigJson(): void {
  const ed = byId<HTMLTextAreaElement>("cfg-editor");
  try {
    ed.value = JSON.stringify(JSON.parse(ed.value), null, 2);
  } catch (e: unknown) {
    toast("JSON inválido: " + (e as Error).message);
  }
}

async function saveConfigJson(): Promise<void> {
  const json = byId<HTMLTextAreaElement>("cfg-editor").value;
  try {
    JSON.parse(json);
    const res = await api<{ saved: boolean; path: string }>("/api/config", {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ json })
    });
    toast("Salvo em: " + res.path);
    await loadChaos();
    await loadOverview();
  } catch (e: unknown) {
    const err = e as { data?: { error?: string }; message?: string };
    toast("Erro ao salvar: " + (err.data?.error ?? err.message ?? "erro"));
  }
}

function importContainerIntoSimulator(): void {
  const name = byId<HTMLSelectElement>("sim-container-import").value;
  if (!name || !overviewData) return;
  const c = overviewData.containers.find(x => x.name === name);
  if (!c) return;

  byId<HTMLInputElement>("sim-total-items").value = String(Math.max(1000, c.itemCount));
  byId<HTMLInputElement>("sim-pk-paths").value = c.partitionKeyPaths.join(", ");
  byId<HTMLSelectElement>("sim-indexing-mode").value = c.indexingMode;
  byId<HTMLInputElement>("sim-prov-override").value = String(c.throughput);
  void runCostSimulation();
}

function applyCostPreset(preset: "efficient" | "scan" | "cross"): void {
  if (preset === "efficient") {
    byId<HTMLTextAreaElement>("sim-sql").value = "SELECT TOP 100 c.id, c.total, c.status FROM c WHERE c.tenantId = @tenantId AND c.status = 'paid' ORDER BY c.createdAt DESC";
    byId<HTMLInputElement>("sim-total-items").value = "100000";
    byId<HTMLInputElement>("sim-returned-items").value = "100";
    byId<HTMLInputElement>("sim-doc-kb").value = "1";
    byId<HTMLInputElement>("sim-partitions").value = "1";
    byId<HTMLInputElement>("sim-pk-paths").value = "/tenantId";
    byId<HTMLSelectElement>("sim-indexing-mode").value = "Consistent";
    byId<HTMLInputElement>("sim-queries-month").value = "1000000";
    byId<HTMLInputElement>("sim-storage-gb").value = "10";
  } else if (preset === "scan") {
    byId<HTMLTextAreaElement>("sim-sql").value = "SELECT * FROM c WHERE CONTAINS(UPPER(c.nome), 'MOUSE')";
    byId<HTMLInputElement>("sim-total-items").value = "50000";
    byId<HTMLInputElement>("sim-returned-items").value = "500";
    byId<HTMLInputElement>("sim-doc-kb").value = "16";
    byId<HTMLInputElement>("sim-partitions").value = "4";
    byId<HTMLInputElement>("sim-pk-paths").value = "/categoria";
    byId<HTMLSelectElement>("sim-indexing-mode").value = "None";
    byId<HTMLInputElement>("sim-queries-month").value = "500000";
    byId<HTMLInputElement>("sim-storage-gb").value = "20";
  } else {
    byId<HTMLTextAreaElement>("sim-sql").value = "SELECT * FROM c WHERE c.status = 'paid'";
    byId<HTMLInputElement>("sim-total-items").value = "250000";
    byId<HTMLInputElement>("sim-returned-items").value = "1000";
    byId<HTMLInputElement>("sim-doc-kb").value = "4";
    byId<HTMLInputElement>("sim-partitions").value = "8";
    byId<HTMLInputElement>("sim-pk-paths").value = "/tenantId";
    byId<HTMLSelectElement>("sim-indexing-mode").value = "Consistent";
    byId<HTMLInputElement>("sim-queries-month").value = "1000000";
    byId<HTMLInputElement>("sim-storage-gb").value = "15";
  }
  void runCostSimulation();
}

async function runCostSimulation(): Promise<void> {
  const pkPaths = byId<HTMLInputElement>("sim-pk-paths").value
    .split(",")
    .map(s => s.trim())
    .filter(Boolean);

  const payload = {
    region: byId<HTMLSelectElement>("sim-region").value || "brazil-south",
    sql: byId<HTMLTextAreaElement>("sim-sql").value,
    totalItemsInContainer: Number(byId<HTMLInputElement>("sim-total-items").value),
    returnedItemsCount: Number(byId<HTMLInputElement>("sim-returned-items").value),
    avgDocumentSizeKb: Number(byId<HTMLInputElement>("sim-doc-kb").value),
    physicalPartitions: Number(byId<HTMLInputElement>("sim-partitions").value),
    partitionKeyPaths: pkPaths.length > 0 ? pkPaths : ["/id"],
    indexingMode: byId<HTMLSelectElement>("sim-indexing-mode").value,
    queriesPerMonth: Number(byId<HTMLInputElement>("sim-queries-month").value),
    storageGb: Number(byId<HTMLInputElement>("sim-storage-gb").value),
    provisionedRuOverride: Number(byId<HTMLInputElement>("sim-prov-override").value),
    consistencyLevel: byId<HTMLSelectElement>("sim-consistency").value,
    applyFreeTier: byId<HTMLInputElement>("sim-free-tier").checked,
    availabilityZones: byId<HTMLInputElement>("sim-az").checked
  };

  const res = await api<SimulationResult>("/api/cost/simulate", {
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

async function resetSeed(): Promise<void> {
  await api("/api/seed/reset", { method: "POST" });
  toast("Containers limpos e re-populados!");
  await refreshAll();
}

async function refreshAll(): Promise<void> {
  await loadOverview();
  await runQuery();
  await loadChaos();
}

byId("exp-sql").addEventListener("keydown", (e: KeyboardEvent) => {
  if (e.ctrlKey && e.key === "Enter") void runQuery();
});

window.setInterval(() => {
  if (byId("tab-logs").classList.contains("active") &&
      byId<HTMLInputElement>("log-auto").checked) {
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
