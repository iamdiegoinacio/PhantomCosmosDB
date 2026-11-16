# Arquitetura: Phantom.CosmosDB

Este documento detalha a arquitetura do **Phantom.CosmosDB** em quatro visões: arquitetura em camadas do motor (`Phantom.CosmosDB.Core`), visão geral de componentes, ativação orientada por Connection String e fluxo em runtime (Chaos + Telemetria + Simulador de Custos + PhantomDB Dashboard).

---

## 1. Clean Architecture do Motor (`Phantom.CosmosDB.Core`)

O núcleo em memória segue **Clean Architecture** e **Ports & Adapters (DIP)**, garantidos por *Fitness Functions* automatizadas em `ArchitectureFitnessTests.cs`:

- **`Domain/`**: primitivas (`ETagValue`, `RequestChargeCalculator`, `ContainerConstants`, `CosmosSubStatusCodes`), parsers/validadores puros (`DocumentValidator`, `UniqueKeyValidator`, `JsonParseHelpers`) e contratos de domínio. **Não depende de `Application` nem de `Infrastructure`.**
- **`Application/`**: casos de uso e motor de execução (`Crud`, `Patch`, `Batch`, `ChangeFeed`, `Scripts`, `Account`, `Builders`, `Query/Parsing`, `Query/Evaluation`, `Query/Functions` com `Registry` declarativo e `GeospatialFunctions`). Comunica-se com persistência/SDK exclusivamente por portas em `Application/Contracts/` (`IDocumentStore`, `IChangeFeedLog`, `ISdkResponseMapper`, `IFeedIteratorFactory`, `IDynamicContainerRegistry`, `IGatewayClientFactory`). **Não referencia `Infrastructure`.**
- **`Infrastructure/`**: adaptadores concretos (`Storage/InMemoryItemStore`, `Storage/InMemoryChangeFeedLog`, `HttpGateway/CosmosRoutingMessageHandler`, `GatewayDatabaseLifecycleRouter`, `GatewayContainerLifecycleRouter`, `HybridRowBatchCodec`, `SdkInterop/CosmosResponseFactory`, `FeedIterators/`, `Persistence/`).
- **Raiz (`CorePortDefaults`, `InMemoryContainer` em `partial` files, `FakeCosmosHandler`)**: Composition Root interno e fachada compatível com `Microsoft.Azure.Cosmos.Container`.

### Integração em Soluções Multicamadas (Host vs Demais Camadas)
Em soluções divididas em múltiplos assemblies (`Domain`, `Application`, `Infrastructure`, `Api`/Host):
- **`Domain` / `Application`**: não possuem dependência de `Phantom.CosmosDB`.
- **`Infrastructure` (Repositórios)**: depende apenas de `Microsoft.Azure.Cosmos` e injeta `CosmosClient` normalmente.
- **`Api` (Host / Composition Root)**: instala `Phantom.CosmosDB`, chama `builder.AddCosmosDb()` (ou `services.AddCosmosDb(environment, configuration)` quando o DI está centralizado em um módulo `IoC`) e `app.UsePhantomDashboard()`. O arquivo `phantom.config.json` e `Resources/Seeds/*.json` podem residir no `ContentRootPath` do Host ou na raiz da `.sln`.

---

## 2. Visão Geral de Componentes

