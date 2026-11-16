using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Phantom.CosmosDB.ChaosEngineering;
using Phantom.CosmosDB.Configuration;
using Phantom.CosmosDB.Core;
using Phantom.CosmosDB.Dashboard.State;
using Phantom.CosmosDB.Seeding.Interfaces;

namespace Phantom.CosmosDB.Dashboard;

internal sealed class CosmosPhantomDashboardMiddleware
{
    private static readonly Lazy<string> DashboardHtml = new(LoadEmbeddedDashboardHtml);
    private readonly RequestDelegate _next;
    private readonly string _routePrefix;

    public CosmosPhantomDashboardMiddleware(RequestDelegate next, string routePrefix)
    {
        _next = next;
        _routePrefix = routePrefix;
    }

    public async Task InvokeAsync(
        HttpContext context,
        CosmosPhantomRuntimeRegistry registry,
        IHostEnvironment environment,
        ICosmosDbSeederService? seederService = null)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        var method = context.Request.Method;

        if (method == HttpMethods.Get && (path == string.Empty || path == "/"))
        {
            var html = DashboardHtml.Value.Replace("__PHANTOM_BASE_PATH__", _routePrefix, StringComparison.Ordinal);
            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.WriteAsync(html, Encoding.UTF8, context.RequestAborted);
            return;
        }

        if (method == HttpMethods.Get && path.Equals("/api/overview", StringComparison.OrdinalIgnoreCase))
        {
            await WriteJsonAsync(context, BuildOverviewPayload(registry));
            return;
        }

        if (path.Equals("/api/logs", StringComparison.OrdinalIgnoreCase))
        {
            if (method == HttpMethods.Get)
            {
                var take = int.TryParse(context.Request.Query["take"], out var t) ? t : 200;
                await WriteJsonAsync(context, new { items = registry.TelemetryLog.GetRecent(take) });
                return;
            }

            if (method == HttpMethods.Delete)
            {
                registry.TelemetryLog.Clear();
                await WriteJsonAsync(context, new { cleared = true });
                return;
            }
        }

        if (path.Equals("/api/chaos", StringComparison.OrdinalIgnoreCase))
        {
            if (method == HttpMethods.Get)
            {
                await WriteJsonAsync(context, registry.LiveChaosConfig);
                return;
            }

            if (method == HttpMethods.Put)
            {
                await HandleUpdateChaosAsync(context, registry);
                return;
            }
        }

        if (path.Equals("/api/config", StringComparison.OrdinalIgnoreCase))
        {
            if (method == HttpMethods.Get)
            {
                await HandleGetConfigAsync(context, registry, environment);
                return;
            }

            if (method == HttpMethods.Put)
            {
                await HandleSaveConfigAsync(context, registry, environment);
                return;
            }
        }

        if (method == HttpMethods.Post && path.Equals("/api/seed/reset", StringComparison.OrdinalIgnoreCase))
        {
            await HandleResetSeedAsync(context, registry, environment, seederService);
            return;
        }

        if (path.StartsWith("/api/containers/", StringComparison.OrdinalIgnoreCase))
        {
            if (await TryHandleContainerRouteAsync(context, path, method, registry))
                return;
        }

