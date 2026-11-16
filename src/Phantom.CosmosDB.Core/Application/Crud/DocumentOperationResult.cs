#nullable enable
using System.Net;
using Newtonsoft.Json.Linq;

namespace Phantom.CosmosDB.Core.Application.Crud;

/// <summary>
/// Result Pattern representation of a single-document CRUD operation before adapting to
/// <see cref="Microsoft.Azure.Cosmos.ResponseMessage"/> or <see cref="Microsoft.Azure.Cosmos.ItemResponse{T}"/>.
/// </summary>
internal readonly record struct DocumentOperationResult(
    bool IsSuccess,
    HttpStatusCode StatusCode,
    int SubStatusCode,
    string? ErrorMessage,
    string? Json,
    string? ETag,
    JObject? ResponseBodyOverride)
{
    public static DocumentOperationResult Success(
        HttpStatusCode statusCode,
        string? json = null,
        string? etag = null,
        JObject? responseBodyOverride = null)
        => new(true, statusCode, 0, null, json, etag, responseBodyOverride);

    public static DocumentOperationResult Failure(
        HttpStatusCode statusCode,
        int subStatusCode = 0,
        string? errorMessage = null)
        => new(false, statusCode, subStatusCode, errorMessage ?? statusCode.ToString(), null, null, null);
}