```mermaid
flowchart TD
    subgraph ConsumerAPI ["API Consumidora (Host)"]
        AppSettings["appsettings*.json (ConnectionStrings:CosmosDb)"]
        PhantomCfg["phantom.config.json (Containers, Geo & Chaos)"]
        Program["Program.cs (builder.AddCosmosDb / app.UsePhantomDashboard)"]
        Seeds["Resources/Seeds/*.json"]
    end

    subgraph PhantomLib ["Phantom.CosmosDB"]
        Parser["CosmosConnectionStringParser"]
        AzureStrat["AzureCosmosClientStrategy (Real)"]
        PhantomStrat["PhantomCosmosClientStrategy (Memória)"]
        HostedSeed["CosmosPhantomSeedingHostedService"]
        Chaos["ChaosEngineeringConfigurator"]
        Telemetry["CosmosPhantomTelemetryHandler"]
        CostSim["CostSimulator (RU -> Azure USD/BRL)"]
        Registry["CosmosPhantomRuntimeRegistry"]
        Dashboard["PhantomDB Dashboard (/phantom)"]
    end

    subgraph CoreEngine ["Phantom.CosmosDB.Core"]
        Router["CosmosRoutingMessageHandler"]
        Handler["FakeCosmosHandler"]
        InMemDB[("InMemoryContainer")]
    end

    Program --> Parser
    AppSettings --> Parser
    Parser -->|Mode = Real| AzureStrat
    Parser -->|Mode = Phantom| PhantomStrat

    PhantomStrat -->|Lê topologia, geo & chaos| PhantomCfg
    PhantomStrat --> Registry
    PhantomStrat --> HostedSeed
    HostedSeed -->|Lê JSONs e popula| Seeds
    HostedSeed -->|Upsert| InMemDB

    PhantomStrat --> Telemetry
    Telemetry --> Router
    Telemetry --> CostSim
    Router --> Handler
    Chaos -->|FaultInjector| Handler
    Handler --> InMemDB

    Program --> Dashboard
    Dashboard --> Registry
    Dashboard --> CostSim
```

---

## 3. Ativação Orientada por Connection String (Sequence Diagram)

```mermaid
sequenceDiagram
    participant App as Program.cs
    participant Ext as PhantomCosmosExtensions
    participant Parser as CosmosConnectionStringParser
    participant Strategy as Strategy (Real ou Phantom)
    participant Hosted as CosmosPhantomSeedingHostedService
    participant Core as InMemoryContainer

    App->>Ext: builder.AddCosmosDb()
    Ext->>Parser: Parse(ConnectionStrings:CosmosDb)

    alt Connection String Real (Azure)
        Parser-->>Ext: Mode = Real
        Ext->>Strategy: AzureCosmosClientStrategy.Register()
        Strategy-->>App: Registra CosmosClient oficial (nuvem)
    else Connection String Phantom (https://phantom.local/)
        Parser-->>Ext: Mode = Phantom
        Ext->>Strategy: PhantomCosmosClientStrategy.Register()
        Strategy->>Strategy: Valida guardrail Production + phantom.config.json
        Strategy-->>App: Registra CosmosClient emulado, Telemetry, Registry e HostedService
        App->>Hosted: Host StartAsync()
        Hosted->>Core: Cria containers (Geo, UniqueKeys, ComputedProperties) e executa Upsert de Resources/Seeds/*.json
    end
```

---

## 4. Tempo de Execução: Telemetria, Chaos e Dashboard

```mermaid
sequenceDiagram
    participant Repo as Endpoint / Repositório
    participant SDK as CosmosClient (Gateway)
    participant Tel as CosmosPhantomTelemetryHandler
    participant Chaos as ChaosEngineeringConfigurator
    participant InMem as InMemoryContainer
    participant UI as PhantomDB Dashboard (/phantom)

    Repo->>SDK: ReadItemAsync / Query (SQL/Geo) / CreateItemAsync
    SDK->>Tel: HttpRequestMessage
    Tel->>Chaos: Repassa ao FakeCosmosHandler

    alt Chaos desativado ou sem falha sorteada
        Chaos->>InMem: Executa operação em memória
        InMem-->>Chaos: 200 OK / 201 Created + RU
        Chaos-->>Tel: Resposta
    else Chaos simulando falha (ex: 429)
        Chaos-->>Tel: HTTP 429 (x-ms-substatus: 3200, retry-after)
    end

    Tel->>Tel: Registra status, substatus, RU, custo Serverless, duração e SQL no CosmosPhantomTelemetryLog
    Tel-->>SDK: HttpResponseMessage
    SDK-->>Repo: Resultado ou CosmosException

    UI->>Tel: GET /phantom/api/logs (live) e métricas de custo
    UI->>Chaos: PUT /phantom/api/chaos (altera ChaosConfig em runtime)
```
