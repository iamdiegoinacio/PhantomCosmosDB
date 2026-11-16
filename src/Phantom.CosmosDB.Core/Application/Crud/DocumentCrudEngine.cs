#nullable enable
using System.Net;
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Phantom.CosmosDB.Core.Domain.Primitives;
using Phantom.CosmosDB.Core.Domain.Validation;
using Phantom.CosmosDB.Core.Application.Contracts;

namespace Phantom.CosmosDB.Core.Application.Crud;

/// <summary>
/// Unified single-document CRUD engine returning <see cref="DocumentOperationResult"/> so that
/// stream and typed Cosmos adapters share one validated execution path (DRY + Result Pattern).
/// </summary>
internal static class DocumentCrudEngine
{
    public static async Task<DocumentOperationResult> CreateAsync(
        DocumentOperationContext context,
        JObject jObj,
        PartitionKey? partitionKey,
        ItemRequestOptions? requestOptions,
        bool allowGeneratedId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        jObj = context.ExecutePreTriggers(requestOptions, jObj, "Create");
        var json = jObj.ToString(Formatting.None);

        if (!DocumentValidator.IsDocumentSizeValid(json))
            return DocumentOperationResult.Failure(HttpStatusCode.RequestEntityTooLarge, errorMessage: "Document size exceeds the maximum allowed limit.");
        if (!DocumentValidator.IsPerItemTtlValid(jObj))
            return DocumentOperationResult.Failure(HttpStatusCode.BadRequest, errorMessage: "Invalid per-item ttl value.");

        var rawItemId = jObj["id"]?.ToString();
        var itemId = allowGeneratedId ? (rawItemId ?? Guid.NewGuid().ToString()) : rawItemId;
        if (string.IsNullOrEmpty(itemId) || !CosmosResourceNameValidator.IsDocumentIdValid(itemId))
            return DocumentOperationResult.Failure(HttpStatusCode.BadRequest, errorMessage: $"Document id '{itemId}' is invalid.");

        if (!PartitionKeyProcessor.IsPartitionKeyConsistent(partitionKey, jObj, context.PartitionKeyPaths))
            return DocumentOperationResult.Failure(
                HttpStatusCode.BadRequest,
                CosmosSubStatusCodes.PartitionKeyMismatch,
                "PartitionKey extracted from document does not match the PartitionKey header.");

        var pk = PartitionKeyProcessor.ExtractPartitionKeyValue(partitionKey, jObj, context.PartitionKeyPaths);
        var key = (itemId, pk!);
        var store = context.Store;

        return await DocumentWriteCommitter.WithItemLockAsync(store, key, () =>
        {
            store.EvictIfExpired(key, context.DefaultTimeToLive, context.PartitionKeyPaths, context.ChangeFeedLog);

            using (UniqueKeyWriteScope.Enter(context))
            {
                if (!UniqueKeyValidator.ValidateUniqueKeysStream(
                        context.ContainerProperties.UniqueKeyPolicy, store.Items, jObj, pk))
                {
                    return DocumentOperationResult.Failure(
                        HttpStatusCode.Conflict,
                        CosmosSubStatusCodes.UniqueKeyConstraintViolation,
                        "Unique index constraint violation.");
                }

                if (!store.Items.TryAdd(key, json))
                    return DocumentOperationResult.Failure(HttpStatusCode.Conflict, errorMessage: $"Entity with id '{itemId}' already exists.");
            }

            store.TrackWrite(key);
            store.AddInsertionOrder(key);
            var committed = DocumentWriteCommitter.Write(context, key, json);

            try
            {
                var committedDoc = JsonParseHelpers.ParseJson(store.Items[key]);
                var responseOverride = context.ExecutePostTriggers(requestOptions, committedDoc, "Create");
                DocumentWriteCommitter.ApplyPostTriggerChanges(store, key, committedDoc);
                context.ChangeFeedLog.RecordChangeFeed(itemId, pk, store.Items[key]);
                return DocumentOperationResult.Success(HttpStatusCode.Created, store.Items[key], committed.ETag, responseOverride);
            }
            catch
            {
                DocumentWriteCommitter.Remove(store, key);
                throw;
            }
        }, cancellationToken);
    }

    public static DocumentOperationResult Read(
        DocumentOperationContext context,
        string id,
        PartitionKey partitionKey,
        ItemRequestOptions? requestOptions,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var store = context.Store;
        var key = (id, PartitionKeyProcessor.PartitionKeyToString(partitionKey)!);

        if (!store.Items.TryGetValue(key, out var json) || store.IsExpired(key, context.DefaultTimeToLive))
        {
            store.EvictIfExpired(key, context.DefaultTimeToLive, context.PartitionKeyPaths, context.ChangeFeedLog);
            return DocumentOperationResult.Failure(HttpStatusCode.NotFound, errorMessage: $"Entity with id '{id}' not found.");
        }

        var etag = store.Etags.GetValueOrDefault(key);
        if (!store.CheckIfNoneMatchStream(requestOptions, key))
            return DocumentOperationResult.Success(HttpStatusCode.NotModified, null, etag);

        return DocumentOperationResult.Success(HttpStatusCode.OK, json, etag);
    }

