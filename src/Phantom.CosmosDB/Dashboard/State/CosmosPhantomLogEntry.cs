using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Phantom.CosmosDB.Dashboard.State;

[JsonObject(NamingStrategyType = typeof(CamelCaseNamingStrategy))]
internal sealed record CosmosPhantomLogEntry(
    long Sequence,
    DateTimeOffset TimestampUtc,
    string Method,
    string Path,
    string? ContainerName,
    int StatusCode,
    int SubStatusCode,
    double RequestCharge,
    double EstimatedCostUsd,
    string RegionId,
    double DurationMs,
    string? SqlQuery,
    bool IsChaosInjected);
