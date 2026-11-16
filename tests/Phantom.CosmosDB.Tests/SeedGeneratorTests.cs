using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json.Linq;
using Phantom.CosmosDB.Configuration;
using Phantom.CosmosDB.Seeding.Generation;
using Xunit;

namespace Phantom.CosmosDB.Tests;

public sealed class SeedGeneratorTests
{
    [Fact]
    public void IdGenerator_ShouldSupport_AllStrategies_IncludingDeterministicGuidV5AndV7()
    {
        var rng = new Random(123);
        var doc1 = new JObject { ["nome"] = "Diego Silva", ["cpf"] = "123.456.789-00" };
        var doc2 = new JObject { ["nome"] = "Diego Silva", ["cpf"] = "123.456.789-00" };

        var v5Det1 = SeedIdGenerator.GenerateId(SeedIdStrategy.GuidV5, "Clientes", 1, null, GuidV5InputMode.DeterministicTemplate, "{nome}+{cpf}", null, doc1, rng);
        var v5Det2 = SeedIdGenerator.GenerateId(SeedIdStrategy.GuidV5, "Clientes", 2, null, GuidV5InputMode.DeterministicTemplate, "{nome}+{cpf}", null, doc2, rng);
        Assert.Equal(v5Det1, v5Det2);
        Assert.Equal('5', v5Det1[14]);

        var v5Rand1 = SeedIdGenerator.GenerateId(SeedIdStrategy.GuidV5, "Clientes", 1, null, GuidV5InputMode.Random, null, null, doc1, rng);
        var v5Rand2 = SeedIdGenerator.GenerateId(SeedIdStrategy.GuidV5, "Clientes", 2, null, GuidV5InputMode.Random, null, null, doc2, rng);
        Assert.NotEqual(v5Rand1, v5Rand2);
        Assert.Equal('5', v5Rand1[14]);

        var v7 = SeedIdGenerator.GenerateId(SeedIdStrategy.GuidV7, "Clientes", 1, null, GuidV5InputMode.Random, null, null, doc1, rng);
        Assert.Equal('7', v7[14]);

        var ulid = SeedIdGenerator.GenerateId(SeedIdStrategy.Ulid, "Clientes", 1, null, GuidV5InputMode.Random, null, null, doc1, rng);
        Assert.Equal(26, ulid.Length);

        var seq = SeedIdGenerator.GenerateId(SeedIdStrategy.PrefixSequential, "Clientes", 42, "cli-", GuidV5InputMode.Random, null, null, doc1, rng);
        Assert.Equal("cli-42", seq);

        var num = SeedIdGenerator.GenerateId(SeedIdStrategy.NumericString, "Clientes", 99, null, GuidV5InputMode.Random, null, null, doc1, rng);
        Assert.Equal("99", num);
    }

    [Fact]
    public void TemplateAnalyzer_ShouldGenerateUniqueIdsAndUniqueKeys_ForNestedClienteAndProduto()
    {
        var template = JObject.Parse("""
        {
          "id": "cli-1",
          "nome": "Diego Silva",
          "cpf": "111.222.333-44",
          "sku": "SKU-001",
          "meta": { "regiao": "sul" }
        }
        """);

        var cfg = new ContainerConfig
        {
            Name = "Clientes",
            PartitionKeyPath = "/meta/regiao",
            UniqueKeys = [["/sku"]]
        };

        var profile = SeedTemplateAnalyzer.BuildProfile(template, [template], cfg);
        var (inferredStrategy, prefix, start) = SeedIdGenerator.InferFromTemplateId("cli-1");
        Assert.Equal(SeedIdStrategy.PrefixSequential, inferredStrategy);
        Assert.Equal("cli-", prefix);
        Assert.Equal(2, start);

        var ids = new HashSet<string>();
        var skus = new HashSet<string>();
        var rng = new Random(7);

        for (var i = 0; i < 500; i++)
        {
            var doc = SeedTemplateAnalyzer.SynthesizeDocument(
                profile, "Clientes", start + i, inferredStrategy, prefix,
                GuidV5InputMode.DeterministicTemplate, null, null, rng);

            Assert.True(ids.Add(doc["id"]!.ToString()));
            Assert.True(skus.Add(doc["sku"]!.ToString()));
            Assert.False(string.IsNullOrWhiteSpace(doc["meta"]?["regiao"]?.ToString()));
            Assert.Contains('.', doc["cpf"]!.ToString());
        }
    }

    [Fact]
    public async Task Dashboard_SeedsEndpoints_ShouldList_Preview_AndGeneratePersistedBatch()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "CosmosSeedGenTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        File.WriteAllText(Path.Combine(tempRoot, "phantom.config.json"), """
        {
          "CosmosDbEmulator": {
            "DatabaseName": "LojaDb",
            "Containers": [{ "Name": "Clientes", "PartitionKeyPath": "/meta/regiao" }]
          }
        }
        """);

        var seedsDir = Path.Combine(tempRoot, "Resources", "Seeds");
        Directory.CreateDirectory(seedsDir);
        File.WriteAllText(Path.Combine(seedsDir, "Clientes.json"), """
        [
          { "id": "cli-1", "nome": "Diego Silva", "meta": { "regiao": "sul" } }
        ]
        """);

        try
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Development,
                ContentRootPath = tempRoot
            });
            builder.WebHost.UseTestServer();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:CosmosDb"] = "AccountEndpoint=https://phantom.local/;AccountKey=phantom-in-memory;",
                ["CosmosDb:DatabaseName"] = "LojaDb",
                ["CosmosDb:ContainerName"] = "Clientes"
            });

            builder.AddCosmosDb();

            await using var app = builder.Build();
            app.UsePhantomDashboard();
            await app.StartAsync();

            var http = app.GetTestClient();

            var listJson = JObject.Parse(await http.GetStringAsync("/phantom/api/seeds"));
            Assert.Equal(1, listJson["items"]![0]!["seedItemCount"]?.Value<int>());

            var previewResp = await http.PostAsync(
                "/phantom/api/seeds/Clientes/preview",
                new StringContent("{\"idStrategy\":\"GuidV7\",\"randomSeed\":42}", Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.OK, previewResp.StatusCode);
            var previewJson = JObject.Parse(await previewResp.Content.ReadAsStringAsync());
            Assert.Equal(5, ((JArray)previewJson["items"]!).Count);

            var genResp = await http.PostAsync(
                "/phantom/api/seeds/Clientes/generate",
                new StringContent("{\"count\":50,\"mode\":\"PersistAndLoad\",\"append\":true,\"idStrategy\":\"Auto\",\"randomSeed\":42}", Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.Accepted, genResp.StatusCode);
            var jobId = JObject.Parse(await genResp.Content.ReadAsStringAsync())["jobId"]!.ToString();

            JObject? finalStatus = null;
            for (var i = 0; i < 50; i++)
            {
                await Task.Delay(50);
                finalStatus = JObject.Parse(await http.GetStringAsync($"/phantom/api/seeds/jobs/{jobId}"));
                if (finalStatus["status"]?.ToString() != "Running")
                    break;
            }

            Assert.NotNull(finalStatus);
            Assert.Equal("Completed", finalStatus!["status"]?.ToString());
            Assert.Equal(50, finalStatus["processedCount"]?.Value<int>());

            var afterList = JObject.Parse(await http.GetStringAsync("/phantom/api/seeds"));
            Assert.Equal(51, afterList["items"]![0]!["seedItemCount"]?.Value<int>());
            Assert.Equal(51, afterList["items"]![0]!["inMemoryCount"]?.Value<int>());

            await app.StopAsync();
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }
}
