using OpenRebar.Domain.Models;
using OpenRebar.Domain.Ports;

namespace OpenRebar.Application.UseCases;

/// <summary>
/// Clearance and clash check. A gap is enough only when it meets the required value.
/// A bar is inside the working area when its distance outside is at most the geometry grid.
/// </summary>
public static class ClearanceChecker
{
  public const double InsideToleranceMm = 0.1;
  public const double MinimumClearGapMm = 25;
  private const double SearchRadiusMm = 101;
  private const double SameLineMm = 1;

  public static IReadOnlyList<ClashReport> Check(
      IReadOnlyList<ReinforcementZone> zones,
      SlabGeometry slab,
      IPlanarGeometry? geometry = null,
      double jointRatioMax = 0.5)
  {
    var clashes = new List<ClashReport>();
    var bars = Collect(zones);
    var outer = OuterWorkingArea(slab, geometry);
    foreach (var bar in bars)
      AddBoundaryClash(clashes, bar, outer);

    foreach (var opening in GrownOpenings(slab, geometry))
    {
      foreach (var bar in bars)
        AddOpeningClash(clashes, bar, opening, slab.OpeningClearanceMm);
    }

    var pairs = CandidatePairs(bars, geometry);
    foreach (var (left, right) in pairs)
    {
      if (SameLayer(left, right) && Parallel(left.PhysicalStart, left.PhysicalEnd, right.PhysicalStart, right.PhysicalEnd))
        AddClearanceClash(clashes, left, right);
      else if (SameFace(left, right) && left.Zone.Direction != right.Zone.Direction)
        AddStackClash(clashes, left, right, slab);
    }

    AddLapClashes(clashes, bars, jointRatioMax);
    return clashes
        .OrderBy(clash => clash.Kind, StringComparer.Ordinal)
        .ThenBy(clash => clash.BarIds[0], StringComparer.Ordinal)
        .ThenBy(clash => clash.X)
        .ThenBy(clash => clash.Y)
        .ToList();
  }

  private static void AddBoundaryClash(List<ClashReport> clashes, BarRef bar, PlanarRegion outer)
  {
    if (outer.Polygons.Count == 0)
      return;

    Point2D? worst = null;
    double worstDistance = InsideToleranceMm;
    foreach (var point in Samples(bar.PhysicalStart, bar.PhysicalEnd))
    {
      double outside = DistanceOutside(outer, point);
      if (outside > worstDistance)
      {
        worstDistance = outside;
        worst = point;
      }
    }

    if (worst is Point2D at)
    {
      clashes.Add(new ClashReport
      {
        Type = "hard",
        Kind = "barOutsideWorkingArea",
        X = at.X,
        Y = at.Y,
        BarIds = [bar.Id]
      });
    }
  }

  private static void AddOpeningClash(List<ClashReport> clashes, BarRef bar, Polygon opening, double clearanceMm)
  {
    bool through = HitsPolygon(bar.PhysicalStart, bar.PhysicalEnd, opening);
    double gap = through ? 0 : GapToPolygon(bar.PhysicalStart, bar.PhysicalEnd, opening);
    if (!through && gap >= clearanceMm)
      return;

    clashes.Add(new ClashReport
    {
      Type = "hard",
      Kind = "barInOpening",
      X = bar.PhysicalStart.X,
      Y = bar.PhysicalStart.Y,
      BarIds = [bar.Id]
    });
  }

  private static void AddClearanceClash(List<ClashReport> clashes, BarRef left, BarRef right)
  {
    double lineDistance = LineDistance(left, right);
    if (lineDistance <= SameLineMm)
      return;

    double centerDistance = SegmentDistance(left.PhysicalStart, left.PhysicalEnd, right.PhysicalStart, right.PhysicalEnd);
    double gap = centerDistance - (left.Segment.DiameterMm + right.Segment.DiameterMm) / 2.0;
    double required = Math.Max(Math.Max(left.Segment.DiameterMm, right.Segment.DiameterMm), MinimumClearGapMm);
    if (gap >= required)
      return;

    var mid = Mid(left.Segment.Start, right.Segment.Start);
    clashes.Add(new ClashReport
    {
      Type = "hard",
      Kind = "layerClearance",
      X = mid.X,
      Y = mid.Y,
      BarIds = [left.Id, right.Id]
    });
  }

