#nullable enable
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Scripts;
using Newtonsoft.Json.Linq;

namespace Phantom.CosmosDB.Core.Application.Scripts;

internal sealed record RegisteredTrigger(
    TriggerType TriggerType,
    TriggerOperation TriggerOperation,
    Func<JObject, JObject>? PreHandler,
    Action<JObject>? PostHandler);

/// <summary>
/// Thread-safe catalog of stored procedures, triggers, and user-defined functions (both C# delegates and JS properties).
/// </summary>
internal sealed class ScriptCatalog
{
    public static readonly Func<object?[], object?> UdfPlaceholder = _ => null;

    public Dictionary<string, Func<object?[], object?>> UserDefinedFunctions { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, Func<PartitionKey, dynamic[], string>> StoredProcedures { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, StoredProcedureProperties> StoredProcedureProperties { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, RegisteredTrigger> Triggers { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, UserDefinedFunctionProperties> UdfProperties { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, TriggerProperties> TriggerProperties { get; } = new(StringComparer.Ordinal);

    public void Clear()
    {
        StoredProcedures.Clear();
        UserDefinedFunctions.Clear();
        UdfProperties.Clear();
        Triggers.Clear();
        StoredProcedureProperties.Clear();
        TriggerProperties.Clear();
    }

    public void RegisterUdf(string name, Func<object?[], object?> implementation)
    {
        ArgumentNullException.ThrowIfNull(name);
        UserDefinedFunctions["UDF." + name.TrimStart('.')] = implementation;
    }

    public void DeregisterUdf(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        UserDefinedFunctions.Remove("UDF." + name.TrimStart('.'));
    }

    public void RegisterStoredProcedure(string sprocId, Func<PartitionKey, dynamic[], string> handler)
    {
        ArgumentNullException.ThrowIfNull(sprocId);
        ArgumentNullException.ThrowIfNull(handler);
        StoredProcedures[sprocId] = handler;
    }

    public void DeregisterStoredProcedure(string sprocId)
    {
        ArgumentNullException.ThrowIfNull(sprocId);
        StoredProcedures.Remove(sprocId);
    }

    public void RegisterTrigger(
        string triggerId,
        TriggerType triggerType,
        TriggerOperation triggerOperation,
        Func<JObject, JObject> preHandler)
    {
        ArgumentNullException.ThrowIfNull(triggerId);
        Triggers[triggerId] = new RegisteredTrigger(triggerType, triggerOperation, preHandler, null);
    }

    public void RegisterTrigger(
        string triggerId,
        TriggerType triggerType,
        TriggerOperation triggerOperation,
        Action<JObject> postHandler)
    {
        ArgumentNullException.ThrowIfNull(triggerId);
        Triggers[triggerId] = new RegisteredTrigger(triggerType, triggerOperation, null, postHandler);
    }

    public void DeregisterTrigger(string triggerId)
    {
        ArgumentNullException.ThrowIfNull(triggerId);
        Triggers.Remove(triggerId);
    }

    private const string JsTriggersNotInstalled =
        "JavaScript stored procedure execution requires the Phantom.CosmosDB.JsTriggers package. " +
        "Install it and call container.UseJsTriggers(), or use the C# delegate overload instead.";

    private const string JsUdfNotInstalled =
        "JavaScript UDF execution requires the Phantom.CosmosDB.JsTriggers package. " +
        "Install it and call container.UseJsTriggers(), or use the C# delegate overload instead.";

    private const string JsTriggerNotInstalled =
        "JavaScript trigger execution requires the Phantom.CosmosDB.JsTriggers package. " +
        "Install it and call container.UseJsTriggers(), or use the C# delegate overload instead.";

    public void RegisterJsStoredProcedure(string id, string jsBody, ISprocEngine? sprocEngine, InMemoryContainer container)
    {
        if (sprocEngine is null)
            throw new NotImplementedException(JsTriggersNotInstalled);

        RegisterStoredProcedure(id, (pk, args) =>
            sprocEngine.Execute(jsBody, pk, args, new PartitionScopedCollectionContext(container, pk)) ?? "null");

        StoredProcedureProperties[id] = new StoredProcedureProperties { Id = id, Body = jsBody };
    }

    public void RegisterJsUdf(string name, string jsBody, IJsTriggerEngine? jsTriggerEngine)
    {
        if (jsTriggerEngine is not IJsUdfEngine)
            throw new NotImplementedException(JsUdfNotInstalled);

        UserDefinedFunctions["UDF." + name.TrimStart('.')] = UdfPlaceholder;
        UdfProperties[name] = new UserDefinedFunctionProperties { Id = name, Body = jsBody };
    }

    public void RegisterJsTrigger(
        string id,
        TriggerType type,
        TriggerOperation operation,
        string jsBody,
        IJsTriggerEngine? jsTriggerEngine)
    {
        if (jsTriggerEngine is null)
            throw new NotImplementedException(JsTriggerNotInstalled);

        TriggerProperties[id] = new TriggerProperties
        {
            Id = id,
            TriggerType = type,
            TriggerOperation = operation,
            Body = jsBody
        };

        if (type == TriggerType.Pre)
        {
            RegisterTrigger(id, type, operation, doc => jsTriggerEngine.ExecutePreTrigger(jsBody, doc));
        }
        else
        {
            RegisterTrigger(id, type, operation, (Action<JObject>)(doc => jsTriggerEngine.ExecutePostTrigger(jsBody, doc)));
        }
    }
}
