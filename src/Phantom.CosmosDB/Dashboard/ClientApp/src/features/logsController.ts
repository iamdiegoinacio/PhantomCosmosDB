namespace PhantomDashboard.Features {
  import LogEntry = PhantomDashboard.Domain.LogEntry;
  import DashboardApiClient = PhantomDashboard.Infrastructure.DashboardApiClient;
  import byId = PhantomDashboard.Infrastructure.byId;
  import toast = PhantomDashboard.Infrastructure.toast;

  export class LogsController {
    private cachedLogs: LogEntry[] = [];

    public constructor(private readonly api: DashboardApiClient) {}

    public async load(): Promise<void> {
      const res = await this.api.get<{ items: LogEntry[] }>("/api/logs?take=200");
      this.cachedLogs = res.items ?? [];
      this.render();
    }

    public render(): void {
      const q = (byId<HTMLInputElement>("log-filter").value || "").toLowerCase();
      const tbody = byId("logs-body");
      tbody.innerHTML = "";

      for (const entry of this.cachedLogs) {
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

    public async clear(): Promise<void> {
      await this.api.delete("/api/logs");
      this.cachedLogs = [];
      this.render();
      toast("Logs limpos.");
    }
  }
}
