#nullable enable
using System;
using Newtonsoft.Json.Linq;

namespace Phantom.CosmosDB.Seeding.Generation;

internal static class GeoSeedSynthesizer
{
    private const string Base32 = "0123456789bcdefghjkmnpqrstuvwxyz";

    public static bool TrySynthesizeGeoJson(
        JObject templateObj,
        Random random,
        out JObject? synthesized,
        out (double Lon, double Lat)? primaryPoint)
    {
        synthesized = null;
        primaryPoint = null;

        var type = templateObj["type"]?.ToString();
        if (string.IsNullOrWhiteSpace(type) || templateObj["coordinates"] is not JArray coords)
            return false;

        switch (type)
        {
            case "Point":
            {
                var (baseLon, baseLat) = ExtractFirstPoint(coords);
                var (lon, lat) = JitterPoint(baseLon, baseLat, 0.12, random);
                primaryPoint = (lon, lat);
                synthesized = new JObject
                {
                    ["type"] = "Point",
                    ["coordinates"] = new JArray(lon, lat)
                };
                return true;
            }

            case "LineString":
            {
                var (baseLon, baseLat) = ExtractFirstPoint(coords);
                var (lon, lat) = JitterPoint(baseLon, baseLat, 0.08, random);
                primaryPoint = (lon, lat);
                var lineCoords = new JArray();
                var steps = Math.Max(3, Math.Min(6, coords.Count));
                for (var i = 0; i < steps; i++)
                {
                    lineCoords.Add(new JArray(lon, lat));
                    lon = ClampLon(Math.Round(lon + (random.NextDouble() * 0.01 - 0.003), 6));
                    lat = ClampLat(Math.Round(lat + (random.NextDouble() * 0.01 - 0.005), 6));
                }
                synthesized = new JObject
                {
                    ["type"] = "LineString",
                    ["coordinates"] = lineCoords
                };
                return true;
            }

            case "Polygon":
            {
                var (baseLon, baseLat) = ExtractFirstPoint(coords);
                var (cLon, cLat) = JitterPoint(baseLon, baseLat, 0.08, random);
                primaryPoint = (cLon, cLat);
                synthesized = new JObject
                {
                    ["type"] = "Polygon",
                    ["coordinates"] = new JArray(BuildCcwRectangle(cLon, cLat, 0.01 + random.NextDouble() * 0.02))
                };
                return true;
            }

            case "MultiPolygon":
            {
                var (baseLon, baseLat) = ExtractFirstPoint(coords);
                var (cLon1, cLat1) = JitterPoint(baseLon, baseLat, 0.08, random);
                var cLon2 = ClampLon(Math.Round(cLon1 + 0.04, 6));
                var cLat2 = ClampLat(Math.Round(cLat1 + 0.04, 6));
                primaryPoint = (cLon1, cLat1);
                synthesized = new JObject
                {
                    ["type"] = "MultiPolygon",
                    ["coordinates"] = new JArray(
                        new JArray(BuildCcwRectangle(cLon1, cLat1, 0.012)),
                        new JArray(BuildCcwRectangle(cLon2, cLat2, 0.012)))
                };
                return true;
            }

            default:
                return false;
        }
    }

    public static string EncodeGeohash(double lat, double lon, int precision = 6)
    {
        double minLat = -90.0, maxLat = 90.0;
        double minLon = -180.0, maxLon = 180.0;
        var hash = new char[precision];
        var isEven = true;
        var bit = 0;
        var ch = 0;
        var idx = 0;

        while (idx < precision)
        {
            if (isEven)
            {
                var mid = (minLon + maxLon) / 2.0;
                if (lon >= mid)
                {
                    ch |= 1 << (4 - bit);
                    minLon = mid;
                }
                else
                {
                    maxLon = mid;
                }
            }
            else
            {
                var mid = (minLat + maxLat) / 2.0;
                if (lat >= mid)
                {
                    ch |= 1 << (4 - bit);
                    minLat = mid;
                }
                else
                {
                    maxLat = mid;
                }
            }

            isEven = !isEven;
            if (bit < 4)
            {
                bit++;
            }
            else
            {
                hash[idx++] = Base32[ch];
                bit = 0;
                ch = 0;
            }
        }

        return new string(hash);
    }

    private static JArray BuildCcwRectangle(double centerLon, double centerLat, double halfSize)
    {
        var minLon = ClampLon(Math.Round(centerLon - halfSize, 6));
        var maxLon = ClampLon(Math.Round(centerLon + halfSize, 6));
        var minLat = ClampLat(Math.Round(centerLat - halfSize, 6));
        var maxLat = ClampLat(Math.Round(centerLat + halfSize, 6));

        return new JArray(
            new JArray(minLon, minLat),
            new JArray(maxLon, minLat),
            new JArray(maxLon, maxLat),
            new JArray(minLon, maxLat),
            new JArray(minLon, minLat));
    }

    private static (double Lon, double Lat) JitterPoint(double baseLon, double baseLat, double spreadDeg, Random random)
    {
        var lon = ClampLon(Math.Round(baseLon + (random.NextDouble() * 2.0 - 1.0) * spreadDeg, 6));
        var lat = ClampLat(Math.Round(baseLat + (random.NextDouble() * 2.0 - 1.0) * spreadDeg, 6));
        return (lon, lat);
    }

    private static (double Lon, double Lat) ExtractFirstPoint(JToken token)
    {
        if (token is JArray arr)
        {
            if (arr.Count >= 2 &&
                arr[0].Type is JTokenType.Float or JTokenType.Integer &&
                arr[1].Type is JTokenType.Float or JTokenType.Integer)
            {
                return (arr[0].Value<double>(), arr[1].Value<double>());
            }

            if (arr.Count > 0)
                return ExtractFirstPoint(arr[0]);
        }

        // Fallback: Rio de Janeiro
        return (-43.2096, -22.9035);
    }

    private static double ClampLon(double lon) => Math.Clamp(lon, -179.999, 179.999);
    private static double ClampLat(double lat) => Math.Clamp(lat, -89.999, 89.999);
}