  private static void AddStackClash(List<ClashReport> clashes, BarRef left, BarRef right, SlabGeometry slab)
  {
    double centerDistance = SegmentDistance(left.PhysicalStart, left.PhysicalEnd, right.PhysicalStart, right.PhysicalEnd);
    double touch = (left.Segment.DiameterMm + right.Segment.DiameterMm) / 2.0;
    if (centerDistance > touch)
      return;

    double available = slab.ThicknessMm - 2 * slab.CoverMm;
    if (left.Segment.DiameterMm + right.Segment.DiameterMm <= available)
      return;

    var mid = Mid(left.Segment.Start, right.Segment.Start);
    clashes.Add(new ClashReport
    {
      Type = "hard",
      Kind = "crossingStack",
      X = mid.X,
      Y = mid.Y,
      BarIds = [left.Id, right.Id]
    });
  }

  private static void AddLapClashes(List<ClashReport> clashes, IReadOnlyList<BarRef> bars, double jointRatioMax)
  {
    foreach (var layer in bars.GroupBy(bar => LayerKeyOf(bar.Zone)))
    {
      var lines = ClusterLines(layer.ToList());
      foreach (var run in ClusterRuns(lines))
      {
        var joints = run.SelectMany(line => line.Joints).ToList();
        if (joints.Count == 0 || run.Count == 0)
          continue;

        var origin = run[0].Bars[0].PhysicalStart;
        var axis = run[0].Bars[0].PhysicalEnd - origin;
        double axisLength = Math.Sqrt(axis.X * axis.X + axis.Y * axis.Y);
        if (axisLength < 1e-9)
          continue;
        var unit = new Point2D(axis.X / axisLength, axis.Y / axisLength);
        double Project(Point2D point) => (point.X - origin.X) * unit.X + (point.Y - origin.Y) * unit.Y;

        foreach (var joint in joints)
        {
          double at = Project(joint.Center);
          double window = LapPlanner.SectionLengthFactor * joint.Length;
          int occupied = run.Count(line => line.Joints.Any(other => Math.Abs(Project(other.Center) - at) <= window + 1e-6));
          if (occupied / (double)run.Count <= jointRatioMax)
            continue;

          clashes.Add(new ClashReport
          {
            Type = "soft",
            Kind = "lapOverload",
            X = joint.Center.X,
            Y = joint.Center.Y,
            BarIds = [joint.Left.Id, joint.Right.Id]
          });
          break;
        }
      }
    }
  }

  private static List<LineCluster> ClusterLines(List<BarRef> bars)
  {
    var parent = Enumerable.Range(0, bars.Count).ToArray();
    int Find(int index)
    {
      while (parent[index] != index)
        index = parent[index] = parent[parent[index]];
      return index;
    }

    for (int i = 0; i < bars.Count; i++)
    {
      for (int j = i + 1; j < bars.Count; j++)
      {
        if (!Parallel(bars[i].PhysicalStart, bars[i].PhysicalEnd, bars[j].PhysicalStart, bars[j].PhysicalEnd))
          continue;
        if (LineDistance(bars[i], bars[j]) > SameLineMm)
          continue;
        if (LongitudinalOverlap(bars[i], bars[j]) < -SameLineMm)
          continue;
        parent[Find(i)] = Find(j);
      }
    }

    var lines = new List<LineCluster>();
    foreach (var cluster in bars.Select((bar, index) => (bar, root: Find(index))).GroupBy(item => item.root))
    {
      var members = cluster.Select(item => item.bar).ToList();
      var joints = new List<LapSample>();
      for (int i = 0; i < members.Count; i++)
      {
        for (int j = i + 1; j < members.Count; j++)
        {
          if (!TryOverlap(members[i], members[j], out var center, out double overlap))
            continue;
          joints.Add(new LapSample(center, overlap, members[i], members[j]));
        }
      }

      lines.Add(new LineCluster(members, joints));
    }

    return lines;
  }

  private static List<List<LineCluster>> ClusterRuns(List<LineCluster> lines)
  {
    var parent = Enumerable.Range(0, lines.Count).ToArray();
    int Find(int index)
    {
      while (parent[index] != index)
        index = parent[index] = parent[parent[index]];
      return index;
    }

    for (int i = 0; i < lines.Count; i++)
    {
      for (int j = i + 1; j < lines.Count; j++)
      {
        var left = lines[i].Bars[0];
        var right = lines[j].Bars[0];
        if (!Parallel(left.PhysicalStart, left.PhysicalEnd, right.PhysicalStart, right.PhysicalEnd))
          continue;
        if (!SpansOverlap(lines[i], lines[j]))
          continue;
        parent[Find(i)] = Find(j);
      }
    }

    return lines
        .Select((line, index) => (line, root: Find(index)))
        .GroupBy(item => item.root)
        .Select(group => group.Select(item => item.line).ToList())
        .ToList();
  }

