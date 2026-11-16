#nullable enable
using System.Net;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Phantom.CosmosDB.Core.Infrastructure.HttpGateway;

/// <summary>
/// Handles Gateway HTTP metadata routes (<c>/</c>, <c>/pkranges</c>, <c>/dbs</c>, <c>/colls</c>, <c>/offers</c>)
/// for a single-container <see cref="FakeCosmosHandler"/>.
/// </summary>
internal static class GatewayMetadataRouteHelper
{
    public static async Task<HttpResponseMessage?> TryHandleAsync(
        HttpRequestMessage request,
        string method,
        string path,
        InMemoryContainer container,
        string databaseRid,
        string collectionRid,
        int partitionKeyRangeCount,
        Func<string> getCollectionMetadata,
        CancellationToken cancellationToken)
    {
        if (method == "GET" && path is "/" or "")
            return CosmosHttpHeaders.CreateJsonResponse(CosmosGatewayMetadataGenerator.AccountMetadata, container.CurrentSessionToken);

        if (method == "GET" && path.Contains("/pkranges"))
        {
            return CosmosGatewayMetadataGenerator.HandlePartitionKeyRanges(
                request, databaseRid, collectionRid, partitionKeyRangeCount, container.CurrentSessionToken);
        }

        if (method == "GET" && path.Contains("/colls/") && !path.Contains("/docs"))
            return CosmosHttpHeaders.CreateJsonResponse(getCollectionMetadata(), container.CurrentSessionToken);

        if (Regex.IsMatch(path, @"^/dbs/?$"))
            return await HandleDatabaseListOrCreateAsync(request, method, databaseRid, container.CurrentSessionToken, cancellationToken);

        if (Regex.IsMatch(path, @"^/dbs/[^/]+/?$"))
            return HandleSingleDatabase(method, path, databaseRid, container.CurrentSessionToken);

        if (Regex.IsMatch(path, @"^/dbs/[^/]+/colls/?$"))
            return HandleCollectionListOrCreate(method, databaseRid, getCollectionMetadata(), container.CurrentSessionToken);

        if (path.Contains("/offers"))
            return CosmosGatewayMetadataGenerator.HandleOffersRequest(container, databaseRid, collectionRid);

        return null;
    }

    private static async Task<HttpResponseMessage> HandleDatabaseListOrCreateAsync(
        HttpRequestMessage request,
        string method,
        string databaseRid,
        string sessionToken,
        CancellationToken cancellationToken)
    {
        if (method == "POST")
        {
            var body = request.Content is not null ? await request.Content.ReadAsStringAsync(cancellationToken) : "{}";
            var dbName = JObject.Parse(body)["id"]?.ToString() ?? "fake-db";
            return CosmosHttpHeaders.CreateJsonResponse(
                CosmosGatewayMetadataGenerator.GetDatabaseMetadata(dbName, databaseRid), sessionToken, HttpStatusCode.Created);
        }

        var dbList = new JObject
        {
            ["_rid"] = "",
            ["Databases"] = new JArray(JObject.Parse(CosmosGatewayMetadataGenerator.GetDatabaseMetadata("fake-db", databaseRid))),
            ["_count"] = 1
        };
        return CosmosHttpHeaders.CreateJsonResponse(dbList.ToString(Formatting.None), sessionToken);
    }

    private static HttpResponseMessage HandleSingleDatabase(
        string method,
        string path,
        string databaseRid,
        string sessionToken)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var dbName = segments.Length > 1 ? Uri.UnescapeDataString(segments[1]) : "fake-db";
        return method switch
        {
            "GET" or "PUT" => CosmosHttpHeaders.CreateJsonResponse(
                CosmosGatewayMetadataGenerator.GetDatabaseMetadata(dbName, databaseRid), sessionToken),
            "DELETE" => CosmosHttpHeaders.CreateNoContentResponse(),
            _ => new HttpResponseMessage(HttpStatusCode.MethodNotAllowed)
        };
    }

    private static HttpResponseMessage HandleCollectionListOrCreate(
        string method,
        string databaseRid,
        string collectionMetadata,
        string sessionToken)
    {
        if (method == "POST")
            return CosmosHttpHeaders.CreateJsonResponse(collectionMetadata, sessionToken, HttpStatusCode.Created);

        var containerList = new JObject
        {
            ["_rid"] = databaseRid,
            ["DocumentCollections"] = new JArray(JObject.Parse(collectionMetadata)),
            ["_count"] = 1
        };
        return CosmosHttpHeaders.CreateJsonResponse(containerList.ToString(Formatting.None), sessionToken);
    }
}
