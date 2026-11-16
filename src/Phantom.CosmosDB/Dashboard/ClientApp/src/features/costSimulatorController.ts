namespace PhantomDashboard.Features {
  import OverviewPayload = PhantomDashboard.Domain.OverviewPayload;
  import RegionPricing = PhantomDashboard.Domain.RegionPricing;
  import SimulationResult = PhantomDashboard.Domain.SimulationResult;
  import DashboardApiClient = PhantomDashboard.Infrastructure.DashboardApiClient;
  import byId = PhantomDashboard.Infrastructure.byId;
  import toast = PhantomDashboard.Infrastructure.toast;

  export class CostSimulatorController {
    private regions: RegionPricing[] = [];

    public constructor(
      private readonly api: DashboardApiClient,
      private readonly getOverview: () => OverviewPayload | null,
      private readonly onSettingsChanged: () => Promise<void>
    ) {}

    public async loadRegions(): Promise<void> {
      const res = await this.api.get<{ regions: RegionPricing[] }>("/api/cost/regions");
      this.regions = res.regions;
      this.populateRegionSelect("header-region");
      this.populateRegionSelect("sim-region");
    }

    public async updateCostSettings(): Promise<void> {
      const region = byId<HTMLSelectElement>("header-region").value;
      const logQueryCost = byId<HTMLInputElement>("header-log-cost").checked;

      await this.api.put("/api/cost/settings", { Region: region, LogQueryCost: logQueryCost });
      toast(`Região: ${region} | LogQueryCost: ${logQueryCost ? "ON" : "OFF"}`);
      await this.onSettingsChanged();
    }

    public importContainer(): void {
      const name = byId<HTMLSelectElement>("sim-container-import").value;
      const ov = this.getOverview();
      if (!name || !ov) return;
      const c = ov.containers.find(x => x.name === name);
      if (!c) return;

      byId<HTMLInputElement>("sim-total-items").value = String(Math.max(1000, c.itemCount));
      byId<HTMLInputElement>("sim-pk-paths").value = c.partitionKeyPaths.join(", ");
      byId<HTMLSelectElement>("sim-indexing-mode").value = c.indexingMode;
      byId<HTMLInputElement>("sim-prov-override").value = String(c.throughput);
      void this.runSimulation();
    }

    public applyPreset(preset: "efficient" | "scan" | "cross"): void {
      if (preset === "efficient") {
        byId<HTMLTextAreaElement>("sim-sql").value =
          "SELECT TOP 100 c.id, c.total, c.status FROM c WHERE c.tenantId = @tenantId AND c.status = 'paid' ORDER BY c.createdAt DESC";
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
      void this.runSimulation();
    }

    public async runSimulation(): Promise<void> {
      const pkPaths = byId<HTMLInputElement>("sim-pk-paths").value
        .split(",")
        .map(s => s.trim())
        .filter(Boolean);

      const docKb = Number(byId<HTMLInputElement>("sim-doc-kb").value);
      const res = await this.api.post<SimulationResult>("/api/cost/simulate", {
        region: byId<HTMLSelectElement>("sim-region").value || "brazil-south",
        sql: byId<HTMLTextAreaElement>("sim-sql").value,
        totalItemsInContainer: Number(byId<HTMLInputElement>("sim-total-items").value),
        returnedItemsCount: Number(byId<HTMLInputElement>("sim-returned-items").value),
        avgDocumentSizeKb: docKb,
        physicalPartitions: Number(byId<HTMLInputElement>("sim-partitions").value),
        partitionKeyPaths: pkPaths.length > 0 ? pkPaths : ["/id"],
        indexingMode: byId<HTMLSelectElement>("sim-indexing-mode").value,
        queriesPerMonth: Number(byId<HTMLInputElement>("sim-queries-month").value),
        storageGb: Number(byId<HTMLInputElement>("sim-storage-gb").value),
        provisionedRuOverride: Number(byId<HTMLInputElement>("sim-prov-override").value),
        consistencyLevel: byId<HTMLSelectElement>("sim-consistency").value,
        applyFreeTier: byId<HTMLInputElement>("sim-free-tier").checked,
        availabilityZones: byId<HTMLInputElement>("sim-az").checked
      });

      this.renderSimulation(res, docKb);
    }

    private populateRegionSelect(selectId: string): void {
      const sel = byId<HTMLSelectElement>(selectId);
      const prev = sel.value;
      sel.innerHTML = "";
      for (const r of this.regions) {
        const opt = document.createElement("option");
        opt.value = r.id;
        opt.textContent = `${r.country} — ${r.regionName} ($${r.serverlessPerMillionRuUsd.toFixed(3)}/1M)`;
        sel.appendChild(opt);
      }
      if (prev && this.regions.some(r => r.id === prev)) sel.value = prev;
    }

    private renderSimulation(res: SimulationResult, docKb: number): void {
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
}
