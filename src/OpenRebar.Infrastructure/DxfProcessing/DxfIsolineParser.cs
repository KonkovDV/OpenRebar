using OpenRebar.Domain.Models;
using OpenRebar.Domain.Ports;
using OpenRebar.Domain.Exceptions;
using IxMilia.Dxf.Entities;

namespace OpenRebar.Infrastructure.DxfProcessing;

/// <summary>
/// Parses DXF isoline files using IxMilia.Dxf library.
/// Extracts polygons from hatches/polylines and maps colors to reinforcement specs.
/// </summary>
public sealed class DxfIsolineParser : IIsolineParser
{
  private static readonly GeometryTolerance ComputationalTolerance = GeometryTolerance.Computational;

  public IReadOnlyList<string> SupportedExtensions => [".dxf"];

  public Task<IReadOnlyList<ReinforcementZone>> ParseAsync(
      string filePath,
      ColorLegend legend,
      CancellationToken cancellationToken = default)
  {
    if (!File.Exists(filePath))
      throw new InvalidIsolineFileException(filePath, "File not found.");

    try
    {
      // Read DXF file
      using var stream = File.OpenRead(filePath);
      var dxfFile = IxMilia.Dxf.DxfFile.Load(stream);

      var zones = new List<ReinforcementZone>();
      int zoneIndex = 0;

      foreach (var entity in dxfFile.Entities)
      {
        cancellationToken.ThrowIfCancellationRequested();

        // Extract polylines and hatches as zone boundaries
        var (polygon, color) = ExtractPolygonFromEntity(entity, dxfFile);
        if (polygon is null || color is null)
          continue;

        // Match color to legend
        var legendEntry = legend.FindClosest(color.Value);
        if (legendEntry is null)
          continue;

        zones.Add(new ReinforcementZone
        {
          Id = $"DXF-{++zoneIndex:D4}",
          Boundary = polygon,
          Spec = legendEntry.Spec,
          Direction = RebarDirection.X, // Will be refined by ZoneDetector
          ZoneType = ZoneType.Simple    // Will be classified by ZoneDetector
        });
      }

      return Task.FromResult<IReadOnlyList<ReinforcementZone>>(zones);
    }
    catch (InvalidIsolineFileException)
    {
      throw;
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
      throw new InvalidIsolineFileException(filePath, ex.Message);
    }
  }

  private static (Polygon? Polygon, IsolineColor? Color) ExtractPolygonFromEntity(
      IxMilia.Dxf.Entities.DxfEntity entity,
      IxMilia.Dxf.DxfFile dxfFile)
  {
    switch (entity)
    {
      case DxfLwPolyline polyline:
        {
          var polygon = BuildPolygonFromLwPolyline(polyline);
          if (polygon is null) return (null, null);

          var color = ResolveEntityColor(polyline, dxfFile);
          return (polygon, color);
        }

      case DxfPolyline polyline3d:
        {
          var polygon = BuildPolygonFromPolyline(polyline3d);
          if (polygon is null) return (null, null);

          var color = ResolveEntityColor(polyline3d, dxfFile);
          return (polygon, color);
        }

      case DxfHatch hatch:
        {
          var polygon = BuildPolygonFromHatch(hatch);
          if (polygon is null) return (null, null);

          var color = ResolveEntityColor(hatch, dxfFile);
          return (polygon, color);
        }

      default:
        return (null, null);
    }
  }

  private static Polygon? BuildPolygonFromLwPolyline(DxfLwPolyline polyline)
  {
    if (polyline.Vertices.Count < 2)
      return null;

    var points = new List<Point2D>();
    for (int i = 0; i < polyline.Vertices.Count - 1; i++)
    {
      var current = polyline.Vertices[i];
      var next = polyline.Vertices[i + 1];
      AppendBulgedEdge(points, current.X, current.Y, next.X, next.Y, current.Bulge);
    }

    if (polyline.IsClosed)
    {
      var current = polyline.Vertices[^1];
      var next = polyline.Vertices[0];
      AppendBulgedEdge(points, current.X, current.Y, next.X, next.Y, current.Bulge);
    }

    return CreatePolygon(points);
  }

