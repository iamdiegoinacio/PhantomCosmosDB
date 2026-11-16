#nullable enable
using System.Net;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Scripts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Phantom.CosmosDB.Core.Infrastructure.SdkInterop;

namespace Phantom.CosmosDB.Core.Application.Scripts;

/// <summary>
/// In-memory implementation of <see cref="Microsoft.Azure.Cosmos.Scripts.Scripts"/> for stored procedures, triggers, and UDFs.
/// </summary>
internal sealed class InMemoryScripts : Microsoft.Azure.Cosmos.Scripts.Scripts
{
    private readonly InMemoryContainer _c;
    private readonly ScriptCatalog _catalog;

    public InMemoryScripts(InMemoryContainer container, ScriptCatalog catalog)
    {
        _c = container;
        _catalog = catalog;
    }

    private ResponseMessage BuildStream(HttpStatusCode status, string? json)
        => _c.CreateResponseMessage(status, json);

    // ── Stored Procedure CRUD & Execute ───────────────────────────────

    public override Task<StoredProcedureResponse> CreateStoredProcedureAsync(
        StoredProcedureProperties storedProcedureProperties, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)
        => ScriptResourceCrudHelper.CreateAsync<StoredProcedureProperties, StoredProcedureResponse>(
            _catalog.StoredProcedureProperties, storedProcedureProperties.Id, storedProcedureProperties, "StoredProcedure", EnrichStoredProcedureSystemProperties);

    public override Task<StoredProcedureResponse> ReadStoredProcedureAsync(
        string id, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)
        => ScriptResourceCrudHelper.ReadAsync<StoredProcedureProperties, StoredProcedureResponse>(
            _catalog.StoredProcedureProperties, id, "StoredProcedure", EnrichStoredProcedureSystemProperties);

    public override Task<StoredProcedureResponse> ReplaceStoredProcedureAsync(
        StoredProcedureProperties storedProcedureProperties, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)
        => ScriptResourceCrudHelper.ReplaceAsync<StoredProcedureProperties, StoredProcedureResponse>(
            _catalog.StoredProcedureProperties, storedProcedureProperties.Id, storedProcedureProperties, "StoredProcedure", EnrichStoredProcedureSystemProperties);

    public override Task<StoredProcedureResponse> DeleteStoredProcedureAsync(
        string id, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)
        => ScriptResourceCrudHelper.DeleteAsync<StoredProcedureProperties, StoredProcedureResponse>(
            _catalog.StoredProcedureProperties, id, "StoredProcedure", k => _catalog.StoredProcedures.Remove(k));

    public override Task<ResponseMessage> CreateStoredProcedureStreamAsync(
        StoredProcedureProperties storedProcedureProperties, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)
        => ScriptResourceCrudHelper.CreateStreamAsync(
            _catalog.StoredProcedureProperties, storedProcedureProperties.Id, storedProcedureProperties, "StoredProcedure", BuildStream, EnrichStoredProcedureSystemProperties);

    public override Task<ResponseMessage> ReadStoredProcedureStreamAsync(
        string id, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)
        => ScriptResourceCrudHelper.ReadStreamAsync(
            _catalog.StoredProcedureProperties, id, "StoredProcedure", BuildStream, EnrichStoredProcedureSystemProperties);

    public override Task<ResponseMessage> ReplaceStoredProcedureStreamAsync(
        StoredProcedureProperties storedProcedureProperties, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)
        => ScriptResourceCrudHelper.ReplaceStreamAsync(
            _catalog.StoredProcedureProperties, storedProcedureProperties.Id, storedProcedureProperties, "StoredProcedure", BuildStream, EnrichStoredProcedureSystemProperties);

    public override Task<ResponseMessage> DeleteStoredProcedureStreamAsync(
        string id, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)
        => ScriptResourceCrudHelper.DeleteStreamAsync(
            _catalog.StoredProcedureProperties, id, "StoredProcedure", BuildStream, k => _catalog.StoredProcedures.Remove(k));

    public override Task<StoredProcedureExecuteResponse<TOutput>> ExecuteStoredProcedureAsync<TOutput>(
        string storedProcedureId, PartitionKey partitionKey, dynamic[] parameters, StoredProcedureRequestOptions? requestOptions = null, CancellationToken cancellationToken = default)
        => StoredProcedureExecutionService.ExecuteAsync<TOutput>(_c, _catalog, storedProcedureId, partitionKey, parameters);

