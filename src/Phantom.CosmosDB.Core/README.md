# Phantom.CosmosDB.Core

Motor em memória de alta fidelidade do Azure Cosmos DB SDK para .NET 8 — projetado para testes unitários, de integração e execução local rápida.

**Substitutos diretos em memória para `CosmosClient`, `Database` e `Container`** — CRUD completo, SQL Engine (125+ funções incluindo geolocalização `ST_*`), LINQ, patch, transactional batch, change feed, ETag, TTL e mais. Sem rede, sem Docker e sem assinatura Azure.

## Tipos Principais

| Tipo | Finalidade |
|------|-----------|
| `InMemoryCosmos` | **Ponto de entrada recomendado** — setup em uma linha ou via `Builder()`, com fidelidade total de SDK e fault injection |
| `FakeCosmosHandler` | Interceptador HTTP (`HttpMessageHandler`) — zero alteração em código de produção |
| `InMemoryContainer` | Implementação interna de `Container` (particionada em `partial` files por área: Core, Items, Query/ChangeFeed, Lifecycle/Setup) |

## Quick Start

```csharp
using Phantom.CosmosDB.Core;
using Microsoft.Azure.Cosmos;

using var cosmos = InMemoryCosmos.Create("my-container", "/partitionKey");

await cosmos.Container.CreateItemAsync(
    new { id = "1", partitionKey = "pk1", name = "Alice" },
    new PartitionKey("pk1"));

var iterator = cosmos.Container.GetItemQueryIterator<dynamic>(
    "SELECT * FROM c WHERE c.name = 'Alice'");
```

## Multi-Container

```csharp
using var cosmos = InMemoryCosmos.Builder()
    .AddContainer("orders", "/customerId")
    .AddContainer("products", "/categoryId")
    .Build();

var orders = cosmos.Containers["orders"];
var products = cosmos.Containers["products"];
```

## Setup de Testes & Fault Injection

```csharp
// Registrar UDFs, stored procedures e triggers
cosmos.SetupContainer("orders").RegisterUdf("myUdf", args => args[0]);

// Injetar falhas: throttling 429, 503, timeouts
cosmos.Handler.FaultInjector = req => new HttpResponseMessage((HttpStatusCode)429);
```

## Arquitetura Interna

- **`Domain/`**: primitivas e validadores puros (sem dependências externas de camada).
- **`Application/`**: serviços de CRUD, Patch, Batch, ChangeFeed, Scripts e Query Engine (`Parsing`, `Evaluation`, `Functions` com `Registry` declarativo e `GeospatialFunctions`), dependentes apenas de portas em `Application/Contracts/`.
- **`Infrastructure/`**: adaptadores de armazenamento em memória, gateway HTTP (`CosmosRoutingMessageHandler`, `HybridRowBatchCodec`), interoperabilidade com o SDK e persistência em disco.
- Limites arquiteturais verificados por `ArchitectureFitnessTests`.
