using OpenRebar.Domain.Models;

namespace OpenRebar.Domain.Ports;

public enum BufferJoin
{
  Round,
  Mitre,
  Bevel
}

/// <summary>
/// Boolean geometry. The implementation lives in Infrastructure.
/// Coordinates are millimetres on a 0.1 mm grid.
/// </summary>
public interface IPlanarGeometry
{
  bool IsValid(PlanarRegion region);

  PlanarRegion MakeValid(PlanarRegion region);

  PlanarRegion Union(PlanarRegion left, PlanarRegion right);

  PlanarRegion Difference(PlanarRegion left, PlanarRegion right);

  PlanarRegion Intersection(PlanarRegion left, PlanarRegion right);

  PlanarRegion SymmetricDifference(PlanarRegion left, PlanarRegion right);

  PlanarRegion Buffer(PlanarRegion region, double distanceMm, BufferJoin join);

  IReadOnlyList<PlanarRegion> Polygonize(IReadOnlyList<(Point2D Start, Point2D End)> segments);

  PlanarRegion Simplify(PlanarRegion region, double toleranceMm);

  PlanarRegion SnapToGrid(PlanarRegion region, double gridMm);

  IPlanarIndex Index(IReadOnlyList<PlanarRegion> regions);

  bool IsValidCoverage(IReadOnlyList<PlanarRegion> regions);

  IReadOnlyList<PlanarRegion> SimplifyCoverage(IReadOnlyList<PlanarRegion> regions, double toleranceMm);

  PlanarRegion UnionCoverage(IReadOnlyList<PlanarRegion> regions);
}

public interface IPlanarIndex
{
  IReadOnlyList<int> Query(PlanarRegion region);
}
