using CosmosPhantom.CosmosDB.Core;
using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Microsoft.Azure.Cosmos;

namespace CosmosPhantom.ChaosEngineering;

public static class ChaosEngineeringConfigurator
{
    public static bool IsBypassed { get; set; } = false;

    public static void ConfigureFaultInjector(FakeCosmosHandler handler, ChaosConfig? chaosConfig)
    {
        if (chaosConfig == null) return;

        // Usa Random.Shared (thread-safe) em vez de instância compartilhada não-thread-safe.
        handler.FaultInjector = req =>
        {
            if (IsBypassed) return null;

            // Não injetar falhas em requisições de metadados do Cosmos SDK (apenas em documentos)
            if (req.RequestUri != null && !req.RequestUri.AbsolutePath.Contains("/docs", StringComparison.OrdinalIgnoreCase))
                return null; 

            // Simulação de Alta Latência (Ping / Delay)
            if (chaosConfig.SimulateHighLatency)
            {
                int delay = Random.Shared.Next(chaosConfig.MinLatencyMs, chaosConfig.MaxLatencyMs);
                // FaultInjector é síncrono (sem variante async no pacote); espera sem Thread.Sleep.
                Task.Delay(delay).GetAwaiter().GetResult();
            }

            // Simulação de Throttling Randômico
            if (chaosConfig.EnableRandomThrottling && Random.Shared.NextDouble() > chaosConfig.ThrottlingProbability)
                return null; 

            return ObterFalhaSimulada(chaosConfig);
        };
    }

    private static HttpResponseMessage? ObterFalhaSimulada(ChaosConfig chaos)
    {
        // O Cosmos SDK traduz automaticamente respostas HTTP para CosmosException.
        return true switch
        {
            _ when chaos.Simulate429_TooManyRequests => CriarRespostaCosmos(429, 3200, "Request rate is large. More Request Units may be needed, so no changes were made. Please retry this request later.", chaos.RetryAfterMs),
            _ when chaos.Simulate503_ServiceUnavailable => CriarRespostaCosmos(503, 0, "Service is currently unavailable."),
            _ when chaos.Simulate408_RequestTimeout => CriarRespostaCosmos(408, 0, "Request timed out."),
            _ when chaos.Simulate403_Forbidden => CriarRespostaCosmos(403, 0, "Forbidden: the request is not authorized to perform this operation."),
            _ when chaos.Simulate401_Unauthorized => CriarRespostaCosmos(401, 0, "The input authorization token can't serve the request."),
            _ when chaos.Simulate409_Conflict => CriarRespostaCosmos(409, 0, "Entity with the specified id already exists in the system."),
            _ when chaos.Simulate413_EntityTooLarge => CriarRespostaCosmos(413, 0, "Request entity is too large."),
            _ when chaos.Simulate412_PreconditionFailed => CriarRespostaCosmos(412, 0, "One of the specified pre-condition is not met."),
            _ when chaos.Simulate400_BadRequest => CriarRespostaCosmos(400, 0, "The request payload is invalid."),
            _ => null // Sem falha configurada
        };
    }

    private static HttpResponseMessage CriarRespostaCosmos(int statusCode, int subStatus, string message, int? retryAfterMs = null)
    {
        var body = new JObject
        {
            ["code"] = ((HttpStatusCode)statusCode).ToString(),
            ["message"] = message
        }.ToString(Formatting.None);

        var response = new HttpResponseMessage((HttpStatusCode)statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        response.Headers.TryAddWithoutValidation("x-ms-substatus", subStatus.ToString());
        response.Headers.TryAddWithoutValidation("x-ms-activity-id", Guid.NewGuid().ToString());
        response.Headers.TryAddWithoutValidation("x-ms-request-charge", "0");
        if (retryAfterMs.HasValue)
            response.Headers.TryAddWithoutValidation("x-ms-retry-after-ms", retryAfterMs.Value.ToString());
        return response;
    }
}
