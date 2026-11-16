#nullable enable
using Phantom.CosmosDB.Core.Domain.Primitives;

namespace Phantom.CosmosDB.Core.Domain.Spatial;

internal static class SpatialMathEngine
{
    private const double Epsilon = 1e-10;

    public static double DistancePointToPoint(GeoPoint p1, GeoPoint p2, bool isGeometry)
    {
        if (isGeometry)
        {
            var dx = p1.X - p2.X;
            var dy = p1.Y - p2.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        return HaversineDistanceMeters(p1.Lat, p1.Lon, p2.Lat, p2.Lon);
    }

    public static double DistancePointToLineString(GeoPoint p, GeoLineString line, bool isGeometry)
    {
        var minDist = double.MaxValue;
        for (var i = 0; i < line.Points.Count - 1; i++)
        {
            var d = DistancePointToSegment(p, line.Points[i], line.Points[i + 1], isGeometry);
            if (d < minDist) minDist = d;
        }
        return minDist == double.MaxValue ? 0.0 : minDist;
    }

    public static double DistancePointToPolygon(GeoPoint p, GeoPolygon polygon, bool isGeometry)
    {
        if (PointInPolygon(p, polygon))
            return 0.0;

        var minDist = double.MaxValue;
        foreach (var ring in polygon.Rings)
        {
            for (var i = 0; i < ring.Count - 1; i++)
            {
                var d = DistancePointToSegment(p, ring[i], ring[i + 1], isGeometry);
                if (d < minDist) minDist = d;
            }
        }
        return minDist == double.MaxValue ? 0.0 : minDist;
    }

    public static double DistancePointToMultiPolygon(GeoPoint p, GeoMultiPolygon multi, bool isGeometry)
    {
        var minDist = double.MaxValue;
        foreach (var poly in multi.Polygons)
        {
            var d = DistancePointToPolygon(p, poly, isGeometry);
            if (d < minDist) minDist = d;
            if (minDist <= Epsilon) return 0.0;
        }
        return minDist == double.MaxValue ? 0.0 : minDist;
    }

    public static bool PointInPolygon(GeoPoint point, GeoPolygon polygon)
    {
        if (polygon.Rings.Count == 0) return false;
        if (!PointInRingOrOnBoundary(point, polygon.Rings[0])) return false;

        for (var i = 1; i < polygon.Rings.Count; i++)
        {
            if (PointInRing(point, polygon.Rings[i])) return false;
        }

        return true;
    }

    public static bool PointInMultiPolygon(GeoPoint point, GeoMultiPolygon multi)
    {
        foreach (var poly in multi.Polygons)
        {
            if (PointInPolygon(point, poly)) return true;
        }
        return false;
    }

    public static bool PointOnLineString(GeoPoint point, GeoLineString line)
    {
        for (var i = 0; i < line.Points.Count - 1; i++)
        {
            if (IsPointOnSegment(point, line.Points[i], line.Points[i + 1]))
                return true;
        }
        return false;
    }

    public static bool LineIntersectsLine(GeoLineString l1, GeoLineString l2)
    {
        for (var i = 0; i < l1.Points.Count - 1; i++)
        {
            for (var j = 0; j < l2.Points.Count - 1; j++)
            {
                if (SegmentsIntersect(l1.Points[i], l1.Points[i + 1], l2.Points[j], l2.Points[j + 1]))
                    return true;
            }
        }
        return false;
    }

    public static bool LineIntersectsPolygon(GeoLineString line, GeoPolygon polygon)
    {
        foreach (var pt in line.Points)
        {
            if (PointInPolygon(pt, polygon)) return true;
        }

        for (var i = 0; i < line.Points.Count - 1; i++)
        {
            var a = line.Points[i];
            var b = line.Points[i + 1];
            foreach (var ring in polygon.Rings)
            {
                for (var j = 0; j < ring.Count - 1; j++)
                {
                    if (SegmentsIntersect(a, b, ring[j], ring[j + 1]))
                        return true;
                }
            }
        }

        return false;
    }

    public static bool LineWithinPolygon(GeoLineString line, GeoPolygon polygon)
    {
        if (line.Points.Count == 0) return false;
        foreach (var pt in line.Points)
        {
            if (!PointInPolygon(pt, polygon)) return false;
        }

        // Ensure no segment exits and re-enters the polygon or enters a hole
        for (var i = 0; i < line.Points.Count - 1; i++)
        {
            var mid = new GeoPoint(
                (line.Points[i].Lat + line.Points[i + 1].Lat) / 2.0,
                (line.Points[i].Lon + line.Points[i + 1].Lon) / 2.0);
            if (!PointInPolygon(mid, polygon)) return false;
        }

        return true;
    }

    public static bool PolygonsIntersect(GeoPolygon p1, GeoPolygon p2)
    {
        foreach (var pt in p1.Rings[0])
        {
            if (PointInPolygon(pt, p2)) return true;
        }

        foreach (var pt in p2.Rings[0])
        {
            if (PointInPolygon(pt, p1)) return true;
        }

        foreach (var ring1 in p1.Rings)
        {
            for (var i = 0; i < ring1.Count - 1; i++)
            {
                foreach (var ring2 in p2.Rings)
                {
                    for (var j = 0; j < ring2.Count - 1; j++)
                    {
                        if (SegmentsIntersect(ring1[i], ring1[i + 1], ring2[j], ring2[j + 1]))
                            return true;
                    }
                }
            }
        }

        return false;
    }

    public static bool PolygonWithinPolygon(GeoPolygon inner, GeoPolygon outer)
    {
        if (inner.Rings.Count == 0) return false;
        foreach (var pt in inner.Rings[0])
        {
            if (!PointInPolygon(pt, outer)) return false;
        }

        // Check no edge of inner crosses an edge of outer into exterior/hole
        var line = new GeoLineString(inner.Rings[0]);
        return LineWithinPolygon(line, outer);
    }

    public static double ComputePolygonArea(GeoPolygon polygon, bool isGeometry)
    {
        if (polygon.Rings.Count == 0) return 0.0;
        var area = isGeometry
            ? Math.Abs(PlanarSignedArea(polygon.Rings[0]))
            : SphericalRingAreaMeters2(polygon.Rings[0]);

        for (var i = 1; i < polygon.Rings.Count; i++)
        {
            var hole = isGeometry
                ? Math.Abs(PlanarSignedArea(polygon.Rings[i]))
                : SphericalRingAreaMeters2(polygon.Rings[i]);
            area -= hole;
        }

        return Math.Max(0.0, area);
    }

    public static double ComputeMultiPolygonArea(GeoMultiPolygon multi, bool isGeometry)
    {
        double total = 0;
        foreach (var poly in multi.Polygons)
        {
            total += ComputePolygonArea(poly, isGeometry);
        }
        return total;
    }

    /// <summary>
    /// Shoelace signed area in (Lon/X, Lat/Y). Positive => Counter-Clockwise (CCW / Left-Hand Rule).
    /// </summary>
    public static double PlanarSignedArea(IReadOnlyList<GeoPoint> ring)
    {
        double sum = 0;
        for (var i = 0; i < ring.Count - 1; i++)
        {
            sum += (ring[i].Lon * ring[i + 1].Lat) - (ring[i + 1].Lon * ring[i].Lat);
        }
        return sum / 2.0;
    }

    public static bool RingSelfIntersects(IReadOnlyList<GeoPoint> ring)
    {
        var edgeCount = ring.Count - 1;
        for (var i = 0; i < edgeCount; i++)
        {
            for (var j = i + 2; j < edgeCount; j++)
            {
                // Skip adjacent closing edge (0 and edgeCount - 1)
                if (i == 0 && j == edgeCount - 1) continue;

                if (SegmentsIntersect(ring[i], ring[i + 1], ring[j], ring[j + 1]))
                    return true;
            }
        }
        return false;
    }

    public static bool SegmentsIntersect(GeoPoint a, GeoPoint b, GeoPoint c, GeoPoint d)
    {
        var d1 = Cross(c, d, a);
        var d2 = Cross(c, d, b);
        var d3 = Cross(a, b, c);
        var d4 = Cross(a, b, d);

        if (((d1 > Epsilon && d2 < -Epsilon) || (d1 < -Epsilon && d2 > Epsilon)) &&
            ((d3 > Epsilon && d4 < -Epsilon) || (d3 < -Epsilon && d4 > Epsilon)))
        {
            return true;
        }

        if (Math.Abs(d1) <= Epsilon && OnBoundingBox(c, d, a)) return true;
        if (Math.Abs(d2) <= Epsilon && OnBoundingBox(c, d, b)) return true;
        if (Math.Abs(d3) <= Epsilon && OnBoundingBox(a, b, c)) return true;
        if (Math.Abs(d4) <= Epsilon && OnBoundingBox(a, b, d)) return true;

        return false;
    }

    private static double SphericalRingAreaMeters2(IReadOnlyList<GeoPoint> ring)
    {
        double sum = 0;
        var count = ring.Count - 1;
        for (var i = 0; i < count; i++)
        {
            var lon1 = DegreesToRadians(ring[i].Lon);
            var lat1 = DegreesToRadians(ring[i].Lat);
            var lon2 = DegreesToRadians(ring[(i + 1) % count].Lon);
            var lat2 = DegreesToRadians(ring[(i + 1) % count].Lat);
            sum += (lon2 - lon1) * (2.0 + Math.Sin(lat1) + Math.Sin(lat2));
        }

        return Math.Abs(sum * ContainerConstants.EarthRadiusMeters * ContainerConstants.EarthRadiusMeters / 2.0);
    }

    private static double DistancePointToSegment(GeoPoint p, GeoPoint a, GeoPoint b, bool isGeometry)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var lenSq = dx * dx + dy * dy;
        if (lenSq <= Epsilon * Epsilon)
            return DistancePointToPoint(p, a, isGeometry);

        var t = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lenSq;
        t = Math.Clamp(t, 0.0, 1.0);
        var proj = new GeoPoint(a.Y + t * dy, a.X + t * dx);
        return DistancePointToPoint(p, proj, isGeometry);
    }

