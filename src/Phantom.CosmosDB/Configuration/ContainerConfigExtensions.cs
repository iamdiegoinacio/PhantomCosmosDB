using Microsoft.Azure.Cosmos;
using System.Collections.ObjectModel;
using System;
using System.Collections.Generic;

namespace Phantom.CosmosDB.Configuration;

public static class ContainerConfigExtensions
{
    public static ContainerProperties ToContainerProperties(this ContainerConfig containerConfig)
    {
        var pkPaths = containerConfig.GetPartitionKeyPaths();
        var containerProperties = pkPaths.Count > 1
            ? new ContainerProperties(containerConfig.Name, pkPaths.ToList())
            : new ContainerProperties(containerConfig.Name, pkPaths[0]);

        containerProperties.DefaultTimeToLive = containerConfig.DefaultTimeToLive;

        var geospatialType = ParseGeospatialType(containerConfig.GeospatialType, containerConfig.Name);
        containerProperties.GeospatialConfig = new GeospatialConfig(geospatialType);

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

            foreach (var spatialIndex in containerConfig.IndexingPolicy.SpatialIndexes)
            {
                var spatialPath = new SpatialPath { Path = spatialIndex.Path };
                foreach (var typeName in spatialIndex.Types)
                {
                    spatialPath.SpatialTypes.Add(ParseSpatialType(typeName, containerConfig.Name));
                }

                if (spatialIndex.BoundingBox != null)
                {
                    if (spatialIndex.BoundingBox.Xmin >= spatialIndex.BoundingBox.Xmax ||
                        spatialIndex.BoundingBox.Ymin >= spatialIndex.BoundingBox.Ymax)
                    {
                        throw new Phantom.CosmosDB.Exceptions.CosmosDbEmulatorConfigurationException(
                            $"O container '{containerConfig.Name}' define BoundingBox inválido no índice espacial '{spatialIndex.Path}': Xmin < Xmax e Ymin < Ymax são obrigatórios.");
                    }

                    spatialPath.BoundingBox = new BoundingBoxProperties
                    {
                        Xmin = spatialIndex.BoundingBox.Xmin,
                        Ymin = spatialIndex.BoundingBox.Ymin,
                        Xmax = spatialIndex.BoundingBox.Xmax,
                        Ymax = spatialIndex.BoundingBox.Ymax
                    };
                }
                else if (geospatialType == GeospatialType.Geometry)
                {
                    throw new Phantom.CosmosDB.Exceptions.CosmosDbEmulatorConfigurationException(
                        $"O container '{containerConfig.Name}' usa GeospatialType='Geometry' e exige 'BoundingBox' no índice espacial '{spatialIndex.Path}'.");
                }

                containerProperties.IndexingPolicy.SpatialIndexes.Add(spatialPath);
            }
        }

        return containerProperties;
    }

    private static GeospatialType ParseGeospatialType(string? value, string containerName)
    {
        if (string.IsNullOrWhiteSpace(value) || string.Equals(value, "Geography", StringComparison.OrdinalIgnoreCase))
            return GeospatialType.Geography;

        if (string.Equals(value, "Geometry", StringComparison.OrdinalIgnoreCase))
            return GeospatialType.Geometry;

        throw new Phantom.CosmosDB.Exceptions.CosmosDbEmulatorConfigurationException(
            $"GeospatialType inválido '{value}' no container '{containerName}'. Valores suportados: 'Geography' ou 'Geometry'.");
    }

    private static SpatialType ParseSpatialType(string value, string containerName)
    {
        if (Enum.TryParse<SpatialType>(value, ignoreCase: true, out var parsed))
            return parsed;

        throw new Phantom.CosmosDB.Exceptions.CosmosDbEmulatorConfigurationException(
            $"SpatialType inválido '{value}' no container '{containerName}'. Valores suportados: Point, LineString, Polygon, MultiPolygon.");
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
            throw new Phantom.CosmosDB.Exceptions.CosmosDbEmulatorConfigurationException(
                $"O container '{containerConfig.Name}' define {paths.Count} PartitionKeyPaths; o máximo permitido (MultiHash) é 3.");

        return paths;
    }
}

