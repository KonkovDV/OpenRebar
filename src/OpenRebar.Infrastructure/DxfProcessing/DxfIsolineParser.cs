using OpenRebar.Domain.Models;
using OpenRebar.Domain.Ports;
using OpenRebar.Domain.Exceptions;
using OpenRebar.Domain.Rules;
using IxMilia.Dxf;
using IxMilia.Dxf.Entities;

namespace OpenRebar.Infrastructure.DxfProcessing;

/// <summary>
/// Parses DXF isoline files using IxMilia.Dxf library.
/// Extracts polygons from hatches/polylines and maps colors to reinforcement specs.
/// </summary>
public sealed class DxfIsolineParser : IIsolineParser
{
  private static readonly GeometryTolerance ComputationalTolerance = GeometryTolerance.Computational;
  private static readonly AsyncLocal<DxfParseSession?> ActiveSession = new();

  public IReadOnlyList<string> SupportedExtensions => [".dxf"];

  public IsolineParseStats Stats { get; private set; } = IsolineParseStats.Empty;

  /// <summary>
  /// Unit used when the file does not set $INSUNITS. Coordinates are still stored in millimetres.
  /// </summary>
  public DxfUnits UnitsWhenUnset { get; set; } = DxfUnits.Millimeters;

  public bool TrySetUnitsWhenUnset(string name)
  {
    switch (name.Trim().ToLowerInvariant())
    {
      case "mm":
        UnitsWhenUnset = DxfUnits.Millimeters;
        return true;
      case "cm":
        UnitsWhenUnset = DxfUnits.Centimeters;
        return true;
      case "m":
        UnitsWhenUnset = DxfUnits.Meters;
        return true;
      case "in":
      case "inch":
        UnitsWhenUnset = DxfUnits.Inches;
        return true;
      default:
        return false;
    }
  }

  public Task<IReadOnlyList<ReinforcementZone>> ParseAsync(
      string filePath,
      ColorLegend legend,
      CancellationToken cancellationToken = default)
  {
    if (!File.Exists(filePath))
      throw new InvalidIsolineFileException(filePath, "File not found.");

    var session = new DxfParseSession();
    ActiveSession.Value = session;
    try
    {
      using var stream = File.OpenRead(filePath);
      var dxfFile = DxfFile.Load(stream);
      session.UnitsWhenUnset = UnitsWhenUnset;
      session.ScaleToMillimetres = ResolveScale(dxfFile.Header.DefaultDrawingUnits, session);

      var candidates = new List<ZoneCandidate>();
      foreach (var entity in dxfFile.Entities)
      {
        cancellationToken.ThrowIfCancellationRequested();
        CollectEntity(entity, dxfFile, legend, candidates, session, blockStack: []);
      }

      var zones = Deduplicate(candidates, session);
      Stats = session.ToStats();
      return Task.FromResult<IReadOnlyList<ReinforcementZone>>(zones);
    }
    catch (InvalidIsolineFileException)
    {
      Stats = session.ToStats();
      throw;
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
      Stats = session.ToStats();
      throw new InvalidIsolineFileException(filePath, ex.Message);
    }
    finally
    {
      ActiveSession.Value = null;
    }
  }

  private void CollectEntity(
      DxfEntity entity,
      DxfFile dxfFile,
      ColorLegend legend,
      List<ZoneCandidate> candidates,
      DxfParseSession session,
      HashSet<string> blockStack)
  {
    session.ParsedEntityCount++;

    if (entity is DxfInsert insert)
    {
      ExplodeInsert(insert, dxfFile, legend, candidates, session, blockStack);
      return;
    }

    if (entity is DxfEllipse or DxfSpline)
    {
      session.Add("unsupportedCurve");
      return;
    }

    var (polygon, color, holes) = ExtractPolygonFromEntity(entity, dxfFile);
    if (polygon is null || color is null)
      return;

    var legendEntry = legend.FindClosest(color.Value);
    if (legendEntry is null)
      return;

    candidates.Add(new ZoneCandidate(
        polygon,
        legendEntry.Spec,
        entity.Layer,
        Priority: entity is DxfHatch ? 2 : 1,
        holes));
  }