    private static bool PointInRingOrOnBoundary(GeoPoint point, IReadOnlyList<GeoPoint> ring)
    {
        for (var i = 0; i < ring.Count - 1; i++)
        {
            if (IsPointOnSegment(point, ring[i], ring[i + 1]))
                return true;
        }
        return PointInRing(point, ring);
    }

    private static bool PointInRing(GeoPoint point, IReadOnlyList<GeoPoint> ring)
    {
        var inside = false;
        for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
        {
            if ((ring[i].Lat > point.Lat) != (ring[j].Lat > point.Lat) &&
                point.Lon < (ring[j].Lon - ring[i].Lon) * (point.Lat - ring[i].Lat) / (ring[j].Lat - ring[i].Lat) + ring[i].Lon)
            {
                inside = !inside;
            }
        }
        return inside;
    }

    private static bool IsPointOnSegment(GeoPoint p, GeoPoint a, GeoPoint b)
    {
        return Math.Abs(Cross(a, b, p)) <= 1e-8 && OnBoundingBox(a, b, p);
    }

    private static double Cross(GeoPoint a, GeoPoint b, GeoPoint c)
        => (b.Lon - a.Lon) * (c.Lat - a.Lat) - (b.Lat - a.Lat) * (c.Lon - a.Lon);

    private static bool OnBoundingBox(GeoPoint a, GeoPoint b, GeoPoint p)
    {
        return p.Lon >= Math.Min(a.Lon, b.Lon) - Epsilon &&
               p.Lon <= Math.Max(a.Lon, b.Lon) + Epsilon &&
               p.Lat >= Math.Min(a.Lat, b.Lat) - Epsilon &&
               p.Lat <= Math.Max(a.Lat, b.Lat) + Epsilon;
    }

    private static double HaversineDistanceMeters(double lat1, double lon1, double lat2, double lon2)
    {
        var dLat = DegreesToRadians(lat2 - lat1);
        var dLon = DegreesToRadians(lon2 - lon1);
        var a = Math.Sin(dLat / 2.0) * Math.Sin(dLat / 2.0) +
                Math.Cos(DegreesToRadians(lat1)) * Math.Cos(DegreesToRadians(lat2)) *
                Math.Sin(dLon / 2.0) * Math.Sin(dLon / 2.0);
        var c = 2.0 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1.0 - a));
        return ContainerConstants.EarthRadiusMeters * c;
    }

    private static double DegreesToRadians(double degrees) => degrees * (Math.PI / 180.0);
}
