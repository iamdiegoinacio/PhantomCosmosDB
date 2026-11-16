using System;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace Phantom.CosmosDB.Seeding.Generation;

[JsonConverter(typeof(StringEnumConverter))]
public enum SeedStorageMode
{
    PersistAndLoad = 0,
    InMemoryOnly = 1
}

[JsonObject(NamingStrategyType = typeof(CamelCaseNamingStrategy))]
public sealed class SeedGenerationRequest
{
    public string? TemplateJson { get; set; }
    public long Count { get; set; } = 100;
    public SeedStorageMode Mode { get; set; } = SeedStorageMode.PersistAndLoad;
    public bool Append { get; set; } = true;
    public SeedIdStrategy IdStrategy { get; set; } = SeedIdStrategy.Auto;
    public string? IdPrefix { get; set; }
    public long? IdStartNumber { get; set; }
    public GuidV5InputMode GuidV5Mode { get; set; } = GuidV5InputMode.DeterministicTemplate;
    public string? GuidV5Template { get; set; }
    public string? GuidV5Namespace { get; set; }
    public int? RandomSeed { get; set; }
}

[JsonObject(NamingStrategyType = typeof(CamelCaseNamingStrategy))]
internal sealed class SeedGenerationJobStatus
{
    public required string JobId { get; init; }
    public required string ContainerName { get; init; }
    public required long TotalCount { get; init; }
    public long ProcessedCount;
    public long PersistedCount;
    public string Status { get; set; } = "Running";
    public string? Error { get; set; }
    public string? SavedFilePath { get; set; }
    public DateTimeOffset StartedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAtUtc { get; set; }

    public double Percent => TotalCount <= 0 ? 100.0 : Math.Round((double)Volatile.Read(ref ProcessedCount) / TotalCount * 100.0, 2);
    public double ElapsedSeconds => Math.Round(((CompletedAtUtc ?? DateTimeOffset.UtcNow) - StartedAtUtc).TotalSeconds, 2);
    public double ItemsPerSecond => ElapsedSeconds <= 0.05 ? Volatile.Read(ref ProcessedCount) : Math.Round(Volatile.Read(ref ProcessedCount) / ElapsedSeconds, 0);
}