        await _next(context);
    }

    private static object BuildOverviewPayload(CosmosPhantomRuntimeRegistry registry)
    {
        var containers = registry.Handlers.Select(kvp =>
        {
            var c = kvp.Value.BackingInMemoryContainer;
            var cfg = registry.EmulatorConfig.Containers
                .FirstOrDefault(x => string.Equals(x.Name, kvp.Key, StringComparison.Ordinal));

            return new
            {
                name = c.Id,
                itemCount = c.ItemCount,
                throughput = c._throughput,
                defaultTtl = c.DefaultTimeToLive,
                partitionKeyPaths = c.PartitionKeyPaths,
                indexingMode = c.IndexingPolicy.IndexingMode.ToString(),
                automaticIndexing = c.IndexingPolicy.Automatic,
                compositeIndexCount = c.IndexingPolicy.CompositeIndexes.Count,
                uniqueKeys = c.UniqueKeyPolicy.UniqueKeys.Select(uk => uk.Paths).ToList(),
                computedProperties = c.ContainerPropertiesInternal.ComputedProperties
                    .Select(cp => new { cp.Name, cp.Query })
                    .ToList(),
                configured = cfg != null
            };
        }).OrderBy(x => x.name, StringComparer.Ordinal).ToList();

        return new
        {
            databaseName = registry.DatabaseName,
            configFilePath = registry.RootConfigFilePath,
            totalItems = containers.Sum(c => c.itemCount),
            containerCount = containers.Count,
            chaosActive = IsAnyChaosEnabled(registry.LiveChaosConfig),
            containers
        };
    }

    private static bool IsAnyChaosEnabled(ChaosConfig c) =>
        c.EnableRandomThrottling ||
        c.SimulateHighLatency ||
        c.Simulate429_TooManyRequests ||
        c.Simulate503_ServiceUnavailable ||
        c.Simulate408_RequestTimeout ||
        c.Simulate403_Forbidden ||
        c.Simulate401_Unauthorized ||
        c.Simulate409_Conflict ||
        c.Simulate413_EntityTooLarge ||
        c.Simulate412_PreconditionFailed ||
        c.Simulate400_BadRequest;

    private static async Task<bool> TryHandleContainerRouteAsync(
        HttpContext context,
        string path,
        string method,
        CosmosPhantomRuntimeRegistry registry)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        // Expected: api / containers / {name} / (items | query) [/ {id}]
        if (segments.Length < 4) return false;

        var containerName = Uri.UnescapeDataString(segments[2]);
        if (!registry.Handlers.TryGetValue(containerName, out var handler))
        {
            await WriteJsonAsync(context, new { error = $"Container '{containerName}' não encontrado." }, StatusCodes.Status404NotFound);
            return true;
        }

        var container = handler.BackingInMemoryContainer;
        var action = segments[3].ToLowerInvariant();

        if (action == "items" && segments.Length == 4)
        {
            if (method == HttpMethods.Get)
            {
                var take = int.TryParse(context.Request.Query["take"], out var t) ? Math.Clamp(t, 1, 200) : 50;
                var items = await ExecuteQueryAsync(container, $"SELECT TOP {take} * FROM c", enableScan: true, context.RequestAborted);
                await WriteJsonAsync(context, new { container = containerName, count = items.Items.Count, totalRu = items.TotalRu, items = items.Items });
                return true;
            }

            if (method == HttpMethods.Post)
            {
                var body = await ReadBodyAsync(context);
                var doc = JObject.Parse(body);
                var resp = await container.UpsertItemAsync(doc, cancellationToken: context.RequestAborted);
                await WriteJsonAsync(context, new { status = (int)resp.StatusCode, ru = resp.RequestCharge, item = resp.Resource }, StatusCodes.Status200OK);
                return true;
            }
        }

        if (action == "items" && segments.Length == 5 && method == HttpMethods.Delete)
        {
            var id = Uri.UnescapeDataString(segments[4]);
            var pkRaw = context.Request.Query["pk"].FirstOrDefault();
            var pk = ParsePartitionKey(pkRaw, id);
            try
            {
                var resp = await container.DeleteItemAsync<JObject>(id, pk, cancellationToken: context.RequestAborted);
                await WriteJsonAsync(context, new { deleted = true, id, ru = resp.RequestCharge });
            }
            catch (CosmosException ex)
            {
                await WriteJsonAsync(context, new { error = ex.Message, subStatus = ex.SubStatusCode }, (int)ex.StatusCode);
            }
            return true;
        }

        if (action == "query" && segments.Length == 4 && method == HttpMethods.Post)
        {
            var body = await ReadBodyAsync(context);
            var req = JObject.Parse(body);
            var sql = req["sql"]?.ToString();
            if (string.IsNullOrWhiteSpace(sql))
            {
                await WriteJsonAsync(context, new { error = "Campo 'sql' é obrigatório." }, StatusCodes.Status400BadRequest);
                return true;
            }

            var enableScan = req["enableScan"]?.Value<bool>() ?? true;
            try
            {
                var result = await ExecuteQueryAsync(container, sql, enableScan, context.RequestAborted);
                await WriteJsonAsync(context, new { container = containerName, count = result.Items.Count, totalRu = result.TotalRu, items = result.Items });
            }
            catch (CosmosException ex)
            {
                await WriteJsonAsync(context, new { error = ex.Message, subStatus = ex.SubStatusCode }, (int)ex.StatusCode);
            }
            return true;
        }

        return false;
    }

    private static async Task<(List<JToken> Items, double TotalRu)> ExecuteQueryAsync(
        InMemoryContainer container,
        string sql,
        bool enableScan,
        CancellationToken ct)
    {
        var opts = new QueryRequestOptions { EnableScanInQuery = enableScan };
        var iterator = container.GetItemQueryIterator<JToken>(new QueryDefinition(sql), requestOptions: opts);
        var items = new List<JToken>();
        double totalRu = 0;

        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(ct);
            totalRu += page.RequestCharge;
            items.AddRange(page);
        }

        return (items, Math.Round(totalRu, 2));
    }

    private static PartitionKey ParsePartitionKey(string? raw, string fallbackId)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return new PartitionKey(fallbackId);

        if (raw.TrimStart().StartsWith('['))
        {
            var arr = JArray.Parse(raw);
            var builder = new PartitionKeyBuilder();
            foreach (var token in arr)
            {
                switch (token.Type)
                {
                    case JTokenType.Integer or JTokenType.Float:
                        builder.Add(token.Value<double>());
                        break;
                    case JTokenType.Boolean:
                        builder.Add(token.Value<bool>());
                        break;
                    case JTokenType.Null:
                        builder.AddNullValue();
                        break;
                    default:
                        builder.Add(token.ToString());
                        break;
                }
            }
            return builder.Build();
        }

        return new PartitionKey(raw);
    }

    private static async Task HandleUpdateChaosAsync(HttpContext context, CosmosPhantomRuntimeRegistry registry)
    {
        var body = await ReadBodyAsync(context);
        var updated = JsonConvert.DeserializeObject<ChaosConfig>(body);
        if (updated == null)
        {
            await WriteJsonAsync(context, new { error = "Payload de ChaosConfig inválido." }, StatusCodes.Status400BadRequest);
            return;
        }

        CopyChaosProperties(updated, registry.LiveChaosConfig);
        await WriteJsonAsync(context, registry.LiveChaosConfig);
    }

    private static void CopyChaosProperties(ChaosConfig source, ChaosConfig target)
    {
        target.EnableRandomThrottling = source.EnableRandomThrottling;
        target.ThrottlingProbability = source.ThrottlingProbability;
        target.SimulateHighLatency = source.SimulateHighLatency;
        target.MinLatencyMs = source.MinLatencyMs;
        target.MaxLatencyMs = source.MaxLatencyMs;
        target.RetryAfterMs = source.RetryAfterMs;
        target.Simulate429_TooManyRequests = source.Simulate429_TooManyRequests;
        target.Simulate503_ServiceUnavailable = source.Simulate503_ServiceUnavailable;
        target.Simulate408_RequestTimeout = source.Simulate408_RequestTimeout;
        target.Simulate403_Forbidden = source.Simulate403_Forbidden;
        target.Simulate401_Unauthorized = source.Simulate401_Unauthorized;
        target.Simulate409_Conflict = source.Simulate409_Conflict;
        target.Simulate413_EntityTooLarge = source.Simulate413_EntityTooLarge;
        target.Simulate412_PreconditionFailed = source.Simulate412_PreconditionFailed;
        target.Simulate400_BadRequest = source.Simulate400_BadRequest;
    }

    private static async Task HandleGetConfigAsync(
        HttpContext context,
        CosmosPhantomRuntimeRegistry registry,
        IHostEnvironment environment)
    {
        var path = registry.RootConfigFilePath
                   ?? Path.Combine(environment.ContentRootPath, PhantomResourcePathResolver.RootConfigFileName);

        string json;
        var exists = File.Exists(path);
        if (exists)
        {
            json = await File.ReadAllTextAsync(path, context.RequestAborted);
        }
        else
        {
            var fallback = new JObject
            {
                ["CosmosDbEmulator"] = JObject.FromObject(registry.EmulatorConfig),
                ["CosmosChaosEngineering"] = JObject.FromObject(registry.LiveChaosConfig)
            };
            json = fallback.ToString(Formatting.Indented);
        }

        await WriteJsonAsync(context, new { path, exists, json });
    }

    private static async Task HandleSaveConfigAsync(
        HttpContext context,
        CosmosPhantomRuntimeRegistry registry,
        IHostEnvironment environment)
    {
        var body = await ReadBodyAsync(context);
        string rawJson;
        try
        {
            var envelope = JObject.Parse(body);
            rawJson = envelope["json"]?.ToString() ?? body;
            var parsedConfig = JObject.Parse(rawJson);

            var targetPath = registry.RootConfigFilePath
                             ?? Path.Combine(environment.ContentRootPath, PhantomResourcePathResolver.RootConfigFileName);

            await File.WriteAllTextAsync(targetPath, parsedConfig.ToString(Formatting.Indented), Encoding.UTF8, context.RequestAborted);
            registry.RootConfigFilePath = targetPath;

            var chaosToken = parsedConfig["CosmosChaosEngineering"] ?? parsedConfig["Chaos"];
            var updatedChaos = chaosToken?.ToObject<ChaosConfig>();
            if (updatedChaos != null)
            {
                CopyChaosProperties(updatedChaos, registry.LiveChaosConfig);
            }

            await WriteJsonAsync(context, new { saved = true, path = targetPath });
        }
        catch (JsonException ex)
        {
            await WriteJsonAsync(context, new { error = $"JSON inválido: {ex.Message}" }, StatusCodes.Status400BadRequest);
        }
    }

    private static async Task HandleResetSeedAsync(
        HttpContext context,
        CosmosPhantomRuntimeRegistry registry,
        IHostEnvironment environment,
        ICosmosDbSeederService? seederService)
    {
        foreach (var handler in registry.Handlers.Values)
        {
            handler.BackingInMemoryContainer.ClearItems();
        }

        if (seederService != null)
        {
            var seedsFolder = PhantomResourcePathResolver.ResolvePrimarySeedsFolder(environment.ContentRootPath);
            try
            {
                ChaosEngineeringConfigurator.IsBypassed = true;
                await seederService.SeedAsync(seedsFolder, context.RequestAborted);
            }
            finally
            {
                ChaosEngineeringConfigurator.IsBypassed = false;
            }
        }

        await WriteJsonAsync(context, BuildOverviewPayload(registry));
    }

    private static async Task<string> ReadBodyAsync(HttpContext context)
    {
        using var reader = new StreamReader(context.Request.Body, Encoding.UTF8);
        return await reader.ReadToEndAsync(context.RequestAborted);
    }

    private static async Task WriteJsonAsync(HttpContext context, object payload, int statusCode = (int)HttpStatusCode.OK)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";
        var json = JsonConvert.SerializeObject(payload, Formatting.Indented);
        await context.Response.WriteAsync(json, Encoding.UTF8, context.RequestAborted);
    }

    private static string LoadEmbeddedDashboardHtml()
    {
        var assembly = Assembly.GetExecutingAssembly();
        const string resourceName = "Phantom.CosmosDB.Resources.Dashboard.index.html";
        using var stream = assembly.GetManifestResourceStream(resourceName)
                           ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' não encontrado.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
