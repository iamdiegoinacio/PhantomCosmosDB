#nullable enable
using System.Net;
using System.Text;
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Phantom.CosmosDB.Core.Infrastructure.HttpGateway;

/// <summary>
/// Handles Gateway HTTP DDL operations for containers (<c>/dbs/{db}/colls</c> and <c>/dbs/{db}/colls/{coll}</c>)
/// and resolves container handlers by key or collection RID.
/// </summary>
internal sealed class GatewayContainerLifecycleRouter
{
    private readonly DynamicContainerRegistry _registry;
    private readonly FakeCosmosHandler _default;

    public GatewayContainerLifecycleRouter(DynamicContainerRegistry registry, FakeCosmosHandler defaultHandler)
    {
        _registry = registry;
        _default = defaultHandler;
    }

    public async Task<HttpResponseMessage> HandleCreateContainerAsync(
        HttpRequestMessage request,
        string dbName,
        CancellationToken cancellationToken)
    {
        var body = request.Content is not null
            ? await request.Content.ReadAsStringAsync(cancellationToken)
            : "{}";
        var containerProps = JsonConvert.DeserializeObject<ContainerProperties>(body)
            ?? new ContainerProperties("unknown", "/id");

        var containerName = containerProps.Id;
        var registryKey = GetNewRegistryKey(dbName, containerName);

        var isIfNotExists = CosmosHttpHeaders.IsUpsertRequest(request);

        if (_registry.Handlers.ContainsKey(registryKey))
        {
            if (isIfNotExists && _registry.Handlers.TryGetValue(registryKey, out var existingHandler))
            {
                return CreateContainerMetadataResponse(existingHandler, HttpStatusCode.OK);
            }

            return new HttpResponseMessage(HttpStatusCode.Conflict)
            {
                Content = new StringContent(
                    $"{{\"message\":\"Container '{containerName}' already exists.\"}}",
                    Encoding.UTF8, "application/json")
            };
        }

        var newContainer = new InMemoryContainer(containerProps);

        if (request.Headers.TryGetValues("x-ms-offer-throughput", out var throughputValues) &&
            int.TryParse(throughputValues.FirstOrDefault(), out var throughputValue))
        {
            newContainer._throughput = throughputValue;
        }

        var newHandler = new FakeCosmosHandler(newContainer);

        _registry.Handlers[registryKey] = newHandler;
        _registry.BackingContainers[registryKey] = newContainer;

        var dbMap = _registry.DatabaseContainerKeys.GetOrAdd(dbName, _ => new(StringComparer.Ordinal));
        dbMap[containerName] = registryKey;

        if (_registry.Client is not null)
        {
            _registry.SdkContainers[registryKey] =
                _registry.Client.GetContainer(dbName, containerName);
        }

        return CreateContainerMetadataResponse(newHandler, HttpStatusCode.Created);
    }

    public HttpResponseMessage HandleDeleteContainer(string dbName, string containerName)
    {
        var registryKey = ResolveExistingRegistryKey(dbName, containerName);
        if (registryKey is null || !_registry.Handlers.TryRemove(registryKey, out var handler))
        {
            return new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent(
                    $"{{\"message\":\"Container '{containerName}' not found.\"}}",
                    Encoding.UTF8, "application/json")
            };
        }

        _registry.BackingContainers.TryRemove(registryKey, out _);
        _registry.SdkContainers.TryRemove(registryKey, out _);

        if (_registry.DatabaseContainerKeys.TryGetValue(dbName, out var dbMap))
            dbMap.TryRemove(containerName, out _);

        handler.BackingInMemoryContainer._isDeleted = true;

