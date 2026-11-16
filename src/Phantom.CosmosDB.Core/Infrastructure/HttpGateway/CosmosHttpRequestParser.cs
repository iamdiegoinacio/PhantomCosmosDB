#nullable enable
using System.Text;
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json.Linq;

namespace Phantom.CosmosDB.Core.Infrastructure.HttpGateway;

/// <summary>
/// Parses Cosmos DB HTTP Gateway request headers, continuation tokens, document routes, and patch bodies.
/// </summary>
internal static class CosmosHttpRequestParser
{
    public static string ExtractDocumentId(HttpRequestMessage request)
    {
        var path = request.RequestUri?.AbsolutePath ?? "";
        var docsIndex = path.LastIndexOf("/docs/", StringComparison.OrdinalIgnoreCase);
        if (docsIndex >= 0)
        {
            var id = path[(docsIndex + 6)..];
            return Uri.UnescapeDataString(id.TrimEnd('/'));
        }
        return "";
    }

    public static bool HasDocumentId(string path)
    {
        var docsIndex = path.LastIndexOf("/docs/", StringComparison.OrdinalIgnoreCase);
        if (docsIndex < 0) return false;
        var afterDocs = path[(docsIndex + 6)..].TrimEnd('/');
        return afterDocs.Length > 0;
    }

    public static ItemRequestOptions BuildItemRequestOptions(HttpRequestMessage request)
    {
        var options = new ItemRequestOptions();
        if (request.Headers.IfMatch.Any())
            options.IfMatchEtag = request.Headers.IfMatch.First().Tag;
        if (request.Headers.IfNoneMatch.Any())
            options.IfNoneMatchEtag = request.Headers.IfNoneMatch.First().Tag;

        var preTriggers = ReadCommaSeparatedHeader(request, "x-ms-documentdb-pre-trigger-include");
        if (preTriggers.Count > 0)
            options.PreTriggers = preTriggers;

        var postTriggers = ReadCommaSeparatedHeader(request, "x-ms-documentdb-post-trigger-include");
        if (postTriggers.Count > 0)
            options.PostTriggers = postTriggers;

        return options;
    }

