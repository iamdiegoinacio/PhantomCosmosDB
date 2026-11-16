<div align="center">
  <img src="Docs/Img/phantom_logo.png" alt="Phantom.CosmosDB Logo" width="250"/>
</div>

# Phantom.CosmosDB

![.NET 8](https://img.shields.io/badge/.NET-8.0-blueviolet.svg)
![Conventional Commits](https://img.shields.io/badge/Conventional%20Commits-1.0.0-%23FE5196?logo=conventionalcommits&logoColor=white)
![License](https://img.shields.io/badge/license-MIT-green)

**Phantom.CosmosDB** é um emulador em memória de Azure Cosmos DB e harness de desenvolvimento/testes para .NET. Ele permite desenvolver localmente, testar resiliência e inspecionar documentos sem infraestrutura pesada ou custos de nuvem — usando **exatamente o mesmo código de aplicação** e alternando entre Azure real e memória pela Connection String.

## Principais Recursos

- **Troca transparente via Connection String (`AddCosmosDb`):** usa o `CosmosClient` real da Microsoft em produção e ativa o emulador em memória quando `ConnectionStrings:CosmosDb` aponta para o Phantom (`https://phantom.local/` ou `phantom://in-memory`).
- **Auto-Seeding automático (`IHostedService`):** lê `Resources/Seeds/*.json` e popula os containers antes da API receber requisições, sem chamadas extras no `Program.cs`.
- **Chaos Engineering embutido e Live:** injeta latência, throttling (`429`, substatus `3200`) e erros HTTP (`503`, `408`, `400`, `401`, `403`, `409`, `412`, `413`), configurável por arquivo ou ao vivo pelo Dashboard.
- **PhantomDB Dashboard (`/_phantom`):** interface gráfica Web embutida com Visão Geral, Data Explorer & Query Runner SQL, controle live de Chaos, Logs ao vivo e editor de `phantom.config.json`.
- **Motor de Alta Fidelidade (`Phantom.CosmosDB.Core`):** parser SQL completo, `ORDER BY`, `ComputedProperties`, `CompositeIndexes`, `MultiHash` (até 3 níveis), `UniqueKeys`, `IndexingMode.None` + `EnableScanInQuery`, `TransactionalBatch`, `Bulk`, `ChangeFeed` (`Incremental` e `AllVersionsAndDeletes`), `TTL` e `Sprocs`/`Triggers`/`UDFs`.

---

## Arquitetura Unificada (`Phantom.CosmosDB.Core`)

1. **`Phantom.CosmosDB.Core`:** Motor em memória (`InMemoryCosmosClient`, `InMemoryContainer`, `FakeCosmosHandler`, gateway HTTP compatível com o SDK oficial `Microsoft.Azure.Cosmos`).
2. **`Phantom.CosmosDB.JsTriggers`:** Engine JavaScript (Jint) para Stored Procedures, Triggers e UDFs.
3. **`Phantom.CosmosDB.ProductionExtensions`:** Extensões auxiliares para interceptação de iteradores em testes.
4. **`Phantom.CosmosDB`:** Pacote NuGet principal com ativação por Connection String (Strategy Pattern), `phantom.config.json`, Auto-Seeding (`IHostedService`), Chaos Engineering e **PhantomDB Dashboard** (`/_phantom`).

---

## 1. Instalação

```xml
<ItemGroup>
    <PackageReference Include="Phantom.CosmosDB" Version="1.0.0" />
</ItemGroup>
```

---

## 2. Como Usar (`Program.cs` + `appsettings*.json`)

A separação de responsabilidades mantém:
- **`appsettings.json` / `appsettings.Development.json`**: apenas dados de conexão (`ConnectionStrings:CosmosDb`, `DatabaseName`, `ContainerName`), exatamente como em qualquer banco real.
- **`phantom.config.json`**: criação de containers, políticas e `CosmosChaosEngineering`.

### 2.1 `Program.cs` (único para Dev e Produção)

```csharp
using Phantom.CosmosDB;

var builder = WebApplication.CreateBuilder(args);

// Alterna automaticamente entre Azure Cosmos DB real e Phantom pela Connection String
builder.AddCosmosDb();

var app = builder.Build();

// Opcional: habilita a UI Web em /_phantom (desativado automaticamente em Production ou modo real)
app.UsePhantomDashboard();

app.Run();
```

### 2.2 `appsettings.json` (Produção — Online) vs `appsettings.Development.json` (Local — Memória)

```json
// appsettings.json (Produção)
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
// appsettings.Development.json (Local - Em Memória)
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

**Marcadores aceitos para modo em memória:** `https://phantom.local/`, `https://cosmosphantom.local/`, `https://in-memory.cosmos.local/`, `phantom://in-memory`, ou parâmetros `Mode=InMemory;` / `UsePhantom=true;`.

> **Guardrail de Segurança:** Se uma Connection String Phantom for detectada com `ASPNETCORE_ENVIRONMENT=Production`, o `AddCosmosDb()` falha imediatamente na inicialização (salvo opt-in explícito via `AllowPhantomInProduction = true`).
>
> **Modo Legado:** `AddCosmosPhantomEmulator(...)` e `UseCosmosPhantomSeederAsync()` continuam suportados para projetos que utilizam a flag `"UseCosmosPhantom": true`.

---

## 3. Configuração de Containers e Caos (`phantom.config.json`)

Crie **`phantom.config.json`** na raiz da solução/projeto (ao lado do `.sln`), no `ContentRootPath` ou em `Resources/` *(também há fallback automático para `CosmosPhantom.config.json`)*:

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
      }
    ]
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

---

## 4. Auto-Seeding (`Resources/Seeds`)

```text
projeto-x/
├── projeto-x.sln
├── phantom.config.json
├── Resources/
│   └── Seeds/
│       ├── Produtos.json
│       └── Pedidos.json
└── src/
    └── ProjetoX.Api/
        ├── appsettings.json
        ├── appsettings.Development.json
        └── Program.cs
```

1. Adicione arquivos JSON com o **mesmo nome do container** em `Resources/Seeds/`.
2. O conteúdo deve ser um array JSON contendo as propriedades obrigatórias (`id` e a Partition Key configurada).
3. O seeder roda automaticamente no startup via `CosmosPhantomSeedingHostedService` (idempotente com `UpsertItemAsync`).

---

## 5. PhantomDB Dashboard (`/_phantom`)

Com `app.UsePhantomDashboard();` no `Program.cs`, acesse `http://localhost:<porta>/_phantom` em `Development` para:
- **Visão Geral:** listar banco, containers, contagem de itens, `RU/s` e políticas, além de **Reset & Re-seed** em 1 clique.
- **Data Explorer & SQL:** executar queries SQL em memória, inspecionar JSON, criar/atualizar ou excluir itens.
- **Chaos Engineering (Live):** ativar/desativar falhas HTTP, throttling e latência em tempo real sem reiniciar a API.
- **Logs ao Vivo:** acompanhar requisições interceptadas com status, substatus (`1001`, `1004`, `1008`, `3200`), RU/s, duração e SQL.
- **Editor `phantom.config.json`:** visualizar, formatar e salvar alterações no disco.

---

## Arquitetura

👉 **[Ver Diagramas de Arquitetura Completos (Fluxo e Sequência)](Docs/Architecture.md)**
