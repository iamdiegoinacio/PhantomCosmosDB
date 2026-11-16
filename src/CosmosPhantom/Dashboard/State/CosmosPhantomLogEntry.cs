using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace CosmosPhantom.Dashboard.State;

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
    double DurationMs,
    string? SqlQuery,
    bool IsChaosInjected);
