namespace PhantomDashboard.Features {
  import SeedSummary = PhantomDashboard.Domain.SeedSummary;
  import ContainerSeedResponse = PhantomDashboard.Domain.ContainerSeedResponse;
  import SeedGenerationJobStatus = PhantomDashboard.Domain.SeedGenerationJobStatus;
  import DashboardApiClient = PhantomDashboard.Infrastructure.DashboardApiClient;
  import byId = PhantomDashboard.Infrastructure.byId;
  import toast = PhantomDashboard.Infrastructure.toast;

  export class SeedsController {
    private activeJobId: string | null = null;
    private pollTimer: number | null = null;

    public constructor(
      private readonly api: DashboardApiClient,
      private readonly onCompleted: () => Promise<void>
    ) {}

    public async loadSummary(): Promise<void> {
      const res = await this.api.get<{ seedsFolder: string; items: SeedSummary[] }>("/api/seeds");
      const sel = byId<HTMLSelectElement>("seed-container");
      const prev = sel.value;
      sel.innerHTML = "";

      for (const s of res.items) {
        const opt = document.createElement("option");
        opt.value = s.containerName;
        opt.textContent = `${s.containerName} (disco: ${s.seedItemCount} | memória: ${s.inMemoryCount})`;
        sel.appendChild(opt);
      }

      if (prev && res.items.some(i => i.containerName === prev)) {
        sel.value = prev;
      }

      await this.loadSelectedContainer();
    }

    public async loadSelectedContainer(): Promise<void> {
      const name = byId<HTMLSelectElement>("seed-container").value;
      if (!name) return;

      const data = await this.api.get<ContainerSeedResponse>(`/api/seeds/${encodeURIComponent(name)}?take=25&skip=0`);
      byId("seed-file-info").textContent =
        `${data.filePath} (${data.exists ? `${data.totalCount} itens gravados` : "arquivo ainda não existe"})`;

      byId<HTMLTextAreaElement>("seed-template").value = JSON.stringify(data.suggestedTemplate, null, 2);
      byId("seed-preview-count").textContent = String(data.items.length);
      byId("seed-stored-list").innerHTML = data.items.length === 0
        ? `<span class="muted-hint">Nenhuma seed gravada para este container.</span>`
        : data.items.map(item => `<pre style="margin-bottom:8px">${JSON.stringify(item, null, 2)}</pre>`).join("");
    }

    public onIdStrategyChanged(): void {
      const strategy = byId<HTMLSelectElement>("seed-id-strategy").value;
      byId("seed-prefix-group").style.display =
        strategy === "Auto" || strategy === "PrefixSequential" ? "flex" : "none";
      byId("seed-v5-group").style.display = strategy === "GuidV5" ? "grid" : "none";
    }

    public setVolumePreset(count: number): void {
      byId<HTMLInputElement>("seed-count").value = String(count);
    }

    public async previewGeneration(): Promise<void> {
      const name = byId<HTMLSelectElement>("seed-container").value;
      if (!name) return;
      try {
        const payload = this.buildPayload(5);
        const res = await this.api.post<{ count: number; items: Record<string, unknown>[] }>(
          `/api/seeds/${encodeURIComponent(name)}/preview`,
          payload
        );
        byId("seed-generated-preview").innerHTML =
          res.items.map(item => `<pre style="margin-bottom:8px">${JSON.stringify(item, null, 2)}</pre>`).join("");
        toast("Prévia de 5 itens gerada!");
      } catch (e: unknown) {
        const err = e as { data?: { error?: string }; message?: string };
        toast("Erro na prévia: " + (err.data?.error ?? err.message ?? "erro"));
      }
    }

    public async startGeneration(): Promise<void> {
      const name = byId<HTMLSelectElement>("seed-container").value;
      if (!name) return;
      try {
        const count = Number(byId<HTMLInputElement>("seed-count").value);
        const payload = this.buildPayload(count);
        const job = await this.api.post<SeedGenerationJobStatus>(
          `/api/seeds/${encodeURIComponent(name)}/generate`,
          payload
        );
        this.activeJobId = job.jobId;
        byId("seed-job-box").style.display = "block";
        this.renderJobStatus(job);
        this.startPolling();
      } catch (e: unknown) {
        const err = e as { data?: { error?: string }; message?: string };
        toast("Erro ao iniciar geração: " + (err.data?.error ?? err.message ?? "erro"));
      }
    }

    public async cancelActiveJob(): Promise<void> {
      if (!this.activeJobId) return;
      await this.api.delete(`/api/seeds/jobs/${encodeURIComponent(this.activeJobId)}`);
      toast("Cancelamento solicitado...");
    }

    private buildPayload(count: number): Record<string, unknown> {
      const templateJson = byId<HTMLTextAreaElement>("seed-template").value;
      JSON.parse(templateJson);

      const startRaw = byId<HTMLInputElement>("seed-id-start").value.trim();
      const randomSeedRaw = byId<HTMLInputElement>("seed-random-seed").value.trim();

      return {
        templateJson,
        count,
        mode: byId<HTMLSelectElement>("seed-storage-mode").value,
        append: byId<HTMLInputElement>("seed-append").checked,
        idStrategy: byId<HTMLSelectElement>("seed-id-strategy").value,
        idPrefix: byId<HTMLInputElement>("seed-id-prefix").value.trim() || null,
        idStartNumber: startRaw ? Number(startRaw) : null,
        guidV5Mode: byId<HTMLSelectElement>("seed-v5-mode").value,
        guidV5Template: byId<HTMLInputElement>("seed-v5-template").value.trim() || null,
        guidV5Namespace: byId<HTMLInputElement>("seed-v5-ns").value.trim() || null,
        randomSeed: randomSeedRaw ? Number(randomSeedRaw) : null
      };
    }

    private startPolling(): void {
      if (this.pollTimer !== null) window.clearInterval(this.pollTimer);
      this.pollTimer = window.setInterval(() => void this.pollJob(), 400);
    }

    private async pollJob(): Promise<void> {
      if (!this.activeJobId) return;
      const status = await this.api.get<SeedGenerationJobStatus>(`/api/seeds/jobs/${encodeURIComponent(this.activeJobId)}`);
      this.renderJobStatus(status);

      if (status.status !== "Running") {
        if (this.pollTimer !== null) {
          window.clearInterval(this.pollTimer);
          this.pollTimer = null;
        }
        this.activeJobId = null;
        toast(`Geração ${status.status}: ${status.processedCount} itens (${status.elapsedSeconds}s)`);
        await this.loadSummary();
        await this.onCompleted();
      }
    }

    private renderJobStatus(job: SeedGenerationJobStatus): void {
      byId("seed-job-bar").style.width = `${Math.min(100, job.percent)}%`;
      byId("seed-job-text").textContent =
        `Status: ${job.status} | ${job.processedCount} / ${job.totalCount} (${job.percent}%) | ${job.itemsPerSecond} itens/s | ${job.elapsedSeconds}s` +
        (job.error ? ` | Erro: ${job.error}` : "");
    }
  }
}
