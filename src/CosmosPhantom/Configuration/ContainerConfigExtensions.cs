using Microsoft.Azure.Cosmos;
using System.Collections.ObjectModel;
using System;
using System.Collections.Generic;

namespace CosmosPhantom.Configuration;

public static class ContainerConfigExtensions
{
    public static ContainerProperties ToContainerProperties(this ContainerConfig containerConfig)
    {
        var pkPaths = containerConfig.GetPartitionKeyPaths();
        var containerProperties = pkPaths.Count > 1
            ? new ContainerProperties(containerConfig.Name, pkPaths.ToList())
            : new ContainerProperties(containerConfig.Name, pkPaths[0]);

        containerProperties.DefaultTimeToLive = containerConfig.DefaultTimeToLive;

        if (containerConfig.UniqueKeys is { Count: > 0 })
        {
            foreach (var keyPaths in containerConfig.UniqueKeys)
            {
                var uniqueKey = new UniqueKey();
                foreach (var p in keyPaths)
                {
                    uniqueKey.Paths.Add(p);
                }
                containerProperties.UniqueKeyPolicy.UniqueKeys.Add(uniqueKey);
            }
        }

        if (containerConfig.ComputedProperties is { Count: > 0 })
        {
            foreach (var cp in containerConfig.ComputedProperties)
            {
                containerProperties.ComputedProperties.Add(new ComputedProperty
                {
                    Name = cp.Name,
                    Query = cp.Query
                });
            }
        }

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
            throw new CosmosPhantom.Exceptions.CosmosDbEmulatorConfigurationException(
                $"O container '{containerConfig.Name}' define {paths.Count} PartitionKeyPaths; o máximo permitido (MultiHash) é 3.");

        return paths;
    }
}

