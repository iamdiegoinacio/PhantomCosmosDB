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
using Xunit;

namespace Phantom.CosmosDB.Tests;

public sealed class DashboardTests
{
    [Fact]
    public async Task Dashboard_ShouldServeUi_Query_ToggleChaosLive_CaptureLogs_AndResetSeed()
    {
        var tempRoot = CreateTempWorkspace();
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
                ["ConnectionStrings:CosmosDb"] = "AccountEndpoint=https://cosmosphantom.local/;AccountKey=phantom-in-memory;",
                ["CosmosDb:DatabaseName"] = "LojaDb",
                ["CosmosDb:ContainerName"] = "Produtos"
            });

            builder.AddCosmosDb();

            await using var app = builder.Build();
            app.UsePhantomDashboard();
            await app.StartAsync();

            var http = app.GetTestClient();

            // 1. HTML UI + separated CSS/JS assets
            var htmlResp = await http.GetAsync("/_phantom");
            Assert.Equal(HttpStatusCode.OK, htmlResp.StatusCode);
            var html = await htmlResp.Content.ReadAsStringAsync();
            Assert.Contains("PhantomDB Dashboard", html, StringComparison.Ordinal);
            Assert.Contains("/_phantom/assets/styles.css", html, StringComparison.Ordinal);
            Assert.Contains("/_phantom/assets/app.js", html, StringComparison.Ordinal);

            var cssResp = await http.GetAsync("/_phantom/assets/styles.css");
            Assert.Equal(HttpStatusCode.OK, cssResp.StatusCode);
            Assert.Equal("text/css", cssResp.Content.Headers.ContentType?.MediaType);

            var jsResp = await http.GetAsync("/_phantom/assets/app.js");
            Assert.Equal(HttpStatusCode.OK, jsResp.StatusCode);
            Assert.Equal("application/javascript", jsResp.Content.Headers.ContentType?.MediaType);

            var logoResp = await http.GetAsync("/_phantom/assets/logo.png");
            Assert.Equal(HttpStatusCode.OK, logoResp.StatusCode);
            Assert.Equal("image/png", logoResp.Content.Headers.ContentType?.MediaType);

            // 2. Overview
            var overview = JObject.Parse(await http.GetStringAsync("/_phantom/api/overview"));
            Assert.Equal("LojaDb", overview["databaseName"]?.ToString());
            Assert.Equal(1, overview["totalItems"]?.Value<int>());

            // 3. Query Runner
            var queryResp = await http.PostAsync(
                "/_phantom/api/containers/Produtos/query",
                new StringContent("{\"sql\":\"SELECT * FROM c\",\"enableScan\":true}", Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.OK, queryResp.StatusCode);
            var queryJson = JObject.Parse(await queryResp.Content.ReadAsStringAsync());
            Assert.Equal(1, queryJson["count"]?.Value<int>());

            // 4. Live Chaos Toggle -> ReadItemAsync fails with 400, then recovers when disabled
            var container = app.Services.GetRequiredService<Container>();
            var okRead = await container.ReadItemAsync<JObject>("p1", new PartitionKey("tech"));
            Assert.Equal(HttpStatusCode.OK, okRead.StatusCode);

            var chaosPut = await http.PutAsync(
                "/_phantom/api/chaos",
                new StringContent("{\"Simulate400_BadRequest\":true}", Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.OK, chaosPut.StatusCode);

            var ex = await Assert.ThrowsAsync<CosmosException>(() =>
                container.ReadItemAsync<JObject>("p1", new PartitionKey("tech")));
            Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);

            await http.PutAsync(
                "/_phantom/api/chaos",
                new StringContent("{\"Simulate400_BadRequest\":false}", Encoding.UTF8, "application/json"));

            // 5. Logs captured both OK and Chaos 400
            var logsJson = JObject.Parse(await http.GetStringAsync("/_phantom/api/logs"));
            var logs = (JArray)logsJson["items"]!;
            Assert.Contains(logs, l => l["statusCode"]?.Value<int>() == 400 && l["isChaosInjected"]?.Value<bool>() == true);

            // 6. Upsert via Explorer + Reset & Re-seed restores initial count
            await http.PostAsync(
                "/_phantom/api/containers/Produtos/items",
                new StringContent("{\"id\":\"p2\",\"categoria\":\"tech\",\"nome\":\"Teclado\"}", Encoding.UTF8, "application/json"));

            var resetResp = await http.PostAsync("/_phantom/api/seed/reset", content: null);
            Assert.Equal(HttpStatusCode.OK, resetResp.StatusCode);
            var resetOverview = JObject.Parse(await resetResp.Content.ReadAsStringAsync());
            Assert.Equal(1, resetOverview["totalItems"]?.Value<int>());

            await app.StopAsync();
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Dashboard_WithRealConnectionString_ShouldReturn404()
    {
        const string validBase64Key = "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==";
        var tempRoot = CreateTempWorkspace();
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
                ["ConnectionStrings:CosmosDb"] = $"AccountEndpoint=https://real.documents.azure.com:443/;AccountKey={validBase64Key};",
                ["CosmosDb:DatabaseName"] = "LojaDb",
                ["CosmosDb:ContainerName"] = "Produtos"
            });

            builder.AddCosmosDb();

            await using var app = builder.Build();
            app.UsePhantomDashboard();
            await app.StartAsync();

            var http = app.GetTestClient();
            var resp = await http.GetAsync("/_phantom");
            Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);

            await app.StopAsync();
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static string CreateTempWorkspace()
    {
        var root = Path.Combine(Path.GetTempPath(), "CosmosPhantomDashTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "CosmosPhantom.config.json"), """
        {
          "CosmosDbEmulator": {
            "DatabaseName": "LojaDb",
            "Containers": [
              { "Name": "Produtos", "PartitionKeyPath": "/categoria" }
            ]
          }
        }
        """);

        var seedsDir = Path.Combine(root, "Resources", "Seeds");
        Directory.CreateDirectory(seedsDir);
        File.WriteAllText(Path.Combine(seedsDir, "Produtos.json"), """
        [
          { "id": "p1", "categoria": "tech", "nome": "Mouse" }
        ]
        """);

        return root;
    }
}
