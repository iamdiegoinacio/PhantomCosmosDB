#nullable enable
using System.Net;
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Phantom.CosmosDB.Core.Domain.Validation;
using CosmosResponseFactory = Phantom.CosmosDB.Core.Infrastructure.SdkInterop.CosmosResponseFactory;

namespace Phantom.CosmosDB.Core.Application.Crud;

/// <summary>
/// Adapter translating <see cref="DocumentOperationResult"/> into Cosmos stream <see cref="ResponseMessage"/> responses.
/// </summary>
internal static class StreamDocumentCrudService
{
    public static async Task<ResponseMessage> CreateItemStreamAsync(
        DocumentOperationContext context,
        Stream streamPayload,
        PartitionKey partitionKey,
        ItemRequestOptions? requestOptions,
        CancellationToken cancellationToken = default)
    {
        if (!TryParsePayload(context, streamPayload, out var jObj, out var failure))
            return failure!;

        var res = await DocumentCrudEngine.CreateAsync(context, jObj!, partitionKey, requestOptions, allowGeneratedId: true, cancellationToken);
        return ToStreamResponse(context, res, requestOptions);
    }

    public static Task<ResponseMessage> ReadItemStreamAsync(
        DocumentOperationContext context,
        string id,
        PartitionKey partitionKey,
        ItemRequestOptions? requestOptions,
        CancellationToken cancellationToken = default)
    {
        var res = DocumentCrudEngine.Read(context, id, partitionKey, requestOptions, cancellationToken);
        return Task.FromResult(ToStreamResponse(context, res, requestOptions: null));
    }

    public static async Task<ResponseMessage> UpsertItemStreamAsync(
        DocumentOperationContext context,
        Stream streamPayload,
        PartitionKey partitionKey,
        ItemRequestOptions? requestOptions,
        CancellationToken cancellationToken = default)
    {
        if (!TryParsePayload(context, streamPayload, out var jObj, out var failure))
            return failure!;

        var res = await DocumentCrudEngine.UpsertAsync(context, jObj!, partitionKey, requestOptions, allowGeneratedId: true, cancellationToken);
        return ToStreamResponse(context, res, requestOptions);
    }

    public static async Task<ResponseMessage> ReplaceItemStreamAsync(
        DocumentOperationContext context,
        Stream streamPayload,
        string id,
        PartitionKey partitionKey,
        ItemRequestOptions? requestOptions,
        CancellationToken cancellationToken = default)
    {
        if (!TryParsePayload(context, streamPayload, out var jObj, out var failure))
            return failure!;

        var res = await DocumentCrudEngine.ReplaceAsync(context, jObj!, id, partitionKey, requestOptions, cancellationToken);
        return ToStreamResponse(context, res, requestOptions);
    }

    public static async Task<ResponseMessage> DeleteItemStreamAsync(
        DocumentOperationContext context,
        string id,
        PartitionKey partitionKey,
        ItemRequestOptions? requestOptions,
        CancellationToken cancellationToken = default)
    {
        var res = await DocumentCrudEngine.DeleteAsync(context, id, partitionKey, requestOptions, cancellationToken);
        return ToStreamResponse(context, res, requestOptions: null);
    }

    private static bool TryParsePayload(
        DocumentOperationContext context,
        Stream streamPayload,
        out JObject? document,
        out ResponseMessage? failure)
    {
        var json = CosmosResponseFactory.ReadStream(streamPayload);
        document = null;
        failure = null;

        if (!DocumentValidator.IsDocumentSizeValid(json))
        {
            failure = CosmosResponseFactory.CreateResponseMessage(HttpStatusCode.RequestEntityTooLarge, context.SessionToken);
            return false;
        }

        try
        {
            document = JsonParseHelpers.ParseJson(json);
            return true;
        }
        catch (JsonReaderException)
        {
            failure = CosmosResponseFactory.CreateResponseMessage(HttpStatusCode.BadRequest, context.SessionToken);
            return false;
        }
    }

    private static ResponseMessage ToStreamResponse(
        DocumentOperationContext context,
        DocumentOperationResult res,
        ItemRequestOptions? requestOptions)
    {
        if (!res.IsSuccess)
            return CosmosResponseFactory.CreateResponseMessage(res.StatusCode, context.SessionToken, subStatusCode: res.SubStatusCode);

        var body = requestOptions?.EnableContentResponseOnWrite == false ? null : res.Json;
        return CosmosResponseFactory.CreateResponseMessage(res.StatusCode, context.SessionToken, body, res.ETag);
    }
}
