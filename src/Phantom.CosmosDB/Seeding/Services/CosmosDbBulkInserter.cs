using Microsoft.Azure.Cosmos;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Phantom.CosmosDB.Configuration;
using Phantom.CosmosDB.Exceptions;
using Phantom.CosmosDB.Seeding.Interfaces;

namespace Phantom.CosmosDB.Seeding.Services;

public class CosmosDbBulkInserter(ILogger<CosmosDbBulkInserter> logger) : ICosmosDbBulkInserter
{
    public async Task<int> BulkInsertAsync(Container container, ContainerConfig containerConfig, string jsonContent, CancellationToken ct)
    {
        int count = 0;
        var pkPaths = containerConfig.GetPartitionKeyPaths();
        var tasks = new List<Task>(50);

        using var sr = new StringReader(jsonContent);
        using var reader = new JsonTextReader(sr);

        try
        {
            if (!reader.Read() || reader.TokenType != JsonToken.StartArray)
            {
                logger.LogWarning("[CosmosDbSeeder] O arquivo de seed do container '{Container}' está vazio ou não é um array válido.", containerConfig.Name);
                return 0;
            }

            while (reader.Read())
            {
                if (reader.TokenType == JsonToken.EndArray)
                    break;

                if (reader.TokenType != JsonToken.StartObject)
                    continue;

                var item = await JObject.LoadAsync(reader, ct);
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

                if (tasks.Count >= 50)
                {
                    await Task.WhenAll(tasks);
                    tasks.Clear();
                }
            }
        }
        catch (JsonException ex)
        {
            throw new CosmosDbEmulatorSeedingException(
                $"Falha crítica ao realizar o parse do arquivo de seed para o container '{containerConfig.Name}'. O arquivo contém um JSON inválido.", ex);
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
