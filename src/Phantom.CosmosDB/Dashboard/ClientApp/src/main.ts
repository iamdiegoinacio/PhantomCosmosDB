namespace PhantomDashboard {
  import DashboardApiClient = PhantomDashboard.Infrastructure.DashboardApiClient;
  import byId = PhantomDashboard.Infrastructure.byId;
  import toast = PhantomDashboard.Infrastructure.toast;
  import OverviewController = PhantomDashboard.Features.OverviewController;
  import ExplorerController = PhantomDashboard.Features.ExplorerController;
  import CostSimulatorController = PhantomDashboard.Features.CostSimulatorController;
  import ChaosController = PhantomDashboard.Features.ChaosController;
  import LogsController = PhantomDashboard.Features.LogsController;
  import SeedsController = PhantomDashboard.Features.SeedsController;
  import ConfigController = PhantomDashboard.Features.ConfigController;
  import ResourceTreeController = PhantomDashboard.Features.ResourceTreeController;

  const TAB_TITLES: Record<string, string> = {
    quickstart: "Quickstart",
    overview: "Home",
    explorer: "Data Explorer",
    cost: "Simulador de Custos",
    chaos: "Chaos Engineering",
    seeds: "Seeds & Gerador",
    logs: "Logs ao Vivo",
    config: "phantom.config.json"
  };

  const api = new DashboardApiClient();

  const resourceTree = new ResourceTreeController({
    onSelectHome: () => {
      switchTab("overview", document.querySelector<HTMLButtonElement>("[data-tab=overview]"));
    },
    onSelectItems: (containerName: string) => {
      byId<HTMLSelectElement>("exp-container").value = containerName;
      switchTab("explorer", document.querySelector<HTMLButtonElement>("[data-tab=explorer]"));
      byId("ws-active-title").textContent = `${containerName} > Items`;
      void explorer.loadContainerItems();
    },
    onSelectNewQuery: (containerName: string) => {
      byId<HTMLSelectElement>("exp-container").value = containerName;
      explorer.hideContainerSettings();
      switchTab("explorer", document.querySelector<HTMLButtonElement>("[data-tab=explorer]"));
      byId("ws-active-title").textContent = `${containerName} > New SQL Query`;
      byId<HTMLTextAreaElement>("exp-sql").focus();
    },
    onSelectSettings: (containerName: string) => {
      byId<HTMLSelectElement>("exp-container").value = containerName;
      switchTab("explorer", document.querySelector<HTMLButtonElement>("[data-tab=explorer]"));
      byId("ws-active-title").textContent = `${containerName} > Scale & Settings`;
      explorer.showContainerSettings(containerName);
    }
  });

  const overview = new OverviewController(
    api,
    async () => {
      await explorer.runQuery();
      await chaos.load();
    },
    ov => {
      resourceTree.render(ov);
    }
  );

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

  const seeds = new SeedsController(api, async () => {
    await refreshAll();
  });

  const config = new ConfigController(api, async () => {
    await chaos.load();
    await overview.loadOverview();
  });

  async function refreshAll(): Promise<void> {
    await overview.loadOverview();
    await explorer.runQuery();
    await chaos.load();
  }

  function switchTab(name: string, btn: HTMLButtonElement | null): void {
    document.querySelectorAll(".tab").forEach(t => t.classList.remove("active"));
    document.querySelectorAll(".rail-btn").forEach(b => b.classList.remove("active"));
    byId("tab-" + name).classList.add("active");
    if (btn) {
      btn.classList.add("active");
    } else {
      document.querySelector<HTMLButtonElement>(`[data-tab=${name}]`)?.classList.add("active");
    }
    byId("ws-active-title").textContent = TAB_TITLES[name] ?? name;
    if (name === "logs") void logs.load();
    if (name === "config") void config.load();
    if (name === "chaos") void chaos.load();
    if (name === "cost") void cost.runSimulation();
    if (name === "seeds") void seeds.loadSummary();
  }

  function applyTheme(dark: boolean): void {
    document.body.classList.toggle("theme-dark", dark);
    const btn = byId("theme-toggle-btn");
    btn.textContent = dark ? "☀ Light" : "🌙 Dark";
  }

  function toggleTheme(): void {
    const isDark = !document.body.classList.contains("theme-dark");
    localStorage.setItem("phantom-theme", isDark ? "dark" : "light");
    applyTheme(isDark);
  }

  async function copyField(id: string): Promise<void> {
    const input = byId<HTMLInputElement>(id);
    try {
      await navigator.clipboard.writeText(input.value);
    } catch {
      input.select();
      document.execCommand("copy");
    }
    toast("Copiado para a área de transferência!");
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
    toggleTheme,
    copyField,
    resetSeed: () => overview.resetSeed(),
    updateCostSettings: () => cost.updateCostSettings(),
    loadContainerItems: () => explorer.loadContainerItems(),
    runQuery: () => explorer.runQuery(),
    upsertItem: () => explorer.upsertItem(),
    newExplorerItem: () => explorer.newItem(),
    deleteSelectedItem: () => explorer.deleteSelectedItem(),
    applyExplorerGeoPreset: () => explorer.applyGeoPreset(),
    applyChaos: () => chaos.apply(),
    disableAllChaos: () => chaos.disableAll(),
    loadLogs: () => logs.load(),
    renderLogs: () => logs.render(),
    clearLogs: () => logs.clear(),
    formatConfigJson: () => config.format(),
    saveConfigJson: () => config.save(),
    importContainerIntoSimulator: () => cost.importContainer(),
    applyCostPreset: (p: "efficient" | "scan" | "cross" | "spatial-indexed" | "spatial-scan") => cost.applyPreset(p),
    runCostSimulation: () => cost.runSimulation(),
    runFleetSimulation: () => cost.runFleetSimulation(),
    loadSeedContainer: () => seeds.loadSelectedContainer(),
    onSeedIdStrategyChanged: () => seeds.onIdStrategyChanged(),
    setSeedVolumePreset: (n: number) => seeds.setVolumePreset(n),
    previewSeedGeneration: () => seeds.previewGeneration(),
    startSeedGeneration: () => seeds.startGeneration(),
    cancelSeedJob: () => seeds.cancelActiveJob()
  });

  applyTheme(localStorage.getItem("phantom-theme") === "dark");

  void (async () => {
    await cost.loadRegions();
    await refreshAll();
    await logs.load();
    await seeds.loadSummary();
    await config.load();
    await cost.runSimulation();
  })();
}
