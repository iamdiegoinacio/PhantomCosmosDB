namespace PhantomDashboard.Features {
  import OverviewPayload = PhantomDashboard.Domain.OverviewPayload;
  import DashboardApiClient = PhantomDashboard.Infrastructure.DashboardApiClient;
  import byId = PhantomDashboard.Infrastructure.byId;
  import toast = PhantomDashboard.Infrastructure.toast;
  import extractPartitionKeyParam = PhantomDashboard.Infrastructure.extractPartitionKeyParam;

  export class ExplorerController {
    public constructor(
      private readonly api: DashboardApiClient,
      private readonly getOverview: () => OverviewPayload | null,
      private readonly onMutated: () => Promise<void>
    ) {}

    public async loadContainerItems(): Promise<void> {
      const name = byId<HTMLSelectElement>("exp-container").value;
      if (!name) return;
      byId<HTMLTextAreaElement>("exp-sql").value = "SELECT TOP 50 * FROM c";
      await this.runQuery();
    }

    public async runQuery(): Promise<void> {
      const name = byId<HTMLSelectElement>("exp-container").value;
      if (!name) return;
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
        JSON.parse(raw);
        await this.api.post(`/api/containers/${encodeURIComponent(name)}/items`, raw);
        toast("Item salvo com sucesso.");
        await this.onMutated();
      } catch (e: unknown) {
        const err = e as { data?: { error?: string }; message?: string };
        toast("Erro no JSON/Upsert: " + (err.data?.error ?? err.message ?? "inválido"));
      }
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
}
