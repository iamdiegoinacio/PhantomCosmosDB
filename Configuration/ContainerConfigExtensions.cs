using Microsoft.Azure.Cosmos;
using System.Collections.ObjectModel;
using System;
using System.Collections.Generic;

namespace Cosmos.Phantom.SDK.Configuration;

public static class ContainerConfigExtensions
{
    public static ContainerProperties ToContainerProperties(this ContainerConfig containerConfig)
    {
        var containerProperties = new ContainerProperties(containerConfig.Name, containerConfig.PartitionKeyPath);

        if (containerConfig.IndexingPolicy != null)
        {
            containerProperties.IndexingPolicy.IndexingMode = containerConfig.IndexingPolicy.IndexingMode;
            containerProperties.IndexingPolicy.Automatic = containerConfig.IndexingPolicy.Automatic;

            foreach (var path in containerConfig.IndexingPolicy.IncludedPaths)
            {
                containerProperties.IndexingPolicy.IncludedPaths.Add(new IncludedPath { Path = path });
            }

            foreach (var path in containerConfig.IndexingPolicy.ExcludedPaths)
            {
                containerProperties.IndexingPolicy.ExcludedPaths.Add(new ExcludedPath { Path = path });
            }

            foreach (var compositeIndexList in containerConfig.IndexingPolicy.CompositeIndexes)
            {
                var cosmosCompositeList = new Collection<CompositePath>();
                foreach (var path in compositeIndexList)
                {
                    cosmosCompositeList.Add(new CompositePath
                    {
                        Path = path.Path,
                        Order = string.Equals(path.Order, "descending", StringComparison.OrdinalIgnoreCase) 
                            ? CompositePathSortOrder.Descending 
                            : CompositePathSortOrder.Ascending
                    });
                }
                containerProperties.IndexingPolicy.CompositeIndexes.Add(cosmosCompositeList);
            }
        }

        return containerProperties;
    }

    /// <summary>
    /// Retorna os caminhos efetivos de partition key (PartitionKeyPaths tem precedência; máximo 3 para MultiHash).
    /// </summary>
    public static IReadOnlyList<string> GetPartitionKeyPaths(this ContainerConfig containerConfig)
    {
        var paths = containerConfig.PartitionKeyPaths is { Count: > 0 }
            ? containerConfig.PartitionKeyPaths
            : [containerConfig.PartitionKeyPath];

        if (paths.Count > 3)
            throw new Cosmos.Phantom.SDK.Exceptions.CosmosDbEmulatorConfigurationException(
                $"O container '{containerConfig.Name}' define {paths.Count} PartitionKeyPaths; o máximo permitido (MultiHash) é 3.");

        return paths;
    }
}

