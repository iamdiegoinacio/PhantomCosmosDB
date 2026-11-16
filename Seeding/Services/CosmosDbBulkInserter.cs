using Microsoft.Azure.Cosmos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using Cosmos.Phantom.SDK.Configuration;
using Cosmos.Phantom.SDK.Exceptions;
using Cosmos.Phantom.SDK.Seeding.Interfaces;

namespace Cosmos.Phantom.SDK.Seeding.Services;

public class CosmosDbBulkInserter(ILogger<CosmosDbBulkInserter> logger) : ICosmosDbBulkInserter
{

    public async Task<int> BulkInsertAsync(Container container, ContainerConfig containerConfig, string jsonContent, CancellationToken ct)
    {
        JArray jArray;
        try
        {
            jArray = JArray.Parse(jsonContent);
        }
        catch (Exception ex)
        {
            throw new CosmosDbEmulatorSeedingException(
                $"Falha crítica ao realizar o parse do arquivo de seed para o container '{containerConfig.Name}'. O arquivo contém um JSON inválido.", ex);
        }

        if (jArray == null)
        {
            logger.LogWarning("[CosmosDbSeeder] O arquivo de seed do container '{Container}' está vazio ou não é um array válido.", containerConfig.Name);
            return 0;
        }

        int count = 0;
        var pkPaths = containerConfig.GetPartitionKeyPaths();
        var tasks = new List<Task>();

        foreach (var item in jArray.OfType<JObject>())
        {
            if (!TryBuildPartitionKey(item, pkPaths, out var partitionKey))
            {
                logger.LogWarning("[CosmosDbSeeder] Objeto sem a(s) propriedade(s) de PK '{PkName}' no container '{Container}'. Pulando...", string.Join(", ", pkPaths), containerConfig.Name);
                continue;
            }

            var itemId = item["id"]?.ToString();

            tasks.Add(container.UpsertItemAsync(item, partitionKey, null, ct)
                .ContinueWith(t =>
                {
                    if (t.IsCompletedSuccessfully)
                        Interlocked.Increment(ref count);
                    else
                        logger.LogError(t.Exception, "[CosmosDbSeeder] Falha ao inserir item com id {ItemId}", itemId);
                }, CancellationToken.None));

            // Lotes de 50 inserções assíncronas concorrentes
            if (tasks.Count >= 50)
            {
                await Task.WhenAll(tasks);
                tasks.Clear();
            }
        }

        if (tasks.Count > 0)
        {
            await Task.WhenAll(tasks);
        }

        logger.LogInformation("[CosmosDbSeeder] {Count} itens inseridos no container '{Container}'.", count, containerConfig.Name);
        
        return count;
    }

    private static bool TryBuildPartitionKey(JObject item, IReadOnlyList<string> pkPaths, out PartitionKey partitionKey)
    {
        var builder = new PartitionKeyBuilder();

        foreach (var path in pkPaths)
        {
            // Suporta caminhos aninhados (ex.: /tenant/id) via SelectToken
            var token = item.SelectToken(path.TrimStart('/').Replace('/', '.'));

            if (token == null)
            {
                partitionKey = default;
                return false;
            }

            switch (token.Type)
            {
                case JTokenType.String:
                    var s = token.Value<string>();
                    if (string.IsNullOrEmpty(s)) { partitionKey = default; return false; }
                    builder.Add(s);
                    break;
                case JTokenType.Integer:
                case JTokenType.Float:
                    builder.Add(token.Value<double>());
                    break;
                case JTokenType.Boolean:
                    builder.Add(token.Value<bool>());
                    break;
                case JTokenType.Null:
                    builder.AddNullValue();
                    break;
                default:
                    partitionKey = default;
                    return false;
            }
        }

        partitionKey = builder.Build();
        return true;
    }
}