  private static Polygon? BuildPolygonFromPolyline(DxfPolyline polyline)
  {
    if (polyline.Vertices.Count < 2)
      return null;

    var points = new List<Point2D>();
    for (int i = 0; i < polyline.Vertices.Count - 1; i++)
    {
      var current = polyline.Vertices[i];
      var next = polyline.Vertices[i + 1];
      AppendBulgedEdge(
          points,
          current.Location.X,
          current.Location.Y,
          next.Location.X,
          next.Location.Y,
          current.Bulge);
    }

    if (polyline.IsClosed)
    {
      var current = polyline.Vertices[^1];
      var next = polyline.Vertices[0];
      AppendBulgedEdge(
          points,
          current.Location.X,
          current.Location.Y,
          next.Location.X,
          next.Location.Y,
          current.Bulge);
    }

    return CreatePolygon(points);
  }

  private static Polygon? BuildPolygonFromHatch(DxfHatch hatch)
  {
    var candidates = hatch.BoundaryPaths
        .Select(BuildPolygonFromBoundaryPath)
        .Where(p => p is not null)
        .Cast<Polygon>()
        .OrderByDescending(p => p.CalculateArea())
        .ToList();

    return candidates.FirstOrDefault();
  }

  private static Polygon? BuildPolygonFromBoundaryPath(DxfHatch.BoundaryPathBase path)
  {
    return path switch
    {
      DxfHatch.PolylineBoundaryPath polylinePath => BuildPolygonFromPolylineBoundaryPath(polylinePath),
      DxfHatch.NonPolylineBoundaryPath nonPolylinePath => BuildPolygonFromNonPolylineBoundaryPath(nonPolylinePath),
      _ => null
    };
  }

  private static Polygon? BuildPolygonFromPolylineBoundaryPath(DxfHatch.PolylineBoundaryPath path)
  {
    if (path.Vertices.Count < 2)
      return null;

    var points = new List<Point2D>();
    for (int i = 0; i < path.Vertices.Count - 1; i++)
    {
      var current = path.Vertices[i];
      var next = path.Vertices[i + 1];
      AppendBulgedEdge(
          points,
          current.Location.X,
          current.Location.Y,
          next.Location.X,
          next.Location.Y,
          current.Bulge);
    }

    if (path.IsClosed)
    {
      var current = path.Vertices[^1];
      var next = path.Vertices[0];
      AppendBulgedEdge(
          points,
          current.Location.X,
          current.Location.Y,
          next.Location.X,
          next.Location.Y,
          current.Bulge);
    }

    return CreatePolygon(points);
  }

  private static Polygon? BuildPolygonFromNonPolylineBoundaryPath(DxfHatch.NonPolylineBoundaryPath path)
  {
    if (path.Edges.Count == 0)
      return null;

    var points = new List<Point2D>();

    foreach (var edge in path.Edges)
    {
      switch (edge)
      {
        case DxfHatch.LineBoundaryPathEdge lineEdge:
          AppendOrderedSegment(
              points,
              [
                  new Point2D(lineEdge.StartPoint.X, lineEdge.StartPoint.Y),
                            new Point2D(lineEdge.EndPoint.X, lineEdge.EndPoint.Y)
              ]);
          break;

        case DxfHatch.CircularArcBoundaryPathEdge arcEdge:
          AppendOrderedSegment(
              points,
              SampleCircularArc(
                  arcEdge.Center.X,
                  arcEdge.Center.Y,
                  arcEdge.Radius,
                  arcEdge.StartAngle,
                  arcEdge.EndAngle,
                  arcEdge.IsCounterClockwise));
          break;
      }
    }

    return CreatePolygon(points);
  }

  private static void AppendBulgedEdge(
      List<Point2D> points,
      double startX,
      double startY,
      double endX,
      double endY,
      double bulge)
  {
    var start = new Point2D(startX, startY);
    var end = new Point2D(endX, endY);
    AppendPoint(points, start);

    if (Math.Abs(bulge) <= 1e-10)
    {
      AppendPoint(points, end);
      return;
    }

    if (!DxfArc.TryCreateFromVertices(startX, startY, bulge, endX, endY, out var arc))
    {
      AppendPoint(points, end);
      return;
    }

    var sampled = SampleCircularArc(
        arc.Center.X,
        arc.Center.Y,
        arc.Radius,
        AngleFromCenter(startX, startY, arc.Center.X, arc.Center.Y),
        AngleFromCenter(endX, endY, arc.Center.X, arc.Center.Y),
        bulge > 0);

    foreach (var point in sampled.Skip(1))
      AppendPoint(points, point);
  }

