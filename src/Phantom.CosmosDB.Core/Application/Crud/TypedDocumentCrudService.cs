#nullable enable
using System.Net;
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Phantom.CosmosDB.Core.Domain.Primitives;
using CosmosResponseFactory = Phantom.CosmosDB.Core.Infrastructure.SdkInterop.CosmosResponseFactory;

namespace Phantom.CosmosDB.Core.Application.Crud;

/// <summary>
/// Adapter translating <see cref="DocumentOperationResult"/> into typed Cosmos <see cref="ItemResponse{T}"/> responses.
/// </summary>
internal static class TypedDocumentCrudService
{
    public static async Task<ItemResponse<T>> CreateItemAsync<T>(
        DocumentOperationContext context,
        T item,
        PartitionKey? partitionKey,
        ItemRequestOptions? requestOptions,
        CancellationToken cancellationToken = default)
    {
        var jObj = SerializeToJObject(context, item);
        var res = await DocumentCrudEngine.CreateAsync(context, jObj, partitionKey, requestOptions, allowGeneratedId: false, cancellationToken);
        return ToTypedResponse<T>(context, res, requestOptions);
    }

    public static Task<ItemResponse<T>> ReadItemAsync<T>(
        DocumentOperationContext context,
        string id,
        PartitionKey partitionKey,
        ItemRequestOptions? requestOptions,
        CancellationToken cancellationToken = default)
    {
        var res = DocumentCrudEngine.Read(context, id, partitionKey, requestOptions, cancellationToken);
        return Task.FromResult(ToTypedResponse<T>(context, res, requestOptions: null));
    }

    public static async Task<ItemResponse<T>> UpsertItemAsync<T>(
        DocumentOperationContext context,
        T item,
        PartitionKey? partitionKey,
        ItemRequestOptions? requestOptions,
        CancellationToken cancellationToken = default)
    {
        var jObj = SerializeToJObject(context, item);
        var res = await DocumentCrudEngine.UpsertAsync(context, jObj, partitionKey, requestOptions, allowGeneratedId: false, cancellationToken);
        return ToTypedResponse<T>(context, res, requestOptions);
    }

    public static async Task<ItemResponse<T>> ReplaceItemAsync<T>(
        DocumentOperationContext context,
        T item,
        string id,
        PartitionKey? partitionKey,
        ItemRequestOptions? requestOptions,
        CancellationToken cancellationToken = default)
    {
        var jObj = SerializeToJObject(context, item);
        var res = await DocumentCrudEngine.ReplaceAsync(context, jObj, id, partitionKey, requestOptions, cancellationToken);
        return ToTypedResponse<T>(context, res, requestOptions);
    }

    public static async Task<ItemResponse<T>> DeleteItemAsync<T>(
        DocumentOperationContext context,
        string id,
        PartitionKey partitionKey,
        ItemRequestOptions? requestOptions,
        CancellationToken cancellationToken = default)
    {
        var res = await DocumentCrudEngine.DeleteAsync(context, id, partitionKey, requestOptions, cancellationToken);
        return ToTypedResponse<T>(context, res, requestOptions: null);
    }

    private static JObject SerializeToJObject<T>(DocumentOperationContext context, T item)
    {
        var json = JsonConvert.SerializeObject(item, context.JsonSettings);
        return JsonParseHelpers.ParseJson(json);
    }

    private static ItemResponse<T> ToTypedResponse<T>(
        DocumentOperationContext context,
        DocumentOperationResult res,
        ItemRequestOptions? requestOptions)
    {
        if (!res.IsSuccess)
        {
            throw InMemoryCosmosException.Create(
                res.ErrorMessage ?? res.StatusCode.ToString(),
                res.StatusCode,
                res.SubStatusCode,
                Guid.NewGuid().ToString(),
                ContainerConstants.SyntheticRequestCharge);
        }

        if (res.StatusCode == HttpStatusCode.NoContent)
            return CosmosResponseFactory.CreateItemResponse<T>(default!, HttpStatusCode.NoContent, context.SessionToken);

        var suppressContent = requestOptions?.EnableContentResponseOnWrite == false;
        T body = default!;
        if (!suppressContent)
        {
            body = res.ResponseBodyOverride is not null
                ? res.ResponseBodyOverride.ToObject<T>(JsonSerializer.Create(context.JsonSettings))!
                : (res.Json is not null ? JsonConvert.DeserializeObject<T>(res.Json, context.JsonSettings)! : default!);
        }

        return CosmosResponseFactory.CreateItemResponse(body, res.StatusCode, context.SessionToken, res.ETag, suppressContent);
    }
}
