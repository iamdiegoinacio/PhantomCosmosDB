using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Phantom.CosmosDB.Configuration;
using Phantom.CosmosDB.Core;
using ContainerConfig = Phantom.CosmosDB.Configuration.ContainerConfig;

namespace Phantom.CosmosDB.Seeding.Generation;

internal static class SeedGenerationService
{
    private const int BatchSize = 500;

    public static List<JObject> ReadSeedItemsStreaming(string filePath, int skip, int take, out long totalCount)
    {
        totalCount = 0;
        var items = new List<JObject>();
        if (!File.Exists(filePath))
            return items;

        using var sr = new StreamReader(filePath, Encoding.UTF8);
        using var reader = new JsonTextReader(sr);

        if (!reader.Read() || reader.TokenType != JsonToken.StartArray)
            return items;

        while (reader.Read())
        {
            if (reader.TokenType == JsonToken.EndArray)
                break;

            if (reader.TokenType == JsonToken.StartObject)
            {
                var obj = JObject.Load(reader);
                if (totalCount >= skip && items.Count < take)
                {
                    items.Add(obj);
                }
                totalCount++;
            }
        }

        return items;
    }

    public static List<JObject> GeneratePreview(
        string containerName,
        ContainerConfig? containerConfig,
        string? seedFilePath,
        SeedGenerationRequest request,
        int previewCount = 5)
    {
        var existingSamples = !string.IsNullOrWhiteSpace(seedFilePath) && File.Exists(seedFilePath)
            ? ReadSeedItemsStreaming(seedFilePath, 0, 200, out _)
            : [];

        var (template, strategy, prefix, startIndex, guidV5Namespace) = ResolveGenerationParameters(
            containerName, existingSamples, request);

        var profile = SeedTemplateAnalyzer.BuildProfile(template, existingSamples, containerConfig);
        var random = request.RandomSeed.HasValue ? new Random(request.RandomSeed.Value) : Random.Shared;

        var results = new List<JObject>(previewCount);
        for (var i = 0; i < previewCount; i++)
        {
            results.Add(SeedTemplateAnalyzer.SynthesizeDocument(
                profile,
                containerName,
                startIndex + i,
                strategy,
                prefix,
                request.GuidV5Mode,
                request.GuidV5Template,
                guidV5Namespace,
                random));
        }

        return results;
    }

    public static async Task ExecuteGenerationJobAsync(
        SeedGenerationJobStatus job,
        InMemoryContainer container,
        ContainerConfig? containerConfig,
        string seedsFolderPath,
        SeedGenerationRequest request,
        CancellationToken ct)
    {
        var seedFilePath = Path.Combine(seedsFolderPath, $"{container.Id}.json");
        var existingItems = File.Exists(seedFilePath)
            ? ReadSeedItemsStreaming(seedFilePath, 0, int.MaxValue, out _)
            : [];

        var (template, strategy, prefix, startIndex, guidV5Namespace) = ResolveGenerationParameters(
            container.Id, existingItems, request);

        var profile = SeedTemplateAnalyzer.BuildProfile(template, existingItems, containerConfig);
        var random = request.RandomSeed.HasValue ? new Random(request.RandomSeed.Value) : Random.Shared;
        var pkPaths = containerConfig?.GetPartitionKeyPaths() ?? container.PartitionKeyPaths;

        if (!request.Append)
        {
            container.ClearItems();
            existingItems = [];
        }

        JsonTextWriter? jsonWriter = null;
        StreamWriter? streamWriter = null;
        string? tempFilePath = null;

        try
        {
            if (request.Mode == SeedStorageMode.PersistAndLoad)
            {
                Directory.CreateDirectory(seedsFolderPath);
                tempFilePath = seedFilePath + ".tmp";
                streamWriter = new StreamWriter(tempFilePath, append: false, Encoding.UTF8, bufferSize: 65536);
                jsonWriter = new JsonTextWriter(streamWriter) { Formatting = Formatting.Indented };
                await jsonWriter.WriteStartArrayAsync(ct);

                if (request.Append)
                {
                    foreach (var existing in existingItems)
                    {
                        await existing.WriteToAsync(jsonWriter, ct);
                    }
                }
            }

            var batch = new List<Task>(BatchSize);
            for (long i = 0; i < request.Count; i++)
            {
                ct.ThrowIfCancellationRequested();

                var doc = SeedTemplateAnalyzer.SynthesizeDocument(
                    profile,
                    container.Id,
                    startIndex + i,
                    strategy,
                    prefix,
                    request.GuidV5Mode,
                    request.GuidV5Template,
                    guidV5Namespace,
                    random);

                if (jsonWriter != null)
                {
                    await doc.WriteToAsync(jsonWriter, ct);
                    Interlocked.Increment(ref job.PersistedCount);
                }

                if (TryBuildPartitionKey(doc, pkPaths, out var pk))
                {
                    batch.Add(container.UpsertItemAsync(doc, pk, cancellationToken: ct));
                }

                if (batch.Count >= BatchSize)
                {
                    await Task.WhenAll(batch);
                    batch.Clear();
                    Interlocked.Add(ref job.ProcessedCount, BatchSize);
                    await Task.Yield();
                }
            }

            if (batch.Count > 0)
            {
                var remaining = batch.Count;
                await Task.WhenAll(batch);
                batch.Clear();
                Interlocked.Add(ref job.ProcessedCount, remaining);
            }

            if (jsonWriter != null)
            {
                await jsonWriter.WriteEndArrayAsync(ct);
                await jsonWriter.FlushAsync(ct);
                await jsonWriter.CloseAsync();
                jsonWriter = null;
                streamWriter = null;

                File.Move(tempFilePath!, seedFilePath, overwrite: true);
                job.SavedFilePath = seedFilePath;
            }

            job.Status = "Completed";
            job.CompletedAtUtc = DateTimeOffset.UtcNow;
        }
        catch (OperationCanceledException)
        {
            job.Status = "Cancelled";
            job.CompletedAtUtc = DateTimeOffset.UtcNow;
        }
        catch (Exception ex)
        {
            job.Status = "Failed";
            job.Error = ex.Message;
            job.CompletedAtUtc = DateTimeOffset.UtcNow;
        }
        finally
        {
            if (jsonWriter != null) await jsonWriter.CloseAsync();
            if (streamWriter != null) await streamWriter.DisposeAsync();
            if (tempFilePath != null && File.Exists(tempFilePath))
            {
                try { File.Delete(tempFilePath); } catch { /* ignore cleanup */ }
            }
        }
    }

