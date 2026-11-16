#nullable enable
using Newtonsoft.Json.Linq;
using Phantom.CosmosDB.Core.Domain.Spatial;

namespace Phantom.CosmosDB.Core.Application.Query.Functions;

/// <summary>
/// Evaluates Cosmos DB GeoJSON spatial SQL functions (<c>ST_DISTANCE</c>, <c>ST_WITHIN</c>,
/// <c>ST_INTERSECTS</c>, <c>ST_ISVALID</c>, <c>ST_ISVALIDDETAILED</c>, and <c>ST_AREA</c>)
/// across <c>Point</c>, <c>LineString</c>, <c>Polygon</c>, and <c>MultiPolygon</c>.
/// </summary>
internal static class SpatialGeoFunctions
{
    public static object? StDistance(object? left, object? right, bool isGeometry = false)
    {
        var p1 = GeoJsonParser.ExtractPoint(left, isGeometry);
        var p2 = GeoJsonParser.ExtractPoint(right, isGeometry);
        if (p1 is not null && p2 is not null)
            return SpatialMathEngine.DistancePointToPoint(p1.Value, p2.Value, isGeometry);

        if (p1 is not null)
            return DistancePointToOther(p1.Value, right, isGeometry);

        if (p2 is not null)
            return DistancePointToOther(p2.Value, left, isGeometry);

        if (StIntersects(left, right, isGeometry))
            return 0.0;

        return DistanceComplexFallback(left, right, isGeometry);
    }

    public static bool StWithin(object? inner, object? outer, bool isGeometry = false)
    {
        var polyOuter = GeoJsonParser.ExtractPolygon(outer, isGeometry);
        var multiOuter = polyOuter is null ? GeoJsonParser.ExtractMultiPolygon(outer, isGeometry) : null;
        var circleOuter = polyOuter is null && multiOuter is null ? GeoJsonParser.ExtractCircle(outer, isGeometry) : null;

        var p = GeoJsonParser.ExtractPoint(inner, isGeometry);
        if (p is not null)
        {
            if (polyOuter is not null) return SpatialMathEngine.PointInPolygon(p.Value, polyOuter);
            if (multiOuter is not null) return SpatialMathEngine.PointInMultiPolygon(p.Value, multiOuter);
            if (circleOuter is not null)
                return SpatialMathEngine.DistancePointToPoint(p.Value, circleOuter.Value.Center, isGeometry) <= circleOuter.Value.RadiusMeters;
            return false;
        }

        var line = GeoJsonParser.ExtractLineString(inner, isGeometry);
        if (line is not null)
        {
            if (polyOuter is not null) return SpatialMathEngine.LineWithinPolygon(line, polyOuter);
            if (multiOuter is not null) return multiOuter.Polygons.Any(poly => SpatialMathEngine.LineWithinPolygon(line, poly));
            return false;
        }

        var polyInner = GeoJsonParser.ExtractPolygon(inner, isGeometry);
        if (polyInner is not null)
        {
            if (polyOuter is not null) return SpatialMathEngine.PolygonWithinPolygon(polyInner, polyOuter);
            if (multiOuter is not null) return multiOuter.Polygons.Any(poly => SpatialMathEngine.PolygonWithinPolygon(polyInner, poly));
            return false;
        }

        return false;
    }

    public static bool StIntersects(object? geo1, object? geo2, bool isGeometry = false)
    {
        var p1 = GeoJsonParser.ExtractPoint(geo1, isGeometry);
        var p2 = GeoJsonParser.ExtractPoint(geo2, isGeometry);
        if (p1 is not null && p2 is not null)
            return Math.Abs(p1.Value.Lat - p2.Value.Lat) < 1e-10 && Math.Abs(p1.Value.Lon - p2.Value.Lon) < 1e-10;

        if (p1 is not null)
            return IntersectsPointWithOther(p1.Value, geo2, isGeometry);

        if (p2 is not null)
            return IntersectsPointWithOther(p2.Value, geo1, isGeometry);

        var line1 = GeoJsonParser.ExtractLineString(geo1, isGeometry);
        var line2 = GeoJsonParser.ExtractLineString(geo2, isGeometry);
        if (line1 is not null && line2 is not null)
            return SpatialMathEngine.LineIntersectsLine(line1, line2);

        if (line1 is not null)
            return IntersectsLineWithArea(line1, geo2, isGeometry);

        if (line2 is not null)
            return IntersectsLineWithArea(line2, geo1, isGeometry);

        var polys1 = ExtractPolygons(geo1, isGeometry);
        var polys2 = ExtractPolygons(geo2, isGeometry);
        if (polys1 is not null && polys2 is not null)
        {
            return polys1.Any(a => polys2.Any(b => SpatialMathEngine.PolygonsIntersect(a, b)));
        }

        return false;
    }

