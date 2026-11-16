using System;
using Microsoft.Azure.Cosmos;

namespace CosmosPhantom.Configuration;

/// <summary>
/// Opções de conexão da aplicação consumidora para <c>AddCosmosDb</c>.
/// Permite alternar entre Azure Cosmos DB real e CosmosPhantom apenas pela Connection String.
/// </summary>
public sealed class CosmosDbConnectionOptions
{
    /// <summary>Nome da chave em <c>ConnectionStrings</c>. Padrão: <c>"CosmosDb"</c>.</summary>
    public string ConnectionStringName { get; set; } = "CosmosDb";

    /// <summary>Connection String explícita (opcional; sobrescreve <c>IConfiguration</c>).</summary>
    public string? ConnectionString { get; set; }

    /// <summary>Nome do banco de dados que a aplicação consome (opcional).</summary>
    public string? DatabaseName { get; set; }

    /// <summary>Nome do container principal que a aplicação consome (opcional).</summary>
    public string? ContainerName { get; set; }

    /// <summary>Instância opcional de <see cref="CosmosClientOptions"/> aplicada ao cliente real.</summary>
    public CosmosClientOptions? CosmosClientOptions { get; set; }

    /// <summary>Callback opcional para customizar <see cref="CosmosClientOptions"/>.</summary>
    public Action<CosmosClientOptions>? ConfigureClientOptions { get; set; }

    /// <summary>
    /// Permite explicitamente usar o emulador em memória em ambiente <c>Production</c>.
    /// Padrão: <c>false</c> (fail-fast para evitar uso acidental em produção).
    /// </summary>
    public bool AllowPhantomInProduction { get; set; }
}
