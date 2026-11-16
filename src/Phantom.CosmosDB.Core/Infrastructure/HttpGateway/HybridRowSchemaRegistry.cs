#nullable enable
using System.Reflection;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Serialization.HybridRow;
using Microsoft.Azure.Cosmos.Serialization.HybridRow.Layouts;
using HybridRowSchemas = Microsoft.Azure.Cosmos.Serialization.HybridRow.Schemas;

namespace Phantom.CosmosDB.Core.Infrastructure.HttpGateway;

/// <summary>
/// Provides HybridRow <see cref="Layout"/> and <see cref="LayoutResolverNamespace"/> definitions
/// for Cosmos DB Transactional Batch requests and responses, with reflection fallback.
/// </summary>
internal static class HybridRowSchemaRegistry
{
    public static class BatchOperationTypes
    {
        public const int Create = 0;
        public const int Patch = 1;
        public const int Read = 2;
        public const int Delete = 4;
        public const int Replace = 5;
        public const int Upsert = 20;
    }

    private static readonly Lazy<(Layout OperationLayout, Layout ResultLayout, LayoutResolverNamespace Resolver)> Schemas =
        new(BuildSchemas);

    public static Layout OperationLayout => Schemas.Value.OperationLayout;
    public static Layout ResultLayout => Schemas.Value.ResultLayout;
    public static LayoutResolverNamespace Resolver => Schemas.Value.Resolver;

    public static bool BatchSchemasAvailable
    {
        get
        {
            _ = OperationLayout;
            return true;
        }
    }

    private static (Layout, Layout, LayoutResolverNamespace) BuildSchemas()
    {
        try
        {
            return BuildSchemasFromDefinition();
        }
        catch (Exception ex)
        {
            try
            {
                return BuildSchemasFromReflection();
            }
            catch (Exception reflectionEx)
            {
                throw new InvalidOperationException(
                    $"Failed to initialise HybridRow batch schemas. " +
                    $"Self-built schema error: {ex.Message}. " +
                    $"Reflection fallback error: {reflectionEx.Message}. " +
                    $"This may indicate an incompatible Cosmos SDK version " +
                    $"({typeof(CosmosClient).Assembly.GetName().Version}). " +
                    $"Batch operations will not work.", ex);
            }
        }
    }

