using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using Phantom.CosmosDB.Cost;

namespace Phantom.CosmosDB.Dashboard.State;

/// <summary>
/// Intercepta chamadas HTTP enviadas pelo Cosmos SDK para o <c>FakeCosmosHandler</c>
/// e registra telemetria estruturada (status, substatus, RU, custo USD regional, latência e SQL).
/// </summary>
internal sealed class CosmosPhantomTelemetryHandler : DelegatingHandler
{
    private readonly CosmosPhantomRuntimeRegistry _registry;

    public CosmosPhantomTelemetryHandler(HttpMessageHandler innerHandler, CosmosPhantomRuntimeRegistry registry)
        : base(innerHandler)
    {
        _registry = registry;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var path = request.RequestUri?.AbsolutePath ?? "/";
        var method = request.Method.Method;
        var sqlQuery = await TryExtractSqlQueryAsync(request);
        var containerName = TryExtractContainerName(path);

        var sw = Stopwatch.StartNew();
        var response = await base.SendAsync(request, cancellationToken);
        sw.Stop();

        var subStatus = TryGetIntHeader(response, "x-ms-substatus");
        var ru = TryGetDoubleHeader(response, "x-ms-request-charge");
        var isChaos = response.Headers.Contains("x-ms-retry-after-ms") ||
                      (response.Headers.TryGetValues("x-ms-request-charge", out var ruVals) &&
                       string.Equals(ruVals.FirstOrDefault(), "0", StringComparison.Ordinal) &&
                       (int)response.StatusCode >= 400);

        var costOptions = _registry.LiveCostOptions;
        var estimatedCostUsd = CosmosQueryCostSimulator.EstimateServerlessUsd(ru, costOptions);

        _registry.TelemetryLog.Add(
            method,
            path,
            containerName,
            (int)response.StatusCode,
            subStatus,
            ru,
            estimatedCostUsd,
            costOptions.Region,
            sw.Elapsed.TotalMilliseconds,
            sqlQuery,
            isChaos);

        if (costOptions.LogQueryCost &&
            _registry.TelemetryLogger is { } logger &&
            (sqlQuery != null || path.Contains("/docs", StringComparison.OrdinalIgnoreCase)))
        {
            logger.LogInformation(
                "[PhantomDB Cost] {Method} {Path} | Status={StatusCode} | RU={RequestCharge:F2} | EstServerless={EstimatedCostUsd:F8} USD ({Region}) | Duration={DurationMs:F2}ms{SqlSuffix}",
                method,
                path,
                (int)response.StatusCode,
                ru,
                estimatedCostUsd,
                costOptions.Region,
                sw.Elapsed.TotalMilliseconds,
                sqlQuery is not null ? $" | SQL={sqlQuery}" : string.Empty);
        }

        return response;
    }

    private static async Task<string?> TryExtractSqlQueryAsync(HttpRequestMessage request)
    {
        if (!request.Headers.Contains("x-ms-documentdb-isquery") || request.Content == null)
            return null;

        try
        {
            var body = await request.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(body) || !body.TrimStart().StartsWith('{'))
                return null;

            var jObj = JObject.Parse(body);
            return jObj["query"]?.ToString();
        }
        catch
        {
            return null;
        }
    }

    private static string? TryExtractContainerName(string path)
    {
        var match = Regex.Match(path, @"/colls/([^/]+)", RegexOptions.IgnoreCase);
        if (!match.Success) return null;

        var name = Uri.UnescapeDataString(match.Groups[1].Value);
        return name.Contains('=') ? null : name;
    }

    private static int TryGetIntHeader(HttpResponseMessage response, string name)
    {
        return response.Headers.TryGetValues(name, out var values) &&
               int.TryParse(values.FirstOrDefault(), out var val)
            ? val
            : 0;
    }

    private static double TryGetDoubleHeader(HttpResponseMessage response, string name)
    {
        return response.Headers.TryGetValues(name, out var values) &&
               double.TryParse(values.FirstOrDefault(), NumberStyles.Float, CultureInfo.InvariantCulture, out var val)
            ? val
            : 0d;
    }
}
