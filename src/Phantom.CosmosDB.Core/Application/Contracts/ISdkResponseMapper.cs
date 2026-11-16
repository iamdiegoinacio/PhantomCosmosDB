#nullable enable
using System.Net;
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json;

namespace Phantom.CosmosDB.Core.Application.Contracts;

/// <summary>
/// Port for constructing Azure Cosmos DB SDK response objects and introspecting opaque SDK types.
/// </summary>
internal interface ISdkResponseMapper
{
    CosmosDiagnostics DefaultDiagnostics { get; }

    ContainerResponse CreateContainerResponse(
        Container container,
        ContainerProperties? properties,
        HttpStatusCode statusCode);

    ThroughputResponse CreateThroughputResponse(
        ThroughputProperties properties,
        HttpStatusCode statusCode = HttpStatusCode.OK);

    DatabaseResponse CreateDatabaseResponse(
        Database database,
        HttpStatusCode statusCode,
        DatabaseProperties? properties = null);

    ResponseMessage CreateResponseMessage(
        HttpStatusCode statusCode,
        string sessionToken,
        string? json = null,
        string? etag = null,
        int subStatusCode = 0);

    ItemResponse<T> CreateItemResponse<T>(
        T item,
        HttpStatusCode statusCode,
        string sessionToken,
        string? etag = null,
        bool suppressContent = false);

    Dictionary<string, object?> ExtractQueryParameters(QueryDefinition queryDefinition);
    object? GetPatchValue(PatchOperation operation);
    string? GetPatchSourcePath(PatchOperation operation);
    FeedRange? ExtractFeedRangeFromStartFrom(ChangeFeedStartFrom startFrom);
    DateTime? ExtractStartTime(ChangeFeedStartFrom startFrom);

    List<(DateTimeOffset Timestamp, string Id, string PartitionKey, string Json, bool IsDelete)> FilterChangeFeedEntriesByFeedRange(
        List<(DateTimeOffset Timestamp, string Id, string PartitionKey, string Json, bool IsDelete)> entries,
        FeedRange? feedRange);

    List<string> FilterByFeedRange(
        List<string> items,
        FeedRange feedRange,
        IReadOnlyList<string> partitionKeyPaths,
        JsonSerializerSettings jsonSettings);
}
