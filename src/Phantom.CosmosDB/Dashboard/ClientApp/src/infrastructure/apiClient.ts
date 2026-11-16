namespace PhantomDashboard.Infrastructure {
  export class DashboardApiClient {
    private readonly basePath = "__PHANTOM_BASE_PATH__";

    public async request<T>(path: string, opts: RequestInit = {}): Promise<T> {
      const res = await fetch(this.basePath + path, opts);
      const text = await res.text();
      let data: unknown = text;
      try { data = JSON.parse(text); } catch { /* plain text */ }
      if (!res.ok) throw { status: res.status, data };
      return data as T;
    }

    public get<T>(path: string): Promise<T> {
      return this.request<T>(path);
    }

    public post<T>(path: string, body?: unknown): Promise<T> {
      return this.request<T>(path, {
        method: "POST",
        headers: body !== undefined ? { "Content-Type": "application/json" } : undefined,
        body: body !== undefined ? (typeof body === "string" ? body : JSON.stringify(body)) : undefined
      });
    }

    public put<T>(path: string, body: unknown): Promise<T> {
      return this.request<T>(path, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: typeof body === "string" ? body : JSON.stringify(body)
      });
    }

    public delete<T>(path: string): Promise<T> {
      return this.request<T>(path, { method: "DELETE" });
    }
  }
}
