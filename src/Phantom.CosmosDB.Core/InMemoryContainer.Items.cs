#nullable enable
using Phantom.CosmosDB.Core.Application.Crud;
using Phantom.CosmosDB.Core.Application.Patch;
using Microsoft.Azure.Cosmos;

namespace Phantom.CosmosDB.Core;

internal partial class InMemoryContainer
{
    // ─── Typed CRUD & Patch ───────────────────────────────────────────────────

    public override Task<ItemResponse<T>> CreateItemAsync<T>(
        T item,
        PartitionKey? partitionKey = null,
        ItemRequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default)
        => TypedDocumentCrudService.CreateItemAsync(
            CreateOperationContext(), item, partitionKey, requestOptions, cancellationToken);

    public override Task<ItemResponse<T>> ReadItemAsync<T>(
        string id,
        PartitionKey partitionKey,
        ItemRequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default)
        => TypedDocumentCrudService.ReadItemAsync<T>(
            CreateOperationContext(), id, partitionKey, requestOptions, cancellationToken);

    public override Task<ItemResponse<T>> UpsertItemAsync<T>(
        T item,
        PartitionKey? partitionKey = null,
        ItemRequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default)
        => TypedDocumentCrudService.UpsertItemAsync(
            CreateOperationContext(), item, partitionKey, requestOptions, cancellationToken);

    public override Task<ItemResponse<T>> ReplaceItemAsync<T>(
        T item,
        string id,
        PartitionKey? partitionKey = null,
        ItemRequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default)
        => TypedDocumentCrudService.ReplaceItemAsync(
            CreateOperationContext(), item, id, partitionKey, requestOptions, cancellationToken);

    public override Task<ItemResponse<T>> DeleteItemAsync<T>(
        string id,
        PartitionKey partitionKey,
        ItemRequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default)
        => TypedDocumentCrudService.DeleteItemAsync<T>(
            CreateOperationContext(), id, partitionKey, requestOptions, cancellationToken);

    public override Task<ItemResponse<T>> PatchItemAsync<T>(
        string id,
        PartitionKey partitionKey,
        IReadOnlyList<PatchOperation> patchOperations,
        PatchItemRequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default)
        => DocumentPatchEngine.PatchItemAsync<T>(
            id, partitionKey, patchOperations, requestOptions, _store, _changeFeedLog, Id,
            PartitionKeyPaths, _containerProperties, DefaultTimeToLive, JsonSettings,
            CreateQueryContext, ExecutePreTriggers, ExecutePostTriggers, cancellationToken);

    // ─── Stream CRUD & Patch ──────────────────────────────────────────────────

    public override Task<ResponseMessage> CreateItemStreamAsync(
        Stream streamPayload,
        PartitionKey partitionKey,
        ItemRequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default)
        => StreamDocumentCrudService.CreateItemStreamAsync(
            CreateOperationContext(), streamPayload, partitionKey, requestOptions, cancellationToken);

    public override Task<ResponseMessage> ReadItemStreamAsync(
        string id,
        PartitionKey partitionKey,
        ItemRequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default)
        => StreamDocumentCrudService.ReadItemStreamAsync(
            CreateOperationContext(), id, partitionKey, requestOptions, cancellationToken);

    public override Task<ResponseMessage> UpsertItemStreamAsync(
        Stream streamPayload,
        PartitionKey partitionKey,
        ItemRequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default)
        => StreamDocumentCrudService.UpsertItemStreamAsync(
            CreateOperationContext(), streamPayload, partitionKey, requestOptions, cancellationToken);

    public override Task<ResponseMessage> ReplaceItemStreamAsync(
        Stream streamPayload,
        string id,
        PartitionKey partitionKey,
        ItemRequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default)
        => StreamDocumentCrudService.ReplaceItemStreamAsync(
            CreateOperationContext(), streamPayload, id, partitionKey, requestOptions, cancellationToken);

    public override Task<ResponseMessage> DeleteItemStreamAsync(
        string id,
        PartitionKey partitionKey,
        ItemRequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default)
        => StreamDocumentCrudService.DeleteItemStreamAsync(
            CreateOperationContext(), id, partitionKey, requestOptions, cancellationToken);

    public override Task<ResponseMessage> PatchItemStreamAsync(
        string id,
        PartitionKey partitionKey,
        IReadOnlyList<PatchOperation> patchOperations,
        PatchItemRequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default)
        => DocumentPatchEngine.PatchItemStreamAsync(
            id, partitionKey, patchOperations, requestOptions, _store, _changeFeedLog, Id,
            PartitionKeyPaths, _containerProperties, DefaultTimeToLive,
            CreateQueryContext, ExecutePreTriggers, ExecutePostTriggers, cancellationToken);

    // ─── Bulk ReadMany & Partition Delete ─────────────────────────────────────

    public override Task<FeedResponse<T>> ReadManyItemsAsync<T>(
        IReadOnlyList<(string id, PartitionKey partitionKey)> items,
        ReadManyRequestOptions? readManyRequestOptions = null,
        CancellationToken cancellationToken = default)
        => BulkDocumentReadDeleteService.ReadManyItemsAsync<T>(
            CreateOperationContext(), items, readManyRequestOptions, cancellationToken);

    public override Task<ResponseMessage> ReadManyItemsStreamAsync(
        IReadOnlyList<(string id, PartitionKey partitionKey)> items,
        ReadManyRequestOptions? readManyRequestOptions = null,
        CancellationToken cancellationToken = default)
        => BulkDocumentReadDeleteService.ReadManyItemsStreamAsync(
            CreateOperationContext(), items, readManyRequestOptions, cancellationToken);

    public override Task<ResponseMessage> DeleteAllItemsByPartitionKeyStreamAsync(
        PartitionKey partitionKey,
        RequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default)
        => BulkDocumentReadDeleteService.DeleteAllItemsByPartitionKeyStreamAsync(
            CreateOperationContext(), partitionKey, cancellationToken);
}
