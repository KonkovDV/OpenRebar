using OpenRebar.Domain.Models;

namespace OpenRebar.Infrastructure.ReinforcementEngine;

/// <summary>
/// Groups bar lines into runs. Adjacent lines join when their intervals differ by at most the snap
/// tolerance, and the run length grows to the outer end so a bar is never shortened.
/// </summary>
public static class BarRunBuilder
{
  public const double DefaultSnapToleranceMm = 50;
  public const double LengthStepMm = 10;

  public static IReadOnlyList<BarRun> Build(
      ReinforcementZone zone,
      SlabGeometry slab,
      Polygon? boundary = null,
      IReadOnlyList<Polygon>? holes = null,
      double snapToleranceMm = DefaultSnapToleranceMm)
  {
    boundary ??= zone.Boundary;
    holes ??= zone.Holes;
    bool oblique = IsOblique(boundary);
    double snap = oblique ? 0.1 : snapToleranceMm;
    var box = boundary.GetBoundingBox();
    bool alongX = zone.Direction == RebarDirection.X;
    double min = alongX ? box.Min.Y : box.Min.X;
    double max = alongX ? box.Max.Y : box.Max.X;
    var stations = Stations(zone, min, max, zone.EffectiveSpec.SpacingMm).ToList();

    var open = new List<OpenRun>();
    var finished = new List<OpenRun>();
    foreach (double station in stations)
    {
      var intervals = alongX
          ? Cut(boundary, holes, slab.Openings, station, horizontal: true)
          : Cut(boundary, holes, slab.Openings, station, horizontal: false);
      if (oblique)
        intervals = intervals.Select(interval => QuantizeOutward(interval, LengthStepMm)).ToList();
      if (intervals.Count == 0 || !Compatible(open, intervals, snap))
      {
        finished.AddRange(open);
        open = intervals.Select(interval => new OpenRun(interval.Start, interval.End, station)).ToList();
        continue;
      }

      for (int i = 0; i < open.Count; i++)
        open[i].Extend(intervals[i].Start, intervals[i].End, station);
    }

    finished.AddRange(open);
    return finished.Select(run => run.ToBarRun(zone)).ToList();
  }

  private static bool IsOblique(Polygon polygon)
  {
    var vertices = polygon.Vertices;
    for (int i = 0; i < vertices.Count; i++)
    {
      var a = vertices[i];
      var b = vertices[(i + 1) % vertices.Count];
      if (Math.Abs(a.X - b.X) > 1e-6 && Math.Abs(a.Y - b.Y) > 1e-6)
        return true;
    }

    return false;
  }

  private static (double Start, double End) QuantizeOutward((double Start, double End) interval, double step)
  {
    double start = Math.Floor(interval.Start / step) * step;
    double end = Math.Ceiling(interval.End / step - 1e-9) * step;
    if (end <= start)
      end = start + step;
    return (start, end);
  }

  private static bool Compatible(
      List<OpenRun> open,
      IReadOnlyList<(double Start, double End)> intervals,
      double snapToleranceMm)
  {
    if (open.Count == 0 || open.Count != intervals.Count)
      return false;

    for (int i = 0; i < open.Count; i++)
    {
      if (Math.Abs(open[i].RawStart - intervals[i].Start) > snapToleranceMm)
        return false;
      if (Math.Abs(open[i].RawEnd - intervals[i].End) > snapToleranceMm)
        return false;
    }

    return true;
  }

  private static List<(double Start, double End)> Cut(
      Polygon boundary,
      IReadOnlyList<Polygon> holes,
      IReadOnlyList<Polygon> openings,
      double station,
      bool horizontal)
  {
    var intervals = Intervals(boundary, station, horizontal);
    var cuts = holes.Concat(openings).Select(polygon => Intervals(polygon, station, horizontal));
    return Subtract(intervals, cuts);
  }

  /// <summary>Spans of the line that lie inside the shells and outside the holes.</summary>
  public static List<(double Start, double End)> IntervalsOnLine(
      IReadOnlyList<Polygon> shells,
      IReadOnlyList<Polygon> holes,
      double station,
      bool horizontal)
  {
    var covered = new List<(double Start, double End)>();
    foreach (var shell in shells)
      covered.AddRange(Intervals(shell, station, horizontal));
    covered = Merge(covered);
    return Subtract(covered, holes.Select(hole => Intervals(hole, station, horizontal)));
  }

  private static List<(double Start, double End)> Merge(List<(double Start, double End)> intervals)
  {
    if (intervals.Count == 0)
      return intervals;

    var ordered = intervals.OrderBy(interval => interval.Start).ToList();
    var merged = new List<(double Start, double End)> { ordered[0] };
    for (int i = 1; i < ordered.Count; i++)
    {
      var last = merged[^1];
      if (ordered[i].Start <= last.End + 1e-6)
        merged[^1] = (last.Start, Math.Max(last.End, ordered[i].End));
      else
        merged.Add(ordered[i]);
    }

    return merged;
  }

