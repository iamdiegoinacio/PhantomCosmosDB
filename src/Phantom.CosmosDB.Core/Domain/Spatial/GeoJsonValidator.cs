#nullable enable
using Newtonsoft.Json.Linq;

namespace Phantom.CosmosDB.Core.Domain.Spatial;

internal static class GeoJsonValidator
{
    private const double ClosureEpsilon = 1e-10;

    public static bool IsValid(object? geo, bool isGeometry)
    {
        return ValidateDetailed(geo, isGeometry).Valid;
    }

    public static (bool Valid, string Reason) ValidateDetailed(object? geo, bool isGeometry)
    {
        var obj = GeoJsonParser.AsJObject(geo);
        if (obj is null)
            return (false, "Not a valid GeoJSON object.");

        var type = obj["type"]?.ToString();
        if (string.IsNullOrWhiteSpace(type))
            return (false, "GeoJSON object missing 'type' property.");

        return type switch
        {
            "Point" => ValidatePoint(obj, isGeometry),
            "LineString" => ValidateLineString(obj, isGeometry),
            "Polygon" => ValidatePolygonToken(obj["coordinates"], isGeometry),
            "MultiPolygon" => ValidateMultiPolygon(obj, isGeometry),
            _ => (false, $"Unsupported GeoJSON type: {type}. Cosmos DB NoSQL indexes Point, LineString, Polygon, and MultiPolygon.")
        };
    }

    public static JObject ToDetailedJObject(object? geo, bool isGeometry)
    {
        var (valid, reason) = ValidateDetailed(geo, isGeometry);
        return JObject.FromObject(new { valid, reason });
    }

    private static (bool Valid, string Reason) ValidatePoint(JObject obj, bool isGeometry)
    {
        if (obj["coordinates"] is not JArray coords || !GeoJsonParser.TryReadCoord(coords, out var lon, out var lat))
            return (false, "Point must have coordinates [longitude, latitude].");

        if (!isGeometry && (lat < -90.0 || lat > 90.0))
            return (false, $"Latitude {lat} is out of WGS-84 range [-90, 90]. Ensure GeoJSON [longitude, latitude] coordinate order.");

        if (!isGeometry && (lon < -180.0 || lon > 180.0))
            return (false, $"Longitude {lon} is out of WGS-84 range [-180, 180].");

        return (true, string.Empty);
    }

    private static (bool Valid, string Reason) ValidateLineString(JObject obj, bool isGeometry)
    {
        if (obj["coordinates"] is not JArray arr || arr.Count < 2)
            return (false, "LineString must have at least 2 positions.");

        var coordsResult = ValidateCoordinateList(arr, isGeometry);
        return coordsResult;
    }

    private static (bool Valid, string Reason) ValidateMultiPolygon(JObject obj, bool isGeometry)
    {
        if (obj["coordinates"] is not JArray polyArray || polyArray.Count == 0)
            return (false, "MultiPolygon coordinates must be a non-empty array of polygons.");

        for (var i = 0; i < polyArray.Count; i++)
        {
            var res = ValidatePolygonToken(polyArray[i], isGeometry);
            if (!res.Valid)
                return (false, $"MultiPolygon[{i}]: {res.Reason}");
        }

        return (true, string.Empty);
    }

    private static (bool Valid, string Reason) ValidatePolygonToken(JToken? token, bool isGeometry)
    {
        if (token is not JArray ringsToken || ringsToken.Count == 0)
            return (false, "Polygon coordinates must be an array of linear rings.");

        var parsedRings = new List<List<GeoPoint>>(ringsToken.Count);
        for (var r = 0; r < ringsToken.Count; r++)
        {
            if (ringsToken[r] is not JArray ringArr || ringArr.Count < 4)
                return (false, "Polygon rings must have at least 4 positions and be closed.");

            var coordRes = ValidateCoordinateList(ringArr, isGeometry);
            if (!coordRes.Valid)
                return coordRes;

            var points = GeoJsonParser.ExtractCoordinateArray(ringArr, isGeometry)!;
            var first = points[0];
            var last = points[^1];
            if (Math.Abs(first.Lat - last.Lat) > ClosureEpsilon || Math.Abs(first.Lon - last.Lon) > ClosureEpsilon)
                return (false, "Polygon rings must be closed (first and last position must be identical).");

            if (SpatialMathEngine.RingSelfIntersects(points))
                return (false, $"Polygon ring {r} has self-intersecting edges.");

            var signedArea = SpatialMathEngine.PlanarSignedArea(points);
            if (Math.Abs(signedArea) <= 1e-14)
                return (false, $"Polygon ring {r} is degenerate (zero area).");

            if (!isGeometry)
            {
                if (r == 0 && signedArea < 0)
                {
                    return (false,
                        "Polygon exterior ring has inverted orientation (clockwise). Use counter-clockwise (left-hand rule) order in GeoJSON.");
                }
                if (r > 0 && signedArea > 0)
                {
                    return (false,
                        $"Polygon hole ring {r} has inverted orientation (counter-clockwise). Interior rings must be clockwise.");
                }
            }

            parsedRings.Add(points);
        }

        // Verify holes lie inside exterior ring
        if (parsedRings.Count > 1)
        {
            var exterior = new GeoPolygon([parsedRings[0]]);
            for (var h = 1; h < parsedRings.Count; h++)
            {
                if (!SpatialMathEngine.PointInPolygon(parsedRings[h][0], exterior))
                    return (false, $"Polygon hole ring {h} lies outside the exterior ring.");
            }
        }

        return (true, string.Empty);
    }

    private static (bool Valid, string Reason) ValidateCoordinateList(JArray arr, bool isGeometry)
    {
        for (var i = 0; i < arr.Count; i++)
        {
            if (arr[i] is not JArray coord || !GeoJsonParser.TryReadCoord(coord, out var lon, out var lat))
                return (false, $"Invalid coordinate pair at index {i}.");

            if (!isGeometry && (lat < -90.0 || lat > 90.0))
                return (false, $"Latitude {lat} at index {i} is out of WGS-84 range [-90, 90]. Ensure [longitude, latitude] order.");

            if (!isGeometry && (lon < -180.0 || lon > 180.0))
                return (false, $"Longitude {lon} at index {i} is out of WGS-84 range [-180, 180].");
        }

        return (true, string.Empty);
    }
}
