using NtsPolygon = NetTopologySuite.Geometries.Polygon;
using NetTopologySuite.Coverage;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Utilities;
using NetTopologySuite.Index.Strtree;
using NetTopologySuite.Operation.Buffer;
using NetTopologySuite.Precision;
using NetTopologySuite.Operation.Polygonize;
using NetTopologySuite.Simplify;
using OpenRebar.Domain.Models;
using OpenRebar.Domain.Ports;

namespace OpenRebar.Infrastructure.Geometry;

/// <summary>
/// Boolean geometry on a fixed 0.1 mm grid. Overlay uses that precision model.
/// </summary>
public sealed class NtsPlanarGeometry : IPlanarGeometry
{
  public const double MillimetreGrid = 0.1;
  private const double GridScale = 10;

  private readonly GeometryFactory _factory = new(new PrecisionModel(GridScale), 0);

  public bool IsValid(PlanarRegion region) => ToRaw(region).IsValid;

  public PlanarRegion MakeValid(PlanarRegion region) => FromGeometry(Fix(ToGeometry(region)));

  public PlanarRegion Union(PlanarRegion left, PlanarRegion right) =>
      FromGeometry(Fix(ToGeometry(left).Union(ToGeometry(right))));

  public PlanarRegion Difference(PlanarRegion left, PlanarRegion right) =>
      FromGeometry(Fix(ToGeometry(left).Difference(ToGeometry(right))));

  public PlanarRegion Intersection(PlanarRegion left, PlanarRegion right) =>
      FromGeometry(Fix(ToGeometry(left).Intersection(ToGeometry(right))));

  public PlanarRegion SymmetricDifference(PlanarRegion left, PlanarRegion right) =>
      FromGeometry(Fix(ToGeometry(left).SymmetricDifference(ToGeometry(right))));

  public PlanarRegion Buffer(PlanarRegion region, double distanceMm, BufferJoin join)
  {
    var parameters = new BufferParameters
    {
      JoinStyle = join switch
      {
        BufferJoin.Round => JoinStyle.Round,
        BufferJoin.Bevel => JoinStyle.Bevel,
        _ => JoinStyle.Mitre
      }
    };
    return FromGeometry(Fix(ToGeometry(region).Buffer(distanceMm, parameters)));
  }

  public IReadOnlyList<PlanarRegion> Polygonize(IReadOnlyList<(Point2D Start, Point2D End)> segments)
  {
    var polygonizer = new Polygonizer();
    foreach (var (start, end) in segments)
    {
      polygonizer.Add(_factory.CreateLineString(
      [
          new Coordinate(start.X, start.Y),
          new Coordinate(end.X, end.Y)
      ]));
    }

    return polygonizer.GetPolygons()
        .Cast<NetTopologySuite.Geometries.Geometry>()
        .Select(geometry => FromGeometry(Fix(geometry)))
        .ToList();
  }

  public PlanarRegion Simplify(PlanarRegion region, double toleranceMm) =>
      FromGeometry(Fix(TopologyPreservingSimplifier.Simplify(ToGeometry(region), toleranceMm)));

  public PlanarRegion SnapToGrid(PlanarRegion region, double gridMm)
  {
    if (gridMm <= 0)
      throw new ArgumentOutOfRangeException(nameof(gridMm), "Grid size must be positive.");

    var reduced = GeometryPrecisionReducer.Reduce(ToGeometry(region), new PrecisionModel(1.0 / gridMm));
    return FromGeometry(Fix(reduced));
  }

  public IPlanarIndex Index(IReadOnlyList<PlanarRegion> regions)
  {
    var tree = new STRtree<int>();
    for (int i = 0; i < regions.Count; i++)
      tree.Insert(ToGeometry(regions[i]).EnvelopeInternal, i);
    tree.Build();
    return new StrTreeIndex(tree, regions, this);
  }

  public bool IsValidCoverage(IReadOnlyList<PlanarRegion> regions)
  {
    var results = CoverageValidator.Validate(Coverage(regions));
    return results.All(result => result is null || result.IsValid);
  }

  public IReadOnlyList<PlanarRegion> SimplifyCoverage(IReadOnlyList<PlanarRegion> regions, double toleranceMm)
  {
    var simplified = CoverageSimplifier.Simplify(Coverage(regions), toleranceMm);
    return simplified.Select(geometry => FromGeometry(Fix(geometry))).ToList();
  }

  public PlanarRegion UnionCoverage(IReadOnlyList<PlanarRegion> regions) =>
      FromGeometry(Fix(CoverageUnion.Union(Coverage(regions))));

  private NetTopologySuite.Geometries.Geometry[] Coverage(IReadOnlyList<PlanarRegion> regions) =>
      regions.Select(ToGeometry).ToArray();

