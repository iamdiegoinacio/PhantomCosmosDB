using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Phantom.CosmosDB.Dashboard.Infrastructure;

namespace Phantom.CosmosDB.Dashboard.Endpoints;

internal sealed class DashboardAssetsEndpoint : IDashboardEndpointHandler
{
    private static readonly Lazy<string> HtmlAsset = new(() => LoadEmbeddedAsset("index.html"));
    private static readonly Lazy<string> CssAsset = new(() => LoadEmbeddedAsset("styles.css"));
    private static readonly Lazy<string> JsAsset = new(() => LoadEmbeddedAsset("app.js"));

    public async Task<bool> TryHandleAsync(DashboardRequestContext ctx)
    {
        if (ctx.Method != HttpMethods.Get)
            return false;

        if (ctx.Path is "" or "/")
        {
            var html = HtmlAsset.Value.Replace("__PHANTOM_BASE_PATH__", ctx.RoutePrefix, StringComparison.Ordinal);
            await ctx.HttpContext.WriteTextAsync(html, "text/html; charset=utf-8");
            return true;
        }

        if (ctx.Path.Equals("/assets/styles.css", StringComparison.OrdinalIgnoreCase))
        {
            await ctx.HttpContext.WriteTextAsync(CssAsset.Value, "text/css; charset=utf-8");
            return true;
        }

        if (ctx.Path.Equals("/assets/app.js", StringComparison.OrdinalIgnoreCase))
        {
            var js = JsAsset.Value.Replace("__PHANTOM_BASE_PATH__", ctx.RoutePrefix, StringComparison.Ordinal);
            await ctx.HttpContext.WriteTextAsync(js, "application/javascript; charset=utf-8");
            return true;
        }

        return false;
    }

    private static string LoadEmbeddedAsset(string fileName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = $"Phantom.CosmosDB.Resources.Dashboard.{fileName}";
        using var stream = assembly.GetManifestResourceStream(resourceName)
                           ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' não encontrado.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