        return new HttpResponseMessage(HttpStatusCode.NoContent)
        {
            Headers = { { "x-ms-request-charge", "1" } }
        };
    }

    public HttpResponseMessage HandleReadContainer(string dbName, string containerName)
    {
        var registryKey = ResolveExistingRegistryKey(dbName, containerName);
        if (registryKey is not null && _registry.Handlers.TryGetValue(registryKey, out var handler))
        {
            return CreateContainerMetadataResponse(handler, HttpStatusCode.OK);
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent(
                $"{{\"message\":\"Container '{containerName}' not found.\"}}",
                Encoding.UTF8, "application/json")
        };
    }

    public HttpResponseMessage HandleListContainers(string dbName)
    {
        var containerArray = new JArray();

        if (_registry.DatabaseContainerKeys.TryGetValue(dbName, out var containerKeys))
        {
            foreach (var (_, registryKey) in containerKeys)
            {
                if (_registry.Handlers.TryGetValue(registryKey, out var h))
                    containerArray.Add(JObject.Parse(h.GetCollectionMetadataInternal()));
            }
        }
        else
        {
            foreach (var handler in _registry.Handlers.Values)
            {
                containerArray.Add(JObject.Parse(handler.GetCollectionMetadataInternal()));
            }
        }

        var result = new JObject
        {
            ["_rid"] = _default.DatabaseRidInternal,
            ["DocumentCollections"] = containerArray,
            ["_count"] = containerArray.Count
        };

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(result.ToString(Formatting.None), Encoding.UTF8, "application/json")
        };
        response.Headers.TryAddWithoutValidation("x-ms-request-charge", "1");
        return response;
    }

    public async Task<HttpResponseMessage> HandleReplaceContainerAsync(
        HttpRequestMessage request,
        string dbName,
        string containerName,
        CancellationToken cancellationToken)
    {
        var registryKey = ResolveExistingRegistryKey(dbName, containerName);
        if (registryKey is null || !_registry.Handlers.TryGetValue(registryKey, out var handler))
        {
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        if (request.Content is not null)
        {
            var body = await request.Content.ReadAsStringAsync(cancellationToken);
            var props = JObject.Parse(body);
            var newPkPath = props.SelectToken("partitionKey.paths[0]")?.ToString();
            if (newPkPath is not null)
            {
                var existingPkPath = handler.BackingInMemoryContainer.PartitionKeyPaths.FirstOrDefault();
                if (existingPkPath is not null && !string.Equals(newPkPath, existingPkPath, StringComparison.Ordinal))
                {
                    return new HttpResponseMessage(HttpStatusCode.BadRequest)
                    {
                        Content = new StringContent(
                            $"{{\"message\":\"Partition key path cannot be changed. " +
                            $"Existing: '{existingPkPath}', Requested: '{newPkPath}'\"}}",
                            Encoding.UTF8, "application/json")
                    };
                }
            }

            var container = handler.BackingInMemoryContainer;
            if (props["defaultTtl"] is { Type: JTokenType.Integer } ttlToken)
            {
                var ttl = ttlToken.Value<int>();
                if (ttl != 0) container.DefaultTimeToLive = ttl;
            }
            else if (props["defaultTtl"] is { Type: JTokenType.Null })
            {
                container.DefaultTimeToLive = null;
            }

            var replacedProps = JsonConvert.DeserializeObject<ContainerProperties>(body);
            if (replacedProps is not null)
            {
                if (replacedProps.IndexingPolicy is not null && props["indexingPolicy"] is not null)
                {
                    container.IndexingPolicy = replacedProps.IndexingPolicy;
                }

                if (replacedProps.UniqueKeyPolicy is not null && props["uniqueKeyPolicy"] is not null)
                {
                    container.UniqueKeyPolicy = replacedProps.UniqueKeyPolicy;
                }

                if (props["computedProperties"] is not null)
                {
                    container.ContainerPropertiesInternal.ComputedProperties.Clear();
                    foreach (var cp in replacedProps.ComputedProperties)
                    {
                        container.ContainerPropertiesInternal.ComputedProperties.Add(cp);
                    }
                    container.ResetComputedPropertiesCache();
                }
            }
        }

        return CreateContainerMetadataResponse(handler, HttpStatusCode.OK);
    }

    public string? ResolveExistingRegistryKey(string dbName, string containerName)
    {
        var compoundKey = $"{dbName}/{containerName}";
        if (_registry.Handlers.ContainsKey(compoundKey))
            return compoundKey;
        if (_registry.Handlers.ContainsKey(containerName))
            return containerName;

        // Match by collection RID (e.g. "AQAAAA==") when SDK refreshes collection metadata by RID
        var decoded = Uri.UnescapeDataString(containerName);
        if (_registry.DatabaseContainerKeys.TryGetValue(dbName, out var dbMap))
        {
            foreach (var (_, registryKey) in dbMap)
            {
                if (_registry.Handlers.TryGetValue(registryKey, out var h) &&
                    string.Equals(h.CollectionRidInternal, decoded, StringComparison.Ordinal))
                {
                    return registryKey;
                }
            }
        }

        foreach (var (registryKey, h) in _registry.Handlers)
        {
            if (string.Equals(h.CollectionRidInternal, decoded, StringComparison.Ordinal))
            {
                return registryKey;
            }
        }

        return null;
    }

    private string GetNewRegistryKey(string dbName, string containerName)
    {
        return _registry.CompoundKeyDatabases.Contains(dbName)
            ? $"{dbName}/{containerName}"
            : containerName;
    }

    private static HttpResponseMessage CreateContainerMetadataResponse(
        FakeCosmosHandler handler,
        HttpStatusCode statusCode)
    {
        var metadata = handler.GetCollectionMetadataInternal();
        var response = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(metadata, Encoding.UTF8, "application/json")
        };
        response.Headers.TryAddWithoutValidation("x-ms-request-charge", "1");
        response.Headers.TryAddWithoutValidation("etag", $"\"{Guid.NewGuid()}\"");
        return response;
    }
}
