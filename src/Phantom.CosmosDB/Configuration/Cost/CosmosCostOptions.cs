namespace Phantom.CosmosDB.Configuration.Cost;

/// <summary>
/// Opções de custo e região Azure para estimativa financeira em tempo real e no Simulador de Custos.
/// </summary>
public sealed class CosmosCostOptions
{
    /// <summary>Região Azure (ex.: <c>brazil-south</c>, <c>east-us-2</c>). Padrão: <c>brazil-south</c>.</summary>
    public string Region { get; set; } = CosmosRegionalPricingCatalog.DefaultRegionId;

    /// <summary>Modalidade de cobrança padrão (<c>Serverless</c>, <c>Provisioned</c> ou <c>Autoscale</c>).</summary>
    public string BillingMode { get; set; } = "Serverless";

    /// <summary>Quando <c>true</c>, registra no console (<c>ILogger</c>) o custo estimado (RU e USD) das operações/queries.</summary>
    public bool LogQueryCost { get; set; }

    /// <summary>Considera a camada gratuita vitalícia (1.000 RU/s + 25 GB) em Provisioned/Autoscale.</summary>
    public bool ApplyFreeTier { get; set; }

    /// <summary>Nível de consistência (<c>Session</c>, <c>Eventual</c>, <c>Strong</c>, <c>BoundedStaleness</c>).</summary>
    public string ConsistencyLevel { get; set; } = "Session";

    /// <summary>Adiciona 25% ao custo de throughput/RUs por zonas de disponibilidade.</summary>
    public bool AvailabilityZones { get; set; }

    /// <summary>Quantidade de regiões adicionais de leitura (multi-região).</summary>
    public int AdditionalRegionsCount { get; set; }

    /// <summary>Habilita cobrança de múltiplas regiões de gravação.</summary>
    public bool MultiRegionWrites { get; set; }
}
