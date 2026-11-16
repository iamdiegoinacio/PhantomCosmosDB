#nullable enable
using System.Collections.Concurrent;
using System.Net;
using Phantom.CosmosDB.Core.Application.Crud;
using Phantom.CosmosDB.Core.Application.Patch;
using Phantom.CosmosDB.Core.Application.Query;
using Phantom.CosmosDB.Core.Application.Scripts;
using Phantom.CosmosDB.Core.Domain.Primitives;
using Phantom.CosmosDB.Core.Domain.Validation;
using Phantom.CosmosDB.Core.Infrastructure.Persistence;
using Phantom.CosmosDB.Core.Infrastructure.Storage;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Scripts;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using NSubstitute;
using static Phantom.CosmosDB.Core.Domain.Primitives.ContainerConstants;
using CosmosResponseFactory = Phantom.CosmosDB.Core.Infrastructure.SdkInterop.CosmosResponseFactory;

namespace Phantom.CosmosDB.Core;

/// <summary>
/// In-memory implementation of <see cref="Container"/> for testing. Supports the full
/// breadth of the Cosmos DB SDK surface area: CRUD, SQL queries (40+ built-in functions),
/// LINQ, transactional batches, change feed, patch operations, ETags, TTL, ReadMany,
/// stream APIs, stored procedures, UDFs, state persistence, and more.
/// </summary>
internal partial class InMemoryContainer : Container, IContainerTestSetup
{
    private static readonly JsonSerializerSettings JsonSettings = new()
    {
        TypeNameHandling = TypeNameHandling.None,
        DateParseHandling = DateParseHandling.None,
        ContractResolver = new DefaultContractResolver(),
        Converters = { new StringEnumConverter { AllowIntegerValues = true } }
    };

    private readonly InMemoryItemStore _store = new();
    private readonly InMemoryChangeFeedLog _changeFeedLog = new();
    private readonly ScriptCatalog _scriptCatalog = new();
    private readonly ContainerPersistenceService _persistence = new();
    private readonly ComputedPropertyCompiler _computedProperties = new();
    private readonly Scripts _scripts;

    internal int _throughput = 400;
    internal bool _isDeleted;

    private ContainerProperties _containerProperties;
    private Database? _cachedDatabase;
    private string? _parentDatabaseId;
    private int? _defaultTimeToLive;

    private IndexingPolicy _indexingPolicy = new()
    {
        Automatic = true,
        IndexingMode = IndexingMode.Consistent,
        IncludedPaths = { new IncludedPath { Path = "/*" } },
        ExcludedPaths = { new ExcludedPath { Path = "/\"_etag\"/?" } },
    };

    /// <summary>Optional JavaScript trigger engine set via <c>UseJsTriggers()</c>.</summary>
    public IJsTriggerEngine JsTriggerEngine { get; set; } = null!;

    /// <summary>Optional JavaScript stored procedure engine set via <c>UseJsStoredProcedures()</c>.</summary>
    public ISprocEngine SprocEngine { get; set; } = null!;

    internal Action? OnDeleted { get; set; }

    public InMemoryContainer(string id = "in-memory-container", string partitionKeyPath = "/id")
        : this(new ContainerProperties(id, partitionKeyPath))
    {
    }

    public InMemoryContainer(string id, IReadOnlyList<string> partitionKeyPaths)
        : this(new ContainerProperties(id, partitionKeyPaths))
    {
    }

    public InMemoryContainer(ContainerProperties containerProperties)
    {
        DocumentValidator.ValidateComputedProperties(containerProperties);
        _containerProperties = containerProperties;
        Id = containerProperties.Id;
        _defaultTimeToLive = containerProperties.DefaultTimeToLive;
        if (_defaultTimeToLive == 0)
        {
            throw InMemoryCosmosException.Create(
                "The value of DefaultTimeToLive must be either null, -1, or a positive integer.",
                HttpStatusCode.BadRequest, 0, Guid.NewGuid().ToString(), SyntheticRequestCharge);
        }

        var paths = containerProperties.PartitionKeyPaths;
        PartitionKeyPaths = paths is null || paths.Count == 0
            ? new[] { containerProperties.PartitionKeyPath ?? "/id" }
            : paths;
        _scripts = new InMemoryScripts(this, _scriptCatalog);
    }

    public override string Id { get; } = default!;

    public override Database Database
    {
        get
        {
            if (_cachedDatabase is null)
            {
                var db = Substitute.For<Database>();
                db.Id.Returns(_parentDatabaseId ?? Id);
                _cachedDatabase = db;
            }
            return _cachedDatabase;
        }
    }

    internal void SetParentDatabase(string databaseId) => _parentDatabaseId = databaseId;
    internal bool ExplicitlyCreated { get; set; } = true;
    public override Conflicts Conflicts => Substitute.For<Conflicts>();
    public override Scripts Scripts => _scripts;

    public int? DefaultTimeToLive
    {
        get => _defaultTimeToLive;
        set
        {
            if (value == 0)
            {
                throw InMemoryCosmosException.Create(
                    "The value of DefaultTimeToLive must be either null, -1, or a positive integer.",
                    HttpStatusCode.BadRequest, 0, Guid.NewGuid().ToString(), SyntheticRequestCharge);
            }
            _defaultTimeToLive = value;
        }
    }

    public UniqueKeyPolicy UniqueKeyPolicy
    {
        get => _containerProperties.UniqueKeyPolicy;
        set => _containerProperties.UniqueKeyPolicy = value ?? new UniqueKeyPolicy();
    }

