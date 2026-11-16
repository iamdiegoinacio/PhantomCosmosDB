#nullable enable
using System.Net;
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json;
using Phantom.CosmosDB.Core.Application.Contracts;
using Phantom.CosmosDB.Core.Infrastructure.FeedIterators;

namespace Phantom.CosmosDB.Core.Infrastructure.SdkInterop;

/// <summary>
/// Infrastructure adapter implementing <see cref="ISdkResponseMapper"/> over
/// <see cref="CosmosResponseFactory"/>, <see cref="CosmosSdkIntrospector"/>, and <see cref="FeedRangeCalculator"/>.
/// </summary>
internal sealed class SdkResponseMapperAdapter : ISdkResponseMapper
{
    public CosmosDiagnostics DefaultDiagnostics => InMemoryFeedDiagnostics.Instance;

    public ContainerResponse CreateContainerResponse(
        Container container,
        ContainerProperties? properties,
        HttpStatusCode statusCode) =>
        CosmosResponseFactory.CreateContainerResponse(container, properties, statusCode);

    public ThroughputResponse CreateThroughputResponse(
        ThroughputProperties properties,
        HttpStatusCode statusCode = HttpStatusCode.OK) =>
        CosmosResponseFactory.CreateThroughputResponse(properties, statusCode);

    public DatabaseResponse CreateDatabaseResponse(
        Database database,
        HttpStatusCode statusCode,
        DatabaseProperties? properties = null) =>
        CosmosResponseFactory.CreateDatabaseResponse(database, statusCode, properties);

    public ResponseMessage CreateResponseMessage(
        HttpStatusCode statusCode,
        string sessionToken,
        string? json = null,
        string? etag = null,
        int subStatusCode = 0) =>
        CosmosResponseFactory.CreateResponseMessage(statusCode, sessionToken, json, etag, subStatusCode);

    public ItemResponse<T> CreateItemResponse<T>(
        T item,
        HttpStatusCode statusCode,
        string sessionToken,
        string? etag = null,
        bool suppressContent = false) =>
        CosmosResponseFactory.CreateItemResponse(item, statusCode, sessionToken, etag, suppressContent);

    public Dictionary<string, object?> ExtractQueryParameters(QueryDefinition queryDefinition) =>
        CosmosSdkIntrospector.ExtractQueryParameters(queryDefinition);

    public object? GetPatchValue(PatchOperation operation) =>
        CosmosSdkIntrospector.GetPatchValue(operation);

    public string? GetPatchSourcePath(PatchOperation operation) =>
        CosmosSdkIntrospector.GetPatchSourcePath(operation);

    public FeedRange? ExtractFeedRangeFromStartFrom(ChangeFeedStartFrom startFrom) =>
        CosmosSdkIntrospector.ExtractFeedRangeFromStartFrom(startFrom);

    public DateTime? ExtractStartTime(ChangeFeedStartFrom startFrom) =>
        CosmosSdkIntrospector.ExtractStartTime(startFrom);

    public List<(DateTimeOffset Timestamp, string Id, string PartitionKey, string Json, bool IsDelete)> FilterChangeFeedEntriesByFeedRange(
        List<(DateTimeOffset Timestamp, string Id, string PartitionKey, string Json, bool IsDelete)> entries,
        FeedRange? feedRange) =>
        FeedRangeCalculator.FilterChangeFeedEntriesByFeedRange(entries, feedRange);

    public List<string> FilterByFeedRange(
        List<string> items,
        FeedRange feedRange,
        IReadOnlyList<string> partitionKeyPaths,
        JsonSerializerSettings jsonSettings) =>
        FeedRangeCalculator.FilterByFeedRange(items, feedRange, partitionKeyPaths, jsonSettings);
}
