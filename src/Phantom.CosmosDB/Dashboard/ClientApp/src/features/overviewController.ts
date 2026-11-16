namespace PhantomDashboard.Features {
  import OverviewPayload = PhantomDashboard.Domain.OverviewPayload;
  import DashboardApiClient = PhantomDashboard.Infrastructure.DashboardApiClient;
  import byId = PhantomDashboard.Infrastructure.byId;
  import toast = PhantomDashboard.Infrastructure.toast;

  export class OverviewController {
    private latestOverview: OverviewPayload | null = null;

    public constructor(
      private readonly api: DashboardApiClient,
      private readonly onRefreshDependents: () => Promise<void>
    ) {}

    public get currentOverview(): OverviewPayload | null {
      return this.latestOverview;
    }

    public async loadOverview(): Promise<void> {
      this.latestOverview = await this.api.get<OverviewPayload>("/api/overview");
      const ov = this.latestOverview;

      byId("header-db").textContent =
        `Database: ${ov.databaseName} | Região: ${ov.activeRegion.country} (${ov.activeRegion.regionName})`;
      byId("ov-db").textContent = ov.databaseName;
      byId("ov-containers").textContent = String(ov.containerCount);
      byId("ov-items").textContent = String(ov.totalItems);
      byId("ov-chaos").textContent = ov.chaosActive ? "ATIVO" : "Off";

      byId<HTMLSelectElement>("header-region").value = ov.costOptions.Region;
      byId<HTMLInputElement>("header-log-cost").checked = !!ov.costOptions.LogQueryCost;
      const simRegion = byId<HTMLSelectElement>("sim-region");
      if (!simRegion.value) simRegion.value = ov.costOptions.Region;

      const chaosPill = byId("chaos-pill");
      chaosPill.textContent = ov.chaosActive ? "Chaos: ATIVO" : "Chaos: Desligado";
      chaosPill.className = ov.chaosActive ? "pill chaos" : "pill";

      this.renderContainers(ov);
    }

    public async resetSeed(): Promise<void> {
      await this.api.post("/api/seed/reset");
      toast("Containers limpos e re-populados!");
      await this.loadOverview();
      await this.onRefreshDependents();
    }

    private renderContainers(ov: OverviewPayload): void {
      const grid = byId("containers-grid");
      grid.innerHTML = "";
      const select = byId<HTMLSelectElement>("exp-container");
      const simSelect = byId<HTMLSelectElement>("sim-container-import");
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
}