  private static List<(double Start, double End)> Intervals(Polygon polygon, double station, bool horizontal)
  {
    var hits = new List<double>();
    var vertices = polygon.Vertices;
    for (int i = 0; i < vertices.Count; i++)
    {
      var a = vertices[i];
      var b = vertices[(i + 1) % vertices.Count];
      double aCross = horizontal ? a.Y : a.X;
      double bCross = horizontal ? b.Y : b.X;
      if ((aCross <= station && bCross > station) || (bCross <= station && aCross > station))
      {
        double t = (station - aCross) / (bCross - aCross);
        double along = horizontal ? a.X : a.Y;
        double alongEnd = horizontal ? b.X : b.Y;
        hits.Add(along + t * (alongEnd - along));
      }
    }

    hits.Sort();
    var intervals = new List<(double Start, double End)>();
    for (int i = 0; i + 1 < hits.Count; i += 2)
      intervals.Add((hits[i], hits[i + 1]));
    return intervals;
  }

  private static List<(double Start, double End)> Subtract(
      IReadOnlyList<(double Start, double End)> baseIntervals,
      IEnumerable<IReadOnlyList<(double Start, double End)>> openingIntervalSets)
  {
    var current = baseIntervals.ToList();
    foreach (var openingIntervals in openingIntervalSets)
    {
      foreach (var opening in openingIntervals)
      {
        var next = new List<(double Start, double End)>();
        foreach (var interval in current)
        {
          if (opening.End <= interval.Start || opening.Start >= interval.End)
          {
            next.Add(interval);
            continue;
          }

          if (opening.Start > interval.Start)
            next.Add((interval.Start, Math.Min(opening.Start, interval.End)));
          if (opening.End < interval.End)
            next.Add((Math.Max(opening.End, interval.Start), interval.End));
        }

        current = next;
      }
    }

    return current;
  }

  private static IEnumerable<double> Stations(ReinforcementZone zone, double min, double max, double spacing)
  {
    if (zone.GridMode == LayerGridMode.Mesh && zone.GridOriginMm is double meshOrigin)
      return MeshStations(meshOrigin, spacing, min, max);

    if (zone.BackgroundSpacingMm is int backgroundSpacing
        && backgroundSpacing > 0
        && zone.GridOriginMm is double origin)
      return LayerGridStations(origin, backgroundSpacing, min, max);

    return CoveringStations(min, max, spacing);
  }

  /// <summary>origin + k·spacing. A line on the boundary is moved 0.1 mm inward so the scan hits.</summary>
  private static IEnumerable<double> MeshStations(double origin, double spacing, double min, double max)
  {
    if (max - min <= 1e-6 || spacing <= 0)
      yield break;

    for (double index = Math.Ceiling((min - origin) / spacing - 1e-9); ; index++)
    {
      double position = origin + index * spacing;
      if (position > max + 1e-6)
        yield break;
      if (position < min - 1e-6)
        continue;
      if (position <= min + 1e-6)
        position = Math.Min(max - 0.1, min + 0.1);
      else if (position >= max - 1e-6)
        position = Math.Max(min + 0.1, max - 0.1);
      if (position > min + 1e-6 && position < max - 1e-6)
        yield return position;
    }
  }

  private static IEnumerable<double> LayerGridStations(double origin, int spacing, double min, double max)
  {
    if (max - min <= 1e-6)
      yield break;

    double first = Math.Ceiling((min - origin) / spacing - 0.5 - 1e-9);
    for (double index = first; ; index++)
    {
      double position = origin + (index + 0.5) * spacing;
      if (position >= max - 1e-6)
        yield break;
      if (position > min + 1e-6)
        yield return position;
    }
  }

  private static IEnumerable<double> CoveringStations(double min, double max, double spacing)
  {
    if (max - min <= 1e-6 || spacing <= 0)
      yield break;

    double half = spacing / 2.0;
    if (max - min <= spacing + 1e-6)
    {
      yield return (min + max) / 2.0;
      yield break;
    }

    double last = min;
    for (double position = min + half; position < max - 1e-6; position += spacing)
    {
      yield return position;
      last = position;
    }

    if (last + half < max - 1e-6)
      yield return max - half;
  }

  private sealed class OpenRun
  {
    public OpenRun(double start, double end, double line)
    {
      Start = RawStart = start;
      End = RawEnd = end;
      Lines.Add(line);
    }

    public double Start { get; private set; }
    public double End { get; private set; }
    public double RawStart { get; private set; }
    public double RawEnd { get; private set; }
    public List<double> Lines { get; } = [];

    public void Extend(double start, double end, double line)
    {
      Start = Math.Min(Start, start);
      End = Math.Max(End, end);
      RawStart = start;
      RawEnd = end;
      Lines.Add(line);
    }

    public BarRun ToBarRun(ReinforcementZone zone) => new()
    {
      Layer = zone.DesignLayer,
      Axis = zone.Direction,
      StartCoord = Start,
      EndCoord = End,
      FirstLine = Lines[0],
      LastLine = Lines[^1],
      Count = Lines.Count,
      Spec = zone.Spec,
      Lines = Lines
    };
  }
}
