<div align="center">
  <img src="Docs/Img/phantom_logo.png" alt="Phantom.CosmosDB Logo" width="250"/>
</div>

# Phantom.CosmosDB

![.NET 8](https://img.shields.io/badge/.NET-8.0-blueviolet.svg)
![Conventional Commits](https://img.shields.io/badge/Conventional%20Commits-1.0.0-%23FE5196?logo=conventionalcommits&logoColor=white)
![License](https://img.shields.io/badge/license-MIT-green)

**Phantom.CosmosDB** é um emulador em memória de Azure Cosmos DB e suíte completa de desenvolvimento/testes para .NET 8. Ele permite desenvolver APIs localmente, simular custos, validar queries geoespaciais, testar resiliência com Chaos Engineering e inspecionar dados pelo **PhantomDB Dashboard** — sem Docker, sem emulador pesado e sem custos de nuvem, usando **exatamente o mesmo código de aplicação** e alternando entre Azure real e memória pela Connection String.

---

## Principais Recursos e Funcionalidades

- **Troca transparente via Connection String (`AddCosmosDb`):** registra o `CosmosClient` oficial da Microsoft em produção e ativa o emulador em memória quando `ConnectionStrings:CosmosDb` aponta para o Phantom (`https://phantom.local/`, `phantom://in-memory` ou flags `Mode=InMemory;` / `UsePhantom=true;`).
- **Motor de Alta Fidelidade (`Phantom.CosmosDB.Core`):**
  - **CRUD & Stream APIs:** `Create`, `Read`, `Upsert`, `Replace`, `Delete`, `Patch` (`Set`, `Add`, `Replace`, `Remove`, `Increment`, `Move`, `FilterPredicate`), `ReadMany` e `DeleteAllItemsByPartitionKey`.
  - **SQL Engine completo (125+ funções):** `SELECT`, `DISTINCT`, `TOP`, `VALUE`, `JOIN`, `GROUP BY`, `HAVING`, `ORDER BY` (inclusive multi-propriedade com `CompositeIndexes`), `OFFSET/LIMIT`, subqueries `ARRAY(...)` e `EXISTS(...)`, `LIKE ... ESCAPE`, `BETWEEN`, `IN`, `??`, ternário `? :` e `ComputedProperties`.
  - **Geolocalização (GeoJSON RFC 7946):** suporte a `Point`, `Polygon`, `MultiPolygon` e `LineString`, `GeospatialType` (`Geography` WGS84 e `Geometry` Euclidiano), `SpatialIndexes` e funções `ST_DISTANCE`, `ST_WITHIN`, `ST_INTERSECTS`, `ST_ISVALID`, `ST_ISVALIDDETAILED`, `ST_AREA` e `ST_LENGTH`.
  - **Particionamento & Consistência:** Single e `MultiHash` (até 3 níveis), `UniqueKeys`, `ETag` (`If-Match` / `If-None-Match`), `DefaultTimeToLive` + `_ttl` por item, `IndexingMode.None` + `EnableScanInQuery`.
  - **Transações, Change Feed & Scripts:** `TransactionalBatch` (atômico e `ContinueOnError`), `ChangeFeed` (`Incremental` e `AllVersionsAndDeletes`), `FeedRange`, Stored Procedures, Triggers (`Pre`/`Post`) e UDFs (C# ou JavaScript via `Phantom.CosmosDB.JsTriggers`).
  - **Persistência & Restore:** `ExportState*`, `ImportState*` e `RestoreToPointInTime`.
- **Auto-Seeding automático (`IHostedService`):** lê `Resources/Seeds/*.json` e popula os containers no startup antes da API receber requisições (`UpsertItemAsync` idempotente).
- **Chaos Engineering (Arquivo + Live):** injeta latência, throttling (`429`, substatus `3200`) e erros HTTP (`503`, `408`, `400`, `401`, `403`, `409`, `412`, `413`), configurável no `phantom.config.json` ou ao vivo pelo Dashboard.
- **PhantomDB Dashboard (`/phantom`):** interface Web embutida estilo Azure Cosmos DB Data Explorer, com Data Explorer SQL + Geo, Seed Generator (com Geohash), Simulador de Custos Azure, Chaos Live, Logs ao vivo e editor de `phantom.config.json`.

---

## Arquitetura do Repositório

1. **`Phantom.CosmosDB.Core`:** Motor em memória organizado em Clean Architecture (`Domain` → `Application` via Portas/DIP → `Infrastructure`) com testes de arquitetura (`ArchitectureFitnessTests`), `InMemoryCosmos`, `InMemoryContainer`, `FakeCosmosHandler` e gateway HTTP compatível com `Microsoft.Azure.Cosmos`.
2. **`Phantom.CosmosDB.JsTriggers`:** Engine JavaScript (Jint) para Stored Procedures, Triggers e UDFs.
3. **`Phantom.CosmosDB.ProductionExtensions`:** Extensões auxiliares para interceptação de iteradores em testes.
4. **`Phantom.CosmosDB`:** Pacote NuGet principal com ativação por Connection String (Strategy Pattern), `phantom.config.json`, Auto-Seeding (`IHostedService`), Chaos Engineering, Telemetria de RU/Custos e **PhantomDB Dashboard** (`/phantom`).

👉 **[Ver Diagramas de Arquitetura Completos (Camadas, Fluxo e Sequência)](Docs/Architecture.md)**

---

## 1. Como Integrar o Phantom.CosmosDB a uma API Existente Localmente

Se você já possui uma API ASP.NET Core usando `Microsoft.Azure.Cosmos` (`CosmosClient`), siga os passos abaixo para rodar e testar **100% localmente em memória** sem alterar seus repositórios ou serviços de domínio.

### Passo 1: Instalar o pacote

No `.csproj` da sua API (por exemplo, `MinhaApi.csproj`):

```xml
<ItemGroup>
  <PackageReference Include="Phantom.CosmosDB" Version="2.0.0" />
</ItemGroup>
```

> **Usando pacote local compilado do repositório?** Gere o `.nupkg` com `dotnet pack src\Phantom.CosmosDB\Phantom.CosmosDB.csproj -c Release -o ./local-nuget` e aponte um `nuget.config` para essa pasta.

### Passo 2: Atualizar o `Program.cs`

Substitua o registro manual do `CosmosClient` por `builder.AddCosmosDb()` e habilite o dashboard com `app.UsePhantomDashboard()`:

```csharp
using Phantom.CosmosDB;

var builder = WebApplication.CreateBuilder(args);

// Alterna automaticamente entre Azure Cosmos DB real e Phantom pela Connection String
builder.AddCosmosDb();

var app = builder.Build();

// Habilita a UI Web em http://localhost:<porta>/phantom
// (desativado automaticamente em Production ou quando conectado ao Azure real)
app.UsePhantomDashboard();

app.Run();
```

Seus repositórios, endpoints e serviços continuam injetando `Microsoft.Azure.Cosmos.CosmosClient` normalmente.

### Passo 3: Configurar `appsettings.Development.json` vs `appsettings.json`

Mantenha a Connection String real no `appsettings.json` (ou variáveis de ambiente de produção) e aponte o `appsettings.Development.json` para o Phantom:

```json
// appsettings.json (Produção — Azure Real)
{
  "ConnectionStrings": {
    "CosmosDb": "AccountEndpoint=https://sua-conta.documents.azure.com:443/;AccountKey=REAL_KEY==;"
  },
  "CosmosDb": {
    "DatabaseName": "LojaDb",
    "ContainerName": "Produtos"
  }
}
```

```json
// appsettings.Development.json (Local — Em Memória)
{
  "ConnectionStrings": {
    "CosmosDb": "AccountEndpoint=https://phantom.local/;AccountKey=phantom-in-memory;"
  },
  "CosmosDb": {
    "DatabaseName": "LojaDb",
    "ContainerName": "Produtos"
  }
}
```

**Marcadores aceitos para ativar o modo em memória:**
`https://phantom.local/`, `https://cosmosphantom.local/`, `https://in-memory.cosmos.local/`, `phantom://in-memory`, ou os parâmetros `Mode=InMemory;` / `UsePhantom=true;`.

> [!IMPORTANT]
> **Guardrail de Segurança:** Se uma Connection String Phantom for detectada com `ASPNETCORE_ENVIRONMENT=Production`, o `AddCosmosDb()` falha imediatamente na inicialização para evitar subir produção em memória por engano (salvo opt-in explícito via `AllowPhantomInProduction = true`).
>
> **Modo Legado:** `AddCosmosPhantomEmulator(...)` e `UseCosmosPhantomSeederAsync()` continuam suportados para projetos que utilizam a flag `"UseCosmosPhantom": true`.

### Passo 4: Criar `phantom.config.json` (Containers, Índices, Geo, Custos e Chaos)

Crie **`phantom.config.json`** na raiz da solução (ao lado do `.sln`), no `ContentRootPath` da API ou em `Resources/` *(também há fallback automático para `CosmosPhantom.config.json`)*.

> [!TIP]
> Declare **apenas os containers que a sua aplicação realmente utiliza**. As seções `CosmosCostOptions` e `CosmosChaosEngineering` são **opcionais** — se omitidas, o Phantom usa os padrões (Chaos desativado e custo padrão).

**Exemplo mínimo (1 container):**

```json
{
  "CosmosDbEmulator": {
    "DatabaseName": "MinhaAppDb",
    "Containers": [
      {
        "Name": "Usuarios",
        "PartitionKeyPath": "/id",
        "Throughput": 400
      }
    ]
  }
}
```

**Exemplo completo (MultiHash, UniqueKeys, ComputedProperties, CompositeIndexes, Geo, Custos e Chaos):**

```json
{
  "CosmosDbEmulator": {
    "DatabaseName": "LojaDb",
    "Containers": [
      {
        "Name": "Produtos",
        "PartitionKeyPath": "/categoria",
        "DefaultTimeToLive": -1,
        "Throughput": 400,
        "UniqueKeys": [ [ "/sku" ] ],
        "ComputedProperties": [
          {
            "Name": "nomeLower",
            "Query": "SELECT VALUE LOWER(c.nome) FROM c"
          }
        ],
        "IndexingPolicy": {
          "IndexingMode": "Consistent",
          "Automatic": true,
          "ExcludedPaths": [ "/\"_etag\"/?" ],
          "CompositeIndexes": [
            [
              { "Path": "/categoria", "Order": "ascending" },
              { "Path": "/preco", "Order": "descending" }
            ]
          ]
        }
      },
      {
        "Name": "Pedidos",
        "PartitionKeyPaths": [ "/tenantId", "/clienteId", "/ano" ],
        "Throughput": 800
      },
      {
        "Name": "Drivers",
        "PartitionKeyPath": "/city",
        "GeospatialType": "Geography",
        "IndexingPolicy": {
          "IndexingMode": "Consistent",
          "Automatic": true,
          "SpatialIndexes": [
            { "Path": "/location/*", "Types": [ "Point", "Polygon" ] }
          ]
        }
      }
    ]
  },
  "CosmosCostOptions": {
    "Region": "brazil-south",
    "BillingMode": "Serverless",
    "LogQueryCost": true,
    "ApplyFreeTier": false,
    "ConsistencyLevel": "Session"
  },
  "CosmosChaosEngineering": {
    "EnableRandomThrottling": false,
    "ThrottlingProbability": 0.2,
    "SimulateHighLatency": false,
    "MinLatencyMs": 200,
    "MaxLatencyMs": 500,
    "RetryAfterMs": 150,
    "Simulate429_TooManyRequests": false,
    "Simulate503_ServiceUnavailable": false,
    "Simulate408_RequestTimeout": false,
    "Simulate403_Forbidden": false,
    "Simulate401_Unauthorized": false,
    "Simulate409_Conflict": false,
    "Simulate413_EntityTooLarge": false,
    "Simulate412_PreconditionFailed": false,
    "Simulate400_BadRequest": false
  }
}
```

### Projetos Multicamadas (Vários Assemblies)

Se a sua solução for separada em múltiplas camadas (`Domain`, `Application`, `Infrastructure`, `Api`/Host):
- **Instale o `Phantom.CosmosDB` apenas no projeto Host (`MinhaApp.Api`)**, que é o *Composition Root* da aplicação.
- A camada de `Infrastructure` continua dependendo somente de **`Microsoft.Azure.Cosmos`** e recebe o `CosmosClient` por injeção de dependência normalmente.
- Caso um módulo de `IoC`/`Bootstrapper` separado já registrava um `CosmosClient` manualmente, remova o registro manual (ou use a sobrecarga `services.AddCosmosDb(environment, configuration)` nesse módulo, mantendo `app.UsePhantomDashboard()` no Host).
- Tanto `phantom.config.json` quanto `Resources/Seeds/*.json` podem ficar na pasta do Host ou na raiz da solução (ao lado do `.sln`).

### Passo 5: (Opcional) Adicionar Auto-Seeding em `Resources/Seeds/`

```text
projeto-x/
├── projeto-x.sln
├── phantom.config.json
├── Resources/
│   └── Seeds/
│       ├── Produtos.json
│       ├── Pedidos.json
│       └── Drivers.json
└── src/
    └── ProjetoX.Api/
        ├── appsettings.json
        ├── appsettings.Development.json
        └── Program.cs
```

1. Adicione arquivos `.json` com o **mesmo nome do container** em `Resources/Seeds/`.
2. O conteúdo deve ser um array JSON contendo `id` e a Partition Key configurada.
3. Ao rodar `dotnet run`, o `CosmosPhantomSeedingHostedService` popula os containers automaticamente antes de a API aceitar tráfego.

### Passo 6: Rodar localmente e abrir o Dashboard

```bash
dotnet run --project src/ProjetoX.Api
```

Acesse **`http://localhost:<porta>/phantom`** no navegador. Sua API estará respondendo em memória com dados iniciais carregados, telemetria de RU ativa e todas as ferramentas visuais do Dashboard prontas para uso.

---

## 2. PhantomDB Dashboard (`/phantom`)

Quando `app.UsePhantomDashboard();` está registrado (em `Development` e modo Phantom), o painel em `http://localhost:<porta>/phantom` oferece:

- **Árvore de Recursos & Visão Geral:** navegação estilo Azure Cosmos DB Data Explorer por bancos e containers, exibindo contagem de itens, `RU/s`, `PartitionKeyPaths`, `UniqueKeys`, `ComputedProperties`, `GeospatialType` e botão **Reset & Re-seed** em 1 clique.
- **Data Explorer & Query Runner SQL (+ Geo):** execute queries SQL com parâmetros (`@param`), visualize `IndexMetrics` e teste consultas espaciais (`ST_DISTANCE`, `ST_WITHIN`, `ST_INTERSECTS`, `ST_ISVALIDDETAILED`, `ST_AREA`, `ST_LENGTH`), além de criar, editar ou excluir documentos JSON.
- **Seed Generator (Preview + GeoJSON/Geohash):** crie e visualize arquivos de seed para `Resources/Seeds/*.json`, incluindo geração de geometrias GeoJSON (`Point`, `Polygon`, `MultiPolygon`, `LineString`) com validação e cálculo de **Geohash**.
- **Simulador de Custos Azure:** projeção de custo mensal (Provisionado `RU/s` vs Serverless) por região Azure (ex.: `brazil-south`, `eastus`) calculada com base nas RUs reais consumidas pelas requisições da sua API.
- **Chaos Engineering (Live):** ative ou desative falhas HTTP (`429`, `503`, `408`, etc.), probabilidade de throttling e latência em tempo real sem reiniciar a API.
- **Logs ao Vivo:** acompanhe cada requisição interceptada com método, rota, status HTTP, substatus (`1001`, `1004`, `1008`, `3200`), RU consumida, estimativa de custo Serverless, duração e SQL executada.
- **Editor `phantom.config.json`:** visualize, formate e salve alterações de configuração diretamente no disco.

---

## 3. Testes Automatizados sem Host Web (`InMemoryCosmos`)

Para testes unitários ou de integração direta com `Phantom.CosmosDB.Core` sem subir ASP.NET Core:

```csharp
using Phantom.CosmosDB.Core;
using Microsoft.Azure.Cosmos;

using var cosmos = InMemoryCosmos.Create("Produtos", "/categoria");

await cosmos.Container.CreateItemAsync(
    new { id = "p1", categoria = "eletronicos", nome = "Teclado", preco = 299.90 },
    new PartitionKey("eletronicos"));

var iterator = cosmos.Container.GetItemQueryIterator<dynamic>(
    "SELECT * FROM c WHERE c.preco > 100 ORDER BY c.preco DESC");
```

Para cenários multi-container, use `InMemoryCosmos.Builder()`:

```csharp
using var cosmos = InMemoryCosmos.Builder()
    .AddContainer("Produtos", "/categoria")
    .AddContainer("Pedidos", new[] { "/tenantId", "/clienteId" })
    .Build();
```
