#nullable enable
using Microsoft.Azure.Cosmos;
using Phantom.CosmosDB.Core.Domain.Validation;
using Phantom.CosmosDB.Core.Infrastructure.Storage;

namespace Phantom.CosmosDB.Core.Infrastructure.Persistence;

/// <summary>
/// Coordinates state export, import, file persistence, and point-in-time restore for an <see cref="InMemoryContainer"/>.
/// </summary>
internal sealed class ContainerPersistenceService
{
    public void SaveOnDispose(string? stateFilePath, Func<string> exportJson)
    {
        if (stateFilePath is null) return;

        var dir = Path.GetDirectoryName(stateFilePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(stateFilePath, exportJson());
    }

    public void LoadPersistedState(string? stateFilePath, Action<string> importJson)
    {
        if (stateFilePath is null)
            throw new InvalidOperationException("StateFilePath must be set before calling LoadPersistedState().");

        if (File.Exists(stateFilePath))
            importJson(File.ReadAllText(stateFilePath));
    }

    public string ExportState(InMemoryItemStore store, int? defaultTimeToLive)
        => ContainerStateSerializer.ExportState(
            store.GetOrderedKeysSnapshot(),
            store.Items,
            key => store.IsExpired(key, defaultTimeToLive));

    public void ImportState(
        string json,
        string containerId,
        InMemoryItemStore store,
        IReadOnlyList<string> partitionKeyPaths,
        UniqueKeyPolicy uniqueKeyPolicy,
        Action clearAll)
        => ContainerStateSerializer.ImportState(
            json,
            clearAll,
            jObj => PartitionKeyProcessor.ExtractPartitionKeyValue(null, jObj, partitionKeyPaths),
            (jObj, pk) => UniqueKeyValidator.ValidateUniqueKeys(uniqueKeyPolicy, store.Items, jObj, pk),
            (docJson, etag, ts) => InMemoryItemStore.EnrichWithSystemProperties(containerId, docJson, etag, ts),
            (key, enriched, etag, now) =>
            {
                store.Etags[key] = etag;
                store.Timestamps[key] = now;
                store.Items[key] = enriched;
                store.AddInsertionOrder(key);
            });

    public void RestoreToPointInTime(
        DateTimeOffset pointInTime,
        string containerId,
        InMemoryItemStore store,
        InMemoryChangeFeedLog changeFeedLog)
        => ContainerStateSerializer.RestoreToPointInTime(
            pointInTime,
            changeFeedLog,
            () => store.Clear(includeLocks: true),
            (docJson, etag, ts) => InMemoryItemStore.EnrichWithSystemProperties(containerId, docJson, etag, ts),
            (key, enriched, etag, ts) =>
            {
                store.Items[key] = enriched;
                store.Etags[key] = etag;
                store.Timestamps[key] = ts;
            },
            insertionOrderKeys =>
            {
                lock (store.InsertionOrderLock)
                {
                    foreach (var key in insertionOrderKeys)
                    {
                        if (store.Items.ContainsKey(key))
                            store.InsertionOrder.Add(key);
                    }
                }
            });
}
