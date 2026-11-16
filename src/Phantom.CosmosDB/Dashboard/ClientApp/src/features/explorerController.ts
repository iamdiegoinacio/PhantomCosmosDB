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
      await this.runQuery();
    }

    public async runQuery(): Promise<void> {
      const name = byId<HTMLSelectElement>("exp-container").value;
      if (!name) return;
      this.selectedContainer = name;
      const sql = byId<HTMLTextAreaElement>("exp-sql").value;
      const enableScan = byId<HTMLInputElement>("exp-scan").checked;
      const errBox = byId("exp-error");
      errBox.style.display = "none";

      try {
        const res = await this.api.post<{ count: number; totalRu: number; items: Record<string, unknown>[] }>(
          `/api/containers/${encodeURIComponent(name)}/query`,
          { sql, enableScan }
        );
        byId("exp-count").textContent = String(res.count);
        byId("exp-ru").textContent = `RU: ${res.totalRu}`;
        this.renderQueryResults(name, res.items);
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
      box.innerHTML = `
        <div class="grid">
          <div class="card"><h3>Container</h3><div class="metric" style="font-size:18px;">${cMeta.name}</div></div>
          <div class="card"><h3>Documentos</h3><div class="metric" style="font-size:18px;">${cMeta.itemCount}</div></div>
          <div class="card"><h3>Partition Key</h3><div class="metric" style="font-size:18px;">${pkList}</div></div>
          <div class="card"><h3>Indexing Mode</h3><div class="metric" style="font-size:18px;">${indexing}</div></div>
        </div>
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
  }
}
