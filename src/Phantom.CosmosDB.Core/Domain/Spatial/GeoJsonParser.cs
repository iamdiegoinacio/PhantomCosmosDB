#nullable enable
using System.Globalization;
using Newtonsoft.Json.Linq;
using Phantom.CosmosDB.Core.Domain.Primitives;

namespace Phantom.CosmosDB.Core.Domain.Spatial;

internal static class GeoJsonParser
{
    public static JObject? AsJObject(object? value)
    {
        if (value is null || value is UndefinedValue)
            return null;

        if (value is JObject jo)
            return jo;

        if (value is string s)
        {
            var trimmed = s.TrimStart();
            if (!trimmed.StartsWith("{", StringComparison.Ordinal))
                return null;
            return JsonParseHelpers.ParseJson(s);
        }

        if (value is JToken or ValueType)
            return null;

        try
        {
            return JObject.FromObject(value);
        }
        catch
        {
            return null;
        }
    }

    public static bool IsValidCoordinate(double lon, double lat, bool isGeometry)
    {
        if (double.IsNaN(lon) || double.IsInfinity(lon) || double.IsNaN(lat) || double.IsInfinity(lat))
            return false;

        if (isGeometry)
            return true;

        return lon >= -180.0 && lon <= 180.0 && lat >= -90.0 && lat <= 90.0;
    }

    public static GeoPoint? ExtractPoint(object? value, bool isGeometry)
    {
        var obj = AsJObject(value);
        if (obj is null) return null;
        if (!string.Equals(obj["type"]?.ToString(), "Point", StringComparison.OrdinalIgnoreCase)) return null;
        if (obj["coordinates"] is not JArray coords || coords.Count < 2) return null;
        if (!TryReadCoord(coords, out var lon, out var lat)) return null;
        if (!IsValidCoordinate(lon, lat, isGeometry)) return null;

        return new GeoPoint(lat, lon);
    }

    public static GeoLineString? ExtractLineString(object? value, bool isGeometry)
    {
        var obj = AsJObject(value);
        if (obj is null) return null;
        if (!string.Equals(obj["type"]?.ToString(), "LineString", StringComparison.OrdinalIgnoreCase)) return null;

        var points = ExtractCoordinateArray(obj["coordinates"], isGeometry);
        if (points is null || points.Count < 2) return null;

        return new GeoLineString(points);
    }

    public static GeoPolygon? ExtractPolygon(object? value, bool isGeometry)
    {
        var obj = AsJObject(value);
        if (obj is null) return null;
        if (!string.Equals(obj["type"]?.ToString(), "Polygon", StringComparison.OrdinalIgnoreCase)) return null;

        return ExtractPolygonFromCoordinates(obj["coordinates"], isGeometry);
    }

    public static GeoMultiPolygon? ExtractMultiPolygon(object? value, bool isGeometry)
    {
        var obj = AsJObject(value);
        if (obj is null) return null;
        if (!string.Equals(obj["type"]?.ToString(), "MultiPolygon", StringComparison.OrdinalIgnoreCase)) return null;
        if (obj["coordinates"] is not JArray polyArray || polyArray.Count == 0) return null;

        var polygons = new List<GeoPolygon>(polyArray.Count);
        foreach (var polyToken in polyArray)
        {
            var poly = ExtractPolygonFromCoordinates(polyToken, isGeometry);
            if (poly is null) return null;
            polygons.Add(poly);
        }

        return new GeoMultiPolygon(polygons);
    }

    public static GeoCircle? ExtractCircle(object? value, bool isGeometry)
    {
        var obj = AsJObject(value);
        if (obj is null) return null;

        var center = obj["center"];
        var radius = obj["radius"];
        if (center is null || radius is null) return null;

        var centerPoint = ExtractPoint(center, isGeometry);
        if (centerPoint is null) return null;

        if (!double.TryParse(radius.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var radiusMeters) ||
            radiusMeters < 0 || double.IsNaN(radiusMeters) || double.IsInfinity(radiusMeters))
        {
            return null;
        }

        return new GeoCircle(centerPoint.Value, radiusMeters);
    }

    public static GeoPolygon? ExtractPolygonFromCoordinates(JToken? token, bool isGeometry)
    {
        if (token is not JArray coords || coords.Count == 0) return null;

        var rings = new List<IReadOnlyList<GeoPoint>>(coords.Count);
        foreach (var ringToken in coords)
        {
            var points = ExtractCoordinateArray(ringToken, isGeometry);
            if (points is null || points.Count < 4) return null;
            rings.Add(points);
        }

        return new GeoPolygon(rings);
    }

    public static List<GeoPoint>? ExtractCoordinateArray(JToken? token, bool isGeometry)
    {
        if (token is not JArray arr) return null;

        var points = new List<GeoPoint>(arr.Count);
        foreach (var item in arr)
        {
            if (item is JArray coord && TryReadCoord(coord, out var lon, out var lat) && IsValidCoordinate(lon, lat, isGeometry))
            {
                points.Add(new GeoPoint(lat, lon));
            }
            else
            {
                return null;
            }
        }

        return points;
    }

    public static bool TryReadCoord(JArray coord, out double lon, out double lat)
    {
        lon = 0;
        lat = 0;
        if (coord.Count < 2) return false;
        if (coord[0].Type is not (JTokenType.Float or JTokenType.Integer)) return false;
        if (coord[1].Type is not (JTokenType.Float or JTokenType.Integer)) return false;

        lon = coord[0].Value<double>();
        lat = coord[1].Value<double>();
        return !double.IsNaN(lon) && !double.IsInfinity(lon) && !double.IsNaN(lat) && !double.IsInfinity(lat);
    }
}