    private static (JObject Template, SeedIdStrategy Strategy, string Prefix, long StartIndex, Guid? Namespace)
        ResolveGenerationParameters(
            string containerName,
            IReadOnlyList<JObject> existingItems,
            SeedGenerationRequest request)
    {
        var template = !string.IsNullOrWhiteSpace(request.TemplateJson)
            ? JObject.Parse(request.TemplateJson)
            : existingItems.FirstOrDefault() ?? new JObject { ["id"] = $"{containerName.ToLowerInvariant()}-1" };

        var (inferredStrategy, inferredPrefix, inferredNext) = SeedIdGenerator.InferFromTemplateId(template["id"]?.ToString());

        var strategy = request.IdStrategy == SeedIdStrategy.Auto ? inferredStrategy : request.IdStrategy;
        var prefix = !string.IsNullOrWhiteSpace(request.IdPrefix) ? request.IdPrefix : inferredPrefix;

        long startIndex;
        if (request.IdStartNumber.HasValue)
        {
            startIndex = request.IdStartNumber.Value;
        }
        else if (request.Append && existingItems.Count > 0)
        {
            var maxExisting = existingItems
                .Select(x => SeedIdGenerator.InferFromTemplateId(x["id"]?.ToString()).NextIndex)
                .DefaultIfEmpty(inferredNext)
                .Max();
            startIndex = Math.Max(maxExisting, existingItems.Count + 1);
        }
        else
        {
            startIndex = Math.Max(1, inferredNext);
        }

        Guid? nsGuid = Guid.TryParse(request.GuidV5Namespace, out var parsedNs) ? parsedNs : null;
        return (template, strategy, prefix, startIndex, nsGuid);
    }

    private static bool TryBuildPartitionKey(JObject item, IReadOnlyList<string> pkPaths, out PartitionKey partitionKey)
    {
        var builder = new PartitionKeyBuilder();
        foreach (var path in pkPaths)
        {
            var token = item.SelectToken(path.TrimStart('/').Replace('/', '.'));
            if (token == null)
            {
                partitionKey = default;
                return false;
            }

            switch (token.Type)
            {
                case JTokenType.String:
                    var s = token.Value<string>();
                    if (string.IsNullOrEmpty(s)) { partitionKey = default; return false; }
                    builder.Add(s);
                    break;
                case JTokenType.Integer:
                case JTokenType.Float:
                    builder.Add(token.Value<double>());
                    break;
                case JTokenType.Boolean:
                    builder.Add(token.Value<bool>());
                    break;
                case JTokenType.Null:
                    builder.AddNullValue();
                    break;
                default:
                    partitionKey = default;
                    return false;
            }
        }

        partitionKey = builder.Build();
        return true;
    }
}