  private static List<(BarRef Left, BarRef Right)> CandidatePairs(IReadOnlyList<BarRef> bars, IPlanarGeometry? geometry)
  {
    var pairs = new List<(BarRef, BarRef)>();
    if (bars.Count < 2)
      return pairs;

    var boxes = bars.Select(bar => Box(bar, SearchRadiusMm)).ToList();
    if (geometry is null)
    {
      for (int i = 0; i < bars.Count; i++)
      {
        for (int j = i + 1; j < bars.Count; j++)
        {
          if (BoxesOverlap(boxes[i], boxes[j]))
            pairs.Add((bars[i], bars[j]));
        }
      }

      return pairs;
    }

    var index = geometry.Index(boxes);
    for (int i = 0; i < bars.Count; i++)
    {
      foreach (int hit in index.Query(boxes[i]))
      {
        if (hit > i)
          pairs.Add((bars[i], bars[hit]));
      }
    }

    return pairs;
  }

  private static PlanarRegion OuterWorkingArea(SlabGeometry slab, IPlanarGeometry? geometry)
  {
    if (SlabEdges.HasDeclaredSupport(slab))
      return new PlanarRegion([new PlanarPolygon(SlabEdges.WorkingRectangle(slab))]);

    var region = new PlanarRegion([new PlanarPolygon(slab.OuterBoundary)]);
    if (slab.EdgeCoverMm > InsideToleranceMm && geometry is not null)
      region = geometry.Buffer(region, -slab.EdgeCoverMm, BufferJoin.Mitre);
    return region;
  }

  private static IReadOnlyList<Polygon> GrownOpenings(SlabGeometry slab, IPlanarGeometry? geometry)
  {
    if (slab.OpeningClearanceMm <= InsideToleranceMm || geometry is null)
      return slab.Openings;

    return slab.Openings
        .Select(opening => geometry.Buffer(new PlanarRegion([new PlanarPolygon(opening)]), slab.OpeningClearanceMm, BufferJoin.Mitre))
        .SelectMany(region => region.Polygons)
        .Select(polygon => polygon.Shell)
        .ToList();
  }

  private static List<BarRef> Collect(IReadOnlyList<ReinforcementZone> zones)
  {
    var bars = new List<BarRef>();
    foreach (var zone in zones)
    {
      foreach (var segment in zone.Rebars)
      {
        if (segment.Status == BarInstanceStatus.Discarded)
          continue;
        var (start, end) = Physical(segment);
        bars.Add(new BarRef(zone, segment, IdOf(segment), start, end));
      }
    }

    return bars;
  }

  private static (Point2D Start, Point2D End) Physical(RebarSegment bar) => (bar.Start, bar.End);

  private static IEnumerable<Point2D> Samples(Point2D start, Point2D end)
  {
    yield return start;
    yield return end;
    double length = start.DistanceTo(end);
    int steps = Math.Max(1, (int)Math.Ceiling(length / 200.0));
    for (int i = 1; i < steps; i++)
    {
      double t = i / (double)steps;
      yield return new Point2D(start.X + (end.X - start.X) * t, start.Y + (end.Y - start.Y) * t);
    }
  }

  private static double DistanceOutside(PlanarRegion region, Point2D point)
  {
    if (region.Contains(point))
      return 0;

    double best = double.MaxValue;
    foreach (var polygon in region.Polygons)
    {
      best = Math.Min(best, DistanceToRing(point, polygon.Shell));
      foreach (var hole in polygon.Holes)
        best = Math.Min(best, DistanceToRing(point, hole));
    }

    return best;
  }

  private static double DistanceToRing(Point2D point, Polygon ring)
  {
    double best = double.MaxValue;
    var vertices = ring.Vertices;
    for (int i = 0; i < vertices.Count; i++)
      best = Math.Min(best, PointSegmentDistance(point, vertices[i], vertices[(i + 1) % vertices.Count]));
    return best;
  }

  private static double GapToPolygon(Point2D start, Point2D end, Polygon polygon)
  {
    if (HitsPolygon(start, end, polygon))
      return 0;

    double best = double.MaxValue;
    var vertices = polygon.Vertices;
    for (int i = 0; i < vertices.Count; i++)
      best = Math.Min(best, SegmentDistance(start, end, vertices[i], vertices[(i + 1) % vertices.Count]));
    return best;
  }

  private static bool HitsPolygon(Point2D start, Point2D end, Polygon polygon)
  {
    if (new PlanarPolygon(polygon).Contains(start) || new PlanarPolygon(polygon).Contains(end))
      return true;

    var vertices = polygon.Vertices;
    for (int i = 0; i < vertices.Count; i++)
    {
      if (SegmentsCross(start, end, vertices[i], vertices[(i + 1) % vertices.Count]))
        return true;
    }

    return false;
  }

