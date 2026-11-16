using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json.Linq;
using Phantom.CosmosDB.Core;
using Phantom.CosmosDB.Dashboard.Infrastructure;

namespace Phantom.CosmosDB.Dashboard.Endpoints;

internal sealed class ContainerExplorerEndpoint : IDashboardEndpointHandler
{
    public async Task<bool> TryHandleAsync(DashboardRequestContext ctx)
    {
        if (!ctx.Path.StartsWith("/api/containers/", StringComparison.OrdinalIgnoreCase))
            return false;

        var segments = ctx.Path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 4) return false;

        var containerName = Uri.UnescapeDataString(segments[2]);
        if (!ctx.Registry.Handlers.TryGetValue(containerName, out var handler))
        {
            await ctx.HttpContext.WriteJsonAsync(
                new { error = $"Container '{containerName}' não encontrado." },
                StatusCodes.Status404NotFound);
            return true;
        }

        var container = handler.BackingInMemoryContainer;
        var action = segments[3].ToLowerInvariant();

        if (action == "items" && segments.Length == 4)
        {
            return await HandleCollectionItemsAsync(ctx, containerName, container);
        }

        if (action == "items" && segments.Length == 5 && ctx.Method == HttpMethods.Delete)
        {
            await HandleDeleteItemAsync(ctx, segments[4], container);
            return true;
        }

        if (action == "query" && segments.Length == 4 && ctx.Method == HttpMethods.Post)
        {
            await HandleQueryAsync(ctx, containerName, container);
            return true;
        }

        return false;
    }

    private static async Task<bool> HandleCollectionItemsAsync(
        DashboardRequestContext ctx,
        string containerName,
        InMemoryContainer container)
    {
        if (ctx.Method == HttpMethods.Get)
        {
            var take = int.TryParse(ctx.HttpContext.Request.Query["take"], out var t) ? Math.Clamp(t, 1, 200) : 50;
            var result = await ExecuteQueryAsync(container, $"SELECT TOP {take} * FROM c", enableScan: true, populateIndexMetrics: false, parameters: null, ctx.HttpContext.RequestAborted);
            await ctx.HttpContext.WriteJsonAsync(new { container = containerName, count = result.Items.Count, totalRu = result.TotalRu, items = result.Items });
            return true;
        }

        if (ctx.Method == HttpMethods.Post)
        {
            var body = await ctx.HttpContext.ReadBodyAsync();
            var doc = JObject.Parse(body);
            var resp = await container.UpsertItemAsync(doc, cancellationToken: ctx.HttpContext.RequestAborted);
            await ctx.HttpContext.WriteJsonAsync(new { status = (int)resp.StatusCode, ru = resp.RequestCharge, item = resp.Resource });
            return true;
        }

        return false;
    }

    private static async Task HandleDeleteItemAsync(
        DashboardRequestContext ctx,
        string rawId,
        InMemoryContainer container)
    {
        var id = Uri.UnescapeDataString(rawId);
        var pkRaw = ctx.HttpContext.Request.Query["pk"].FirstOrDefault();
        var pk = ParsePartitionKey(pkRaw, id);

        try
        {
            var resp = await container.DeleteItemAsync<JObject>(id, pk, cancellationToken: ctx.HttpContext.RequestAborted);
            await ctx.HttpContext.WriteJsonAsync(new { deleted = true, id, ru = resp.RequestCharge });
        }
        catch (CosmosException ex)
        {
            await ctx.HttpContext.WriteJsonAsync(new { error = ex.Message, subStatus = ex.SubStatusCode }, (int)ex.StatusCode);
        }
    }

    private static async Task HandleQueryAsync(
        DashboardRequestContext ctx,
        string containerName,
        InMemoryContainer container)
    {
        var body = await ctx.HttpContext.ReadBodyAsync();
        var req = JObject.Parse(body);
        var sql = req["sql"]?.ToString();
        if (string.IsNullOrWhiteSpace(sql))
        {
            await ctx.HttpContext.WriteJsonAsync(new { error = "Campo 'sql' é obrigatório." }, StatusCodes.Status400BadRequest);
            return;
        }

        var enableScan = req["enableScan"]?.Value<bool>() ?? true;
        var populateIndexMetrics = req["populateIndexMetrics"]?.Value<bool>() ?? false;
        var parameters = req["parameters"] as JObject;
        try
        {
            var result = await ExecuteQueryAsync(container, sql, enableScan, populateIndexMetrics, parameters, ctx.HttpContext.RequestAborted);
            await ctx.HttpContext.WriteJsonAsync(new
            {
                container = containerName,
                count = result.Items.Count,
                totalRu = result.TotalRu,
                indexMetrics = result.IndexMetrics,
                items = result.Items
            });
        }
        catch (CosmosException ex)
        {
            await ctx.HttpContext.WriteJsonAsync(new { error = ex.Message, subStatus = ex.SubStatusCode }, (int)ex.StatusCode);
        }
    }

    private static async Task<(List<JToken> Items, double TotalRu, JToken? IndexMetrics)> ExecuteQueryAsync(
        InMemoryContainer container,
        string sql,
        bool enableScan,
        bool populateIndexMetrics,
        JObject? parameters,
        CancellationToken ct)
    {
        var opts = new QueryRequestOptions
        {
            EnableScanInQuery = enableScan,
            PopulateIndexMetrics = populateIndexMetrics
        };
        var qd = new QueryDefinition(sql);
        if (parameters != null)
        {
            foreach (var prop in parameters.Properties())
            {
                var paramName = prop.Name.StartsWith('@') ? prop.Name : "@" + prop.Name;
                qd.WithParameter(paramName, prop.Value);
            }
        }

        var iterator = container.GetItemQueryIterator<JToken>(qd, requestOptions: opts);
        var items = new List<JToken>();
        double totalRu = 0;
        string? rawIndexMetrics = null;

        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(ct);
            totalRu += page.RequestCharge;
            if (populateIndexMetrics && string.IsNullOrEmpty(rawIndexMetrics) && !string.IsNullOrEmpty(page.IndexMetrics))
            {
                rawIndexMetrics = page.IndexMetrics;
            }
            items.AddRange(page);
        }

        if (populateIndexMetrics &&
            Core.CosmosSqlParser.TryParse(sql, out var parsedSql))
        {
            rawIndexMetrics = Core.Domain.Validation.IndexMetricsBuilder.Build(
                parsedSql, container.ContainerPropertiesInternal.IndexingPolicy);
        }

        JToken? parsedMetrics = null;
        if (!string.IsNullOrEmpty(rawIndexMetrics))
        {
            try { parsedMetrics = JToken.Parse(rawIndexMetrics); } catch { parsedMetrics = rawIndexMetrics; }
        }

        return (items, Math.Round(totalRu, 2), parsedMetrics);
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
}