    public override Task<ResponseMessage> ExecuteStoredProcedureStreamAsync(
        string storedProcedureId, PartitionKey partitionKey, dynamic[] parameters, StoredProcedureRequestOptions? requestOptions = null, CancellationToken cancellationToken = default)
        => StoredProcedureExecutionService.ExecuteStreamAsync(_c, _catalog, storedProcedureId, partitionKey, parameters);

    public override Task<ResponseMessage> ExecuteStoredProcedureStreamAsync(
        string storedProcedureId, Stream streamPayload, PartitionKey partitionKey, StoredProcedureRequestOptions? requestOptions = null, CancellationToken cancellationToken = default)
    {
        using var reader = new StreamReader(streamPayload);
        var parameters = JsonConvert.DeserializeObject<dynamic[]>(reader.ReadToEnd()) ?? Array.Empty<dynamic>();
        return ExecuteStoredProcedureStreamAsync(storedProcedureId, partitionKey, parameters, requestOptions, cancellationToken);
    }

    // ── Trigger CRUD ──────────────────────────────────────────────────

    public override Task<TriggerResponse> CreateTriggerAsync(
        TriggerProperties triggerProperties, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)
        => ScriptResourceCrudHelper.CreateAsync<TriggerProperties, TriggerResponse>(
            _catalog.TriggerProperties, triggerProperties.Id, triggerProperties, "Trigger");

    public override Task<TriggerResponse> ReadTriggerAsync(
        string id, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)
        => ScriptResourceCrudHelper.ReadAsync<TriggerProperties, TriggerResponse>(
            _catalog.TriggerProperties, id, "Trigger");

    public override Task<TriggerResponse> ReplaceTriggerAsync(
        TriggerProperties triggerProperties, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)
        => ScriptResourceCrudHelper.ReplaceAsync<TriggerProperties, TriggerResponse>(
            _catalog.TriggerProperties, triggerProperties.Id, triggerProperties, "Trigger");

    public override Task<TriggerResponse> DeleteTriggerAsync(
        string id, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)
        => ScriptResourceCrudHelper.DeleteAsync<TriggerProperties, TriggerResponse>(
            _catalog.TriggerProperties, id, "Trigger", k => _catalog.Triggers.Remove(k));

    public override Task<ResponseMessage> CreateTriggerStreamAsync(
        TriggerProperties triggerProperties, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)
        => ScriptResourceCrudHelper.CreateStreamAsync(_catalog.TriggerProperties, triggerProperties.Id, triggerProperties, "Trigger", BuildStream);

    public override Task<ResponseMessage> ReadTriggerStreamAsync(
        string id, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)
        => ScriptResourceCrudHelper.ReadStreamAsync(_catalog.TriggerProperties, id, "Trigger", BuildStream);

    public override Task<ResponseMessage> ReplaceTriggerStreamAsync(
        TriggerProperties triggerProperties, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)
        => ScriptResourceCrudHelper.ReplaceStreamAsync(_catalog.TriggerProperties, triggerProperties.Id, triggerProperties, "Trigger", BuildStream);

    public override Task<ResponseMessage> DeleteTriggerStreamAsync(
        string id, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)
        => ScriptResourceCrudHelper.DeleteStreamAsync(_catalog.TriggerProperties, id, "Trigger", BuildStream, k => _catalog.Triggers.Remove(k));

    // ── UDF CRUD ──────────────────────────────────────────────────────

    public override Task<UserDefinedFunctionResponse> CreateUserDefinedFunctionAsync(
        UserDefinedFunctionProperties userDefinedFunctionProperties, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)
        => ScriptResourceCrudHelper.CreateAsync<UserDefinedFunctionProperties, UserDefinedFunctionResponse>(
            _catalog.UdfProperties, userDefinedFunctionProperties.Id, userDefinedFunctionProperties, "UserDefinedFunction");

