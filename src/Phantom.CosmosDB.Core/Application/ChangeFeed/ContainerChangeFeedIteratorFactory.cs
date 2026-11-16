#nullable enable
using Phantom.CosmosDB.Core.Application.Contracts;
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json;

namespace Phantom.CosmosDB.Core.Application.ChangeFeed;

/// <summary>
/// Creates typed and stream <see cref="FeedIterator"/> instances for container change feed queries.
/// </summary>
internal static class ContainerChangeFeedIteratorFactory
{
    public static FeedIterator<T> CreateTypedIterator<T>(
        IChangeFeedLog changeFeedLog,
        ChangeFeedStartFrom changeFeedStartFrom,
        ChangeFeedMode changeFeedMode,
        ChangeFeedRequestOptions? changeFeedRequestOptions,
        JsonSerializerSettings jsonSettings)
    {
        var pageSize = changeFeedRequestOptions?.PageSizeHint;
        var typeName = changeFeedStartFrom.GetType().Name;
        var feedRange = CorePortDefaults.SdkMapper.ExtractFeedRangeFromStartFrom(changeFeedStartFrom);

        if (typeName.Contains("Now", StringComparison.OrdinalIgnoreCase) ||
            typeName.Contains("Time", StringComparison.OrdinalIgnoreCase))
        {
            DateTimeOffset? capturedTimestamp = null;
            if (typeName.Contains("Now", StringComparison.OrdinalIgnoreCase))
            {
                capturedTimestamp = changeFeedLog.GetNowTimestamp();
            }
            else
            {
                var startTime = CorePortDefaults.SdkMapper.ExtractStartTime(changeFeedStartFrom);
                if (startTime.HasValue)
                    capturedTimestamp = new DateTimeOffset(startTime.Value, TimeSpan.Zero);
            }

            var isNowStart = typeName.Contains("Now", StringComparison.OrdinalIgnoreCase);
            var ts = capturedTimestamp;
            var capturedRange = feedRange;
            return CorePortDefaults.FeedIterators.CreateLazy<T>(() =>
            {
                var entries = changeFeedLog.GetFilteredEntries(ts, isNowStart);
                entries = CorePortDefaults.SdkMapper.FilterChangeFeedEntriesByFeedRange(entries, capturedRange);

                if (changeFeedMode == ChangeFeedMode.Incremental)
                {
                    entries = ProjectIncremental(entries);
                }

                return entries
                    .Select(entry => JsonConvert.DeserializeObject<T>(entry.Json, jsonSettings)!)
                    .ToList();
            }, pageSize);
        }

        var eagerEntries = changeFeedLog.GetSnapshot();
        eagerEntries = CorePortDefaults.SdkMapper.FilterChangeFeedEntriesByFeedRange(eagerEntries, feedRange);

        if (changeFeedMode == ChangeFeedMode.Incremental)
        {
            eagerEntries = ProjectIncremental(eagerEntries);
        }

        var items = eagerEntries
            .Select(entry => JsonConvert.DeserializeObject<T>(entry.Json, jsonSettings)!)
            .ToList();
        return CorePortDefaults.FeedIterators.CreateTyped(items, pageSize);
    }

    public static FeedIterator<T> CreateCheckpointIterator<T>(
        IChangeFeedLog changeFeedLog,
        long checkpoint,
        JsonSerializerSettings jsonSettings)
    {
        var items = changeFeedLog.GetSnapshot()
            .Skip((int)checkpoint)
            .Select(entry => JsonConvert.DeserializeObject<T>(entry.Json, jsonSettings)!)
            .ToList();
        return CorePortDefaults.FeedIterators.CreateTyped(items, null);
    }

    public static FeedIterator CreateStreamIterator(
        IChangeFeedLog changeFeedLog,
        ChangeFeedStartFrom changeFeedStartFrom,
        ChangeFeedMode changeFeedMode,
        ChangeFeedRequestOptions? changeFeedRequestOptions,
        Func<string> getSessionToken)
    {
        var feedRange = CorePortDefaults.SdkMapper.ExtractFeedRangeFromStartFrom(changeFeedStartFrom);
        var pageSizeHint = changeFeedRequestOptions?.PageSizeHint;
        var creationTime = DateTimeOffset.UtcNow;
        return CorePortDefaults.FeedIterators.CreateStreamFromFactory(() =>
        {
            var entries = changeFeedLog.FilterByStartFrom(changeFeedStartFrom, creationTime);
            entries = CorePortDefaults.SdkMapper.FilterChangeFeedEntriesByFeedRange(entries, feedRange);

            if (changeFeedMode == ChangeFeedMode.Incremental)
            {
                entries = ProjectIncremental(entries);
            }

            return entries.Select(entry => entry.Json).ToList();
        }, getSessionToken, 0, pageSizeHint);
    }

    private static List<(DateTimeOffset Timestamp, string Id, string PartitionKey, string Json, bool IsDelete)> ProjectIncremental(
        List<(DateTimeOffset Timestamp, string Id, string PartitionKey, string Json, bool IsDelete)> entries)
    {
        return entries
            .GroupBy(entry => (entry.Id, entry.PartitionKey))
            .Select(group => group.Last())
            .Where(entry => !entry.IsDelete)
            .ToList();
    }
}
