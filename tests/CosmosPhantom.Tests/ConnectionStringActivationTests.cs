using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CosmosPhantom.Configuration;
using CosmosPhantom.Configuration.Connection;
using CosmosPhantom.Exceptions;
using CosmosPhantom.Seeding;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace CosmosPhantom.Tests;

public sealed class ConnectionStringActivationTests
{
    [Theory]
    [InlineData("AccountEndpoint=https://cosmosphantom.local/;AccountKey=phantom-in-memory;")]
    [InlineData("AccountEndpoint=https://phantom.local:443/;AccountKey=abc==;")]
    [InlineData("phantom://in-memory")]
    [InlineData("AccountEndpoint=https://localhost:8081/;AccountKey=key==;Mode=InMemory;")]
    [InlineData("AccountEndpoint=https://localhost:8081/;AccountKey=key==;UsePhantom=true;")]
    public void Parser_ShouldDetectPhantomMode_ForPhantomMarkers(string connectionString)
    {
        var spec = CosmosConnectionStringParser.Parse(connectionString);
        Assert.Equal(CosmosConnectionMode.Phantom, spec.Mode);
    }

    [Fact]
    public void Parser_ShouldDetectRealMode_AndPreserveBase64EqualsInAccountKey()
    {
        const string realKey = "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==";
        var conn = $"AccountEndpoint=https://my-account.documents.azure.com:443/;AccountKey={realKey};Database=AppDb;Container=Orders;";

        var spec = CosmosConnectionStringParser.Parse(conn);

        Assert.Equal(CosmosConnectionMode.Real, spec.Mode);
        Assert.Equal(realKey, spec.AccountKey);
        Assert.Equal("AppDb", spec.DatabaseName);
        Assert.Equal("Orders", spec.ContainerName);
        Assert.DoesNotContain("Database=", spec.SanitizedAzureConnectionString, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Container=", spec.SanitizedAzureConnectionString, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"AccountKey={realKey}", spec.SanitizedAzureConnectionString, StringComparison.Ordinal);
    }

    [Fact]
    public void AddCosmosDb_WithRealConnectionString_ShouldNotRegisterEmulatorOrSeeding()
    {
        const string validBase64Key = "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==";
        var tempRoot = CreateTempWorkspace(
            configJson: """
            {
              "CosmosDbEmulator": {
                "DatabaseName": "LojaDb",
                "Containers": [{ "Name": "Produtos", "PartitionKeyPath": "/categoria" }]
              }
            }
            """);

        try
        {
            var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
            {
                EnvironmentName = Environments.Development,
                ContentRootPath = tempRoot
            });

            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:CosmosDb"] = $"AccountEndpoint=https://real.documents.azure.com:443/;AccountKey={validBase64Key};",
                ["CosmosDb:DatabaseName"] = "LojaDb",
                ["CosmosDb:ContainerName"] = "Produtos"
            });

            builder.AddCosmosDb();

