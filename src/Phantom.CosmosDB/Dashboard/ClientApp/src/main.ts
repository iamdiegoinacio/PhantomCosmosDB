namespace PhantomDashboard {
  import DashboardApiClient = PhantomDashboard.Infrastructure.DashboardApiClient;
  import byId = PhantomDashboard.Infrastructure.byId;
  import OverviewController = PhantomDashboard.Features.OverviewController;
  import ExplorerController = PhantomDashboard.Features.ExplorerController;
  import CostSimulatorController = PhantomDashboard.Features.CostSimulatorController;
  import ChaosController = PhantomDashboard.Features.ChaosController;
  import LogsController = PhantomDashboard.Features.LogsController;
  import ConfigController = PhantomDashboard.Features.ConfigController;

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

  async function refreshAll(): Promise<void> {
    await overview.loadOverview();
    await explorer.runQuery();
    await chaos.load();
  }

  function switchTab(name: string, btn: HTMLButtonElement): void {
    document.querySelectorAll(".tab").forEach(t => t.classList.remove("active"));
    document.querySelectorAll("nav button").forEach(b => b.classList.remove("active"));
    byId("tab-" + name).classList.add("active");
    btn.classList.add("active");
    if (name === "logs") void logs.load();
    if (name === "config") void config.load();
    if (name === "chaos") void chaos.load();
    if (name === "cost") void cost.runSimulation();
  }

  byId("exp-sql").addEventListener("keydown", (e: KeyboardEvent) => {
    if (e.ctrlKey && e.key === "Enter") void explorer.runQuery();
  });

  window.setInterval(() => {
    if (byId("tab-logs").classList.contains("active") &&
        byId<HTMLInputElement>("log-auto").checked) {
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
    applyCostPreset: (p: "efficient" | "scan" | "cross") => cost.applyPreset(p),
    runCostSimulation: () => cost.runSimulation()
  });

  void (async () => {
    await cost.loadRegions();
    await refreshAll();
    await logs.load();
    await config.load();
    await cost.runSimulation();
  })();
}