  private static bool SegmentsCross(Point2D a, Point2D b, Point2D c, Point2D d)
  {
    double o1 = Cross(b - a, c - a);
    double o2 = Cross(b - a, d - a);
    double o3 = Cross(d - c, a - c);
    double o4 = Cross(d - c, b - c);
    return o1 > 1e-9 && o2 < -1e-9 || o1 < -1e-9 && o2 > 1e-9
        ? o3 > 1e-9 && o4 < -1e-9 || o3 < -1e-9 && o4 > 1e-9
        : false;
  }

  private static double LineDistance(BarRef left, BarRef right)
  {
    double dx = left.PhysicalEnd.X - left.PhysicalStart.X;
    double dy = left.PhysicalEnd.Y - left.PhysicalStart.Y;
    double length = Math.Sqrt(dx * dx + dy * dy);
    if (length < 1e-9)
      return left.PhysicalStart.DistanceTo(right.PhysicalStart);
    return Math.Abs(Cross(new Point2D(dx, dy), right.PhysicalStart - left.PhysicalStart)) / length;
  }

  private static double LongitudinalOverlap(BarRef left, BarRef right)
  {
    var origin = left.PhysicalStart;
    var axis = left.PhysicalEnd - left.PhysicalStart;
    double length = Math.Sqrt(axis.X * axis.X + axis.Y * axis.Y);
    if (length < 1e-9)
      return 0;
    var unit = new Point2D(axis.X / length, axis.Y / length);
    double Project(Point2D point) => (point.X - origin.X) * unit.X + (point.Y - origin.Y) * unit.Y;
    double a0 = Math.Min(Project(left.PhysicalStart), Project(left.PhysicalEnd));
    double a1 = Math.Max(Project(left.PhysicalStart), Project(left.PhysicalEnd));
    double b0 = Math.Min(Project(right.PhysicalStart), Project(right.PhysicalEnd));
    double b1 = Math.Max(Project(right.PhysicalStart), Project(right.PhysicalEnd));
    return Math.Min(a1, b1) - Math.Max(a0, b0);
  }

  private static bool Parallel(Point2D a, Point2D b, Point2D c, Point2D d)
  {
    double cross = Math.Abs(Cross(b - a, d - c));
    double scale = a.DistanceTo(b) * c.DistanceTo(d);
    return scale < 1e-9 || cross <= 1e-6 * scale;
  }

  private static double SegmentDistance(Point2D p1, Point2D q1, Point2D p2, Point2D q2)
  {
    var d1 = q1 - p1;
    var d2 = q2 - p2;
    var r = p1 - p2;
    double a = Dot(d1, d1);
    double e = Dot(d2, d2);
    double f = Dot(d2, r);
    double s;
    double t;
    if (a <= 1e-12 && e <= 1e-12)
      return p1.DistanceTo(p2);
    if (a <= 1e-12)
    {
      s = 0;
      t = Clamp(f / e);
    }
    else
    {
      double c = Dot(d1, r);
      if (e <= 1e-12)
      {
        t = 0;
        s = Clamp(-c / a);
      }
      else
      {
        double b = Dot(d1, d2);
        double denom = a * e - b * b;
        s = denom > 1e-12 ? Clamp((b * f - c * e) / denom) : 0;
        t = (b * s + f) / e;
        if (t < 0)
        {
          t = 0;
          s = Clamp(-c / a);
        }
        else if (t > 1)
        {
          t = 1;
          s = Clamp((b - c) / a);
        }
      }
    }

    var c1 = new Point2D(p1.X + d1.X * s, p1.Y + d1.Y * s);
    var c2 = new Point2D(p2.X + d2.X * t, p2.Y + d2.Y * t);
    return c1.DistanceTo(c2);
  }

  private static double PointSegmentDistance(Point2D point, Point2D start, Point2D end) =>
      SegmentDistance(point, point, start, end);

  private static PlanarRegion Box(BarRef bar, double margin)
  {
    double minX = Math.Min(bar.PhysicalStart.X, bar.PhysicalEnd.X) - margin;
    double maxX = Math.Max(bar.PhysicalStart.X, bar.PhysicalEnd.X) + margin;
    double minY = Math.Min(bar.PhysicalStart.Y, bar.PhysicalEnd.Y) - margin;
    double maxY = Math.Max(bar.PhysicalStart.Y, bar.PhysicalEnd.Y) + margin;
    return new PlanarRegion([new PlanarPolygon(new Polygon(
    [
        new Point2D(minX, minY),
        new Point2D(maxX, minY),
        new Point2D(maxX, maxY),
        new Point2D(minX, maxY)
    ]))]);
  }

