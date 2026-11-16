namespace PhantomDashboard.Infrastructure {
  export function byId<T extends HTMLElement>(id: string): T {
    const el = document.getElementById(id);
    if (!el) throw new Error(`Element #${id} not found.`);
    return el as T;
  }

  export function toast(msg: string): void {
    const el = byId<HTMLDivElement>("toast");
    el.textContent = msg;
    el.style.display = "block";
    window.setTimeout(() => { el.style.display = "none"; }, 2600);
  }

  export function extractPartitionKeyParam(item: Record<string, unknown>, pkPaths: string[]): string {
    const resolvePath = (obj: Record<string, unknown>, path: string): unknown => {
      const parts = path.replace(/^\//, "").split("/");
      let cur: unknown = obj;
      for (const p of parts) {
        if (cur == null || typeof cur !== "object") return null;
        cur = (cur as Record<string, unknown>)[p];
      }
      return cur;
    };

    if (pkPaths.length > 1) {
      return JSON.stringify(pkPaths.map(p => resolvePath(item, p)));
    }
    const val = resolvePath(item, pkPaths[0]) ?? item.id;
    return String(val ?? "");
  }
}
