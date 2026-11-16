using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Phantom.CosmosDB.Configuration;
using Phantom.CosmosDB.Dashboard.Infrastructure;
using Phantom.CosmosDB.Seeding.Generation;

namespace Phantom.CosmosDB.Dashboard.Endpoints;

internal sealed class SeedsEndpoint : IDashboardEndpointHandler
{
    public async Task<bool> TryHandleAsync(DashboardRequestContext ctx)
    {
        if (!ctx.Path.StartsWith("/api/seeds", StringComparison.OrdinalIgnoreCase))
            return false;

        var coordinator = ctx.HttpContext.RequestServices.GetService<SeedGenerationJobCoordinator>();
        var seedsFolder = PhantomResourcePathResolver.ResolvePrimarySeedsFolder(ctx.Environment.ContentRootPath);

        if (ctx.Method == HttpMethods.Get && ctx.Path.Equals("/api/seeds", StringComparison.OrdinalIgnoreCase))
        {
            await HandleListSeedsAsync(ctx, seedsFolder);
            return true;
        }

        var segments = ctx.Path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        // /api/seeds/jobs/{jobId}
        if (segments.Length == 4 && segments[2].Equals("jobs", StringComparison.OrdinalIgnoreCase))
        {
            return await HandleJobRouteAsync(ctx, coordinator, segments[3]);
        }

        // /api/seeds/{containerName}
        if (segments.Length == 3)
        {
            var containerName = Uri.UnescapeDataString(segments[2]);
            if (ctx.Method == HttpMethods.Get)
            {
                await HandleGetContainerSeedAsync(ctx, containerName, seedsFolder);
                return true;
            }

            if (ctx.Method == HttpMethods.Put)
            {
                await HandleSaveContainerSeedAsync(ctx, containerName, seedsFolder);
                return true;
            }
        }

        // /api/seeds/{containerName}/preview or /generate
        if (segments.Length == 4 && ctx.Method == HttpMethods.Post)
        {
            var containerName = Uri.UnescapeDataString(segments[2]);
            var action = segments[3].ToLowerInvariant();
            if (action == "preview")
            {
                await HandlePreviewAsync(ctx, containerName, seedsFolder);
                return true;
            }

            if (action == "generate")
            {
                await HandleStartGenerationAsync(ctx, containerName, seedsFolder, coordinator);
                return true;
            }
        }

        return false;
    }

    private static async Task HandleListSeedsAsync(DashboardRequestContext ctx, string seedsFolder)
    {
        var list = ctx.Registry.Handlers.Select(kvp =>
        {
            var name = kvp.Key;
            var resolvedFile = PhantomResourcePathResolver.ResolveSeedFile(
                seedsFolder, ctx.Environment.ContentRootPath, name);
            var exists = resolvedFile != null && File.Exists(resolvedFile);
            long fileBytes = exists ? new FileInfo(resolvedFile!).Length : 0;
            long seedItemCount = 0;
            if (exists)
            {
                SeedGenerationService.ReadSeedItemsStreaming(resolvedFile!, 0, 1, out seedItemCount);
            }

            return new
            {
                containerName = name,
                filePath = resolvedFile ?? Path.Combine(seedsFolder, $"{name}.json"),
                exists,
                fileBytes,
                seedItemCount,
                inMemoryCount = kvp.Value.BackingInMemoryContainer.ItemCount
            };
        }).OrderBy(x => x.containerName, StringComparer.Ordinal).ToList();

        await ctx.HttpContext.WriteJsonAsync(new { seedsFolder, items = list });
    }

    private static async Task HandleGetContainerSeedAsync(
        DashboardRequestContext ctx,
        string containerName,
        string seedsFolder)
    {
        var resolvedFile = PhantomResourcePathResolver.ResolveSeedFile(
            seedsFolder, ctx.Environment.ContentRootPath, containerName);
        var targetPath = resolvedFile ?? Path.Combine(seedsFolder, $"{containerName}.json");
        var skip = int.TryParse(ctx.HttpContext.Request.Query["skip"], out var s) ? Math.Max(0, s) : 0;
        var take = int.TryParse(ctx.HttpContext.Request.Query["take"], out var t) ? Math.Clamp(t, 1, 200) : 50;

        var items = File.Exists(targetPath)
            ? SeedGenerationService.ReadSeedItemsStreaming(targetPath, skip, take, out var total)
            : [];
        if (!File.Exists(targetPath))
        {
            await ctx.HttpContext.WriteJsonAsync(new
            {
                containerName,
                filePath = targetPath,
                exists = false,
                totalCount = 0L,
                items = Array.Empty<object>(),
                suggestedTemplate = new { id = $"{containerName.ToLowerInvariant()}-1" }
            });
            return;
        }

        var totalCount = SeedGenerationService.ReadSeedItemsStreaming(targetPath, 0, 1, out var counted).FirstOrDefault();
        await ctx.HttpContext.WriteJsonAsync(new
        {
            containerName,
            filePath = targetPath,
            exists = true,
            totalCount = counted,
            items,
            suggestedTemplate = totalCount ?? items.FirstOrDefault() ?? new JObject { ["id"] = $"{containerName.ToLowerInvariant()}-1" }
        });
    }

