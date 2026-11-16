using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json.Linq;
using Phantom.CosmosDB.Configuration;
using Phantom.CosmosDB.Cost;
using Phantom.CosmosDB.Exceptions;
using Phantom.CosmosDB.Seeding.Generation;
using Xunit;

namespace Phantom.CosmosDB.Tests;

public class SpatialGeospatialTests
{
    private static ContainerProperties BuildGeoContainerProps(bool withSpatialIndex = true, GeospatialType geoType = GeospatialType.Geography)
    {
        var cfg = new ContainerConfig
        {
            Name = "GeoItems",
            PartitionKeyPath = "/tenantId",
            GeospatialType = geoType.ToString(),
            IndexingPolicy = new EmulatorIndexingPolicy
            {
                IndexingMode = IndexingMode.Consistent,
                SpatialIndexes = withSpatialIndex
                    ? [
                        new EmulatorSpatialPath
                        {
                            Path = "/location/*",
                            Types = ["Point", "LineString", "Polygon", "MultiPolygon"],
                            BoundingBox = geoType == GeospatialType.Geometry
                                ? new EmulatorBoundingBox { Xmin = 0, Ymin = 0, Xmax = 1000, Ymax = 1000 }
                                : null
                        },
                        new EmulatorSpatialPath
                        {
                            Path = "/route/*",
                            Types = ["LineString"],
                            BoundingBox = geoType == GeospatialType.Geometry
                                ? new EmulatorBoundingBox { Xmin = 0, Ymin = 0, Xmax = 1000, Ymax = 1000 }
                                : null
                        }
                    ]
                    : []
            }
        };

        return cfg.ToContainerProperties();
    }

    [Fact]
    public void ContainerConfig_GeometryWithoutBoundingBox_ShouldThrowConfigurationException()
    {
        var cfg = new ContainerConfig
        {
            Name = "PlanarFloor",
            PartitionKeyPath = "/floor",
            GeospatialType = "Geometry",
            IndexingPolicy = new EmulatorIndexingPolicy
            {
                SpatialIndexes = [new EmulatorSpatialPath { Path = "/location/*", Types = ["Point"] }]
            }
        };

        Assert.Throws<CosmosDbEmulatorConfigurationException>(() => cfg.ToContainerProperties());
    }

