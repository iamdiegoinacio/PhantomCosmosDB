#nullable enable
using System.Globalization;
using System.Net;
using System.Text;
using Phantom.CosmosDB.Core.Application.Batch;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Serialization.HybridRow;
using Microsoft.Azure.Cosmos.Serialization.HybridRow.IO;
using Microsoft.Azure.Cosmos.Serialization.HybridRow.Layouts;
using Microsoft.Azure.Cosmos.Serialization.HybridRow.RecordIO;

namespace Phantom.CosmosDB.Core.Infrastructure.HttpGateway;

/// <summary>
/// Encodes and decodes the Cosmos DB HybridRow / RecordIO binary wire format
/// for Transactional Batch HTTP requests and responses.
/// </summary>
internal static class HybridRowBatchCodec
{
    private record struct BatchOperation(
        int OperationType,
        string? Id,
        byte[]? ResourceBody,
        string? IfMatch,
        string? IfNoneMatch);

    public static bool BatchSchemasAvailable => HybridRowSchemaRegistry.BatchSchemasAvailable;

    public static async Task<HttpResponseMessage> HandleBatchAsync(
        HttpRequestMessage request,
        InMemoryContainer container,
        IBatchSchemaStrategy batchSchemaStrategy,
        CancellationToken cancellationToken)
    {
        if (!batchSchemaStrategy.IsAvailable)
        {
            return new HttpResponseMessage(HttpStatusCode.NotImplemented)
            {
                Content = new StringContent(
                    $"{{\"message\":\"{(batchSchemaStrategy.UnavailableReason ?? "Batch schemas unavailable").Replace("\"", "\\\"")}\"}}",
                    Encoding.UTF8, "application/json")
            };
        }

        var (batchOps, decodeError) = await DecodeBatchOperationsAsync(request.Content, cancellationToken);
        if (decodeError is not null)
            return decodeError;

        if (batchOps.Count == 0)
        {
            return new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("{\"message\":\"Empty batch request.\"}", Encoding.UTF8, "application/json")
            };
        }

        var pk = CosmosHttpHeaders.ExtractPartitionKey(request) ?? PartitionKey.None;
        var isAtomic = ResolveIsAtomic(request);
        var batchResponse = await ExecuteBatchAsync(container, pk, batchOps, isAtomic, cancellationToken);

        return await EncodeBatchResponseAsync(batchResponse, container.CurrentSessionToken);
    }

    private static async Task<(List<BatchOperation> Ops, HttpResponseMessage? Error)> DecodeBatchOperationsAsync(
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        var batchOps = new List<BatchOperation>();
        if (content is null)
            return (batchOps, null);

        var bodyBytes = await content.ReadAsByteArrayAsync(cancellationToken);
        if (bodyBytes.Length == 0)
        {
            var emptyErr = new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("{\"message\":\"Empty batch request body.\"}", Encoding.UTF8, "application/json")
            };
            return (batchOps, emptyErr);
        }

        using var bodyStream = new MemoryStream(bodyBytes);
        var batchLayoutResolver = HybridRowSchemaRegistry.Resolver;

        Func<ReadOnlyMemory<byte>, Result> recordVisitor = (ReadOnlyMemory<byte> record) =>
        {
            var row = new RowBuffer(record.Length);
            if (!row.ReadFrom(record.Span, HybridRowVersion.V1, batchLayoutResolver))
                return Result.Failure;

            var reader = new RowReader(ref row);
            var opType = -1;
            string? id = null;
            byte[]? resourceBody = null;
            string? ifMatch = null;
            string? ifNoneMatch = null;

            while (reader.Read())
            {
                switch (reader.Path)
                {
                    case "operationType":
                        reader.ReadInt32(out int ot);
                        opType = ot;
                        break;
                    case "id":
                        reader.ReadString(out string idVal);
                        id = idVal;
                        break;
                    case "resourceBody":
                        reader.ReadBinary(out byte[] rb);
                        resourceBody = rb;
                        break;
                    case "ifMatch":
                        reader.ReadString(out string im);
                        ifMatch = im;
                        break;
                    case "ifNoneMatch":
                        reader.ReadString(out string inm);
                        ifNoneMatch = inm;
                        break;
                }
            }

            batchOps.Add(new BatchOperation(opType, id, resourceBody, ifMatch, ifNoneMatch));
            return Result.Success;
        };

        var parseResult = await bodyStream.ReadRecordIOAsync(
            recordVisitor, resizer: new MemorySpanResizer<byte>(1024));

        if (parseResult != Result.Success)
        {
            var parseErr = new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent(
                    "{\"message\":\"Failed to parse batch request body.\"}",
                    Encoding.UTF8, "application/json")
            };
            return (batchOps, parseErr);
        }

        return (batchOps, null);
    }

    private static bool ResolveIsAtomic(HttpRequestMessage request)
    {
        var isAtomic = true;
        if (request.Headers.TryGetValues("x-ms-cosmos-batch-atomic", out var atomicValues))
        {
            if (string.Equals(atomicValues.FirstOrDefault(), "false", StringComparison.OrdinalIgnoreCase))
                isAtomic = false;
        }
        if (request.Headers.TryGetValues("x-ms-cosmos-batch-continue-on-error", out var continueValues))
        {
            if (string.Equals(continueValues.FirstOrDefault(), "true", StringComparison.OrdinalIgnoreCase))
                isAtomic = false;
        }
        return isAtomic;
    }

    private static Task<TransactionalBatchResponse> ExecuteBatchAsync(
        InMemoryContainer container,
        PartitionKey pk,
        IReadOnlyList<BatchOperation> batchOps,
        bool isAtomic,
        CancellationToken cancellationToken)
    {
        var batch = container.CreateTransactionalBatch(pk);
        if (batch is InMemoryTransactionalBatch inMemoryBatch)
            inMemoryBatch.IsAtomic = isAtomic;

        foreach (var op in batchOps)
        {
            switch (op.OperationType)
            {
                case HybridRowSchemaRegistry.BatchOperationTypes.Create:
                    if (op.ResourceBody is not null)
                        batch.CreateItemStream(new MemoryStream(op.ResourceBody), BuildBatchItemRequestOptions(op));
                    break;
                case HybridRowSchemaRegistry.BatchOperationTypes.Upsert:
                    if (op.ResourceBody is not null)
                        batch.UpsertItemStream(new MemoryStream(op.ResourceBody), BuildBatchItemRequestOptions(op));
                    break;
                case HybridRowSchemaRegistry.BatchOperationTypes.Replace:
                    if (op.Id is not null && op.ResourceBody is not null)
                        batch.ReplaceItemStream(op.Id, new MemoryStream(op.ResourceBody), BuildBatchItemRequestOptions(op));
                    break;
                case HybridRowSchemaRegistry.BatchOperationTypes.Delete:
                    if (op.Id is not null)
                        batch.DeleteItem(op.Id, BuildBatchItemRequestOptions(op));
                    break;
                case HybridRowSchemaRegistry.BatchOperationTypes.Read:
                    if (op.Id is not null)
                        batch.ReadItem(op.Id, BuildBatchItemRequestOptions(op));
                    break;
                case HybridRowSchemaRegistry.BatchOperationTypes.Patch:
                    if (op.Id is not null && op.ResourceBody is not null)
                    {
                        var patchJson = Encoding.UTF8.GetString(op.ResourceBody);
                        var (patchOps, condition) = CosmosHttpHeaders.ParsePatchBody(patchJson);
                        var patchOptions = new TransactionalBatchPatchItemRequestOptions();
                        if (condition is not null) patchOptions.FilterPredicate = condition;
                        if (op.IfMatch is not null) patchOptions.IfMatchEtag = op.IfMatch;
                        batch.PatchItem(op.Id, patchOps, patchOptions);
                    }
                    break;
            }
        }

        return batch.ExecuteAsync(cancellationToken);
    }

    private static async Task<HttpResponseMessage> EncodeBatchResponseAsync(
        TransactionalBatchResponse batchResponse,
        string sessionToken)
    {
        var inMemoryBatchResponse = batchResponse as InMemoryBatchResponse;
        var batchResultLayout = HybridRowSchemaRegistry.ResultLayout;
        var batchLayoutResolverForResponse = HybridRowSchemaRegistry.Resolver;

        using var responseStream = new MemoryStream();
        var resizer = new MemorySpanResizer<byte>(256);

        await RecordIOStream.WriteRecordIOAsync(
            responseStream,
            default(Segment),
            (long index, out ReadOnlyMemory<byte> buffer) =>
            {
                if (index >= batchResponse.Count)
                {
                    buffer = default;
                    return Result.Success;
                }

                var opResult = batchResponse[(int)index];
                var opSubStatusCode = inMemoryBatchResponse?.GetOperationSubStatusCode((int)index) ?? 0;
                var row = new RowBuffer(256, resizer);
                row.InitLayout(HybridRowVersion.V1, batchResultLayout, batchLayoutResolverForResponse);
                var r = RowWriter.WriteBuffer(ref row, (opResult, opSubStatusCode), (ref RowWriter writer, TypeArgument _, (TransactionalBatchOperationResult result, int subStatus) ctx) =>
                {
                    var wr = writer.WriteInt32("statusCode", (int)ctx.result.StatusCode);
                    if (wr != Result.Success) return wr;

                    if (ctx.subStatus != 0)
                    {
                        wr = writer.WriteInt32("subStatusCode", ctx.subStatus);
                        if (wr != Result.Success) return wr;
                    }

                    if (ctx.result.ETag is not null)
                    {
                        wr = writer.WriteString("eTag", ctx.result.ETag);
                        if (wr != Result.Success) return wr;
                    }

                    if (ctx.result.ResourceStream is not null)
                    {
                        using var ms = new MemoryStream();
                        ctx.result.ResourceStream.CopyTo(ms);
                        ctx.result.ResourceStream.Position = 0;
                        wr = writer.WriteBinary("resourceBody", ms.ToArray());
                        if (wr != Result.Success) return wr;
                    }

                    return writer.WriteFloat64("requestCharge", 1.0);
                });

                if (r != Result.Success)
                {
                    buffer = default;
                    return r;
                }

                buffer = resizer.Memory.Slice(0, row.Length);
                return Result.Success;
            },
            new MemorySpanResizer<byte>(128));

        var httpResponse = new HttpResponseMessage(batchResponse.StatusCode)
        {
            Content = new ByteArrayContent(responseStream.ToArray())
        };
        httpResponse.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        httpResponse.Headers.Add("x-ms-request-charge", "1");
        httpResponse.Headers.Add("x-ms-activity-id", Guid.NewGuid().ToString());
        httpResponse.Headers.Add("x-ms-session-token", sessionToken);
        if (inMemoryBatchResponse is { BatchSubStatusCode: not 0 })
        {
            httpResponse.Headers.Add("x-ms-substatus", inMemoryBatchResponse.BatchSubStatusCode.ToString(CultureInfo.InvariantCulture));
        }
        return httpResponse;
    }

    private static TransactionalBatchItemRequestOptions? BuildBatchItemRequestOptions(BatchOperation op)
    {
        if (op.IfMatch is null && op.IfNoneMatch is null)
            return null;
        return new TransactionalBatchItemRequestOptions
        {
            IfMatchEtag = op.IfMatch,
            IfNoneMatchEtag = op.IfNoneMatch
        };
    }
}
