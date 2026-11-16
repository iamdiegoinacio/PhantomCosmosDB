#nullable enable
using System.Net;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Phantom.CosmosDB.Core.Infrastructure.HttpGateway;

/// <summary>
/// Handles Gateway HTTP lifecycle and metadata routes for databases (<c>/dbs</c>, <c>/dbs/{id}</c>)
/// and offers (<c>/offers</c>).
/// </summary>
internal sealed class GatewayDatabaseLifecycleRouter
{
    private readonly DynamicContainerRegistry _registry;
    private readonly FakeCosmosHandler _default;

    public GatewayDatabaseLifecycleRouter(DynamicContainerRegistry registry, FakeCosmosHandler defaultHandler)
    {
        _registry = registry;
        _default = defaultHandler;
    }

    public async Task<HttpResponseMessage> HandleOffersAsync(
        HttpRequestMessage request,
        string path,
        string method,
        CancellationToken cancellationToken)
    {
        var offers = _registry.Handlers.Values
            .Distinct()
            .Select(h => h.GetOfferJsonInternal())
            .ToList();

        var offerIdMatch = Regex.Match(path, @"/offers/([^/]+)/?$", RegexOptions.IgnoreCase);
        if (method == "GET" && offerIdMatch.Success)
        {
            var offerId = Uri.UnescapeDataString(offerIdMatch.Groups[1].Value);
            var single = offers.FirstOrDefault(o =>
                string.Equals(o["id"]?.ToString(), offerId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(o["_rid"]?.ToString(), offerId, StringComparison.OrdinalIgnoreCase));
            if (single is null)
            {
                return CosmosHttpHeaders.CreateErrorResponse(
                    HttpStatusCode.NotFound,
                    $"Offer '{offerId}' not found.",
                    0,
                    _default.BackingInMemoryContainer.CurrentSessionToken);
            }

            return CosmosHttpHeaders.CreateJsonResponse(
                single.ToString(Formatting.None),
                _default.BackingInMemoryContainer.CurrentSessionToken);
        }

        if (method == "POST" && request.Content is not null)
        {
            var body = await request.Content.ReadAsStringAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(body))
            {
                var queryObj = JObject.Parse(body);
                var sql = queryObj["query"]?.ToString();
                if (!string.IsNullOrWhiteSpace(sql))
                {
                    var parameters = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                    if (queryObj["parameters"] is JArray paramArr)
                    {
                        foreach (var p in paramArr)
                        {
                            var name = p["name"]?.ToString();
                            if (name is not null)
                                parameters[name] = p["value"]?.ToObject<object?>();
                        }
                    }

                    var matched = Application.Query.MetadataSqlQueryExecutor.Execute<JObject, JObject>(
                        offers, sql, parameters);

                    if (matched.Count == 0)
                    {
                        var searchHaystack = sql + " " + string.Join(" ", parameters.Values.Select(v => v?.ToString() ?? string.Empty));
                        matched = offers
                            .Where(o =>
                            {
                                var rid = o["offerResourceId"]?.ToString();
                                return !string.IsNullOrEmpty(rid) && searchHaystack.Contains(rid, StringComparison.Ordinal);
                            })
                            .ToList();
                    }

                    offers = matched;
                }
            }
        }

        return CosmosGatewayMetadataGenerator.CreateOffersResponse(offers);
    }

    public HttpResponseMessage HandleDatabaseCrud(string path, string method)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var dbName = segments.Length > 1 ? Uri.UnescapeDataString(segments[1]) : "fake-db";
        _registry.KnownDatabases.Add(dbName);

        return method switch
        {
            "GET" or "PUT" => CosmosHttpHeaders.CreateJsonResponse(
                CosmosGatewayMetadataGenerator.GetDatabaseMetadata(dbName, _default.DatabaseRidInternal),
                _default.BackingInMemoryContainer.CurrentSessionToken),
            "DELETE" => new HttpResponseMessage(HttpStatusCode.NoContent) { Headers = { { "x-ms-request-charge", "1" } } },
            _ => new HttpResponseMessage(HttpStatusCode.MethodNotAllowed)
        };
    }
}
