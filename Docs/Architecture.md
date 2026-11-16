# Arquitetura: Phantom.CosmosDB

Este documento detalha a arquitetura do **Phantom.CosmosDB** em três visões: componentes, ativação orientada por Connection String e fluxo em runtime (Chaos + Telemetria + PhantomDB Dashboard).

---

## 1. Visão Geral de Componentes

```mermaid
flowchart TD
    subgraph ConsumerAPI ["API Consumidora (Host)"]
        AppSettings["appsettings*.json (ConnectionStrings:CosmosDb)"]
        PhantomCfg["phantom.config.json (Containers & Chaos)"]
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
        Registry["CosmosPhantomRuntimeRegistry"]
        Dashboard["PhantomDB Dashboard (/_phantom)"]
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

    PhantomStrat -->|Lê topologia & chaos| PhantomCfg
    PhantomStrat --> Registry
    PhantomStrat --> HostedSeed
    HostedSeed -->|Lê JSONs e popula| Seeds
    HostedSeed -->|Upsert| InMemDB

    PhantomStrat --> Telemetry
    Telemetry --> Router
    Router --> Handler
    Chaos -->|FaultInjector| Handler
    Handler --> InMemDB

    Program --> Dashboard
    Dashboard --> Registry
```

---

## 2. Ativação Orientada por Connection String (Sequence Diagram)

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
        Hosted->>Core: Cria containers e executa Upsert de Resources/Seeds/*.json
    end
```

---

## 3. Tempo de Execução: Telemetria, Chaos e Dashboard

```mermaid
sequenceDiagram
    participant Repo as Endpoint / Repositório
    participant SDK as CosmosClient (Gateway)
    participant Tel as CosmosPhantomTelemetryHandler
    participant Chaos as ChaosEngineeringConfigurator
    participant InMem as InMemoryContainer
    participant UI as PhantomDB Dashboard (/_phantom)

    Repo->>SDK: ReadItemAsync / Query / CreateItemAsync
    SDK->>Tel: HttpRequestMessage
    Tel->>Chaos: Repassa ao FakeCosmosHandler

    alt Chaos desativado ou sem falha sorteada
        Chaos->>InMem: Executa operação em memória
        InMem-->>Chaos: 200 OK / 201 Created + RU
        Chaos-->>Tel: Resposta
    else Chaos simulando falha (ex: 429)
        Chaos-->>Tel: HTTP 429 (x-ms-substatus: 3200, retry-after)
    end

    Tel->>Tel: Registra status, substatus, RU, duração e SQL no CosmosPhantomTelemetryLog
    Tel-->>SDK: HttpResponseMessage
    SDK-->>Repo: Resultado ou CosmosException

    UI->>Tel: GET /_phantom/api/logs (live)
    UI->>Chaos: PUT /_phantom/api/chaos (altera ChaosConfig em runtime)
```
