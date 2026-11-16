namespace PhantomDashboard.Features {
  import DashboardApiClient = PhantomDashboard.Infrastructure.DashboardApiClient;
  import byId = PhantomDashboard.Infrastructure.byId;
  import toast = PhantomDashboard.Infrastructure.toast;

  export class ChaosController {
    private static readonly BoolFields = [
      "EnableRandomThrottling", "SimulateHighLatency",
      "Simulate429_TooManyRequests", "Simulate503_ServiceUnavailable",
      "Simulate408_RequestTimeout", "Simulate403_Forbidden",
      "Simulate401_Unauthorized", "Simulate409_Conflict",
      "Simulate413_EntityTooLarge", "Simulate412_PreconditionFailed",
      "Simulate400_BadRequest"
    ];

    private static readonly NumFields = [
      "ThrottlingProbability", "MinLatencyMs", "MaxLatencyMs", "RetryAfterMs"
    ];

    public constructor(
      private readonly api: DashboardApiClient,
      private readonly onUpdated: () => Promise<void>
    ) {}

    public async load(): Promise<void> {
      const cfg = await this.api.get<Record<string, unknown>>("/api/chaos");
      for (const f of ChaosController.BoolFields) {
        const el = document.getElementById("ch-" + f) as HTMLInputElement | null;
        if (el) el.checked = !!cfg[f];
      }
      for (const f of ChaosController.NumFields) {
        const el = document.getElementById("ch-" + f) as HTMLInputElement | null;
        if (el) el.value = String(cfg[f] ?? 0);
      }
    }

    public async apply(): Promise<void> {
      const payload: Record<string, unknown> = {};
      for (const f of ChaosController.BoolFields) {
        payload[f] = byId<HTMLInputElement>("ch-" + f).checked;
      }
      for (const f of ChaosController.NumFields) {
        payload[f] = Number(byId<HTMLInputElement>("ch-" + f).value);
      }

      await this.api.put("/api/chaos", payload);
      toast("Chaos Engineering atualizado em memória!");
      await this.onUpdated();
    }

    public async disableAll(): Promise<void> {
      for (const f of ChaosController.BoolFields) {
        byId<HTMLInputElement>("ch-" + f).checked = false;
      }
      await this.apply();
    }
  }
}