    private static (Layout, Layout, LayoutResolverNamespace) BuildSchemasFromDefinition()
    {
        var batchOperationSchema = new HybridRowSchemas.Schema
        {
            Name = "BatchOperation",
            SchemaId = new SchemaId(2145473648),
            Type = HybridRowSchemas.TypeKind.Schema,
            Properties =
            {
                new HybridRowSchemas.Property { Path = "operationType", PropertyType = new HybridRowSchemas.PrimitivePropertyType(HybridRowSchemas.TypeKind.Int32) { Storage = HybridRowSchemas.StorageKind.Fixed, Nullable = true } },
                new HybridRowSchemas.Property { Path = "resourceType", PropertyType = new HybridRowSchemas.PrimitivePropertyType(HybridRowSchemas.TypeKind.Int32) { Storage = HybridRowSchemas.StorageKind.Fixed, Nullable = true } },
                new HybridRowSchemas.Property { Path = "partitionKey", PropertyType = new HybridRowSchemas.PrimitivePropertyType(HybridRowSchemas.TypeKind.Utf8) { Storage = HybridRowSchemas.StorageKind.Variable, Nullable = true } },
                new HybridRowSchemas.Property { Path = "effectivePartitionKey", PropertyType = new HybridRowSchemas.PrimitivePropertyType(HybridRowSchemas.TypeKind.Binary) { Storage = HybridRowSchemas.StorageKind.Variable, Nullable = true } },
                new HybridRowSchemas.Property { Path = "id", PropertyType = new HybridRowSchemas.PrimitivePropertyType(HybridRowSchemas.TypeKind.Utf8) { Storage = HybridRowSchemas.StorageKind.Variable, Nullable = true } },
                new HybridRowSchemas.Property { Path = "binaryId", PropertyType = new HybridRowSchemas.PrimitivePropertyType(HybridRowSchemas.TypeKind.Binary) { Storage = HybridRowSchemas.StorageKind.Variable, Nullable = true } },
                new HybridRowSchemas.Property { Path = "resourceBody", PropertyType = new HybridRowSchemas.PrimitivePropertyType(HybridRowSchemas.TypeKind.Binary) { Storage = HybridRowSchemas.StorageKind.Variable, Nullable = true } },
                new HybridRowSchemas.Property { Path = "indexingDirective", PropertyType = new HybridRowSchemas.PrimitivePropertyType(HybridRowSchemas.TypeKind.Utf8) { Storage = HybridRowSchemas.StorageKind.Sparse, Nullable = true } },
                new HybridRowSchemas.Property { Path = "ifMatch", PropertyType = new HybridRowSchemas.PrimitivePropertyType(HybridRowSchemas.TypeKind.Utf8) { Storage = HybridRowSchemas.StorageKind.Sparse, Nullable = true } },
                new HybridRowSchemas.Property { Path = "ifNoneMatch", PropertyType = new HybridRowSchemas.PrimitivePropertyType(HybridRowSchemas.TypeKind.Utf8) { Storage = HybridRowSchemas.StorageKind.Sparse, Nullable = true } },
                new HybridRowSchemas.Property { Path = "timeToLiveInSeconds", PropertyType = new HybridRowSchemas.PrimitivePropertyType(HybridRowSchemas.TypeKind.Int32) { Storage = HybridRowSchemas.StorageKind.Sparse, Nullable = true } },
                new HybridRowSchemas.Property { Path = "minimalReturnPreference", PropertyType = new HybridRowSchemas.PrimitivePropertyType(HybridRowSchemas.TypeKind.Boolean) { Storage = HybridRowSchemas.StorageKind.Sparse, Nullable = true } },
            },
        };

        var batchResultSchema = new HybridRowSchemas.Schema
        {
            Name = "BatchResult",
            SchemaId = new SchemaId(2145473649),
            Type = HybridRowSchemas.TypeKind.Schema,
            Properties =
            {
                new HybridRowSchemas.Property { Path = "statusCode", PropertyType = new HybridRowSchemas.PrimitivePropertyType(HybridRowSchemas.TypeKind.Int32) { Storage = HybridRowSchemas.StorageKind.Fixed, Nullable = true } },
                new HybridRowSchemas.Property { Path = "subStatusCode", PropertyType = new HybridRowSchemas.PrimitivePropertyType(HybridRowSchemas.TypeKind.Int32) { Storage = HybridRowSchemas.StorageKind.Fixed, Nullable = true } },
                new HybridRowSchemas.Property { Path = "eTag", PropertyType = new HybridRowSchemas.PrimitivePropertyType(HybridRowSchemas.TypeKind.Utf8) { Storage = HybridRowSchemas.StorageKind.Variable, Nullable = true } },
                new HybridRowSchemas.Property { Path = "resourceBody", PropertyType = new HybridRowSchemas.PrimitivePropertyType(HybridRowSchemas.TypeKind.Binary) { Storage = HybridRowSchemas.StorageKind.Variable, Nullable = true } },
                new HybridRowSchemas.Property { Path = "retryAfterMilliseconds", PropertyType = new HybridRowSchemas.PrimitivePropertyType(HybridRowSchemas.TypeKind.UInt32) { Storage = HybridRowSchemas.StorageKind.Sparse, Nullable = true } },
                new HybridRowSchemas.Property { Path = "requestCharge", PropertyType = new HybridRowSchemas.PrimitivePropertyType(HybridRowSchemas.TypeKind.Float64) { Storage = HybridRowSchemas.StorageKind.Sparse, Nullable = true } },
            },
        };

        var ns = new HybridRowSchemas.Namespace
        {
            Name = "Microsoft.Azure.Cosmos.BatchApi",
            Version = HybridRowSchemas.SchemaLanguageVersion.V2,
            Schemas = { batchOperationSchema, batchResultSchema },
        };

        var resolver = new LayoutResolverNamespace(ns, SystemSchema.LayoutResolver);
        var opLayout = resolver.Resolve(batchOperationSchema.SchemaId);
        var resultLayout = resolver.Resolve(batchResultSchema.SchemaId);
        return (opLayout, resultLayout, resolver);
    }

    private static (Layout, Layout, LayoutResolverNamespace) BuildSchemasFromReflection()
    {
        var bspType = typeof(CosmosClient).Assembly.GetType("Microsoft.Azure.Cosmos.BatchSchemaProvider")
            ?? throw new InvalidOperationException(
                "Cannot find BatchSchemaProvider in the Cosmos SDK assembly. " +
                "The SDK may have reorganised its internal types.");
        var flags = BindingFlags.Public | BindingFlags.Static;
        var opLayout = (Layout)(bspType.GetProperty("BatchOperationLayout", flags)?.GetValue(null)
            ?? throw new InvalidOperationException("BatchSchemaProvider.BatchOperationLayout not found."));
        var resultLayout = (Layout)(bspType.GetProperty("BatchResultLayout", flags)?.GetValue(null)
            ?? throw new InvalidOperationException("BatchSchemaProvider.BatchResultLayout not found."));
        var resolver = (LayoutResolverNamespace)(bspType.GetProperty("BatchLayoutResolver", flags)?.GetValue(null)
            ?? throw new InvalidOperationException("BatchSchemaProvider.BatchLayoutResolver not found."));
        return (opLayout, resultLayout, resolver);
    }
}