    public override Task<UserDefinedFunctionResponse> ReadUserDefinedFunctionAsync(
        string id, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)
        => ScriptResourceCrudHelper.ReadAsync<UserDefinedFunctionProperties, UserDefinedFunctionResponse>(
            _catalog.UdfProperties, id, "UserDefinedFunction");

    public override Task<UserDefinedFunctionResponse> ReplaceUserDefinedFunctionAsync(
        UserDefinedFunctionProperties userDefinedFunctionProperties, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)
        => ScriptResourceCrudHelper.ReplaceAsync<UserDefinedFunctionProperties, UserDefinedFunctionResponse>(
            _catalog.UdfProperties, userDefinedFunctionProperties.Id, userDefinedFunctionProperties, "UserDefinedFunction");

    public override Task<UserDefinedFunctionResponse> DeleteUserDefinedFunctionAsync(
        string id, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)
        => ScriptResourceCrudHelper.DeleteAsync<UserDefinedFunctionProperties, UserDefinedFunctionResponse>(
            _catalog.UdfProperties, id, "UserDefinedFunction", k => _catalog.UserDefinedFunctions.Remove($"UDF.{k}"));

    public override Task<ResponseMessage> CreateUserDefinedFunctionStreamAsync(
        UserDefinedFunctionProperties userDefinedFunctionProperties, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)
        => ScriptResourceCrudHelper.CreateStreamAsync(_catalog.UdfProperties, userDefinedFunctionProperties.Id, userDefinedFunctionProperties, "UserDefinedFunction", BuildStream);

    public override Task<ResponseMessage> ReadUserDefinedFunctionStreamAsync(
        string id, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)
        => ScriptResourceCrudHelper.ReadStreamAsync(_catalog.UdfProperties, id, "UserDefinedFunction", BuildStream);

    public override Task<ResponseMessage> ReplaceUserDefinedFunctionStreamAsync(
        UserDefinedFunctionProperties userDefinedFunctionProperties, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)
        => ScriptResourceCrudHelper.ReplaceStreamAsync(_catalog.UdfProperties, userDefinedFunctionProperties.Id, userDefinedFunctionProperties, "UserDefinedFunction", BuildStream);

    public override Task<ResponseMessage> DeleteUserDefinedFunctionStreamAsync(
        string id, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)
        => ScriptResourceCrudHelper.DeleteStreamAsync(_catalog.UdfProperties, id, "UserDefinedFunction", BuildStream, k => _catalog.UserDefinedFunctions.Remove($"UDF.{k}"));

    // ── Query iterators (typed & stream) ──────────────────────────────

    public override FeedIterator<T> GetStoredProcedureQueryIterator<T>(
        string? queryText = null, string? continuationToken = null, QueryRequestOptions? requestOptions = null)
        => new InMemoryFeedIterator<T>(() => FilterMetadata<T, StoredProcedureProperties>(_catalog.StoredProcedureProperties, queryText));

    public override FeedIterator<T> GetStoredProcedureQueryIterator<T>(
        QueryDefinition queryDefinition, string? continuationToken = null, QueryRequestOptions? requestOptions = null)
        => new InMemoryFeedIterator<T>(() => FilterMetadata<T, StoredProcedureProperties>(_catalog.StoredProcedureProperties, queryDefinition));

    public override FeedIterator<T> GetTriggerQueryIterator<T>(
        string? queryText = null, string? continuationToken = null, QueryRequestOptions? requestOptions = null)
        => new InMemoryFeedIterator<T>(() => FilterMetadata<T, TriggerProperties>(_catalog.TriggerProperties, queryText));

    public override FeedIterator<T> GetTriggerQueryIterator<T>(
        QueryDefinition queryDefinition, string? continuationToken = null, QueryRequestOptions? requestOptions = null)
        => new InMemoryFeedIterator<T>(() => FilterMetadata<T, TriggerProperties>(_catalog.TriggerProperties, queryDefinition));

    public override FeedIterator<T> GetUserDefinedFunctionQueryIterator<T>(
        string? queryText = null, string? continuationToken = null, QueryRequestOptions? requestOptions = null)
        => new InMemoryFeedIterator<T>(() => FilterMetadata<T, UserDefinedFunctionProperties>(_catalog.UdfProperties, queryText));

