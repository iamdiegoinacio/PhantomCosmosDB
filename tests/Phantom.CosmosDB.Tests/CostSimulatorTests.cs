using System;
using System.Collections.Generic;
using System.IO;
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
using Phantom.CosmosDB.Configuration.Cost;
using Phantom.CosmosDB.Cost;
using Xunit;

namespace Phantom.CosmosDB.Tests;

public sealed class CostSimulatorTests
{
    [Fact]
    public void Catalog_ShouldContain14Regions_WithBrazilSouth50PercentHigherThanEastUs2()
    {
        Assert.Equal(14, CosmosRegionalPricingCatalog.All.Count);

        var eastUs2 = CosmosRegionalPricingCatalog.GetOrDefault("east-us-2");
        var brazil = CosmosRegionalPricingCatalog.GetOrDefault("brazil-south");

        Assert.Equal(58.40, eastUs2.Monthly1000RuProvisionedUsd);
        Assert.Equal(0.250, eastUs2.ServerlessPerMillionRuUsd);
        Assert.Equal(87.60, brazil.Monthly1000RuProvisionedUsd);
        Assert.Equal(0.375, brazil.ServerlessPerMillionRuUsd);
    }

    [Fact]
    public void Simulator_ShouldMatchEstudoCustosExample_ForBrazilSouth10RuQuery()
    {
        var result = CosmosQueryCostSimulator.Simulate(new QueryCostSimulationRequest
        {
            Region = "brazil-south",
            Sql = "SELECT TOP 100 c.id, c.total, c.status FROM c WHERE c.tenantId = @tenantId AND c.status = 'paid' ORDER BY c.createdAt DESC",
            TotalItemsInContainer = 100_000,
            ReturnedItemsCount = 100,
            AvgDocumentSizeKb = 1.0,
            PhysicalPartitions = 1,
            PartitionKeyPaths = ["/tenantId"],
            IndexingMode = "Consistent",
            QueriesPerMonth = 1_000_000,
            StorageGb = 10.0,
            ProvisionedRuOverride = 400
        });

        Assert.Equal(10.0, result.RuBreakdown.TotalRuPerExecution);
        Assert.Equal(7.50, result.Serverless.TotalUsd);
        Assert.Equal(38.79, result.Provisioned.TotalUsd);
        Assert.Equal(93.44, result.BreakEvenMillionRusFor400Ru);
        Assert.Equal(50.0, result.RegionalServerlessDiffPercentVsBaseline);
    }

    [Fact]
    public void Simulator_ShouldPenalizeFullScan_CrossPartition_AndStrongConsistency()
    {
        var indexed = CosmosQueryCostSimulator.Simulate(new QueryCostSimulationRequest
        {
            Region = "east-us-2",
            Sql = "SELECT c.id FROM c WHERE c.tenantId = 't1'",
            TotalItemsInContainer = 10_000,
            ReturnedItemsCount = 50,
            AvgDocumentSizeKb = 4.0,
            PhysicalPartitions = 4,
            PartitionKeyPaths = ["/tenantId"],
            IndexingMode = "Consistent",
            ConsistencyLevel = "Session"
        });

        var fullScanStrong = CosmosQueryCostSimulator.Simulate(new QueryCostSimulationRequest
        {
            Region = "east-us-2",
            Sql = "SELECT * FROM c WHERE UPPER(c.status) = 'PAID'",
            TotalItemsInContainer = 10_000,
            ReturnedItemsCount = 50,
            AvgDocumentSizeKb = 4.0,
            PhysicalPartitions = 4,
            PartitionKeyPaths = ["/tenantId"],
            IndexingMode = "None",
            ConsistencyLevel = "Strong"
        });

        Assert.False(indexed.RuBreakdown.IsCrossPartition);
        Assert.True(fullScanStrong.RuBreakdown.IsCrossPartition);
        Assert.True(fullScanStrong.RuBreakdown.IsFullScan);
        Assert.Equal(2.0, fullScanStrong.RuBreakdown.ConsistencyMultiplier);
        Assert.True(fullScanStrong.RuBreakdown.TotalRuPerExecution > indexed.RuBreakdown.TotalRuPerExecution * 10);
        Assert.NotEmpty(fullScanStrong.Warnings);
    }

    [Fact]
    public async Task Dashboard_CostEndpoints_ShouldListRegions_UpdateSettingsLive_AndSimulate()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "CosmosCostTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        File.WriteAllText(Path.Combine(tempRoot, "phantom.config.json"), """
        {
          "CosmosDbEmulator": {
            "DatabaseName": "LojaDb",
            "Containers": [{ "Name": "Produtos", "PartitionKeyPath": "/categoria" }]
          },
          "CosmosCostOptions": {
            "Region": "brazil-south",
            "LogQueryCost": false
          }
        }
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
                ["CosmosDb:ContainerName"] = "Produtos"
            });

            builder.AddCosmosDb();

            await using var app = builder.Build();
            app.UsePhantomDashboard();
            await app.StartAsync();

            var http = app.GetTestClient();

            var regions = JObject.Parse(await http.GetStringAsync("/_phantom/api/cost/regions"));
            Assert.Equal(14, ((JArray)regions["regions"]!).Count);

            var putSettings = await http.PutAsync(
                "/_phantom/api/cost/settings",
                new StringContent("{\"Region\":\"japan-east\",\"LogQueryCost\":true}", Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.OK, putSettings.StatusCode);

            var container = app.Services.GetRequiredService<Container>();
            await container.CreateItemAsync(new { id = "p1", categoria = "tech", nome = "Mouse" }, new PartitionKey("tech"));

            var logsJson = JObject.Parse(await http.GetStringAsync("/_phantom/api/logs"));
            var logs = (JArray)logsJson["items"]!;
            Assert.Contains(logs, l => l["regionId"]?.ToString() == "japan-east" && l["estimatedCostUsd"]?.Value<double>() > 0);

            var simResp = await http.PostAsync(
                "/_phantom/api/cost/simulate",
                new StringContent("{\"region\":\"brazil-south\",\"totalItemsInContainer\":50000,\"returnedItemsCount\":100,\"queriesPerMonth\":1000000,\"storageGb\":10}", Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.OK, simResp.StatusCode);
            var simJson = JObject.Parse(await simResp.Content.ReadAsStringAsync());
            Assert.Equal("brazil-south", simJson["region"]?["id"]?.ToString());
            Assert.Equal(93.44, simJson["breakEvenMillionRusFor400Ru"]?.Value<double>());

            await app.StopAsync();
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }
}
