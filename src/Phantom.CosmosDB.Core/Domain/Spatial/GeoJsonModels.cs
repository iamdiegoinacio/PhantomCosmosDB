#nullable enable
namespace Phantom.CosmosDB.Core.Domain.Spatial;

internal readonly record struct GeoPoint(double Lat, double Lon)
{
    public double X => Lon;
    public double Y => Lat;
}

internal sealed record GeoLineString(IReadOnlyList<GeoPoint> Points);

internal sealed record GeoPolygon(IReadOnlyList<IReadOnlyList<GeoPoint>> Rings);

internal sealed record GeoMultiPolygon(IReadOnlyList<GeoPolygon> Polygons);

internal readonly record struct GeoCircle(GeoPoint Center, double RadiusMeters);
