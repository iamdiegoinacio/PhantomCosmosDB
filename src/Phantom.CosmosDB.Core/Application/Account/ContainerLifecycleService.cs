#nullable enable
using System.Net;
using Phantom.CosmosDB.Core.Application.Contracts;
using Phantom.CosmosDB.Core.Domain.Primitives;
using Phantom.CosmosDB.Core.Domain.Validation;
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json;
using static Phantom.CosmosDB.Core.Domain.Primitives.ContainerConstants;

namespace Phantom.CosmosDB.Core.Application.Account;

/// <summary>
/// Manages container lifecycle operations (Read, Replace, Delete) and throughput configuration.
/// </summary>
internal static class ContainerLifecycleService
{
    public static Task<ContainerResponse> ReadContainerAsync(
        InMemoryContainer container,
        IDocumentStore store,
        ContainerProperties properties,
        JsonSerializerSettings jsonSettings,
        CancellationToken cancellationToken)
    {
        _ = jsonSettings;
        cancellationToken.ThrowIfCancellationRequested();
        if (container._isDeleted || (!container.ExplicitlyCreated && store.Items.IsEmpty))
        {
            throw InMemoryCosmosException.Create(
                $"Container '{container.Id}' not found.",
                HttpStatusCode.NotFound,
                CosmosSubStatusCodes.OwnerResourceNotFound,
                Guid.NewGuid().ToString(),
                SyntheticRequestCharge);
        }

        properties.IndexingPolicy = container.IndexingPolicy;
        properties.DefaultTimeToLive = container.DefaultTimeToLive;
        return Task.FromResult(CorePortDefaults.SdkMapper.CreateContainerResponse(container, properties, HttpStatusCode.OK));
    }

    public static Task<ResponseMessage> ReadContainerStreamAsync(
        InMemoryContainer container,
        IDocumentStore store,
        ContainerProperties properties,
        JsonSerializerSettings jsonSettings,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (container._isDeleted || (!container.ExplicitlyCreated && store.Items.IsEmpty))
        {
            return Task.FromResult(container.CreateResponseMessage(HttpStatusCode.NotFound));
        }

        properties.IndexingPolicy = container.IndexingPolicy;
        properties.DefaultTimeToLive = container.DefaultTimeToLive;
        return Task.FromResult(container.CreateResponseMessage(
            HttpStatusCode.OK, JsonConvert.SerializeObject(properties, jsonSettings)));
    }

    public static ContainerProperties ApplyReplaceContainer(
        InMemoryContainer container,
        ContainerProperties existingProperties,
        ContainerProperties updatedProperties)
    {
        DocumentValidator.ValidateContainerReplace(existingProperties, updatedProperties);
        DocumentValidator.ValidateComputedProperties(updatedProperties);
        if (existingProperties.UniqueKeyPolicy?.UniqueKeys?.Count > 0 &&
            (updatedProperties.UniqueKeyPolicy is null || updatedProperties.UniqueKeyPolicy.UniqueKeys.Count == 0))
        {
            updatedProperties.UniqueKeyPolicy = existingProperties.UniqueKeyPolicy;
        }

        container.DefaultTimeToLive = updatedProperties.DefaultTimeToLive;
        if (updatedProperties.IndexingPolicy is not null)
        {
            container.IndexingPolicy = updatedProperties.IndexingPolicy;
        }

        container.ResetComputedPropertiesCache();
        return updatedProperties;
    }

    public static Task<ContainerResponse> ReplaceContainerAsync(
        InMemoryContainer container,
        ContainerProperties existingProperties,
        ContainerProperties updatedProperties,
        Action<ContainerProperties> applyProperties,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (updatedProperties.Id != container.Id)
        {
            throw InMemoryCosmosException.Create(
                $"Container id '{updatedProperties.Id}' does not match the existing container id '{container.Id}'.",
                HttpStatusCode.BadRequest, 0, Guid.NewGuid().ToString(), SyntheticRequestCharge);
        }

        applyProperties(ApplyReplaceContainer(container, existingProperties, updatedProperties));
        return Task.FromResult(CorePortDefaults.SdkMapper.CreateContainerResponse(container, updatedProperties, HttpStatusCode.OK));
    }

    public static Task<ResponseMessage> ReplaceContainerStreamAsync(
        InMemoryContainer container,
        ContainerProperties existingProperties,
        ContainerProperties updatedProperties,
        Action<ContainerProperties> applyProperties,
        JsonSerializerSettings jsonSettings,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (updatedProperties.Id != container.Id)
            return Task.FromResult(container.CreateResponseMessage(HttpStatusCode.BadRequest));

        try
        {
            applyProperties(ApplyReplaceContainer(container, existingProperties, updatedProperties));
        }
        catch (CosmosException)
        {
            return Task.FromResult(container.CreateResponseMessage(HttpStatusCode.BadRequest));
        }

        return Task.FromResult(container.CreateResponseMessage(
            HttpStatusCode.OK, JsonConvert.SerializeObject(updatedProperties, jsonSettings)));
    }

    public static Task<ContainerResponse> DeleteContainerAsync(
        InMemoryContainer container,
        Action markDeleted,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        markDeleted();
        return Task.FromResult(CorePortDefaults.SdkMapper.CreateContainerResponse(container, null, HttpStatusCode.NoContent));
    }

    public static Task<ResponseMessage> DeleteContainerStreamAsync(
        InMemoryContainer container,
        Action markDeleted,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        markDeleted();
        return Task.FromResult(container.CreateResponseMessage(HttpStatusCode.NoContent));
    }

    public static Task<ThroughputResponse> ReplaceThroughputAsync(
        InMemoryContainer container,
        int throughput)
    {
        DocumentValidator.ValidateThroughput(throughput, isAutoscale: false);
        container._throughput = throughput;
        return Task.FromResult(CorePortDefaults.SdkMapper.CreateThroughputResponse(
            ThroughputProperties.CreateManualThroughput(throughput)));
    }

    public static Task<ThroughputResponse> ReplaceThroughputAsync(
        InMemoryContainer container,
        ThroughputProperties throughputProperties)
    {
        if (throughputProperties.AutoscaleMaxThroughput.HasValue)
        {
            DocumentValidator.ValidateThroughput(throughputProperties.AutoscaleMaxThroughput, isAutoscale: true);
            container._throughput = throughputProperties.AutoscaleMaxThroughput.Value;
        }
        else if (throughputProperties.Throughput.HasValue)
        {
            DocumentValidator.ValidateThroughput(throughputProperties.Throughput, isAutoscale: false);
            container._throughput = throughputProperties.Throughput.Value;
        }

        return Task.FromResult(CorePortDefaults.SdkMapper.CreateThroughputResponse(throughputProperties));
    }
}
