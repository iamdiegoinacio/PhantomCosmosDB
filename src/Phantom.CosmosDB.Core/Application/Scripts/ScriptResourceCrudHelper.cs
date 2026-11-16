#nullable enable
using System.Net;
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json;
using NSubstitute;
using static Phantom.CosmosDB.Core.Domain.Primitives.ContainerConstants;

namespace Phantom.CosmosDB.Core.Application.Scripts;

/// <summary>
/// Generic CRUD helper eliminating boilerplate across StoredProcedure, Trigger, and UDF typed/stream operations.
/// </summary>
internal static class ScriptResourceCrudHelper
{
    public static Task<TResponse> CreateAsync<TProps, TResponse>(
        IDictionary<string, TProps> store,
        string id,
        TProps props,
        string kind,
        Action<TProps>? enrich = null)
        where TResponse : Response<TProps>
    {
        if (store.ContainsKey(id))
            throw InMemoryCosmosException.Create($"{kind} '{id}' already exists.", HttpStatusCode.Conflict, 0, Guid.NewGuid().ToString(), SyntheticRequestCharge);

        store[id] = props;
        enrich?.Invoke(props);
        return Task.FromResult(BuildTypedResponse<TProps, TResponse>(HttpStatusCode.Created, props));
    }

    public static Task<TResponse> ReadAsync<TProps, TResponse>(
        IDictionary<string, TProps> store,
        string id,
        string kind,
        Action<TProps>? enrich = null)
        where TResponse : Response<TProps>
    {
        if (!store.TryGetValue(id, out var props))
            throw InMemoryCosmosException.Create($"{kind} '{id}' not found.", HttpStatusCode.NotFound, 0, Guid.NewGuid().ToString(), SyntheticRequestCharge);

        enrich?.Invoke(props);
        return Task.FromResult(BuildTypedResponse<TProps, TResponse>(HttpStatusCode.OK, props));
    }

    public static Task<TResponse> ReplaceAsync<TProps, TResponse>(
        IDictionary<string, TProps> store,
        string id,
        TProps props,
        string kind,
        Action<TProps>? enrich = null)
        where TResponse : Response<TProps>
    {
        if (!store.ContainsKey(id))
            throw InMemoryCosmosException.Create($"{kind} '{id}' not found.", HttpStatusCode.NotFound, 0, Guid.NewGuid().ToString(), SyntheticRequestCharge);

        store[id] = props;
        enrich?.Invoke(props);
        return Task.FromResult(BuildTypedResponse<TProps, TResponse>(HttpStatusCode.OK, props));
    }

    public static Task<TResponse> DeleteAsync<TProps, TResponse>(
        IDictionary<string, TProps> store,
        string id,
        string kind,
        Action<string>? onDeleted = null)
        where TResponse : Response<TProps>
    {
        if (!store.Remove(id))
            throw InMemoryCosmosException.Create($"{kind} '{id}' not found.", HttpStatusCode.NotFound, 0, Guid.NewGuid().ToString(), SyntheticRequestCharge);

        onDeleted?.Invoke(id);
        var resp = Substitute.For<TResponse>();
        resp.StatusCode.Returns(HttpStatusCode.NoContent);
        return Task.FromResult(resp);
    }

    public static Task<ResponseMessage> CreateStreamAsync<TProps>(
        IDictionary<string, TProps> store,
        string id,
        TProps props,
        string kind,
        Func<HttpStatusCode, string?, ResponseMessage> buildStream,
        Action<TProps>? enrich = null)
    {
        if (store.ContainsKey(id))
            throw InMemoryCosmosException.Create($"{kind} '{id}' already exists.", HttpStatusCode.Conflict, 0, Guid.NewGuid().ToString(), SyntheticRequestCharge);

        store[id] = props;
        enrich?.Invoke(props);
        return Task.FromResult(buildStream(HttpStatusCode.Created, JsonConvert.SerializeObject(props)));
    }

    public static Task<ResponseMessage> ReadStreamAsync<TProps>(
        IDictionary<string, TProps> store,
        string id,
        string kind,
        Func<HttpStatusCode, string?, ResponseMessage> buildStream,
        Action<TProps>? enrich = null)
    {
        if (!store.TryGetValue(id, out var props))
            throw InMemoryCosmosException.Create($"{kind} '{id}' not found.", HttpStatusCode.NotFound, 0, Guid.NewGuid().ToString(), SyntheticRequestCharge);

        enrich?.Invoke(props);
        return Task.FromResult(buildStream(HttpStatusCode.OK, JsonConvert.SerializeObject(props)));
    }

    public static Task<ResponseMessage> ReplaceStreamAsync<TProps>(
        IDictionary<string, TProps> store,
        string id,
        TProps props,
        string kind,
        Func<HttpStatusCode, string?, ResponseMessage> buildStream,
        Action<TProps>? enrich = null)
    {
        if (!store.ContainsKey(id))
            throw InMemoryCosmosException.Create($"{kind} '{id}' not found.", HttpStatusCode.NotFound, 0, Guid.NewGuid().ToString(), SyntheticRequestCharge);

        store[id] = props;
        enrich?.Invoke(props);
        return Task.FromResult(buildStream(HttpStatusCode.OK, JsonConvert.SerializeObject(props)));
    }

    public static Task<ResponseMessage> DeleteStreamAsync<TProps>(
        IDictionary<string, TProps> store,
        string id,
        string kind,
        Func<HttpStatusCode, string?, ResponseMessage> buildStream,
        Action<string>? onDeleted = null)
    {
        if (!store.Remove(id))
            throw InMemoryCosmosException.Create($"{kind} '{id}' not found.", HttpStatusCode.NotFound, 0, Guid.NewGuid().ToString(), SyntheticRequestCharge);

        onDeleted?.Invoke(id);
        return Task.FromResult(buildStream(HttpStatusCode.NoContent, null));
    }

    private static TResponse BuildTypedResponse<TProps, TResponse>(HttpStatusCode statusCode, TProps props)
        where TResponse : Response<TProps>
    {
        var r = Substitute.For<TResponse>();
        r.StatusCode.Returns(statusCode);
        r.Resource.Returns(props);
        return r;
    }
}
