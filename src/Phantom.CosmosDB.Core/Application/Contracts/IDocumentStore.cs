#nullable enable
using System.Collections.Concurrent;
using Microsoft.Azure.Cosmos;

namespace Phantom.CosmosDB.Core.Application.Contracts;

/// <summary>
/// Port for thread-safe in-memory document storage, ETags, TTL eviction, and optimistic concurrency checks.
/// </summary>
internal interface IDocumentStore
{
    ConcurrentDictionary<(string Id, string PartitionKey), string> Items { get; }
    ConcurrentDictionary<(string Id, string PartitionKey), string> Etags { get; }
    ConcurrentDictionary<(string Id, string PartitionKey), DateTimeOffset> Timestamps { get; }
    object UniqueKeyWriteLock { get; }

    SemaphoreSlim GetOrAddLock((string Id, string PartitionKey) key);
    List<(string Id, string PartitionKey)> GetOrderedKeysSnapshot();
    void AddInsertionOrder((string Id, string PartitionKey) key);
    void RemoveInsertionOrder((string Id, string PartitionKey) key);
    void Clear(bool includeLocks = false);

    int GetActiveItemCount(int? defaultTimeToLive);
    bool IsExpired((string Id, string PartitionKey) key, int? defaultTimeToLive);
    void EvictIfExpired(
        (string Id, string PartitionKey) key,
        int? defaultTimeToLive,
        IReadOnlyList<string> partitionKeyPaths,
        IChangeFeedLog? changeFeedLog);

    string EnrichSystemProperties(string containerId, string json, string etag, DateTimeOffset timestamp);
    IEnumerable<string> GetAllItemsForPartition(
        QueryRequestOptions? requestOptions,
        IReadOnlyList<string> partitionKeyPaths,
        int? defaultTimeToLive);

    void CheckIfMatch(ItemRequestOptions? requestOptions, (string Id, string PartitionKey) key);
    void CheckIfNoneMatch(ItemRequestOptions? requestOptions, (string Id, string PartitionKey) key);
    void CheckIfNoneMatchForWrite(ItemRequestOptions? requestOptions, (string Id, string PartitionKey) key);
    bool CheckIfMatchStream(ItemRequestOptions? requestOptions, (string Id, string PartitionKey) key);
    bool CheckIfNoneMatchStream(ItemRequestOptions? requestOptions, (string Id, string PartitionKey) key);

    void TrackWrite((string Id, string PartitionKey) key);

    Dictionary<(string Id, string PartitionKey), string> SnapshotItems();
    Dictionary<(string Id, string PartitionKey), string> SnapshotEtags();
    Dictionary<(string Id, string PartitionKey), DateTimeOffset> SnapshotTimestamps();
    void RestoreSnapshot(
        Dictionary<(string Id, string PartitionKey), string> itemsSnapshot,
        Dictionary<(string Id, string PartitionKey), string> etagsSnapshot,
        Dictionary<(string Id, string PartitionKey), DateTimeOffset> timestampsSnapshot,
        int changeFeedCount,
        IReadOnlySet<(string Id, string PartitionKey)> touchedKeys,
        IChangeFeedLog changeFeedLog);
}
