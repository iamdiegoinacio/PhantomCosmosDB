using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace CosmosPhantom.Dashboard.State;

/// <summary>
/// Intercepta chamadas HTTP enviadas pelo Cosmos SDK para o <c>FakeCosmosHandler</c>
/// e registra telemetria estruturada (status, substatus, RU, latência e SQL) no painel.
/// </summary>
internal sealed class CosmosPhantomTelemetryHandler : DelegatingHandler
{
    private readonly CosmosPhantomTelemetryLog _log;

    public CosmosPhantomTelemetryHandler(HttpMessageHandler innerHandler, CosmosPhantomTelemetryLog log)
        : base(innerHandler)
    {
        _log = log;
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

        _log.Add(
            method,
            path,
            containerName,
            (int)response.StatusCode,
            subStatus,
            ru,
            sw.Elapsed.TotalMilliseconds,
            sqlQuery,
            isChaos);

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
