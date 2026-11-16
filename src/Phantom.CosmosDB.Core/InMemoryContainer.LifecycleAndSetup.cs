#nullable enable
using Phantom.CosmosDB.Core.Application.Account;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Scripts;
using Newtonsoft.Json.Linq;
using CosmosResponseFactory = Phantom.CosmosDB.Core.Infrastructure.SdkInterop.CosmosResponseFactory;

namespace Phantom.CosmosDB.Core;

internal partial class InMemoryContainer
{
    // ─── Container & Throughput Management ────────────────────────────────────

    public override Task<ContainerResponse> ReadContainerAsync(
        ContainerRequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default)
        => ContainerLifecycleService.ReadContainerAsync(
            this, _store, _containerProperties, JsonSettings, cancellationToken);

    public override Task<ResponseMessage> ReadContainerStreamAsync(
        ContainerRequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default)
        => ContainerLifecycleService.ReadContainerStreamAsync(
            this, _store, _containerProperties, JsonSettings, cancellationToken);

    public override Task<ContainerResponse> ReplaceContainerAsync(
        ContainerProperties containerProperties,
        ContainerRequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default)
        => ContainerLifecycleService.ReplaceContainerAsync(
            this, _containerProperties, containerProperties, p => _containerProperties = p, cancellationToken);

    public override Task<ResponseMessage> ReplaceContainerStreamAsync(
        ContainerProperties containerProperties,
        ContainerRequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default)
        => ContainerLifecycleService.ReplaceContainerStreamAsync(
            this, _containerProperties, containerProperties, p => _containerProperties = p, JsonSettings, cancellationToken);

    public override Task<ContainerResponse> DeleteContainerAsync(
        ContainerRequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default)
        => ContainerLifecycleService.DeleteContainerAsync(this, MarkDeleted, cancellationToken);

    public override Task<ResponseMessage> DeleteContainerStreamAsync(
        ContainerRequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default)
        => ContainerLifecycleService.DeleteContainerStreamAsync(this, MarkDeleted, cancellationToken);

    private void MarkDeleted()
    {
        _isDeleted = true;
        _store.Clear();
        _changeFeedLog.Clear();
        _scriptCatalog.Clear();
        OnDeleted?.Invoke();
    }

    public override Task<int?> ReadThroughputAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<int?>(_throughput);

    public override Task<ThroughputResponse> ReadThroughputAsync(
        RequestOptions requestOptions,
        CancellationToken cancellationToken = default)
        => Task.FromResult(CosmosResponseFactory.CreateThroughputResponse(
            ThroughputProperties.CreateManualThroughput(_throughput)));

    public override Task<ThroughputResponse> ReplaceThroughputAsync(
        int throughput,
        RequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default)
        => ContainerLifecycleService.ReplaceThroughputAsync(this, throughput);

    public override Task<ThroughputResponse> ReplaceThroughputAsync(
        ThroughputProperties throughputProperties,
        RequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default)
        => ContainerLifecycleService.ReplaceThroughputAsync(this, throughputProperties);

    // ─── State Persistence, Point-in-Time Restore & Test Setup ───────────────

    public void ClearItems()
    {
        _store.Clear();
        _changeFeedLog.Clear();
    }

    public void Dispose() => _persistence.SaveOnDispose(StateFilePath, ExportState);
    public void LoadPersistedState() => _persistence.LoadPersistedState(StateFilePath, ImportState);
    public string ExportState() => _persistence.ExportState(_store, DefaultTimeToLive);

    public void ImportState(string json)
        => _persistence.ImportState(json, Id, _store, PartitionKeyPaths, _containerProperties.UniqueKeyPolicy, ClearItems);

    public void ExportStateToFile(string filePath) => File.WriteAllText(filePath, ExportState());
    public void ImportStateFromFile(string filePath) => ImportState(File.ReadAllText(filePath));

    public void RestoreToPointInTime(DateTimeOffset pointInTime)
        => _persistence.RestoreToPointInTime(pointInTime, Id, _store, _changeFeedLog);

    // ─── Script & Trigger Registrations ───────────────────────────────────────

    public void RegisterUdf(string name, Func<object[], object?> implementation)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(implementation);
        _scriptCatalog.RegisterUdf(name, args => implementation(args!));
    }

    void IContainerTestSetup.RegisterUdf(string name, Func<object[], object> handler)
        => RegisterUdf(name, (Func<object[], object?>)handler);

    public void DeregisterUdf(string name) => _scriptCatalog.DeregisterUdf(name);

    public void RegisterStoredProcedure(string sprocId, Func<PartitionKey, dynamic[], string> handler)
        => _scriptCatalog.RegisterStoredProcedure(sprocId, handler);

    public void DeregisterStoredProcedure(string sprocId) => _scriptCatalog.DeregisterStoredProcedure(sprocId);

    public void RegisterTrigger(
        string triggerId,
        TriggerType triggerType,
        TriggerOperation triggerOperation,
        Func<JObject, JObject> preHandler)
        => _scriptCatalog.RegisterTrigger(triggerId, triggerType, triggerOperation, preHandler);

    public void RegisterTrigger(
        string triggerId,
        TriggerType triggerType,
        TriggerOperation triggerOperation,
        Action<JObject> postHandler)
        => _scriptCatalog.RegisterTrigger(triggerId, triggerType, triggerOperation, postHandler);

    public void DeregisterTrigger(string triggerId) => _scriptCatalog.DeregisterTrigger(triggerId);

    void IContainerTestSetup.RegisterStoredProcedure(string id, string jsBody)
        => _scriptCatalog.RegisterJsStoredProcedure(id, jsBody, SprocEngine, this);

    void IContainerTestSetup.RegisterUdf(string name, string jsBody)
        => _scriptCatalog.RegisterJsUdf(name, jsBody, JsTriggerEngine);

    void IContainerTestSetup.RegisterTrigger(
        string id,
        TriggerType type,
        TriggerOperation operation,
        string jsBody)
        => _scriptCatalog.RegisterJsTrigger(id, type, operation, jsBody, JsTriggerEngine);
}
