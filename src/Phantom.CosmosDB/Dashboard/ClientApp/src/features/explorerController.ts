namespace PhantomDashboard.Features {
  import OverviewPayload = PhantomDashboard.Domain.OverviewPayload;
  import DashboardApiClient = PhantomDashboard.Infrastructure.DashboardApiClient;
  import byId = PhantomDashboard.Infrastructure.byId;
  import toast = PhantomDashboard.Infrastructure.toast;
  import extractPartitionKeyParam = PhantomDashboard.Infrastructure.extractPartitionKeyParam;

  export class ExplorerController {
    private selectedContainer = "";
    private selectedItemId: string | null = null;
    private selectedItem: Record<string, unknown> | null = null;
    private selectedPkPaths: string[] = ["/id"];

    public constructor(
      private readonly api: DashboardApiClient,
      private readonly getOverview: () => OverviewPayload | null,
      private readonly onMutated: () => Promise<void>
    ) {}

    public async loadContainerItems(): Promise<void> {
      const name = byId<HTMLSelectElement>("exp-container").value;
      if (!name) return;
      this.hideContainerSettings();
      byId<HTMLTextAreaElement>("exp-sql").value = "SELECT TOP 50 * FROM c";
      byId<HTMLTextAreaElement>("exp-params").value = "";
      await this.runQuery();
    }

    public applyGeoPreset(): void {
      const preset = byId<HTMLSelectElement>("exp-geo-preset").value;
      if (!preset) return;
      const sqlEl = byId<HTMLTextAreaElement>("exp-sql");
      const paramsEl = byId<HTMLTextAreaElement>("exp-params");

      if (preset === "nearby") {
        sqlEl.value =
          "SELECT TOP 10 c.id, c.status, ST_DISTANCE(c.location, @point) AS distanceMeters FROM c WHERE ST_DISTANCE(c.location, @point) <= @radius ORDER BY ST_DISTANCE(c.location, @point) ASC";
        paramsEl.value = JSON.stringify(
          {
            "@point": { type: "Point", coordinates: [-43.2096, -22.9035] },
            "@radius": 5000
          },
          null,
          2
        );
      } else if (preset === "within") {
        sqlEl.value = "SELECT c.id, c.location FROM c WHERE ST_WITHIN(c.location, @serviceArea)";
        paramsEl.value = JSON.stringify(
          {
            "@serviceArea": {
              type: "Polygon",
              coordinates: [[[-43.25, -22.95], [-43.17, -22.95], [-43.17, -22.87], [-43.25, -22.87], [-43.25, -22.95]]]
            }
          },
          null,
          2
        );
      } else if (preset === "intersects") {
        sqlEl.value = "SELECT c.id, c.route FROM c WHERE ST_INTERSECTS(c.route, @restrictedArea)";
        paramsEl.value = JSON.stringify(
          {
            "@restrictedArea": {
              type: "Polygon",
              coordinates: [[[-43.22, -22.92], [-43.19, -22.92], [-43.19, -22.89], [-43.22, -22.89], [-43.22, -22.92]]]
            }
          },
          null,
          2
        );
      } else if (preset === "area") {
        sqlEl.value = "SELECT c.id, c.name, ST_AREA(c.boundary) / 1000000 AS areaKm2 FROM c WHERE IS_DEFINED(c.boundary)";
        paramsEl.value = "";
      } else if (preset === "invalid") {
        sqlEl.value =
          "SELECT c.id, ST_ISVALIDDETAILED(c.location) AS validation FROM c WHERE IS_DEFINED(c.location) AND NOT ST_ISVALID(c.location)";
        paramsEl.value = "";
      }
    }

    public async runQuery(): Promise<void> {
      const name = byId<HTMLSelectElement>("exp-container").value;
      if (!name) return;
      this.selectedContainer = name;
      const sql = byId<HTMLTextAreaElement>("exp-sql").value;
      const enableScan = byId<HTMLInputElement>("exp-scan").checked;
      const populateIndexMetrics = byId<HTMLInputElement>("exp-index-metrics").checked;
      const rawParams = byId<HTMLTextAreaElement>("exp-params").value.trim();

      const errBox = byId("exp-error");
      const metricsBox = byId("exp-index-metrics-box");
      errBox.style.display = "none";
      metricsBox.style.display = "none";

      let parameters: Record<string, unknown> | undefined;
      if (rawParams.length > 0) {
        try {
          parameters = JSON.parse(rawParams) as Record<string, unknown>;
        } catch {
          errBox.textContent = "JSON de parâmetros (@) inválido.";
          errBox.style.display = "block";
          return;
        }
      }

      try {
        const res = await this.api.post<{
          count: number;
          totalRu: number;
          indexMetrics?: unknown;
          items: Record<string, unknown>[];
        }>(`/api/containers/${encodeURIComponent(name)}/query`, {
          sql,
          enableScan,
          populateIndexMetrics,
          parameters
        });
        byId("exp-count").textContent = String(res.count);
        byId("exp-ru").textContent = `RU: ${res.totalRu}`;
        if (res.indexMetrics) {
          metricsBox.style.display = "block";
          metricsBox.innerHTML = `<div class="muted-hint" style="font-size:11.5px; margin-bottom:4px;"><strong>IndexMetrics:</strong></div><pre style="margin:0; max-height:130px; overflow:auto;">${JSON.stringify(res.indexMetrics, null, 2)}</pre>`;
        }
        this.renderQueryResults(name, res.items);
        this.renderGeoPreview(res.items);
      } catch (e: unknown) {
        const err = e as { status?: number; data?: { error?: string } };
        errBox.textContent = `Erro (${err.status ?? 500}): ${err.data?.error ?? JSON.stringify(err.data)}`;
        errBox.style.display = "block";
      }
    }

    public async upsertItem(): Promise<void> {
      const name = byId<HTMLSelectElement>("exp-container").value;
      const raw = byId<HTMLTextAreaElement>("exp-upsert").value;
      try {
        const parsed = JSON.parse(raw) as Record<string, unknown>;
        await this.api.post(`/api/containers/${encodeURIComponent(name)}/items`, raw);
        toast("Item salvo com sucesso.");
        if (typeof parsed.id === "string") {
          this.selectedItemId = parsed.id;
          this.selectedItem = parsed;
        }
        await this.onMutated();
      } catch (e: unknown) {
        const err = e as { data?: { error?: string }; message?: string };
        toast("Erro no JSON/Upsert: " + (err.data?.error ?? err.message ?? "inválido"));
      }
    }

    public newItem(): void {
      const name = byId<HTMLSelectElement>("exp-container").value;
      const cMeta = this.getOverview()?.containers.find(c => c.name === name);
      const pkPath = cMeta?.partitionKeyPaths?.[0]?.replace(/^\//, "") || "partitionKey";
      this.selectedItemId = null;
      this.selectedItem = null;
      byId("exp-editor-title").textContent = "Novo Documento";
      byId("exp-delete-btn").style.display = "none";

      const template: Record<string, unknown> = { id: `item-${Date.now()}` };
      if (pkPath !== "id") {
        template[pkPath] = "valor";
      }
      byId<HTMLTextAreaElement>("exp-upsert").value = JSON.stringify(template, null, 2);
      const rows = byId("exp-master-list").querySelectorAll(".master-item-row");
      rows.forEach(r => r.classList.remove("active"));
    }

    public async deleteSelectedItem(): Promise<void> {
      if (!this.selectedContainer || !this.selectedItemId || !this.selectedItem) return;
      await this.deleteItem(this.selectedContainer, this.selectedItemId, this.selectedItem, this.selectedPkPaths);
      this.selectedItemId = null;
      this.selectedItem = null;
      byId("exp-delete-btn").style.display = "none";
      byId("exp-editor-title").textContent = "Documento Selecionado / Upsert";
      byId<HTMLTextAreaElement>("exp-upsert").value = "";
    }

    public showContainerSettings(containerName?: string): void {
      const name = containerName || byId<HTMLSelectElement>("exp-container").value;
      const card = byId("exp-container-settings-card");
      const box = byId("exp-container-settings");
      const cMeta = this.getOverview()?.containers.find(c => c.name === name);

      if (!cMeta) {
        card.style.display = "none";
        return;
      }

      card.style.display = "block";
      const pkList = (cMeta.partitionKeyPaths ?? ["/id"]).join(", ");
      const indexing = cMeta.indexingMode ?? "Consistent";
      const geoType = cMeta.geospatialType ?? "Geography";
      const spatialBadges = (cMeta.spatialIndexes ?? [])
        .map(si => `<span class="tag">${si.path} [${si.types.join(", ")}]</span>`)
        .join(" ");

      box.innerHTML = `
        <div class="grid">
          <div class="card"><h3>Container</h3><div class="metric" style="font-size:18px;">${cMeta.name}</div></div>
          <div class="card"><h3>Documentos</h3><div class="metric" style="font-size:18px;">${cMeta.itemCount}</div></div>
          <div class="card"><h3>Partition Key</h3><div class="metric" style="font-size:18px;">${pkList}</div></div>
          <div class="card"><h3>Indexing / Geo</h3><div class="metric" style="font-size:18px;">${indexing} (${geoType})</div></div>
        </div>
        ${spatialBadges ? `<div class="mt"><strong>SpatialIndexes:</strong> ${spatialBadges}</div>` : ""}
      `;
    }

    public hideContainerSettings(): void {
      byId("exp-container-settings-card").style.display = "none";
    }

    private async deleteItem(
      containerName: string,
      itemId: string,
      item: Record<string, unknown>,
      pkPaths: string[]
    ): Promise<void> {
      const pk = extractPartitionKeyParam(item, pkPaths);
      await this.api.delete(
        `/api/containers/${encodeURIComponent(containerName)}/items/${encodeURIComponent(itemId)}?pk=${encodeURIComponent(pk)}`
      );
      toast(`Item '${itemId}' removido.`);
      await this.onMutated();
    }

    private renderQueryResults(containerName: string, items: Record<string, unknown>[]): void {
      const master = byId("exp-master-list");
      const resultsBox = byId("exp-results");
      master.innerHTML = "";
      resultsBox.innerHTML = "";

      const cMeta = this.getOverview()?.containers.find(c => c.name === containerName);
      const pkPaths = cMeta?.partitionKeyPaths ?? ["/id"];
      this.selectedPkPaths = pkPaths;

      if (items.length === 0) {
        master.innerHTML = '<div class="muted-hint" style="padding:12px;">Nenhum documento encontrado.</div>';
        byId("exp-delete-btn").style.display = "none";
        return;
      }

      let selectedIndex = -1;
      items.forEach((item, index) => {
        const itemId = typeof item.id === "string" ? item.id : undefined;
        if (itemId && itemId === this.selectedItemId) {
          selectedIndex = index;
        }
      });
      if (selectedIndex < 0) {
        selectedIndex = 0;
      }

      items.forEach((item, index) => {
        const itemId = typeof item.id === "string" ? item.id : `projecao-${index + 1}`;
        const pkVal = extractPartitionKeyParam(item, pkPaths);

        const row = document.createElement("div");
        row.className = index === selectedIndex ? "master-item-row active" : "master-item-row";

        const idSpan = document.createElement("span");
        idSpan.className = "master-id";
        idSpan.textContent = itemId;

        const pkSpan = document.createElement("span");
        pkSpan.className = "master-pk";
        pkSpan.textContent = pkVal;

        row.appendChild(idSpan);
        row.appendChild(pkSpan);

        row.onclick = () => {
          master.querySelectorAll(".master-item-row").forEach(el => el.classList.remove("active"));
          row.classList.add("active");
          this.selectItem(containerName, item, pkPaths);
        };

        master.appendChild(row);
      });

      this.selectItem(containerName, items[selectedIndex], pkPaths);
    }

    private selectItem(containerName: string, item: Record<string, unknown>, pkPaths: string[]): void {
      this.selectedContainer = containerName;
      this.selectedPkPaths = pkPaths;
      const itemId = typeof item.id === "string" ? item.id : null;
      this.selectedItemId = itemId;
      this.selectedItem = item;

      byId("exp-editor-title").textContent = itemId ? `Documento: ${itemId}` : "Projeção de Query";
      byId<HTMLTextAreaElement>("exp-upsert").value = JSON.stringify(item, null, 2);
      byId("exp-delete-btn").style.display = itemId ? "inline-flex" : "none";
    }

    private renderGeoPreview(items: Record<string, unknown>[]): void {
      const card = byId("exp-geo-preview-card");
      const preview = byId("exp-geo-preview");
      const summary = byId("exp-geo-summary");
      const features: { id: string; type: string; coords: number[][] }[] = [];

      for (const item of items) {
        const id = typeof item.id === "string" ? item.id : "geo";
        for (const val of Object.values(item)) {
          if (val && typeof val === "object" && !Array.isArray(val)) {
            const g = val as { type?: string; coordinates?: unknown };
            if (g.type === "Point" && Array.isArray(g.coordinates) && g.coordinates.length >= 2) {
              features.push({ id, type: "Point", coords: [[Number(g.coordinates[0]), Number(g.coordinates[1])]] });
            } else if (g.type === "LineString" && Array.isArray(g.coordinates)) {
              const pts = (g.coordinates as number[][]).filter(c => Array.isArray(c) && c.length >= 2);
              if (pts.length >= 2) features.push({ id, type: "LineString", coords: pts });
            } else if (g.type === "Polygon" && Array.isArray(g.coordinates) && Array.isArray(g.coordinates[0])) {
              const pts = (g.coordinates[0] as number[][]).filter(c => Array.isArray(c) && c.length >= 2);
              if (pts.length >= 3) features.push({ id, type: "Polygon", coords: pts });
            }
          }
        }
      }

      if (features.length === 0) {
        card.style.display = "none";
        return;
      }

      let minX = Infinity, maxX = -Infinity, minY = Infinity, maxY = -Infinity;
      for (const f of features) {
        for (const [x, y] of f.coords) {
          if (x < minX) minX = x;
          if (x > maxX) maxX = x;
          if (y < minY) minY = y;
          if (y > maxY) maxY = y;
        }
      }

      const padX = Math.max(0.01, (maxX - minX) * 0.15);
      const padY = Math.max(0.01, (maxY - minY) * 0.15);
      minX -= padX; maxX += padX; minY -= padY; maxY += padY;
      const w = 560, h = 180;
      const sx = (x: number) => ((x - minX) / (maxX - minX)) * w;
      const sy = (y: number) => h - ((y - minY) / (maxY - minY)) * h;

      const svgShapes = features.map(f => {
        if (f.type === "Point") {
          const cx = sx(f.coords[0][0]).toFixed(1);
          const cy = sy(f.coords[0][1]).toFixed(1);
          return `<circle cx="${cx}" cy="${cy}" r="5" fill="#00d4d8" stroke="#061b44" stroke-width="1.5"><title>${f.id} [${f.coords[0][0]}, ${f.coords[0][1]}]</title></circle>`;
        }
        const pts = f.coords.map(c => `${sx(c[0]).toFixed(1)},${sy(c[1]).toFixed(1)}`).join(" ");
        if (f.type === "LineString") {
          return `<polyline points="${pts}" fill="none" stroke="#65d42e" stroke-width="2.5"><title>${f.id} (LineString)</title></polyline>`;
        }
        return `<polygon points="${pts}" fill="rgba(0,120,212,0.22)" stroke="#0078d4" stroke-width="2"><title>${f.id} (Polygon)</title></polygon>`;
      }).join("");

      card.style.display = "block";
      summary.textContent = `${features.length} geometria(s) plotada(s) no bounding box`;
      preview.innerHTML = `<svg viewBox="0 0 ${w} ${h}" style="width:100%; height:180px; background:var(--surface-2); border:1px solid var(--border); border-radius:6px;">${svgShapes}</svg>`;
    }
  }
}
