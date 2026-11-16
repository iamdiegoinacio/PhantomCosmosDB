namespace PhantomDashboard.Features {
  import DashboardApiClient = PhantomDashboard.Infrastructure.DashboardApiClient;
  import byId = PhantomDashboard.Infrastructure.byId;
  import toast = PhantomDashboard.Infrastructure.toast;

  export class ConfigController {
    public constructor(
      private readonly api: DashboardApiClient,
      private readonly onSaved: () => Promise<void>
    ) {}

    public async load(): Promise<void> {
      const res = await this.api.get<{ path: string; exists: boolean; json: string }>("/api/config");
      byId("cfg-path").textContent = res.path + (res.exists ? "" : " (será criado ao salvar)");
      byId<HTMLTextAreaElement>("cfg-editor").value = res.json;
    }

    public format(): void {
      const ed = byId<HTMLTextAreaElement>("cfg-editor");
      try {
        ed.value = JSON.stringify(JSON.parse(ed.value), null, 2);
      } catch (e: unknown) {
        toast("JSON inválido: " + (e as Error).message);
      }
    }

    public async save(): Promise<void> {
      const json = byId<HTMLTextAreaElement>("cfg-editor").value;
      try {
        JSON.parse(json);
        const res = await this.api.put<{ saved: boolean; path: string }>("/api/config", { json });
        toast("Salvo em: " + res.path);
        await this.onSaved();
      } catch (e: unknown) {
        const err = e as { data?: { error?: string }; message?: string };
        toast("Erro ao salvar: " + (err.data?.error ?? err.message ?? "erro"));
      }
    }
  }
}