    public static async Task<DocumentOperationResult> UpsertAsync(
        DocumentOperationContext context,
        JObject jObj,
        PartitionKey? partitionKey,
        ItemRequestOptions? requestOptions,
        bool allowGeneratedId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var rawItemId = jObj["id"]?.ToString();
        var itemId = allowGeneratedId ? (rawItemId ?? Guid.NewGuid().ToString()) : rawItemId;
        if (string.IsNullOrEmpty(itemId) || !CosmosResourceNameValidator.IsDocumentIdValid(itemId))
            return DocumentOperationResult.Failure(HttpStatusCode.BadRequest, errorMessage: $"Document id '{itemId}' is invalid.");

        if (!PartitionKeyProcessor.IsPartitionKeyConsistent(partitionKey, jObj, context.PartitionKeyPaths))
            return DocumentOperationResult.Failure(
                HttpStatusCode.BadRequest,
                CosmosSubStatusCodes.PartitionKeyMismatch,
                "PartitionKey extracted from document does not match the PartitionKey header.");

        var pk = PartitionKeyProcessor.ExtractPartitionKeyValue(partitionKey, jObj, context.PartitionKeyPaths);
        var key = (itemId, pk!);
        var store = context.Store;

        return await DocumentWriteCommitter.WithItemLockAsync(store, key, () =>
        {
            store.EvictIfExpired(key, context.DefaultTimeToLive, context.PartitionKeyPaths, context.ChangeFeedLog);
            var isExisting = store.Items.TryGetValue(key, out var existingJson);
            var triggerOp = isExisting ? "Replace" : "Create";

            jObj = context.ExecutePreTriggers(requestOptions, jObj, triggerOp);
            var json = jObj.ToString(Formatting.None);
            if (!DocumentValidator.IsDocumentSizeValid(json))
                return DocumentOperationResult.Failure(HttpStatusCode.RequestEntityTooLarge, errorMessage: "Document size exceeds the maximum allowed limit.");
            if (!DocumentValidator.IsPerItemTtlValid(jObj))
                return DocumentOperationResult.Failure(HttpStatusCode.BadRequest, errorMessage: "Invalid per-item ttl value.");

            if (isExisting && !store.CheckIfMatchStream(requestOptions, key))
                return DocumentOperationResult.Failure(HttpStatusCode.PreconditionFailed, errorMessage: "ETag precondition failed.");

            using (UniqueKeyWriteScope.Enter(context))
            {
                if (!UniqueKeyValidator.ValidateUniqueKeysStream(
                        context.ContainerProperties.UniqueKeyPolicy, store.Items, jObj, pk, excludeItemId: isExisting ? itemId : null))
                {
                    return DocumentOperationResult.Failure(
                        HttpStatusCode.Conflict,
                        CosmosSubStatusCodes.UniqueKeyConstraintViolation,
                        "Unique index constraint violation.");
                }
            }

            EntrySnapshot? previous = isExisting
                ? DocumentWriteCommitter.Capture(store, key, existingJson!)
                : null;
            if (!isExisting) store.AddInsertionOrder(key);
            var committed = DocumentWriteCommitter.Write(context, key, json);
            store.TrackWrite(key);

            try
            {
                var committedDoc = JsonParseHelpers.ParseJson(store.Items[key]);
                var responseOverride = context.ExecutePostTriggers(requestOptions, committedDoc, triggerOp);
                DocumentWriteCommitter.ApplyPostTriggerChanges(store, key, committedDoc);
                context.ChangeFeedLog.RecordChangeFeed(itemId, pk, store.Items[key]);
                var status = isExisting ? HttpStatusCode.OK : HttpStatusCode.Created;
                return DocumentOperationResult.Success(status, store.Items[key], committed.ETag, responseOverride);
            }
            catch
            {
                RollbackWrite(store, key, previous);
                throw;
            }
        }, cancellationToken);
    }

    private static void RollbackWrite(IDocumentStore store, (string Id, string PartitionKey) key, EntrySnapshot? previous)
    {
        if (previous.HasValue)
            DocumentWriteCommitter.Restore(store, key, previous.Value);
        else
            DocumentWriteCommitter.Remove(store, key);
    }

