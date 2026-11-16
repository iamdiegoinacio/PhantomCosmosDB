namespace PhantomDashboard.Features {
  import OverviewPayload = PhantomDashboard.Domain.OverviewPayload;
  import byId = PhantomDashboard.Infrastructure.byId;

  export interface TreeActionHandler {
    onSelectHome(): void;
    onSelectItems(containerName: string): void;
    onSelectNewQuery(containerName: string): void;
    onSelectSettings(containerName: string): void;
  }

  export class ResourceTreeController {
    private expandedDb = true;
    private expandedContainers: Record<string, boolean> = {};
    private activeNodeKey = "home";
    private lastOverview: OverviewPayload | null = null;

    public constructor(private readonly handler: TreeActionHandler) {}

    public render(overview: OverviewPayload): void {
      this.lastOverview = overview;
      const body = byId("resource-tree-body");
      body.innerHTML = "";

      body.appendChild(
        this.createNode("home", "tree-node", "🏠", "Home", () => {
          this.activeNodeKey = "home";
          this.render(overview);
          this.handler.onSelectHome();
        })
      );

      const dbArrow = this.expandedDb ? "▾" : "▸";
      body.appendChild(
        this.createNode(`db:${overview.databaseName}`, "tree-node", dbArrow, `🗄 ${overview.databaseName}`, () => {
          this.expandedDb = !this.expandedDb;
          this.render(overview);
        })
      );

      if (!this.expandedDb) {
        return;
      }

      for (const container of overview.containers) {
        if (this.expandedContainers[container.name] === undefined) {
          this.expandedContainers[container.name] = true;
        }

        const isExpanded = this.expandedContainers[container.name];
        const arrow = isExpanded ? "▾" : "▸";
        body.appendChild(
          this.createNode(
            `container:${container.name}`,
            "tree-node tree-child",
            arrow,
            `📦 ${container.name} (${container.itemCount})`,
            () => {
              this.expandedContainers[container.name] = !isExpanded;
              this.render(overview);
            }
          )
        );

        if (!isExpanded) {
          continue;
        }

        body.appendChild(
          this.createNode(`items:${container.name}`, "tree-node tree-subchild", "📄", "Items", () => {
            this.activeNodeKey = `items:${container.name}`;
            this.render(overview);
            this.handler.onSelectItems(container.name);
          })
        );

        body.appendChild(
          this.createNode(`query:${container.name}`, "tree-node tree-subchild", "⚡", "New SQL Query", () => {
            this.activeNodeKey = `query:${container.name}`;
            this.render(overview);
            this.handler.onSelectNewQuery(container.name);
          })
        );

        body.appendChild(
          this.createNode(`settings:${container.name}`, "tree-node tree-subchild", "⚙", "Scale & Settings", () => {
            this.activeNodeKey = `settings:${container.name}`;
            this.render(overview);
            this.handler.onSelectSettings(container.name);
          })
        );
      }
    }

    public setActiveNode(key: string): void {
      this.activeNodeKey = key;
      if (this.lastOverview) {
        this.render(this.lastOverview);
      }
    }

    private createNode(
      key: string,
      className: string,
      icon: string,
      label: string,
      onClick: () => void
    ): HTMLDivElement {
      const el = document.createElement("div");
      el.className = this.activeNodeKey === key ? `${className} active` : className;
      el.onclick = onClick;

      const iconSpan = document.createElement("span");
      iconSpan.textContent = icon;
      el.appendChild(iconSpan);

      const textSpan = document.createElement("span");
      textSpan.textContent = label;
      el.appendChild(textSpan);

      return el;
    }
  }
}
