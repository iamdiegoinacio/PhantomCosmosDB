#nullable enable
using Microsoft.Azure.Cosmos;

namespace Phantom.CosmosDB.Core.Application.Contracts;

/// <summary>
/// Port for the append-only in-memory change feed log and session token sequence tracker.
/// </summary>
internal interface IChangeFeedLog
{
    int MaxChangeFeedSize { get; set; }
    string CurrentSessionToken { get; }
    int Count { get; }

    void Clear();
    void RecordChangeFeed(string id, string? partitionKey, string json, bool isDelete = false);
    void RecordDeleteTombstone(
        string id,
        string? pk,
        IReadOnlyList<string> partitionKeyPaths,
        PartitionKey partitionKey = default,
        bool isTtlEviction = false);

    DateTimeOffset GetNowTimestamp();
    List<(DateTimeOffset Timestamp, string Id, string PartitionKey, string Json, bool IsDelete)> GetSnapshot();
    List<(DateTimeOffset Timestamp, string Id, string PartitionKey, string Json, bool IsDelete)> GetSnapshotUpTo(DateTimeOffset cutoff);
    List<(DateTimeOffset Timestamp, string Id, string PartitionKey, string Json, bool IsDelete)> GetFilteredEntries(
        DateTimeOffset? timestamp,
        bool isNowStart);
    List<(DateTimeOffset Timestamp, string Id, string PartitionKey, string Json, bool IsDelete)> FilterByStartFrom(
        ChangeFeedStartFrom startFrom,
        DateTimeOffset? creationTime = null);
    void TrimToCount(int targetCount);
}