  private NetTopologySuite.Geometries.Geometry ToGeometry(PlanarRegion region) => Fix(ToRaw(region));

  private NetTopologySuite.Geometries.Geometry ToRaw(PlanarRegion region)
  {
    if (region.Polygons.Count == 0)
      return _factory.CreatePolygon();

    var polygons = region.Polygons.Select(ToPolygon).ToArray();
    return polygons.Length == 1
        ? polygons[0]
        : _factory.CreateMultiPolygon(polygons);
  }

  private NtsPolygon ToPolygon(PlanarPolygon polygon)
  {
    var shell = _factory.CreateLinearRing(Ring(polygon.Shell.Vertices));
    var holes = polygon.Holes.Select(hole => _factory.CreateLinearRing(Ring(hole.Vertices))).ToArray();
    return _factory.CreatePolygon(shell, holes);
  }

  private static Coordinate[] Ring(IReadOnlyList<Point2D> vertices)
  {
    var coordinates = new List<Coordinate>(vertices.Count + 1);
    foreach (var point in vertices)
    {
      if (coordinates.Count > 0 && AlmostSame(coordinates[^1], point))
        continue;
      coordinates.Add(new Coordinate(point.X, point.Y));
    }

    if (coordinates.Count > 1 && AlmostSame(coordinates[0], coordinates[^1].X, coordinates[^1].Y))
      coordinates.RemoveAt(coordinates.Count - 1);

    coordinates.Add(coordinates[0].Copy());
    return coordinates.ToArray();
  }

  private PlanarRegion FromGeometry(NetTopologySuite.Geometries.Geometry geometry)
  {
    var polygons = new List<PlanarPolygon>();
    Collect(geometry, polygons);
    return polygons.Count == 0 ? PlanarRegion.Empty : new PlanarRegion(polygons);
  }

  private static void Collect(NetTopologySuite.Geometries.Geometry geometry, List<PlanarPolygon> polygons)
  {
    switch (geometry)
    {
      case NtsPolygon polygon when !polygon.IsEmpty:
        polygons.Add(ToPlanar(polygon));
        break;
      case GeometryCollection collection:
        for (int i = 0; i < collection.NumGeometries; i++)
          Collect(collection.GetGeometryN(i), polygons);
        break;
    }
  }

  private static PlanarPolygon ToPlanar(NtsPolygon polygon)
  {
    var holes = new List<OpenRebar.Domain.Models.Polygon>();
    for (int i = 0; i < polygon.NumInteriorRings; i++)
      holes.Add(ToPolygon(polygon.GetInteriorRingN(i)));
    return new PlanarPolygon(ToPolygon(polygon.ExteriorRing), holes);
  }

  private static OpenRebar.Domain.Models.Polygon ToPolygon(LineString ring)
  {
    var points = new List<Point2D>(ring.NumPoints);
    for (int i = 0; i < ring.NumPoints; i++)
    {
      var coordinate = ring.GetCoordinateN(i);
      if (i == ring.NumPoints - 1 && points.Count > 0 && AlmostSame(points[0], coordinate.X, coordinate.Y))
        break;
      points.Add(new Point2D(coordinate.X, coordinate.Y));
    }

    return new OpenRebar.Domain.Models.Polygon(points);
  }

  private NetTopologySuite.Geometries.Geometry Fix(NetTopologySuite.Geometries.Geometry geometry)
  {
    var prepared = _factory.CreateGeometry(geometry);
    return new GeometryFixer(prepared).GetResult();
  }

  private static bool AlmostSame(Coordinate coordinate, Point2D point) =>
      AlmostSame(coordinate, point.X, point.Y);

  private static bool AlmostSame(Coordinate coordinate, double x, double y) =>
      Math.Abs(coordinate.X - x) < 1e-9 && Math.Abs(coordinate.Y - y) < 1e-9;

  private static bool AlmostSame(Point2D point, double x, double y) =>
      Math.Abs(point.X - x) < 1e-9 && Math.Abs(point.Y - y) < 1e-9;

  private sealed class StrTreeIndex : IPlanarIndex
  {
    private readonly STRtree<int> _tree;
    private readonly IReadOnlyList<PlanarRegion> _regions;
    private readonly NtsPlanarGeometry _geometry;

    public StrTreeIndex(STRtree<int> tree, IReadOnlyList<PlanarRegion> regions, NtsPlanarGeometry geometry)
    {
      _tree = tree;
      _regions = regions;
      _geometry = geometry;
    }

    public IReadOnlyList<int> Query(PlanarRegion region)
    {
      var hits = _tree.Query(_geometry.ToGeometry(region).EnvelopeInternal);
      return hits.Where(index => _geometry.Intersection(_regions[index], region).Area > 1e-6).ToList();
    }
  }
}