    private static async Task HandleSaveContainerSeedAsync(
        DashboardRequestContext ctx,
        string containerName,
        string seedsFolder)
    {
        var body = await ctx.HttpContext.ReadBodyAsync();
        try
        {
            var envelope = JObject.Parse(body);
            var rawJson = envelope["json"]?.ToString() ?? body;
            var arr = JArray.Parse(rawJson);

            Directory.CreateDirectory(seedsFolder);
            var targetPath = PhantomResourcePathResolver.ResolveSeedFile(
                                 seedsFolder, ctx.Environment.ContentRootPath, containerName)
                             ?? Path.Combine(seedsFolder, $"{containerName}.json");

            await File.WriteAllTextAsync(targetPath, arr.ToString(Formatting.Indented), Encoding.UTF8, ctx.HttpContext.RequestAborted);
            await ctx.HttpContext.WriteJsonAsync(new { saved = true, filePath = targetPath, count = arr.Count });
        }
        catch (JsonException ex)
        {
            await ctx.HttpContext.WriteJsonAsync(new { error = $"JSON inválido (deve ser um array): {ex.Message}" }, StatusCodes.Status400BadRequest);
        }
    }

    private static async Task HandlePreviewAsync(
        DashboardRequestContext ctx,
        string containerName,
        string seedsFolder)
    {
        var body = await ctx.HttpContext.ReadBodyAsync();
        var req = JsonConvert.DeserializeObject<SeedGenerationRequest>(body) ?? new SeedGenerationRequest();
        var cfg = ctx.Registry.EmulatorConfig.Containers.FirstOrDefault(c => c.Name == containerName);
        var seedFile = PhantomResourcePathResolver.ResolveSeedFile(seedsFolder, ctx.Environment.ContentRootPath, containerName);

        try
        {
            var samples = SeedGenerationService.GeneratePreview(containerName, cfg, seedFile, req, previewCount: 5);
            await ctx.HttpContext.WriteJsonAsync(new { count = samples.Count, items = samples });
        }
        catch (JsonException ex)
        {
            await ctx.HttpContext.WriteJsonAsync(new { error = $"Template JSON inválido: {ex.Message}" }, StatusCodes.Status400BadRequest);
        }
    }

    private static async Task HandleStartGenerationAsync(
        DashboardRequestContext ctx,
        string containerName,
        string seedsFolder,
        SeedGenerationJobCoordinator? coordinator)
    {
        if (coordinator == null || !ctx.Registry.Handlers.TryGetValue(containerName, out var handler))
        {
            await ctx.HttpContext.WriteJsonAsync(
                new { error = $"Container '{containerName}' não encontrado." },
                StatusCodes.Status404NotFound);
            return;
        }

        var body = await ctx.HttpContext.ReadBodyAsync();
        var req = JsonConvert.DeserializeObject<SeedGenerationRequest>(body) ?? new SeedGenerationRequest();
        var cfg = ctx.Registry.EmulatorConfig.Containers.FirstOrDefault(c => c.Name == containerName);

        var job = coordinator.StartJob(handler.BackingInMemoryContainer, cfg, seedsFolder, req);
        await ctx.HttpContext.WriteJsonAsync(job, StatusCodes.Status202Accepted);
    }

    private static async Task<bool> HandleJobRouteAsync(
        DashboardRequestContext ctx,
        SeedGenerationJobCoordinator? coordinator,
        string jobId)
    {
        if (coordinator == null)
            return false;

        if (ctx.Method == HttpMethods.Get)
        {
            if (coordinator.TryGetStatus(jobId, out var status) && status != null)
            {
                await ctx.HttpContext.WriteJsonAsync(status);
                return true;
            }

            await ctx.HttpContext.WriteJsonAsync(new { error = "Job não encontrado." }, StatusCodes.Status404NotFound);
            return true;
        }

        if (ctx.Method == HttpMethods.Delete)
        {
            var cancelled = coordinator.TryCancel(jobId);
            await ctx.HttpContext.WriteJsonAsync(new { cancelled }, cancelled ? StatusCodes.Status200OK : StatusCodes.Status404NotFound);
            return true;
        }

        return false;
    }
}