  private static (Polygon? Polygon, IsolineColor? Color, IReadOnlyList<Polygon> Holes) ExtractPolygonFromEntity(
      IxMilia.Dxf.Entities.DxfEntity entity,
      IxMilia.Dxf.DxfFile dxfFile)
  {
    switch (entity)
    {
      case DxfLwPolyline polyline:
        {
          var polygon = BuildPolygonFromLwPolyline(polyline);
          if (polygon is null) return (null, null, []);

          var color = ResolveEntityColor(polyline, dxfFile);
          return (polygon, color, []);
        }

      case DxfPolyline polyline3d:
        {
          var polygon = BuildPolygonFromPolyline(polyline3d);
          if (polygon is null) return (null, null, []);

          var color = ResolveEntityColor(polyline3d, dxfFile);
          return (polygon, color, []);
        }

      case DxfHatch hatch:
        {
          var shape = BuildHatchShape(hatch);
          if (shape is null) return (null, null, []);

          var color = ResolveEntityColor(hatch, dxfFile);
          return (shape.Value.Outer, color, shape.Value.Holes);
        }

      default:
        return (null, null, []);
    }
  }

  private static Polygon? BuildPolygonFromLwPolyline(DxfLwPolyline polyline)
  {
    if (polyline.Vertices.Count < 2)
      return null;

    var first = polyline.Vertices[0];
    var last = polyline.Vertices[^1];
    if (!IsZoneBoundaryClosed(polyline.IsClosed, first.X, first.Y, last.X, last.Y))
    {
      ActiveSession.Value?.Add("ignoredOpenPolylines");
      return null;
    }

    var points = new List<Point2D>();
    for (int i = 0; i < polyline.Vertices.Count - 1; i++)
    {
      var current = polyline.Vertices[i];
      var next = polyline.Vertices[i + 1];
      AppendBulgedEdge(points, current.X, current.Y, next.X, next.Y, current.Bulge);
    }

    var closing = polyline.Vertices[^1];
    var closingNext = polyline.Vertices[0];
    AppendBulgedEdge(points, closing.X, closing.Y, closingNext.X, closingNext.Y, closing.Bulge);

    return CreatePolygon(points);
  }

  private static Polygon? BuildPolygonFromPolyline(DxfPolyline polyline)
  {
    if (polyline.Vertices.Count < 2)
      return null;

    var first = polyline.Vertices[0].Location;
    var last = polyline.Vertices[^1].Location;
    if (!IsZoneBoundaryClosed(polyline.IsClosed, first.X, first.Y, last.X, last.Y))
    {
      ActiveSession.Value?.Add("ignoredOpenPolylines");
      return null;
    }

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

    var closing = polyline.Vertices[^1];
    var closingNext = polyline.Vertices[0];
    AppendBulgedEdge(
        points,
        closing.Location.X,
        closing.Location.Y,
        closingNext.Location.X,
        closingNext.Location.Y,
        closing.Bulge);

    return CreatePolygon(points);
  }

  private readonly record struct HatchShape(Polygon Outer, IReadOnlyList<Polygon> Holes);

