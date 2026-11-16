using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;

namespace Phantom.CosmosDB.Dashboard.Infrastructure;

internal static class DashboardHttpContextExtensions
{
    public static async Task<string> ReadBodyAsync(this HttpContext context)
    {
        using var reader = new StreamReader(context.Request.Body, Encoding.UTF8);
        return await reader.ReadToEndAsync(context.RequestAborted);
    }

    public static async Task WriteJsonAsync(
        this HttpContext context,
        object payload,
        int statusCode = (int)HttpStatusCode.OK)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";
        var json = JsonConvert.SerializeObject(payload, Formatting.Indented);
        await context.Response.WriteAsync(json, Encoding.UTF8, context.RequestAborted);
    }

    public static async Task WriteTextAsync(
        this HttpContext context,
        string content,
        string contentType)
    {
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = contentType;
        await context.Response.WriteAsync(content, Encoding.UTF8, context.RequestAborted);
    }
}