    public static bool StIsValid(object? geo, bool isGeometry = false)
        => GeoJsonValidator.IsValid(geo, isGeometry);

    public static JObject StIsValidDetailed(object? geo, bool isGeometry = false)
        => GeoJsonValidator.ToDetailedJObject(geo, isGeometry);

    public static object? StArea(object? geo, bool isGeometry = false)
    {
        var poly = GeoJsonParser.ExtractPolygon(geo, isGeometry);
        if (poly is not null)
            return SpatialMathEngine.ComputePolygonArea(poly, isGeometry);

        var multi = GeoJsonParser.ExtractMultiPolygon(geo, isGeometry);
        if (multi is not null)
            return SpatialMathEngine.ComputeMultiPolygonArea(multi, isGeometry);

        return null;
    }

    private static double? DistancePointToOther(GeoPoint p, object? other, bool isGeometry)
    {
        var line = GeoJsonParser.ExtractLineString(other, isGeometry);
        if (line is not null)
            return SpatialMathEngine.DistancePointToLineString(p, line, isGeometry);

        var poly = GeoJsonParser.ExtractPolygon(other, isGeometry);
        if (poly is not null)
            return SpatialMathEngine.DistancePointToPolygon(p, poly, isGeometry);

        var multi = GeoJsonParser.ExtractMultiPolygon(other, isGeometry);
        if (multi is not null)
            return SpatialMathEngine.DistancePointToMultiPolygon(p, multi, isGeometry);

        return null;
    }

    private static double? DistanceComplexFallback(object? left, object? right, bool isGeometry)
    {
        var verticesLeft = ExtractAllVertices(left, isGeometry);
        if (verticesLeft is null || verticesLeft.Count == 0) return null;

        var min = double.MaxValue;
        foreach (var pt in verticesLeft)
        {
            var d = DistancePointToOther(pt, right, isGeometry);
            if (d.HasValue && d.Value < min) min = d.Value;
        }

        return min == double.MaxValue ? null : min;
    }

    private static List<GeoPoint>? ExtractAllVertices(object? geo, bool isGeometry)
    {
        var line = GeoJsonParser.ExtractLineString(geo, isGeometry);
        if (line is not null) return line.Points.ToList();

        var polys = ExtractPolygons(geo, isGeometry);
        if (polys is null) return null;

        return polys.SelectMany(p => p.Rings.SelectMany(r => r)).ToList();
    }

    private static bool IntersectsPointWithOther(GeoPoint p, object? other, bool isGeometry)
    {
        var line = GeoJsonParser.ExtractLineString(other, isGeometry);
        if (line is not null) return SpatialMathEngine.PointOnLineString(p, line);

        var poly = GeoJsonParser.ExtractPolygon(other, isGeometry);
        if (poly is not null) return SpatialMathEngine.PointInPolygon(p, poly);

        var multi = GeoJsonParser.ExtractMultiPolygon(other, isGeometry);
        if (multi is not null) return SpatialMathEngine.PointInMultiPolygon(p, multi);

        var circle = GeoJsonParser.ExtractCircle(other, isGeometry);
        if (circle is not null)
            return SpatialMathEngine.DistancePointToPoint(p, circle.Value.Center, isGeometry) <= circle.Value.RadiusMeters;

        return false;
    }

    private static bool IntersectsLineWithArea(GeoLineString line, object? area, bool isGeometry)
    {
        var poly = GeoJsonParser.ExtractPolygon(area, isGeometry);
        if (poly is not null) return SpatialMathEngine.LineIntersectsPolygon(line, poly);

        var multi = GeoJsonParser.ExtractMultiPolygon(area, isGeometry);
        if (multi is not null) return multi.Polygons.Any(p => SpatialMathEngine.LineIntersectsPolygon(line, p));

        return false;
    }

    private static IReadOnlyList<GeoPolygon>? ExtractPolygons(object? geo, bool isGeometry)
    {
        var poly = GeoJsonParser.ExtractPolygon(geo, isGeometry);
        if (poly is not null) return [poly];

        var multi = GeoJsonParser.ExtractMultiPolygon(geo, isGeometry);
        return multi?.Polygons;
    }
}
