#nullable enable
using System.Net;
using Microsoft.Azure.Cosmos;

namespace Phantom.CosmosDB.Core.Infrastructure.HttpGateway;

/// <summary>
/// Compatibility facade delegating to <see cref="CosmosHeaderCatalog"/>,
/// <see cref="CosmosHttpRequestParser"/>, and <see cref="CosmosHttpResponseBuilder"/>.
/// </summary>
internal static class CosmosHttpHeaders
{
    public static HashSet<string> KnownRequestHeaders => CosmosHeaderCatalog.KnownRequestHeaders;

    public static string ExtractDocumentId(HttpRequestMessage request) =>
        CosmosHttpRequestParser.ExtractDocumentId(request);

    public static bool HasDocumentId(string path) =>
        CosmosHttpRequestParser.HasDocumentId(path);

    public static ItemRequestOptions BuildItemRequestOptions(HttpRequestMessage request) =>
        CosmosHttpRequestParser.BuildItemRequestOptions(request);

    public static bool IsQueryRequest(HttpRequestMessage request) =>
        CosmosHttpRequestParser.IsQueryRequest(request);

    public static bool IsUpsertRequest(HttpRequestMessage request) =>
        CosmosHttpRequestParser.IsUpsertRequest(request);

    public static bool IsBatchRequest(HttpRequestMessage request) =>
        CosmosHttpRequestParser.IsBatchRequest(request);

    public static bool IsQueryPlanRequest(HttpRequestMessage request) =>
        CosmosHttpRequestParser.IsQueryPlanRequest(request);

    public static bool IsChangeFeedRequest(HttpRequestMessage request) =>
        CosmosHttpRequestParser.IsChangeFeedRequest(request);

    public static bool IsFullFidelityChangeFeedRequest(HttpRequestMessage request) =>
        CosmosHttpRequestParser.IsFullFidelityChangeFeedRequest(request);

    public static (IReadOnlyList<PatchOperation> Operations, string? Condition) ParsePatchBody(string body) =>
        CosmosHttpRequestParser.ParsePatchBody(body);

    public static PartitionKey? ExtractPartitionKey(HttpRequestMessage request) =>
        CosmosHttpRequestParser.ExtractPartitionKey(request);

    public static int ExtractMaxItemCount(HttpRequestMessage request) =>
        CosmosHttpRequestParser.ExtractMaxItemCount(request);

    public static string? ExtractPartitionKeyRangeId(HttpRequestMessage request) =>
        CosmosHttpRequestParser.ExtractPartitionKeyRangeId(request);

    public static bool ExtractEnableScanInQuery(HttpRequestMessage request) =>
        CosmosHttpRequestParser.ExtractEnableScanInQuery(request);

    public static (string Key, int Offset)? DecodeContinuation(HttpRequestMessage request) =>
        CosmosHttpRequestParser.DecodeContinuation(request);

    public static string EncodeContinuation(string cacheKey, int offset) =>
        CosmosHttpResponseBuilder.EncodeContinuation(cacheKey, offset);

    internal enum ChargeKind
    {
        Flat,
        Read,
        Write,
    }

    public static HttpResponseMessage ConvertToHttpResponse(
        ResponseMessage cosmosResponse,
        string sessionToken,
        ChargeKind chargeKind = ChargeKind.Flat) =>
        CosmosHttpResponseBuilder.ConvertToHttpResponse(
            cosmosResponse,
            sessionToken,
            (CosmosHttpResponseBuilder.ChargeKind)(int)chargeKind);

    public static string BuildErrorBody(HttpStatusCode statusCode, string message) =>
        CosmosHttpResponseBuilder.BuildErrorBody(statusCode, message);

    public static HttpResponseMessage CreateErrorResponse(
        HttpStatusCode statusCode,
        string message,
        int subStatusCode,
        string sessionToken) =>
        CosmosHttpResponseBuilder.CreateErrorResponse(statusCode, message, subStatusCode, sessionToken);

    public static HttpResponseMessage CreateJsonResponse(
        string json,
        string sessionToken,
        HttpStatusCode statusCode = HttpStatusCode.OK) =>
        CosmosHttpResponseBuilder.CreateJsonResponse(json, sessionToken, statusCode);

    public static HttpResponseMessage CreateNoContentResponse() =>
        CosmosHttpResponseBuilder.CreateNoContentResponse();
}