    public override FeedIterator<T> GetUserDefinedFunctionQueryIterator<T>(
        QueryDefinition queryDefinition, string? continuationToken = null, QueryRequestOptions? requestOptions = null)
        => new InMemoryFeedIterator<T>(() => FilterMetadata<T, UserDefinedFunctionProperties>(_catalog.UdfProperties, queryDefinition));

    public override FeedIterator GetStoredProcedureQueryStreamIterator(
        string? queryText = null, string? continuationToken = null, QueryRequestOptions? requestOptions = null)
        => new InMemoryStreamFeedIterator(() => FilterMetadata<object, StoredProcedureProperties>(_catalog.StoredProcedureProperties, queryText), "StoredProcedures", () => _c.CurrentSessionToken);

    public override FeedIterator GetStoredProcedureQueryStreamIterator(
        QueryDefinition queryDefinition, string? continuationToken = null, QueryRequestOptions? requestOptions = null)
        => new InMemoryStreamFeedIterator(() => FilterMetadata<object, StoredProcedureProperties>(_catalog.StoredProcedureProperties, queryDefinition), "StoredProcedures", () => _c.CurrentSessionToken);

    public override FeedIterator GetTriggerQueryStreamIterator(
        string? queryText = null, string? continuationToken = null, QueryRequestOptions? requestOptions = null)
        => new InMemoryStreamFeedIterator(() => FilterMetadata<object, TriggerProperties>(_catalog.TriggerProperties, queryText), "Triggers", () => _c.CurrentSessionToken);

    public override FeedIterator GetTriggerQueryStreamIterator(
        QueryDefinition queryDefinition, string? continuationToken = null, QueryRequestOptions? requestOptions = null)
        => new InMemoryStreamFeedIterator(() => FilterMetadata<object, TriggerProperties>(_catalog.TriggerProperties, queryDefinition), "Triggers", () => _c.CurrentSessionToken);

    public override FeedIterator GetUserDefinedFunctionQueryStreamIterator(
        string? queryText = null, string? continuationToken = null, QueryRequestOptions? requestOptions = null)
        => new InMemoryStreamFeedIterator(() => FilterMetadata<object, UserDefinedFunctionProperties>(_catalog.UdfProperties, queryText), "UserDefinedFunctions", () => _c.CurrentSessionToken);

    public override FeedIterator GetUserDefinedFunctionQueryStreamIterator(
        QueryDefinition queryDefinition, string? continuationToken = null, QueryRequestOptions? requestOptions = null)
        => new InMemoryStreamFeedIterator(() => FilterMetadata<object, UserDefinedFunctionProperties>(_catalog.UdfProperties, queryDefinition), "UserDefinedFunctions", () => _c.CurrentSessionToken);

    public static void EnrichStoredProcedureSystemProperties(StoredProcedureProperties props)
    {
        var jObj = JObject.Parse(JsonConvert.SerializeObject(props));
        if (string.IsNullOrEmpty(jObj["_etag"]?.ToString())) jObj["_etag"] = $"\"{Guid.NewGuid()}\"";
        if (string.IsNullOrEmpty(jObj["_self"]?.ToString())) jObj["_self"] = $"dbs/db/colls/col/sprocs/{props.Id}";
        if (jObj["_ts"] == null || jObj["_ts"]!.Type == JTokenType.Null) jObj["_ts"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (string.IsNullOrEmpty(jObj["_rid"]?.ToString())) jObj["_rid"] = Convert.ToBase64String(Guid.NewGuid().ToByteArray()).TrimEnd('=');
        JsonConvert.PopulateObject(jObj.ToString(), props);
    }

    private static List<TResult> FilterMetadata<TResult, TValue>(Dictionary<string, TValue> dict, string? queryText)
        => Application.Query.MetadataSqlQueryExecutor.Execute<TResult, TValue>(dict.Values, queryText);

    private static List<TResult> FilterMetadata<TResult, TValue>(Dictionary<string, TValue> dict, QueryDefinition? queryDefinition)
    {
        var parameters = queryDefinition is not null ? CosmosSdkIntrospector.ExtractQueryParameters(queryDefinition) : null;
        return Application.Query.MetadataSqlQueryExecutor.Execute<TResult, TValue>(dict.Values, queryDefinition?.QueryText, parameters);
    }
}
