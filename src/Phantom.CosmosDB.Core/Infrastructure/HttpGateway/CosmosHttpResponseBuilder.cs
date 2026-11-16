#nullable enable
using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Phantom.CosmosDB.Core.Domain.Primitives;

namespace Phantom.CosmosDB.Core.Infrastructure.HttpGateway;

/// <summary>
/// Builds standard Cosmos DB HTTP Gateway responses, error envelopes, and continuation tokens.
/// </summary>
internal static class CosmosHttpResponseBuilder
{
    internal enum ChargeKind
    {
        Flat,
        Read,
        Write,
    }

    public static string EncodeContinuation(string cacheKey, int offset)
    {
        var json = $"{{\"v\":2,\"key\":\"{cacheKey}\",\"offset\":{offset}}}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    public static HttpResponseMessage ConvertToHttpResponse(
        ResponseMessage cosmosResponse,
        string sessionToken,
        ChargeKind chargeKind = ChargeKind.Flat)
    {
        var httpResponse = new HttpResponseMessage(cosmosResponse.StatusCode);
        long payloadBytes = 0;
        if (cosmosResponse.Content is not null)
        {
            using var reader = new StreamReader(cosmosResponse.Content);
            var json = reader.ReadToEnd();
            if (json.Length > 0)
            {
                payloadBytes = Encoding.UTF8.GetByteCount(json);
                httpResponse.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }
        }

        var subStatus = cosmosResponse.Headers["x-ms-substatus"];

        if (httpResponse.Content is null && (int)cosmosResponse.StatusCode >= 400)
        {
            var message = cosmosResponse.ErrorMessage ?? cosmosResponse.StatusCode.ToString();
            httpResponse.Content = new StringContent(
                BuildErrorBody(cosmosResponse.StatusCode, message), Encoding.UTF8, "application/json");
        }

        var charge = chargeKind switch
        {
            ChargeKind.Read when payloadBytes > 0 => RequestChargeCalculator.PointRead(payloadBytes),
            ChargeKind.Write => RequestChargeCalculator.Write(payloadBytes),
            _ => 1d
        };
        httpResponse.Headers.Add("x-ms-request-charge", charge.ToString(CultureInfo.InvariantCulture));
        httpResponse.Headers.Add("x-ms-activity-id", cosmosResponse.Headers["x-ms-activity-id"] ?? Guid.NewGuid().ToString());
        httpResponse.Headers.Add("x-ms-session-token", sessionToken);

        if (subStatus is not null)
            httpResponse.Headers.Add("x-ms-substatus", subStatus);

        var etag = cosmosResponse.Headers["ETag"];
        if (etag is not null)
            httpResponse.Headers.TryAddWithoutValidation("etag", etag);

        return httpResponse;
    }

    /// <summary>
    /// Builds the standard Cosmos DB error envelope: <c>{"code":"NotFound","message":"..."}</c>.
    /// </summary>
    public static string BuildErrorBody(HttpStatusCode statusCode, string message)
    {
        var body = new JObject
        {
            ["code"] = statusCode.ToString(),
            ["message"] = message
        };
        return body.ToString(Formatting.None);
    }

    public static HttpResponseMessage CreateErrorResponse(
        HttpStatusCode statusCode,
        string message,
        int subStatusCode,
        string sessionToken)
    {
        var response = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(BuildErrorBody(statusCode, message), Encoding.UTF8, "application/json")
        };
        response.Headers.Add("x-ms-request-charge", "1");
        response.Headers.Add("x-ms-activity-id", Guid.NewGuid().ToString());
        response.Headers.Add("x-ms-session-token", sessionToken);
        if (subStatusCode != 0)
            response.Headers.Add("x-ms-substatus", subStatusCode.ToString(CultureInfo.InvariantCulture));

        return response;
    }

    public static HttpResponseMessage CreateJsonResponse(string json, string sessionToken, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        var response = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        response.Headers.Add("x-ms-request-charge", "0");
        response.Headers.Add("x-ms-activity-id", Guid.NewGuid().ToString());
        response.Headers.Add("x-ms-session-token", sessionToken);
        return response;
    }

    public static HttpResponseMessage CreateNoContentResponse()
    {
        var response = new HttpResponseMessage(HttpStatusCode.NoContent);
        response.Headers.Add("x-ms-request-charge", "0");
        response.Headers.Add("x-ms-activity-id", Guid.NewGuid().ToString());
        return response;
    }
}