            Assert.Contains(builder.Services, d => d.ServiceType == typeof(CosmosClient));
            Assert.Contains(builder.Services, d => d.ServiceType == typeof(Container));
            Assert.DoesNotContain(builder.Services, d => d.ImplementationType == typeof(CosmosPhantomSeedingHostedService));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task AddCosmosDb_WithPhantomConnectionString_ShouldBootEmulatorAndAutoSeedViaHostedService()
    {
        var tempRoot = CreateTempWorkspace(
            configJson: """
            {
              "CosmosDbEmulator": {
                "DatabaseName": "LojaDb",
                "Containers": [
                  { "Name": "Produtos", "PartitionKeyPath": "/categoria" },
                  { "Name": "Pedidos", "PartitionKeyPath": "/id" }
                ]
              }
            }
            """,
            seedContainer: "Produtos",
            seedJson: """
            [
              { "id": "p1", "categoria": "tech", "nome": "Mouse" }
            ]
            """);

        try
        {
            var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
            {
                EnvironmentName = Environments.Development,
                ContentRootPath = tempRoot
            });

            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:CosmosDb"] = "AccountEndpoint=https://cosmosphantom.local/;AccountKey=phantom-in-memory;",
                ["CosmosDb:DatabaseName"] = "LojaDb",
                ["CosmosDb:ContainerName"] = "Produtos"
            });

            builder.AddCosmosDb();

            using var host = builder.Build();
            await host.StartAsync();

            var defaultContainer = host.Services.GetRequiredService<Container>();
            var keyedPedidos = host.Services.GetRequiredKeyedService<Container>("Pedidos");
            Assert.Equal("Produtos", defaultContainer.Id);
            Assert.Equal("Pedidos", keyedPedidos.Id);

            var read = await defaultContainer.ReadItemAsync<dynamic>("p1", new PartitionKey("tech"));
            Assert.Equal("Mouse", (string)read.Resource.nome);

            await host.StopAsync();
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void AddCosmosDb_WithPhantomConnectionString_InProduction_ShouldThrowFailFast()
    {
        var tempRoot = CreateTempWorkspace("""
        {
          "CosmosDbEmulator": {
            "DatabaseName": "LojaDb",
            "Containers": [{ "Name": "Produtos", "PartitionKeyPath": "/id" }]
          }
        }
        """);

        try
        {
            var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
            {
                EnvironmentName = Environments.Production,
                ContentRootPath = tempRoot
            });

            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:CosmosDb"] = "AccountEndpoint=https://cosmosphantom.local/;AccountKey=phantom-in-memory;"
            });

            var ex = Assert.Throws<CosmosDbEmulatorConfigurationException>(() => builder.AddCosmosDb());
            Assert.Contains("Production", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void AddCosmosDb_WhenAppSettingsContainerNotInPhantomConfig_ShouldThrowFailFast()
    {
        var tempRoot = CreateTempWorkspace("""
        {
          "CosmosDbEmulator": {
            "DatabaseName": "LojaDb",
            "Containers": [{ "Name": "Produtos", "PartitionKeyPath": "/id" }]
          }
        }
        """);

        try
        {
            var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
            {
                EnvironmentName = Environments.Development,
                ContentRootPath = tempRoot
            });

            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:CosmosDb"] = "AccountEndpoint=https://cosmosphantom.local/;AccountKey=phantom-in-memory;",
                ["CosmosDb:DatabaseName"] = "LojaDb",
                ["CosmosDb:ContainerName"] = "Inexistente"
            });

            var ex = Assert.Throws<CosmosDbEmulatorConfigurationException>(() => builder.AddCosmosDb());
            Assert.Contains("Inexistente", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Legacy_AddCosmosPhantomEmulator_ShouldStillWork()
    {
        var tempRoot = CreateTempWorkspace("""
        {
          "UseCosmosPhantom": true,
          "CosmosDbEmulator": {
            "DatabaseName": "LegacyDb",
            "Containers": [{ "Name": "Items", "PartitionKeyPath": "/id" }]
          }
        }
        """);

        try
        {
            var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
            {
                EnvironmentName = Environments.Development,
                ContentRootPath = tempRoot
            });

            builder.Services.AddCosmosPhantomEmulator(builder.Environment, builder.Configuration);
            using var host = builder.Build();
            await host.Services.UseCosmosPhantomSeederAsync();

            var client = host.Services.GetRequiredService<CosmosClient>();
            var container = client.GetContainer("LegacyDb", "Items");
            var created = await container.CreateItemAsync(new { id = "i1" }, new PartitionKey("i1"));
            Assert.Equal(System.Net.HttpStatusCode.Created, created.StatusCode);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static string CreateTempWorkspace(
        string configJson,
        string? seedContainer = null,
        string? seedJson = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "CosmosPhantomTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "CosmosPhantom.config.json"), configJson);

        if (!string.IsNullOrWhiteSpace(seedContainer) && seedJson is not null)
        {
            var seedsDir = Path.Combine(root, "Resources", "Seeds");
            Directory.CreateDirectory(seedsDir);
            File.WriteAllText(Path.Combine(seedsDir, $"{seedContainer}.json"), seedJson);
        }

        return root;
    }
}
