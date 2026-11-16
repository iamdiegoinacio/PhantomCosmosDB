using Microsoft.Azure.Cosmos;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Phantom.CosmosDB.Configuration;

public class CosmosDbEmulatorConfig
{
    [Required(ErrorMessage = "A propriedade 'DatabaseName' é obrigatória na configuração do CosmosDbEmulator.")]
    public required string DatabaseName { get; set; }

    [Required]
    [MinLength(1, ErrorMessage = "A lista de 'Containers' está vazia ou ausente. Ao menos um container deve ser configurado.")]
    public List<ContainerConfig> Containers { get; set; } = [];
}

public class ContainerConfig
{
    [Required(ErrorMessage = "Todos os containers configurados devem possuir 'Name'.")]
    public required string Name { get; set; }

    [Required(ErrorMessage = "Todos os containers configurados devem possuir 'PartitionKeyPath'.")]
    public string PartitionKeyPath { get; set; } = "/id";

    /// <summary>Opcional. Caminhos de hierarchical partition key (MultiHash, máximo 3). Quando preenchido, substitui PartitionKeyPath.</summary>
    public List<string>? PartitionKeyPaths { get; set; }

    /// <summary>Opcional. TTL padrão em segundos (-1 = habilitado sem expiração padrão).</summary>
    public int? DefaultTimeToLive { get; set; }

    /// <summary>Opcional. Throughput manual (RU/s) do container.</summary>
    public int? Throughput { get; set; }

    /// <summary>Opcional. Políticas de chaves únicas; cada item é uma lista de caminhos.</summary>
    public List<List<string>>? UniqueKeys { get; set; }

    /// <summary>Opcional. Propriedades computadas do container.</summary>
    public List<EmulatorComputedProperty>? ComputedProperties { get; set; }
    
    public EmulatorIndexingPolicy? IndexingPolicy { get; set; }
}

public class EmulatorIndexingPolicy
{
    public IndexingMode IndexingMode { get; set; } = IndexingMode.Consistent;
    public bool Automatic { get; set; } = true;
    public List<string> IncludedPaths { get; set; } = [];
    public List<string> ExcludedPaths { get; set; } = [];
    public List<List<EmulatorCompositePath>> CompositeIndexes { get; set; } = [];
}

public class EmulatorComputedProperty
{
    public required string Name { get; set; }
    public required string Query { get; set; }
}

public class EmulatorCompositePath
{
    public required string Path { get; set; }
    public string Order { get; set; } = "ascending";
}

