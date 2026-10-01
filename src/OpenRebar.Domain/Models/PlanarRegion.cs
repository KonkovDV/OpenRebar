using OpenRebar.Domain.Rules;

namespace OpenRebar.Domain.Models;

/// <summary>
/// One or more polygons with holes. The shell is counter-clockwise and each hole is clockwise.
/// </summary>
public sealed class PlanarRegion
{
  public static PlanarRegion Empty { get; } = new([]);

  public PlanarRegion(IReadOnlyList<PlanarPolygon> polygons)
  {
    Polygons = polygons;
  }

  public IReadOnlyList<PlanarPolygon> Polygons { get; }

  public int HoleCount => Polygons.Sum(polygon => polygon.Holes.Count);

  public double Area => Polygons.Sum(polygon => polygon.Area);

  public bool Contains(Point2D point) => Polygons.Any(polygon => polygon.Contains(point));
}

public sealed class PlanarPolygon
{
  public PlanarPolygon(Polygon shell, IReadOnlyList<Polygon>? holes = null)
  {
    Shell = Orient(shell, counterClockwise: true);
    Holes = (holes ?? []).Select(hole => Orient(hole, counterClockwise: false)).ToList();
  }

  public Polygon Shell { get; }

  public IReadOnlyList<Polygon> Holes { get; }

  public double Area => Shell.CalculateArea() - Holes.Sum(hole => hole.CalculateArea());

  public bool Contains(Point2D point)
  {
    if (!PolygonDecomposition.IsPointInPolygon(point, Shell))
      return false;

    return Holes.All(hole => !PolygonDecomposition.IsPointInPolygon(point, hole));
  }

  private static Polygon Orient(Polygon polygon, bool counterClockwise)
  {
    double signed = SignedArea(polygon.Vertices);
    bool isCounterClockwise = signed > 0;
    if (isCounterClockwise == counterClockwise || Math.Abs(signed) < 1e-9)
      return polygon;

    return new Polygon(polygon.Vertices.Reverse().ToList());
  }

  private static double SignedArea(IReadOnlyList<Point2D> vertices)
  {
    double area = 0;
    for (int i = 0; i < vertices.Count; i++)
    {
      var current = vertices[i];
      var next = vertices[(i + 1) % vertices.Count];
      area += current.X * next.Y - next.X * current.Y;
    }

    return area / 2.0;
  }
}