    public static async Task<DocumentOperationResult> ReplaceAsync(
        DocumentOperationContext context,
        JObject jObj,
        string id,
        PartitionKey? partitionKey,
        ItemRequestOptions? requestOptions,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        jObj = context.ExecutePreTriggers(requestOptions, jObj, "Replace");
        var json = jObj.ToString(Formatting.None);

        if (!DocumentValidator.IsDocumentSizeValid(json))
            return DocumentOperationResult.Failure(HttpStatusCode.RequestEntityTooLarge, errorMessage: "Document size exceeds the maximum allowed limit.");
        if (!DocumentValidator.IsPerItemTtlValid(jObj))
            return DocumentOperationResult.Failure(HttpStatusCode.BadRequest, errorMessage: "Invalid per-item ttl value.");

        var itemId = jObj["id"]?.ToString() ?? id;
        if (!CosmosResourceNameValidator.IsDocumentIdValid(itemId))
            return DocumentOperationResult.Failure(HttpStatusCode.BadRequest, errorMessage: $"Document id '{itemId}' is invalid.");

        if (!PartitionKeyProcessor.IsPartitionKeyConsistent(partitionKey, jObj, context.PartitionKeyPaths))
            return DocumentOperationResult.Failure(
                HttpStatusCode.BadRequest,
                CosmosSubStatusCodes.PartitionKeyMismatch,
                "PartitionKey extracted from document does not match the PartitionKey header.");

        var pk = PartitionKeyProcessor.ExtractPartitionKeyValue(partitionKey, jObj, context.PartitionKeyPaths);
        var key = (id, pk!);
        var store = context.Store;

        return await DocumentWriteCommitter.WithItemLockAsync(store, key, () =>
        {
            if (!store.Items.TryGetValue(key, out var existingJson) || store.IsExpired(key, context.DefaultTimeToLive))
            {
                store.EvictIfExpired(key, context.DefaultTimeToLive, context.PartitionKeyPaths, context.ChangeFeedLog);
                return DocumentOperationResult.Failure(HttpStatusCode.NotFound, errorMessage: $"Entity with id '{id}' not found.");
            }

            if (!store.CheckIfMatchStream(requestOptions, key))
                return DocumentOperationResult.Failure(HttpStatusCode.PreconditionFailed, errorMessage: "ETag precondition failed.");

            using (UniqueKeyWriteScope.Enter(context))
            {
                if (!UniqueKeyValidator.ValidateUniqueKeysStream(
                        context.ContainerProperties.UniqueKeyPolicy, store.Items, jObj, pk, excludeItemId: id))
                {
                    return DocumentOperationResult.Failure(
                        HttpStatusCode.Conflict,
                        CosmosSubStatusCodes.UniqueKeyConstraintViolation,
                        "Unique index constraint violation.");
                }
            }

            var previous = DocumentWriteCommitter.Capture(store, key, existingJson);
            var committed = DocumentWriteCommitter.Write(context, key, json);
            store.TrackWrite(key);

            try
            {
                var committedDoc = JsonParseHelpers.ParseJson(store.Items[key]);
                var responseOverride = context.ExecutePostTriggers(requestOptions, committedDoc, "Replace");
                DocumentWriteCommitter.ApplyPostTriggerChanges(store, key, committedDoc);
                context.ChangeFeedLog.RecordChangeFeed(id, pk, store.Items[key]);
                return DocumentOperationResult.Success(HttpStatusCode.OK, store.Items[key], committed.ETag, responseOverride);
            }
            catch
            {
                RollbackWrite(store, key, previous);
                throw;
            }
        }, cancellationToken);
    }

    public static async Task<DocumentOperationResult> DeleteAsync(
        DocumentOperationContext context,
        string id,
        PartitionKey partitionKey,
        ItemRequestOptions? requestOptions,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var store = context.Store;
        var pk = PartitionKeyProcessor.PartitionKeyToString(partitionKey);
        var key = (id, pk!);

        return await DocumentWriteCommitter.WithItemLockAsync(store, key, () =>
        {
            if (!store.Items.TryGetValue(key, out var existingJson) || store.IsExpired(key, context.DefaultTimeToLive))
            {
                store.EvictIfExpired(key, context.DefaultTimeToLive, context.PartitionKeyPaths, context.ChangeFeedLog);
                return DocumentOperationResult.Failure(HttpStatusCode.NotFound, errorMessage: $"Entity with id '{id}' not found.");
            }

            if (!store.CheckIfMatchStream(requestOptions, key))
                return DocumentOperationResult.Failure(HttpStatusCode.PreconditionFailed, errorMessage: "ETag precondition failed.");

            context.ExecutePreTriggers(requestOptions, JsonParseHelpers.ParseJson(existingJson), "Delete");

            var previous = DocumentWriteCommitter.Capture(store, key, existingJson);
            DocumentWriteCommitter.Remove(store, key);
            store.TrackWrite(key);

            try
            {
                context.ExecutePostTriggers(requestOptions, JsonParseHelpers.ParseJson(existingJson), "Delete");
                context.ChangeFeedLog.RecordDeleteTombstone(id, pk, context.PartitionKeyPaths, partitionKey);
                return DocumentOperationResult.Success(HttpStatusCode.NoContent);
            }
            catch
            {
                DocumentWriteCommitter.RestoreDeleted(store, key, previous);
                throw;
            }
        }, cancellationToken);
    }
}
