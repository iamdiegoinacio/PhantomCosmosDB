<div align="center">
  <img src="Docs/Img/phantom_logo.png" alt="Cosmos Phantom Logo" width="250"/>
</div>

# CosmosPhantom

![.NET 8](https://img.shields.io/badge/.NET-8.0-blueviolet.svg)
![Conventional Commits](https://img.shields.io/badge/Conventional%20Commits-1.0.0-%23FE5196?logo=conventionalcommits&logoColor=white)
![License](https://img.shields.io/badge/license-MIT-green)

**CosmosPhantom** é um emulador em memória de Azure Cosmos DB e harness de desenvolvimento/testes para .NET. Ele facilita o desenvolvimento local, a automação de testes, o auto-seeding e a engenharia do caos sem a necessidade de infraestrutura pesada ou acesso a recursos em nuvem reais durante o ciclo de desenvolvimento.

## Principais Recursos

- **Mock de Cosmos DB Em Memória:** Substitui o `CosmosClient` em ambiente de desenvolvimento por um mock leve em memória, sem poluir código de produção.
- **Auto-Seeding (Carga de Dados):** Lê automaticamente arquivos JSON de um diretório e popula as coleções com dados de teste assim que a aplicação sobe.
- **Chaos Engineering Embutido:** Permite injetar falhas aleatórias (throttling, timeouts, erros HTTP variados) direto pela configuração para testar a resiliência da aplicação.
- **Configuração Validada (Fail-Fast):** O SDK valida suas configurações através do "Options Pattern", parando a execução imediatamente se as definições do ambiente não estiverem corretas.
- **Pluguabilidade:** Com apenas duas linhas no seu `Program.cs`, o SDK ativa ou desativa toda a infraestrutura baseada nas propriedades de ambiente.

---

## Arquitetura Unificada (`CosmosPhantom.CosmosDB.Core`)

O **CosmosPhantom** incorpora seu próprio motor de emulação em memória diretamente na solution (`CosmosPhantom.CosmosDB.Core`, `CosmosPhantom.CosmosDB.JsTriggers` e `CosmosPhantom.CosmosDB.ProductionExtensions`), empacotados juntos em um único pacote NuGet **`CosmosPhantom`**.

1. **`CosmosPhantom.CosmosDB.Core` (Motor):** Emula o protocolo do Azure Cosmos DB (`InMemoryCosmosClient`, `InMemoryContainer`, parser SQL completo, `ORDER BY RANK RRF`, `TransactionalBatch` atômico e Bulk não-atômico via HybridRow, `ChangeFeed` `Incremental` e `AllVersionsAndDeletes`, `Sprocs`/`Triggers`/`UDFs`, `SubStatusCodes`, `MultiHash`, `UniqueKeys`, `IndexingPolicy`, `Throughput` e `FakeCosmosHandler`).
2. **`CosmosPhantom` (Host / DX / Harness):**
   - **Auto-seeding Dinâmico:** Lê `Resources/Seeds/*.json` na raiz da solução/Host usando `UpsertItemAsync` idempotente.
   - **Engenharia do Caos (Chaos Engineering):** Intercepta requisições para simular latência, throttling e falhas HTTP Cosmos DB (`429`, `503`, `408`, etc.).
   - **Fail-Fast Configuration:** Valida configurações via *Options Pattern* com `DataAnnotations`.
   - **Descoberta Plug-and-Play:** Lê automaticamente `CosmosPhantom.config.json` na raiz da solução (`.sln`) e mantém `UseCosmosPhantom: false` por padrão.

---

## 1. Instalação Simples (Plug and Play)

Instale o pacote NuGet **`CosmosPhantom`** (ou referencie `src/CosmosPhantom/CosmosPhantom.csproj`):

```xml
<ItemGroup>
    <PackageReference Include="CosmosPhantom" Version="1.0.0" />
</ItemGroup>
```

---

## 2. Como Usar no `Program.cs`

No seu `Program.cs`, importe os namespaces:

```csharp
using CosmosPhantom;
using CosmosPhantom.Seeding;
```

A injeção do SDK foi projetada para ser extremamente limpa:

```csharp
// 1. Fase de Configuração de Serviços
// Adiciona o SDK de emulação injetando a configuração padrão (ou a sua customizada)
builder.Services.AddCosmosPhantomEmulator(builder.Environment, builder.Configuration);

var app = builder.Build();

// 2. Fase de Pipeline / Middleware
// Executa o Seeder para popular os dados antes da API aceitar as requisições
await app.Services.UseCosmosPhantomSeederAsync();

app.Run();
```

*Nota: Por padrão `UseCosmosPhantom` é **`false`**. O SDK só é ativado em ambiente `Development` e quando `UseCosmosPhantom` for explicitamente definido como `true` (via `CosmosPhantom.config.json` ou `appsettings.Development.json`).*

---

## 3. Configurações e Customizações (`CosmosPhantom.config.json`)

O SDK busca automaticamente um arquivo **`CosmosPhantom.config.json`** na raiz da solução/projeto (ao lado do `.sln`), no `ContentRootPath` do Host ou dentro da pasta `Resources/`, dispensando sujar o `appsettings.json` do Host.

> **Padrões Seguros (Defaults):**
> - **`UseCosmosPhantom`**: por padrão é **`false`** (desligado).
> - **`CosmosChaosEngineering`**: por padrão **nenhuma simulação de erro ou latência é retornada** (todas as flags começam como `false`).

### 3.1 Estrutura Completa do `CosmosPhantom.config.json`

Crie o arquivo `CosmosPhantom.config.json` na raiz do seu projeto/solução:

```json
{
  "UseCosmosPhantom": true,
  "CosmosDbEmulator": {
    "DatabaseName": "MeuBancoLocal",
    "Containers": [
      {
        "Name": "Usuarios",
        "PartitionKeyPath": "/id"
      },
      {
        "Name": "Produtos",
        "PartitionKeyPaths": [ "/tenantId", "/categoria" ],
        "DefaultTimeToLive": -1,
        "Throughput": 400,
        "UniqueKeys": [ [ "/codigoSku" ] ],
        "ComputedProperties": [
          {
            "Name": "nomeLower",
            "Query": "SELECT VALUE LOWER(c.nome) FROM c"
          }
        ]
      }
    ]
  },
  "CosmosChaosEngineering": {
    "EnableRandomThrottling": false,
    "ThrottlingProbability": 0.2,
    "SimulateHighLatency": false,
    "MinLatencyMs": 500,
    "MaxLatencyMs": 2000,
    "RetryAfterMs": 1000,
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

*(Opcionalmente, as seções `CosmosDbEmulator` e `CosmosChaosEngineering` também podem ficar separadas em `Resources/Cosmos.Phantom.Settings.json` e `Resources/Chaos.Settings.json`, ou no `IConfiguration` do Host.)*

### Detalhamento das Propriedades

- **`UseCosmosPhantom`**: (`false` por padrão) Liga ou desliga o emulador.
- **`CosmosDbEmulator.DatabaseName`**: Nome do banco de dados simulado.
- **`CosmosDbEmulator.Containers`**: Lista das coleções a serem criadas. Suporta `PartitionKeyPath`, `PartitionKeyPaths` (MultiHash até 3 níveis), `DefaultTimeToLive`, `Throughput`, `UniqueKeys`, `ComputedProperties` e `IndexingPolicy`.
- **`CosmosChaosEngineering`**: (Opcional — tudo `false` por padrão) Configuração das simulações de anomalias.

---

## 4. Auto-Seeding (Pasta `Resources/Seeds` na Raiz)

O SDK busca automaticamente os arquivos de seed na pasta **`Resources/Seeds`** localizada na raiz do projeto/solução (ao lado do `.sln`), no `ContentRootPath` do Host ou no diretório de saída (`AppContext.BaseDirectory`).

### Estrutura recomendada:
```text
projeto-x/
├── projeto-x.sln
├── CosmosPhantom.config.json
├── Resources/
│   └── Seeds/
│       ├── Usuarios.json
│       └── Produtos.json
└── src/
    └── ProjetoX.Host/
        └── ProjetoX.Host.csproj
```

1. Crie a pasta `Resources/Seeds` na raiz do projeto/solução.
2. Adicione arquivos JSON com exatamente o mesmo nome dos seus `Containers` (ex: `Usuarios.json`, `Produtos.json`).
3. O conteúdo do JSON **deve ser um array de objetos válidos** e conter a(s) propriedade(s) da Partition Key do container.

**Exemplo - `Resources/Seeds/Usuarios.json`:**
```json
[
  {
    "id": "1e5509ba-1c25-412e-990a-a5bb9b674b88",
    "nome": "Usuário Teste",
    "email": "teste@exemplo.com"
  },
  {
    "id": "59b4ef54-8c08-41d3-9824-3453b53fc065",
    "nome": "Admin",
    "email": "admin@exemplo.com"
  }
]
```

O *Seeder* agrupa as inserções em lotes usando `UpsertItemAsync` (idempotente) e avisa por logs caso falte algum arquivo.

---

## 5. Chaos Engineering (Testes de Resiliência)

Por padrão, **nenhuma falha ou latência é simulada**. Para testar políticas de Retry e Fallback (Polly), habilite explicitamente em `CosmosChaosEngineering` dentro do `CosmosPhantom.config.json`:

```json
"CosmosChaosEngineering": {
  "EnableRandomThrottling": true,
  "ThrottlingProbability": 0.5,
  "RetryAfterMs": 1000,
  "Simulate429_TooManyRequests": true
}
```
Isso fará o Emulador rejeitar aleatoriamente 50% das requisições devolvendo HTTP `429 Too Many Requests` com substatus `3200` e header `x-ms-retry-after-ms`.

### Parâmetros e Erros HTTP Suportados no `CosmosChaosEngineering`:
- **Latência e Probabilidade:**
  - `EnableRandomThrottling` (`false`), `ThrottlingProbability` (`0.2`)
  - `SimulateHighLatency` (`false`), `MinLatencyMs` (`500`), `MaxLatencyMs` (`2000`)
  - `RetryAfterMs` (`1000`)
- **Simulações de Erro HTTP:**
  - `Simulate429_TooManyRequests` (SubStatus `3200` + `x-ms-retry-after-ms`)
  - `Simulate503_ServiceUnavailable`
  - `Simulate408_RequestTimeout`
  - `Simulate403_Forbidden`
  - `Simulate401_Unauthorized`
  - `Simulate409_Conflict`
  - `Simulate413_EntityTooLarge`
  - `Simulate412_PreconditionFailed`
  - `Simulate400_BadRequest`

---

## Arquitetura

O SDK adota os princípios **SOLID**:
- Configuração suportada pelo **Options Pattern** nativo com `[Required]` DataAnnotations.
- Delegação de tarefas via interfaces injetáveis (`ICosmosDbSeederService`, `ICosmosDbManager`).
- Manipulação da injeção de dependência isolada, evitando poluir o WebApplication Builder do software principal.

👉 **[Ver Diagramas de Arquitetura Completos (Fluxo e Sequência)](Docs/Architecture.md)**

---