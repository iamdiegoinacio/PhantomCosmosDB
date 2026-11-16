using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Phantom.CosmosDB.ChaosEngineering;
using Phantom.CosmosDB.Configuration;
using Phantom.CosmosDB.Configuration.Cost;
using Phantom.CosmosDB.Dashboard.Infrastructure;

namespace Phantom.CosmosDB.Dashboard.Endpoints;

internal sealed class ConfigEndpoint : IDashboardEndpointHandler
{
    public async Task<bool> TryHandleAsync(DashboardRequestContext ctx)
    {
        if (!ctx.Path.Equals("/api/config", StringComparison.OrdinalIgnoreCase))
            return false;

        if (ctx.Method == HttpMethods.Get)
        {
            await HandleGetConfigAsync(ctx);
            return true;
        }

        if (ctx.Method == HttpMethods.Put)
        {
            await HandleSaveConfigAsync(ctx);
            return true;
        }

        return false;
    }

    private static async Task HandleGetConfigAsync(DashboardRequestContext ctx)
    {
        var path = ctx.Registry.RootConfigFilePath
                   ?? Path.Combine(ctx.Environment.ContentRootPath, PhantomResourcePathResolver.RootConfigFileName);

        var exists = File.Exists(path);
        var json = exists
            ? await File.ReadAllTextAsync(path, ctx.HttpContext.RequestAborted)
            : new JObject
            {
                ["CosmosDbEmulator"] = JObject.FromObject(ctx.Registry.EmulatorConfig),
                ["CosmosCostOptions"] = JObject.FromObject(ctx.Registry.LiveCostOptions),
                ["CosmosChaosEngineering"] = JObject.FromObject(ctx.Registry.LiveChaosConfig)
            }.ToString(Formatting.Indented);

        await ctx.HttpContext.WriteJsonAsync(new { path, exists, json });
    }

    private static async Task HandleSaveConfigAsync(DashboardRequestContext ctx)
    {
        var body = await ctx.HttpContext.ReadBodyAsync();
        try
        {
            var envelope = JObject.Parse(body);
            var rawJson = envelope["json"]?.ToString() ?? body;
            var parsedConfig = JObject.Parse(rawJson);

            var targetPath = ctx.Registry.RootConfigFilePath
                             ?? Path.Combine(ctx.Environment.ContentRootPath, PhantomResourcePathResolver.RootConfigFileName);

            await File.WriteAllTextAsync(targetPath, parsedConfig.ToString(Formatting.Indented), Encoding.UTF8, ctx.HttpContext.RequestAborted);
            ctx.Registry.RootConfigFilePath = targetPath;

            var chaosToken = parsedConfig["CosmosChaosEngineering"] ?? parsedConfig["Chaos"];
            var updatedChaos = chaosToken?.ToObject<ChaosConfig>();
            if (updatedChaos != null)
            {
                ChaosEndpoint.CopyChaosProperties(updatedChaos, ctx.Registry.LiveChaosConfig);
            }

            var costToken = parsedConfig["CosmosCostOptions"] ?? parsedConfig["CostOptions"];
            var updatedCost = costToken?.ToObject<CosmosCostOptions>();
            if (updatedCost != null)
            {
                CostEndpoint.CopyCostOptions(updatedCost, ctx.Registry.LiveCostOptions);
            }

            await ctx.HttpContext.WriteJsonAsync(new { saved = true, path = targetPath });
        }
        catch (JsonException ex)
        {
            await ctx.HttpContext.WriteJsonAsync(new { error = $"JSON inválido: {ex.Message}" }, StatusCodes.Status400BadRequest);
        }
    }
}