    private static List<string> ReadCommaSeparatedHeader(HttpRequestMessage request, string headerName)
    {
        var result = new List<string>();
        if (request.Headers.TryGetValues(headerName, out var values))
        {
            foreach (var value in values)
                result.AddRange(value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        return result;
    }

    public static bool IsQueryRequest(HttpRequestMessage request)
    {
        var contentType = request.Content?.Headers?.ContentType?.MediaType ?? "";
        if (contentType.Contains("query+json", StringComparison.OrdinalIgnoreCase))
            return true;
        return HasTrueHeader(request, "x-ms-documentdb-isquery");
    }

    public static bool IsUpsertRequest(HttpRequestMessage request) =>
        HasTrueHeader(request, "x-ms-documentdb-is-upsert");

    public static bool IsBatchRequest(HttpRequestMessage request) =>
        HasTrueHeader(request, "x-ms-cosmos-is-batch-request");

    public static bool IsQueryPlanRequest(HttpRequestMessage request) =>
        HasTrueHeader(request, "x-ms-cosmos-is-query-plan-request");

    private static bool HasTrueHeader(HttpRequestMessage request, string headerName) =>
        request.Headers.TryGetValues(headerName, out var values) &&
        values.Any(v => v.Equals("True", StringComparison.OrdinalIgnoreCase));

    public static bool IsChangeFeedRequest(HttpRequestMessage request)
    {
        if (!request.Headers.TryGetValues("A-IM", out var values))
            return false;

        return values.Any(v => v.Contains("Incremental", StringComparison.OrdinalIgnoreCase)
                               || v.Contains("Full-Fidelity", StringComparison.OrdinalIgnoreCase)
                               || v.Contains("AllVersionsAndDeletes", StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsFullFidelityChangeFeedRequest(HttpRequestMessage request)
    {
        return request.Headers.TryGetValues("A-IM", out var values)
               && values.Any(v => v.Contains("Full-Fidelity", StringComparison.OrdinalIgnoreCase)
                                  || v.Contains("AllVersionsAndDeletes", StringComparison.OrdinalIgnoreCase));
    }

    public static (IReadOnlyList<PatchOperation> Operations, string? Condition) ParsePatchBody(string body)
    {
        var jObj = JObject.Parse(body);
        var operations = new List<PatchOperation>();
        var condition = jObj["condition"]?.ToString();

        var opsToken = jObj["operations"]
            ?? throw new InvalidOperationException("Patch body missing 'operations' array.");

        foreach (var op in opsToken.ToObject<JArray>()!)
        {
            var opType = op["op"]!.ToString().ToLowerInvariant();
            var opPath = op["path"]!.ToString();
            var value = op["value"];

            operations.Add(opType switch
            {
                "set" => PatchOperation.Set(opPath, value),
                "replace" => PatchOperation.Replace(opPath, value),
                "add" => PatchOperation.Add(opPath, value),
                "remove" => PatchOperation.Remove(opPath),
                "incr" => PatchOperation.Increment(opPath, value!.Value<double>()),
                "move" => PatchOperation.Move(op["from"]!.ToString(), opPath),
                _ => throw new InvalidOperationException($"Unknown patch operation: {opType}")
            });
        }

        return (operations, condition);
    }

    public static PartitionKey? ExtractPartitionKey(HttpRequestMessage request)
    {
        if (!request.Headers.TryGetValues("x-ms-documentdb-partitionkey", out var values))
            return null;

        var raw = values.FirstOrDefault();
        if (raw is null)
            return null;

        try
        {
            var arr = JArray.Parse(raw);
            if (arr.Count == 1)
            {
                return arr[0].Type switch
                {
                    JTokenType.String => new PartitionKey(arr[0].Value<string>()),
                    JTokenType.Integer or JTokenType.Float => new PartitionKey(arr[0].Value<double>()),
                    JTokenType.Boolean => new PartitionKey(arr[0].Value<bool>()),
                    JTokenType.Null => PartitionKey.Null,
                    JTokenType.Object => PartitionKey.None, // SDK sends [{}] for PartitionKey.None
                    _ => new PartitionKey(arr[0].ToString())
                };
            }

            if (arr.Count > 1)
            {
                var builder = new PartitionKeyBuilder();
                foreach (var token in arr)
                {
                    switch (token.Type)
                    {
                        case JTokenType.String:
                            builder.Add(token.Value<string>()!);
                            break;
                        case JTokenType.Integer or JTokenType.Float:
                            builder.Add(token.Value<double>());
                            break;
                        case JTokenType.Boolean:
                            builder.Add(token.Value<bool>());
                            break;
                        case JTokenType.Null:
                            builder.AddNullValue();
                            break;
                        default:
                            builder.Add(token.ToString());
                            break;
                    }
                }
                return builder.Build();
            }
        }
        catch
        {
            // Ignore malformed partition key headers
        }

        return null;
    }

    public static int ExtractMaxItemCount(HttpRequestMessage request)
    {
        if (request.Headers.TryGetValues("x-ms-max-item-count", out var values) &&
            int.TryParse(values.FirstOrDefault(), out var count) && count > 0)
        {
            return count;
        }

        return 0;
    }

    public static string? ExtractPartitionKeyRangeId(HttpRequestMessage request) =>
        request.Headers.TryGetValues("x-ms-documentdb-partitionkeyrangeid", out var values)
            ? values.FirstOrDefault()
            : null;

    public static bool ExtractEnableScanInQuery(HttpRequestMessage request) =>
        request.Headers.TryGetValues("x-ms-documentdb-query-enable-scan", out var values) &&
        bool.TryParse(values.FirstOrDefault(), out var enabled) &&
        enabled;

    public static (string Key, int Offset)? DecodeContinuation(HttpRequestMessage request)
    {
        if (!request.Headers.TryGetValues("x-ms-continuation", out var values))
            return null;

        var raw = values.FirstOrDefault();
        if (raw is null)
            return null;

        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(raw));
            var obj = JsonParseHelpers.ParseJson(json);
            var key = obj["key"]?.Value<string>();
            var offset = obj["offset"]?.Value<int>() ?? 0;
            return key is not null ? (key, offset) : null;
        }
        catch
        {
            return null;
        }
    }
}