    internal ContainerProperties ContainerPropertiesInternal => _containerProperties;
    internal void ResetComputedPropertiesCache() => _computedProperties.Reset();
    public IReadOnlyList<string> PartitionKeyPaths { get; }

    public int MaxChangeFeedSize
    {
        get => _changeFeedLog.MaxChangeFeedSize;
        set => _changeFeedLog.MaxChangeFeedSize = value;
    }

    internal string CurrentSessionToken => _changeFeedLog.CurrentSessionToken;
    public int FeedRangeCount { get; set; } = 1;
    public string StateFilePath { get; set; } = null!;

    public IndexingPolicy IndexingPolicy
    {
        get => _indexingPolicy;
        set
        {
            _indexingPolicy = value;
            DocumentValidator.EnsureEtagExcludedPath(_indexingPolicy);
        }
    }

    public int ItemCount => _store.GetActiveItemCount(DefaultTimeToLive);
    public long GetChangeFeedCheckpoint() => _changeFeedLog.Count;

    // ─── Internal Helpers & Context Builders ──────────────────────────────────

    internal static string JTokenToTypedKey(JToken token)
        => PartitionKeyProcessor.JTokenToTypedKey(token)!;

    internal static string BuildSelectTokenPath(IEnumerable<string> segments)
        => PatchOperationApplier.BuildSelectTokenPath(segments);

    internal ResponseMessage CreateResponseMessage(
        HttpStatusCode statusCode,
        string? json = null,
        string? etag = null,
        int subStatusCode = 0)
        => CosmosResponseFactory.CreateResponseMessage(statusCode, CurrentSessionToken, json, etag, subStatusCode);

    internal Dictionary<(string Id, string PartitionKey), string> SnapshotItems() => _store.SnapshotItems();
    internal Dictionary<(string Id, string PartitionKey), string> SnapshotEtags() => _store.SnapshotEtags();
    internal Dictionary<(string Id, string PartitionKey), DateTimeOffset> SnapshotTimestamps() => _store.SnapshotTimestamps();
    internal int GetChangeFeedCount() => _changeFeedLog.Count;
    internal List<(DateTimeOffset Timestamp, string Id, string PartitionKey, string Json, bool IsDelete)> GetChangeFeedEntries()
        => _changeFeedLog.GetSnapshot();
    internal void BeginBatchTracking() => InMemoryItemStore.BeginBatchTracking();
    internal HashSet<(string Id, string PartitionKey)> EndBatchTracking() => InMemoryItemStore.EndBatchTracking();

    internal void RestoreSnapshot(
        Dictionary<(string Id, string PartitionKey), string> itemsSnapshot,
        Dictionary<(string Id, string PartitionKey), string> etagsSnapshot,
        Dictionary<(string Id, string PartitionKey), DateTimeOffset> timestampsSnapshot,
        int changeFeedCount,
        IReadOnlySet<(string Id, string PartitionKey)> touchedKeys)
        => _store.RestoreSnapshot(itemsSnapshot, etagsSnapshot, timestampsSnapshot, changeFeedCount, touchedKeys, _changeFeedLog);

    private PartitionScopedCollectionContext CreateCollectionContextFromDoc(JObject jObj)
    {
        var pkValue = PartitionKeyProcessor.ExtractPartitionKeyValueCore(null, jObj, PartitionKeyPaths);
        var pk = PartitionKeyProcessor.TypedKeyToPartitionKey(pkValue);
        return new PartitionScopedCollectionContext(this, pk);
    }

    private JObject ExecutePreTriggers(ItemRequestOptions? requestOptions, JObject jObj, string operationName)
        => TriggerExecutionService.ExecutePreTriggers(
            requestOptions, jObj, operationName, _scriptCatalog, PartitionKeyPaths, JsTriggerEngine, CreateCollectionContextFromDoc);

    private JObject? ExecutePostTriggers(ItemRequestOptions? requestOptions, JObject committedDoc, string operationName)
        => TriggerExecutionService.ExecutePostTriggers(
            requestOptions, committedDoc, operationName, _scriptCatalog, JsTriggerEngine, CreateCollectionContextFromDoc);

    private IEnumerable<string> GetAllItemsForPartition(QueryRequestOptions? requestOptions)
        => _store.GetAllItemsForPartition(requestOptions, PartitionKeyPaths, DefaultTimeToLive);

    private DocumentOperationContext CreateOperationContext()
        => new(
            _store,
            _changeFeedLog,
            Id,
            PartitionKeyPaths,
            _containerProperties,
            DefaultTimeToLive,
            JsonSettings,
            ExecutePreTriggers,
            ExecutePostTriggers);

    private QueryExecutionContext CreateQueryContext(IDictionary<string, object?> parameters)
        => new(
            parameters,
            _scriptCatalog.UserDefinedFunctions,
            _scriptCatalog.UdfProperties,
            JsTriggerEngine as IJsUdfEngine,
            ScriptCatalog.UdfPlaceholder,
            geospatialType: _containerProperties.GeospatialConfig?.GeospatialType ?? GeospatialType.Geography);

    private List<string> FilterItemsByQuery(
        string queryText,
        IDictionary<string, object?> parameters,
        QueryRequestOptions? requestOptions,
        IEnumerable<string>? preFilteredItems = null)
    {
        IEnumerable<string> items = preFilteredItems ?? GetAllItemsForPartition(requestOptions);
        var context = CreateQueryContext(parameters);
        return CosmosQueryPipeline.Execute(
            queryText,
            items,
            context,
            _computedProperties.GetCompiled(_containerProperties),
            _containerProperties.IndexingPolicy,
            requestOptions?.EnableScanInQuery ?? false);
    }
}