  private static HatchShape? BuildHatchShape(DxfHatch hatch)
  {
    var candidates = hatch.BoundaryPaths
        .Select(BuildPolygonFromBoundaryPath)
        .Where(p => p is not null)
        .Cast<Polygon>()
        .OrderByDescending(p => p.CalculateArea())
        .ToList();

    if (candidates.Count == 0)
      return null;

    var outer = candidates[0];
    var holes = new List<Polygon>();
    for (int i = 1; i < candidates.Count; i++)
    {
      var inner = candidates[i];
      if (PolygonDecomposition.IsPointInPolygon(inner.Vertices[0], outer))
        holes.Add(inner);
      else
        ActiveSession.Value?.Add("holeIgnored");
    }

    return new HatchShape(outer, holes);
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

    var first = path.Vertices[0].Location;
    var last = path.Vertices[^1].Location;
    if (!IsZoneBoundaryClosed(path.IsClosed, first.X, first.Y, last.X, last.Y))
    {
      ActiveSession.Value?.Add("ignoredOpenPolylines");
      return null;
    }

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

    var closing = path.Vertices[^1];
    var closingNext = path.Vertices[0];
    AppendBulgedEdge(
        points,
        closing.Location.X,
        closing.Location.Y,
        closingNext.Location.X,
        closingNext.Location.Y,
        closing.Bulge);

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

        default:
          ActiveSession.Value?.Add("unsupportedBoundaryEdge");
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

  private static bool IsZoneBoundaryClosed(
      bool isClosed,
      double startX,
      double startY,
      double endX,
      double endY)
  {
    if (isClosed)
      return true;

    double tolerance = GeometryTolerance.Default.LinearToleranceMm;
    return Math.Abs(startX - endX) <= tolerance && Math.Abs(startY - endY) <= tolerance;
  }

  private static Polygon ApplyScale(Polygon polygon, double scale)
  {
    if (Math.Abs(scale - 1.0) <= 1e-12)
      return polygon;

    return new Polygon(polygon.Vertices
        .Select(point => new Point2D(point.X * scale, point.Y * scale))
        .ToList());
  }

  private static double ResolveScale(DxfUnits units, DxfParseSession session)
  {
    if (units == DxfUnits.Unitless)
    {
      session.MarkUnitsAssumed();
      return ScaleOf(session.UnitsWhenUnset);
    }

    if (!TryScaleOf(units, out double scale))
    {
      session.MarkUnitsAssumed();
      return ScaleOf(session.UnitsWhenUnset);
    }

    return scale;
  }

  private static double ScaleOf(DxfUnits units)
      => TryScaleOf(units, out double scale) ? scale : 1.0;

  private static bool TryScaleOf(DxfUnits units, out double scale)
  {
    scale = units switch
    {
      DxfUnits.Millimeters => 1.0,
      DxfUnits.Centimeters => 10.0,
      DxfUnits.Decimeters => 100.0,
      DxfUnits.Meters => 1000.0,
      DxfUnits.Kilometers => 1_000_000.0,
      DxfUnits.Inches => 25.4,
      DxfUnits.Feet => 304.8,
      DxfUnits.Yards => 914.4,
      _ => double.NaN
    };
    return !double.IsNaN(scale);
  }

  private void ExplodeInsert(
      DxfInsert insert,
      DxfFile dxfFile,
      ColorLegend legend,
      List<ZoneCandidate> candidates,
      DxfParseSession session,
      HashSet<string> blockStack)
  {
    if (string.IsNullOrWhiteSpace(insert.Name) || !blockStack.Add(insert.Name))
    {
      session.Add("ignoredInsertCycle");
      return;
    }

    var block = dxfFile.Blocks.FirstOrDefault(item =>
        string.Equals(item.Name, insert.Name, StringComparison.OrdinalIgnoreCase));
    if (block is null)
    {
      session.Add("ignoredMissingBlock");
      blockStack.Remove(insert.Name);
      return;
    }

    int before = candidates.Count;

    try
    {
      foreach (var child in block.Entities)
        CollectEntity(child, dxfFile, legend, candidates, session, blockStack);
    }
    finally
    {
      blockStack.Remove(insert.Name);
    }

    var placement = InsertPlacement.FromInsert(insert);
    var placed = new List<ZoneCandidate>();
    for (int i = before; i < candidates.Count; i++)
    {
      for (int row = 0; row < Math.Max(1, (int)insert.RowCount); row++)
      {
        for (int column = 0; column < Math.Max(1, (int)insert.ColumnCount); column++)
        {
          var shift = placement.WithGrid(column * insert.ColumnSpacing, row * insert.RowSpacing);
          placed.Add(candidates[i] with
          {
            Boundary = shift.Apply(candidates[i].Boundary),
            Holes = candidates[i].Holes.Select(shift.Apply).ToList()
          });
        }
      }
    }

    candidates.RemoveRange(before, candidates.Count - before);
    candidates.AddRange(placed);
  }

  private readonly record struct InsertPlacement(
      double M11, double M12, double M21, double M22, double Tx, double Ty)
  {
    public static InsertPlacement FromInsert(DxfInsert insert)
    {
      double sx = insert.XScaleFactor == 0 ? 1.0 : insert.XScaleFactor;
      double sy = insert.YScaleFactor == 0 ? 1.0 : insert.YScaleFactor;
      double radians = insert.Rotation * Math.PI / 180.0;
      double cos = Math.Cos(radians);
      double sin = Math.Sin(radians);
      return new(
          sx * cos,
          -sy * sin,
          sx * sin,
          sy * cos,
          insert.Location.X,
          insert.Location.Y);
    }

    public InsertPlacement WithGrid(double columnShift, double rowShift) => this with
    {
      Tx = Tx + columnShift,
      Ty = Ty + rowShift
    };

    public Polygon Apply(Polygon polygon)
    {
      double m11 = M11;
      double m12 = M12;
      double m21 = M21;
      double m22 = M22;
      double tx = Tx;
      double ty = Ty;
      return new Polygon(polygon.Vertices
          .Select(point => new Point2D(
              m11 * point.X + m12 * point.Y + tx,
              m21 * point.X + m22 * point.Y + ty))
          .ToList());
    }
  }

  private static IReadOnlyList<ReinforcementZone> Deduplicate(
      List<ZoneCandidate> candidates,
      DxfParseSession session)
  {
    var kept = new List<ZoneCandidate>();
    foreach (var candidate in candidates)
    {
      string key = candidate.Key;
      int existing = kept.FindIndex(item => item.Key == key);
      if (existing < 0)
      {
        kept.Add(candidate);
        continue;
      }

      session.Add("duplicateZone");
      if (candidate.Priority > kept[existing].Priority)
        kept[existing] = candidate;
    }

    var zones = new List<ReinforcementZone>(kept.Count);
    for (int i = 0; i < kept.Count; i++)
    {
      zones.Add(new ReinforcementZone
      {
        Id = $"DXF-{i + 1:D4}",
        Boundary = ApplyScale(kept[i].Boundary, session.ScaleToMillimetres),
        Holes = kept[i].Holes.Select(hole => ApplyScale(hole, session.ScaleToMillimetres)).ToList(),
        Spec = kept[i].Spec,
        Direction = RebarDirection.X,
        ZoneType = ZoneType.Simple,
        SourceLayerName = kept[i].LayerName
      });
    }

    return zones;
  }

  private sealed class DxfParseSession
  {
    private readonly Dictionary<string, int> _ignored = new(StringComparer.Ordinal);

    public int ParsedEntityCount { get; set; }
    public double ScaleToMillimetres { get; set; } = 1.0;
    public bool UnitsAssumed { get; set; }
    public string? AssumedUnits { get; set; }
    public DxfUnits UnitsWhenUnset { get; set; } = DxfUnits.Millimeters;

    public void Add(string reason, int count = 1)
    {
      _ignored.TryGetValue(reason, out int current);
      _ignored[reason] = current + count;
    }

    public void MarkUnitsAssumed()
    {
      UnitsAssumed = true;
      AssumedUnits = "mm";
      Add("unitsAssumedMillimetres");
    }

    public IsolineParseStats ToStats() => new()
    {
      ParsedEntityCount = ParsedEntityCount,
      IgnoredByReason = _ignored,
      UnitsAssumed = UnitsAssumed,
      AssumedUnits = AssumedUnits
    };
  }

  private sealed record ZoneCandidate(
      Polygon Boundary,
      ReinforcementSpec Spec,
      string LayerName,
      int Priority,
      IReadOnlyList<Polygon> Holes)
  {
    public string Key => BuildKey(Spec, Boundary);

    private static string BuildKey(ReinforcementSpec spec, Polygon boundary)
    {
      var canonical = CanonicalVertices(boundary.Vertices);
      var builder = new System.Text.StringBuilder();
      builder.Append(spec.SteelClass).Append('|')
          .Append(spec.DiameterMm.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture))
          .Append('|')
          .Append(spec.SpacingMm.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
      foreach (var point in canonical)
      {
        builder.Append('|')
            .Append(point.X.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture))
            .Append(',')
            .Append(point.Y.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
      }

      return builder.ToString();
    }

    private static List<Point2D> CanonicalVertices(IReadOnlyList<Point2D> vertices)
    {
      var rounded = vertices
          .Select(point => new Point2D(Math.Round(point.X, 2), Math.Round(point.Y, 2)))
          .ToList();
      if (rounded.Count > 1 && AlmostEqual(rounded[0], rounded[^1]))
        rounded.RemoveAt(rounded.Count - 1);

      int start = 0;
      for (int i = 1; i < rounded.Count; i++)
      {
        if (rounded[i].X < rounded[start].X - 1e-9
            || (Math.Abs(rounded[i].X - rounded[start].X) <= 1e-9 && rounded[i].Y < rounded[start].Y))
          start = i;
      }

      var forward = Rotate(rounded, start);
      var reverse = Rotate(Enumerable.Reverse(rounded).ToList(), IndexOfMin(Enumerable.Reverse(rounded).ToList()));
      return SequenceIsLess(reverse, forward) ? reverse : forward;
    }

    private static int IndexOfMin(List<Point2D> points)
    {
      int start = 0;
      for (int i = 1; i < points.Count; i++)
      {
        if (points[i].X < points[start].X - 1e-9
            || (Math.Abs(points[i].X - points[start].X) <= 1e-9 && points[i].Y < points[start].Y))
          start = i;
      }

      return start;
    }

    private static List<Point2D> Rotate(List<Point2D> points, int start)
    {
      var rotated = new List<Point2D>(points.Count);
      for (int i = 0; i < points.Count; i++)
        rotated.Add(points[(start + i) % points.Count]);
      return rotated;
    }

    private static bool SequenceIsLess(List<Point2D> left, List<Point2D> right)
    {
      int count = Math.Min(left.Count, right.Count);
      for (int i = 0; i < count; i++)
      {
        if (Math.Abs(left[i].X - right[i].X) > 1e-9)
          return left[i].X < right[i].X;
        if (Math.Abs(left[i].Y - right[i].Y) > 1e-9)
          return left[i].Y < right[i].Y;
      }

      return left.Count < right.Count;
    }
  }

}