  private static IReadOnlyList<Point2D> SampleCircularArc(
      double centerX,
      double centerY,
      double radius,
      double startAngle,
      double endAngle,
      bool isCounterClockwise)
  {
    double sweep = isCounterClockwise
        ? NormalizePositiveAngle(endAngle - startAngle)
        : NormalizePositiveAngle(startAngle - endAngle);

    int segments = Math.Max(4, (int)Math.Ceiling(sweep / 15.0));
    var points = new List<Point2D>(segments + 1);

    for (int i = 0; i <= segments; i++)
    {
      double delta = sweep * i / segments;
      double angle = isCounterClockwise
          ? startAngle + delta
          : startAngle - delta;

      double angleRad = angle * Math.PI / 180.0;
      points.Add(new Point2D(
          centerX + radius * Math.Cos(angleRad),
          centerY + radius * Math.Sin(angleRad)));
    }

    return points;
  }

  private static void AppendOrderedSegment(List<Point2D> points, IReadOnlyList<Point2D> segment)
  {
    if (segment.Count == 0)
      return;

    if (points.Count == 0)
    {
      foreach (var point in segment)
        AppendPoint(points, point);
      return;
    }

    var last = points[^1];
    var startDistance = last.DistanceTo(segment[0]);
    var endDistance = last.DistanceTo(segment[^1]);

    if (endDistance + ComputationalTolerance.LinearToleranceMm < startDistance)
    {
      for (int i = segment.Count - 1; i >= 0; i--)
        AppendPoint(points, segment[i]);
      return;
    }

    foreach (var point in segment)
      AppendPoint(points, point);
  }

  private static void AppendPoint(List<Point2D> points, Point2D point)
  {
    if (points.Count == 0 || !AlmostEqual(points[^1], point))
      points.Add(point);
  }

  private static Polygon? CreatePolygon(List<Point2D> points)
  {
    if (points.Count < 3)
      return null;

    if (AlmostEqual(points[0], points[^1]))
      points.RemoveAt(points.Count - 1);

    if (points.Count < 3)
      return null;

    return new Polygon(points);
  }

  private static bool AlmostEqual(Point2D a, Point2D b)
  {
    double tolerance = ComputationalTolerance.LinearToleranceMm;
    return Math.Abs(a.X - b.X) <= tolerance && Math.Abs(a.Y - b.Y) <= tolerance;
  }

  private static double AngleFromCenter(double x, double y, double centerX, double centerY)
  {
    double angle = Math.Atan2(y - centerY, x - centerX) * 180.0 / Math.PI;
    return angle < 0 ? angle + 360.0 : angle;
  }

  private static double NormalizePositiveAngle(double angle)
  {
    angle %= 360.0;
    if (angle < 0)
      angle += 360.0;
    return angle;
  }

  private static IsolineColor? MapDxfColor(IxMilia.Dxf.DxfColor dxfColor)
  {
    // For ByLayer/ByBlock we'd need layer context; fallback to null
    if (dxfColor.IsByLayer || dxfColor.IsByBlock)
      return null;

    // ACI color index → RGB mapping (full 256-entry palette)
    short aci = dxfColor.RawValue;
    if (aci is < 1 or > 255) return null;
    var (r, g, b) = AciPalette.GetRgb(aci);
    return new IsolineColor(r, g, b);
  }

  /// <summary>
  /// Resolve color for entities with ByLayer color — looks up layer's color.
  /// </summary>
  private static IsolineColor? ResolveEntityColor(
      IxMilia.Dxf.Entities.DxfEntity entity,
      IxMilia.Dxf.DxfFile dxfFile)
  {
    if (entity is DxfHatch hatch)
    {
      var hatchFillColor = MapDxfColor(hatch.FillColor);
      if (hatchFillColor is not null)
        return hatchFillColor;
    }

    if (!entity.Color.IsByLayer && !entity.Color.IsByBlock)
      return MapDxfColor(entity.Color);

    if (entity.Color.IsByBlock)
      return null;

    // Resolve from layer
    var layer = dxfFile.Layers.FirstOrDefault(l =>
        string.Equals(l.Name, entity.Layer, StringComparison.OrdinalIgnoreCase));

    if (layer is null) return null;
    return MapDxfColor(layer.Color);
  }

}