  private static bool BoxesOverlap(PlanarRegion left, PlanarRegion right)
  {
    var a = left.Polygons[0].Shell.GetBoundingBox();
    var b = right.Polygons[0].Shell.GetBoundingBox();
    return a.Min.X <= b.Max.X && a.Max.X >= b.Min.X && a.Min.Y <= b.Max.Y && a.Max.Y >= b.Min.Y;
  }

  private static bool SameLayer(BarRef left, BarRef right) => LayerKeyOf(left.Zone) == LayerKeyOf(right.Zone);

  private static bool SameFace(BarRef left, BarRef right) => FaceOf(left.Zone) == FaceOf(right.Zone);

  private static string LayerKeyOf(ReinforcementZone zone) =>
      zone.DesignLayer?.ToString() ?? $"{zone.Layer}:{zone.Direction}";

  private static string FaceOf(ReinforcementZone zone) => zone.DesignLayer switch
  {
    LayerKey.TopX or LayerKey.TopY => "Top",
    LayerKey.BottomX or LayerKey.BottomY => "Bottom",
    _ => zone.Layer.ToString()
  };

  private static bool TryOverlap(BarRef left, BarRef right, out Point2D center, out double length)
  {
    length = LongitudinalOverlap(left, right);
    center = default;
    if (length <= SameLineMm)
      return false;

    var origin = left.PhysicalStart;
    var axis = left.PhysicalEnd - origin;
    double axisLength = Math.Sqrt(axis.X * axis.X + axis.Y * axis.Y);
    if (axisLength < 1e-9)
      return false;
    var unit = new Point2D(axis.X / axisLength, axis.Y / axisLength);
    double Project(Point2D point) => (point.X - origin.X) * unit.X + (point.Y - origin.Y) * unit.Y;
    double a0 = Math.Min(Project(left.PhysicalStart), Project(left.PhysicalEnd));
    double a1 = Math.Max(Project(left.PhysicalStart), Project(left.PhysicalEnd));
    double b0 = Math.Min(Project(right.PhysicalStart), Project(right.PhysicalEnd));
    double b1 = Math.Max(Project(right.PhysicalStart), Project(right.PhysicalEnd));
    double mid = (Math.Max(a0, b0) + Math.Min(a1, b1)) / 2.0;
    center = new Point2D(origin.X + unit.X * mid, origin.Y + unit.Y * mid);
    return true;
  }

  private static bool SpansOverlap(LineCluster left, LineCluster right)
  {
    var origin = left.Bars[0].PhysicalStart;
    var axis = left.Bars[0].PhysicalEnd - origin;
    double axisLength = Math.Sqrt(axis.X * axis.X + axis.Y * axis.Y);
    if (axisLength < 1e-9)
      return false;
    var unit = new Point2D(axis.X / axisLength, axis.Y / axisLength);
    double Project(Point2D point) => (point.X - origin.X) * unit.X + (point.Y - origin.Y) * unit.Y;
    (double Start, double End) Span(LineCluster line)
    {
      double min = double.PositiveInfinity;
      double max = double.NegativeInfinity;
      foreach (var bar in line.Bars)
      {
        double a = Project(bar.PhysicalStart);
        double b = Project(bar.PhysicalEnd);
        min = Math.Min(min, Math.Min(a, b));
        max = Math.Max(max, Math.Max(a, b));
      }

      return (min, max);
    }

    var aSpan = Span(left);
    var bSpan = Span(right);
    return Math.Min(aSpan.End, bSpan.End) - Math.Max(aSpan.Start, bSpan.Start) > SameLineMm;
  }

  private static string IdOf(RebarSegment bar) =>
      string.IsNullOrEmpty(bar.BarId) ? bar.Mark ?? "?" : bar.BarId;

  private static Point2D Mid(Point2D a, Point2D b) => new((a.X + b.X) / 2, (a.Y + b.Y) / 2);

  private static double Dot(Point2D a, Point2D b) => a.X * b.X + a.Y * b.Y;

  private static double Cross(Point2D a, Point2D b) => a.X * b.Y - a.Y * b.X;

  private static double Clamp(double value) => Math.Clamp(value, 0, 1);

  private sealed record LapSample(Point2D Center, double Length, BarRef Left, BarRef Right);

  private sealed record LineCluster(List<BarRef> Bars, List<LapSample> Joints);

  private sealed record BarRef(
      ReinforcementZone Zone,
      RebarSegment Segment,
      string Id,
      Point2D PhysicalStart,
      Point2D PhysicalEnd);
}