    private static async Task<(WebApplication App, string TempRoot)> CreateGeoTestAppAsync(string configJson, string seedJson)
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "phantom-geo-unit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(tempRoot, "Seeds"));
        await File.WriteAllTextAsync(Path.Combine(tempRoot, "phantom.config.json"), configJson);
        await File.WriteAllTextAsync(Path.Combine(tempRoot, "Seeds", "GeoItems.json"), seedJson);

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development,
            ContentRootPath = tempRoot
        });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:CosmosDb"] = "AccountEndpoint=https://phantom.local/;AccountKey=phantom-in-memory;"
        });
        builder.AddCosmosDb();

        var app = builder.Build();
        await app.StartAsync();
        return (app, tempRoot);
    }

    [Fact]
    public async Task SpatialQueries_OrderByDistance_Within_Intersects_Area_And_Validation_ShouldMatchCosmosSemantics()
    {
        var configJson = """
        {
          "CosmosDbEmulator": {
            "DatabaseName": "GeoDb",
            "Containers": [
              {
                "Name": "GeoItems",
                "PartitionKeyPath": "/tenantId",
                "IndexingPolicy": {
                  "IndexingMode": "Consistent",
                  "SpatialIndexes": [
                    { "Path": "/location/*", "Types": ["Point", "LineString", "Polygon", "MultiPolygon"] },
                    { "Path": "/route/*", "Types": ["LineString"] }
                  ]
                }
              }
            ]
          }
        }
        """;

        var (app, tempRoot) = await CreateGeoTestAppAsync(configJson, "[]");
        try
        {
            var client = app.Services.GetRequiredService<CosmosClient>();
            var container = client.GetContainer("GeoDb", "GeoItems");

        // 1. Valid Points (Copacabana, Ipanema, Centro RJ)
        await container.CreateItemAsync(new
        {
            id = "drv-centro",
            tenantId = "t1",
            status = "available",
            location = new { type = "Point", coordinates = new[] { -43.1750, -22.9068 } }
        }, new PartitionKey("t1"));

        await container.CreateItemAsync(new
        {
            id = "drv-copa",
            tenantId = "t1",
            status = "available",
            location = new { type = "Point", coordinates = new[] { -43.1822, -22.9711 } }
        }, new PartitionKey("t1"));

        // 2. Polygon with hole + MultiPolygon + LineString
        await container.CreateItemAsync(new
        {
            id = "zone-hole",
            tenantId = "t1",
            boundary = new
            {
                type = "Polygon",
                coordinates = new[]
                {
                    new[]
                    {
                        new[] { -43.25, -22.95 },
                        new[] { -43.15, -22.95 },
                        new[] { -43.15, -22.85 },
                        new[] { -43.25, -22.85 },
                        new[] { -43.25, -22.95 }
                    },
                    new[]
                    {
                        new[] { -43.21, -22.91 },
                        new[] { -43.21, -22.89 },
                        new[] { -43.19, -22.89 },
                        new[] { -43.19, -22.91 },
                        new[] { -43.21, -22.91 }
                    }
                }
            }
        }, new PartitionKey("t1"));

        await container.CreateItemAsync(new
        {
            id = "route-cross",
            tenantId = "t1",
            route = new
            {
                type = "LineString",
                coordinates = new[]
                {
                    new[] { -43.30, -22.90 },
                    new[] { -43.10, -22.90 }
                }
            }
        }, new PartitionKey("t1"));

        await container.CreateItemAsync(new
        {
            id = "multi-zone",
            tenantId = "t1",
            boundary = new
            {
                type = "MultiPolygon",
                coordinates = new[]
                {
                    new[] { new[] { new[] { -43.25, -22.95 }, new[] { -43.23, -22.95 }, new[] { -43.23, -22.93 }, new[] { -43.25, -22.93 }, new[] { -43.25, -22.95 } } },
                    new[] { new[] { new[] { -43.19, -22.91 }, new[] { -43.17, -22.91 }, new[] { -43.17, -22.89 }, new[] { -43.19, -22.89 }, new[] { -43.19, -22.91 } } }
                }
            }
        }, new PartitionKey("t1"));

        // 3. Invalid geometries: inverted [lat, lon] and clockwise polygon
        await container.CreateItemAsync(new
        {
            id = "bad-inverted",
            tenantId = "t1",
            location = new { type = "Point", coordinates = new[] { -22.90, -143.20 } }
        }, new PartitionKey("t1"));

        // ORDER BY ST_DISTANCE + PopulateIndexMetrics
        var origin = new { type = "Point", coordinates = new[] { -43.1729, -22.9068 } }; // Centro RJ
        var nearbySql = new QueryDefinition(
            "SELECT TOP 2 c.id, ST_DISTANCE(c.location, @point) AS meters FROM c WHERE c.status = 'available' AND ST_DISTANCE(c.location, @point) <= 10000 ORDER BY ST_DISTANCE(c.location, @point) ASC")
            .WithParameter("@point", origin);

        var nearbyItems = new List<JObject>();
        using (var it = container.GetItemQueryIterator<JObject>(nearbySql, requestOptions: new QueryRequestOptions { PopulateIndexMetrics = true }))
        {
            while (it.HasMoreResults)
            {
                var page = await it.ReadNextAsync();
                nearbyItems.AddRange(page);
            }
        }

        Assert.Equal(2, nearbyItems.Count);
        Assert.Equal("drv-centro", nearbyItems[0]["id"]?.ToString());
        Assert.Equal("drv-copa", nearbyItems[1]["id"]?.ToString());
        Assert.True(nearbyItems[0]["meters"]!.Value<double>() < nearbyItems[1]["meters"]!.Value<double>());

        // ST_INTERSECTS(LineString, Polygon) even when endpoints are outside polygon
        var restrictedArea = new
        {
            type = "Polygon",
            coordinates = new[]
            {
                new[]
                {
                    new[] { -43.22, -22.92 },
                    new[] { -43.18, -22.92 },
                    new[] { -43.18, -22.88 },
                    new[] { -43.22, -22.88 },
                    new[] { -43.22, -22.92 }
                }
            }
        };

        var intersectSql = new QueryDefinition("SELECT c.id FROM c WHERE IS_DEFINED(c.route) AND ST_INTERSECTS(c.route, @area)")
            .WithParameter("@area", restrictedArea);
        var intersected = new List<JObject>();
        using (var it = container.GetItemQueryIterator<JObject>(intersectSql))
        {
            while (it.HasMoreResults) intersected.AddRange(await it.ReadNextAsync());
        }
        Assert.Single(intersected);
        Assert.Equal("route-cross", intersected[0]["id"]?.ToString());

        // ST_WITHIN with hole exclusion + MultiPolygon
        var pointInHoleSql = new QueryDefinition("SELECT c.id FROM c WHERE IS_DEFINED(c.boundary) AND ST_WITHIN(@pt, c.boundary)")
            .WithParameter("@pt", new { type = "Point", coordinates = new[] { -43.20, -22.90 } });
        var withinHole = new List<JObject>();
        using (var it = container.GetItemQueryIterator<JObject>(pointInHoleSql))
        {
            while (it.HasMoreResults) withinHole.AddRange(await it.ReadNextAsync());
        }
        Assert.Empty(withinHole);

        var pointInMultiSql = new QueryDefinition("SELECT c.id FROM c WHERE c.id = 'multi-zone' AND ST_WITHIN(@pt, c.boundary)")
            .WithParameter("@pt", new { type = "Point", coordinates = new[] { -43.18, -22.90 } });
        var withinMulti = new List<JObject>();
        using (var it = container.GetItemQueryIterator<JObject>(pointInMultiSql))
        {
            while (it.HasMoreResults) withinMulti.AddRange(await it.ReadNextAsync());
        }
        Assert.Single(withinMulti);

        // ST_AREA on MultiPolygon
        var areaSql = new QueryDefinition("SELECT c.id, ST_AREA(c.boundary) / 1000000 AS areaKm2 FROM c WHERE c.id = 'multi-zone'");
        var areas = new List<JObject>();
        using (var it = container.GetItemQueryIterator<JObject>(areaSql))
        {
            while (it.HasMoreResults) areas.AddRange(await it.ReadNextAsync());
        }
        Assert.Single(areas);
        Assert.InRange(areas[0]["areaKm2"]!.Value<double>(), 5.0, 50.0);

        // NOT ST_ISVALID + ST_ISVALIDDETAILED
        var invalidSql = new QueryDefinition("SELECT c.id, ST_ISVALIDDETAILED(c.location) AS validation FROM c WHERE IS_DEFINED(c.location) AND NOT ST_ISVALID(c.location)");
        var invalids = new List<JObject>();
        using (var it = container.GetItemQueryIterator<JObject>(invalidSql))
        {
            while (it.HasMoreResults) invalids.AddRange(await it.ReadNextAsync());
        }
        Assert.Single(invalids);
        Assert.Equal("bad-inverted", invalids[0]["id"]?.ToString());
        Assert.False(invalids[0]["validation"]!["valid"]!.Value<bool>());
        Assert.Contains("[-90, 90]", invalids[0]["validation"]!["reason"]!.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
            Directory.Delete(tempRoot, true);
        }
    }

    [Fact]
    public async Task GeometryMode_ShouldComputeEuclideanDistanceAndPlanarArea()
    {
        var configJson = """
        {
          "CosmosDbEmulator": {
            "DatabaseName": "FloorDb",
            "Containers": [
              {
                "Name": "Sensors",
                "PartitionKeyPath": "/tenantId",
                "GeospatialType": "Geometry",
                "IndexingPolicy": {
                  "IndexingMode": "Consistent",
                  "SpatialIndexes": [
                    {
                      "Path": "/location/*",
                      "Types": ["Point"],
                      "BoundingBox": { "Xmin": 0, "Ymin": 0, "Xmax": 1000, "Ymax": 1000 }
                    }
                  ]
                }
              }
            ]
          }
        }
        """;

        var (app, tempRoot) = await CreateGeoTestAppAsync(configJson, "[]");
        try
        {
            var client = app.Services.GetRequiredService<CosmosClient>();
            var container = client.GetContainer("FloorDb", "Sensors");
        await container.CreateItemAsync(new
        {
            id = "s1",
            tenantId = "f1",
            location = new { type = "Point", coordinates = new[] { 3.0, 4.0 } },
            zone = new
            {
                type = "Polygon",
                coordinates = new[] { new[] { new[] { 0.0, 0.0 }, new[] { 10.0, 0.0 }, new[] { 10.0, 5.0 }, new[] { 0.0, 5.0 }, new[] { 0.0, 0.0 } } }
            }
        }, new PartitionKey("f1"));

        var qd = new QueryDefinition("SELECT ST_DISTANCE(c.location, @origin) AS dist, ST_AREA(c.zone) AS area FROM c")
            .WithParameter("@origin", new { type = "Point", coordinates = new[] { 0.0, 0.0 } });

        var res = new List<JObject>();
        using (var it = container.GetItemQueryIterator<JObject>(qd))
        {
            while (it.HasMoreResults) res.AddRange(await it.ReadNextAsync());
        }

        Assert.Single(res);
        Assert.Equal(5.0, res[0]["dist"]!.Value<double>(), precision: 4);
        Assert.Equal(50.0, res[0]["area"]!.Value<double>(), precision: 4);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
            Directory.Delete(tempRoot, true);
        }
    }

    [Fact]
    public void CostSimulator_FleetAndSpatialQuery_ShouldMatchGuideCalculations()
    {
        var fleet = CosmosQueryCostSimulator.SimulateFleet(new GeoFleetSimulationRequest
        {
            Region = "brazil-south",
            Vehicles = 1000,
            IntervalSeconds = 10,
            RuPerWrite = 5,
            DaysInMonth = 30,
            QueriesPerSecond = 0,
            RuPerReadQuery = 0,
            RetentionDays = 0,
            PeakMultiplier = 1.0
        });

        Assert.Equal(100.0, fleet.WritesPerSecond);
        Assert.Equal(259_200_000.0, fleet.WritesPerMonth);
        Assert.Equal(500.0, fleet.WriteRuPerSecond);
        Assert.Equal(1296.0, fleet.TotalMonthlyMillionRus);
        Assert.Equal(486.0, fleet.Serverless.ProcessingUsd);

        var indexedQuery = CosmosQueryCostSimulator.Simulate(new QueryCostSimulationRequest
        {
            Region = "brazil-south",
            Sql = "SELECT TOP 10 c.id FROM c WHERE ST_DISTANCE(c.location, @point) <= 5000 ORDER BY ST_DISTANCE(c.location, @point) ASC",
            TotalItemsInContainer = 100_000,
            ReturnedItemsCount = 10,
            HasSpatialIndex = true
        });
        var unindexedQuery = CosmosQueryCostSimulator.Simulate(new QueryCostSimulationRequest
        {
            Region = "brazil-south",
            Sql = "SELECT TOP 10 c.id FROM c WHERE ST_DISTANCE(c.location, @point) <= 5000 ORDER BY ST_DISTANCE(c.location, @point) ASC",
            TotalItemsInContainer = 100_000,
            ReturnedItemsCount = 10,
            HasSpatialIndex = false
        });

        Assert.True(unindexedQuery.RuBreakdown.TotalRuPerExecution > indexedQuery.RuBreakdown.TotalRuPerExecution);
    }

    [Fact]
    public void GeoSeedSynthesizer_ShouldProduceValidPointAndCoherentGeohash()
    {
        var template = JObject.Parse("""
        {
          "id": "drv-1",
          "geohash": "75cm9s",
          "location": { "type": "Point", "coordinates": [-43.2096, -22.9035] }
        }
        """);

        var profile = SeedTemplateAnalyzer.BuildProfile(template, [template], null);
        var generated = SeedTemplateAnalyzer.SynthesizeDocument(
            profile, "Drivers", 1, SeedIdStrategy.GuidV7, null, GuidV5InputMode.DeterministicTemplate, null, null, new Random(42));

        var coords = (JArray)generated["location"]!["coordinates"]!;
        var lon = coords[0].Value<double>();
        var lat = coords[1].Value<double>();
        Assert.InRange(lon, -180.0, 180.0);
        Assert.InRange(lat, -90.0, 90.0);
        Assert.Equal(GeoSeedSynthesizer.EncodeGeohash(lat, lon, 6), generated["geohash"]!.ToString());
    }

    [Fact]
    public async Task Dashboard_GeoQueryWithParametersAndFleetSimulatorEndpoint_ShouldWork()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "phantom-geo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(tempRoot, "Seeds"));

        try
        {
            await File.WriteAllTextAsync(Path.Combine(tempRoot, "phantom.config.json"), """
            {
              "CosmosDbEmulator": {
                "DatabaseName": "GeoDashDb",
                "Containers": [
                  {
                    "Name": "Drivers",
                    "PartitionKeyPath": "/tenantId",
                    "IndexingPolicy": {
                      "IndexingMode": "Consistent",
                      "SpatialIndexes": [ { "Path": "/location/*", "Types": ["Point"] } ]
                    }
                  }
                ]
              }
            }
            """);

            await File.WriteAllTextAsync(Path.Combine(tempRoot, "Seeds", "Drivers.json"), """
            [
              { "id": "d1", "tenantId": "t1", "location": { "type": "Point", "coordinates": [-43.2096, -22.9035] } }
            ]
            """);

            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Development,
                ContentRootPath = tempRoot
            });
            builder.WebHost.UseTestServer();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:CosmosDb"] = "AccountEndpoint=https://phantom.local/;AccountKey=phantom-in-memory;"
            });
            builder.AddCosmosDb();

            await using var app = builder.Build();
            app.UsePhantomDashboard();
            await app.StartAsync();

            var http = app.GetTestClient();

            var queryResp = await http.PostAsync("/phantom/api/containers/Drivers/query", new StringContent("""
            {
              "sql": "SELECT c.id, ST_DISTANCE(c.location, @point) AS meters FROM c WHERE ST_DISTANCE(c.location, @point) <= 1000",
              "enableScan": true,
              "populateIndexMetrics": true,
              "parameters": { "@point": { "type": "Point", "coordinates": [-43.2096, -22.9035] } }
            }
            """, Encoding.UTF8, "application/json"));

            Assert.Equal(HttpStatusCode.OK, queryResp.StatusCode);
            var qJson = JObject.Parse(await queryResp.Content.ReadAsStringAsync());
            Assert.Equal(1, qJson["count"]!.Value<int>());
            Assert.NotNull(qJson["indexMetrics"]);
            Assert.Contains("/location/*", qJson["indexMetrics"]!.ToString(), StringComparison.Ordinal);

            var fleetResp = await http.PostAsync("/phantom/api/cost/simulate-fleet", new StringContent("""
            { "region": "brazil-south", "vehicles": 1000, "intervalSeconds": 10, "ruPerWrite": 5, "queriesPerSecond": 0, "retentionDays": 0 }
            """, Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.OK, fleetResp.StatusCode);
            var fJson = JObject.Parse(await fleetResp.Content.ReadAsStringAsync());
            Assert.Equal(486.0, fJson["serverless"]!["processingUsd"]!.Value<double>());

            await app.StopAsync();
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }
}
