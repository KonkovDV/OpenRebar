using OpenRebar.Domain.Models;
using OpenRebar.Domain.Rules;

namespace OpenRebar.Application.UseCases;

/// <summary>
/// Cuts a bar that is longer than stock into lapped pieces. Joints of one run are staggered
/// so that a section of 1.3 lap lengths holds at most half the bars. When that is impossible
/// the lap uses the full-section factor.
/// </summary>
public static class LapPlanner
{
  public const double JointStepMm = 100;
  public const double SectionLengthFactor = 1.3;

  public static void Apply(
      IReadOnlyList<ReinforcementZone> zones,
      string concreteClass,
      IReadOnlyList<double> stockLengthsMm,
      double kerfMm,
      double jointRatioMax,
      List<LapLink> laps,
      List<string> warnings)
  {
    double maxPiece = stockLengthsMm.Count == 0 ? 0 : stockLengthsMm.Max() - Math.Max(0, kerfMm);
    if (maxPiece <= 0)
      return;

    bool reportedFullSection = false;
    bool reportedUnsplit = false;
    foreach (var zone in zones)
    {
      double factor = zone.Layer == RebarLayer.Top
          ? NormativeProfiles.Sp63_2018.TopBarAnchorageFactor
          : 1.0;
      var planned = new Dictionary<int, LinePlan>();
      foreach (var group in Groups(zone))
      {
        var plan = Plan(zone, group, concreteClass, factor, maxPiece, stockLengthsMm, kerfMm, jointRatioMax);
        if (plan is null)
        {
          if (!reportedUnsplit)
          {
            warnings.Add("A bar longer than stock was left in one piece because the lap does not fit in a stock length.");
            reportedUnsplit = true;
          }

          continue;
        }

        if (plan.FullSection && !reportedFullSection)
        {
          warnings.Add("Lap joints use the full-section factor because they could not be staggered within the profile ratio.");
          reportedFullSection = true;
        }

        for (int i = 0; i < group.Count; i++)
          planned[group[i].Index] = plan.Lines[i];
      }

      var rebuilt = new List<RebarSegment>(zone.Rebars.Count);
      for (int index = 0; index < zone.Rebars.Count; index++)
      {
        var bar = zone.Rebars[index];
        if (!planned.TryGetValue(index, out var line) || line.Joints.Count == 0)
        {
          rebuilt.Add(bar);
          continue;
        }

        AppendPieces(zone, bar, line, rebuilt, laps);
      }

      zone.Rebars = rebuilt;
    }
  }

  private static void AppendPieces(
      ReinforcementZone zone,
      RebarSegment bar,
      LinePlan line,
      List<RebarSegment> rebuilt,
      List<LapLink> laps)
  {
    double length = bar.Start.DistanceTo(bar.End);
    double ux = length > 1e-9 ? (bar.End.X - bar.Start.X) / length : 1;
    double uy = length > 1e-9 ? (bar.End.Y - bar.Start.Y) / length : 0;
    var cuts = Cuts(length, line.Joints, line.Lap);
    int origin = rebuilt.Count;
    for (int i = 0; i < cuts.Count; i++)
    {
      bool first = i == 0;
      bool last = i == cuts.Count - 1;
      rebuilt.Add(bar with
      {
        Start = At(bar.Start, ux, uy, cuts[i].From),
        End = At(bar.Start, ux, uy, cuts[i].To),
        AnchorageLengthStart = first ? bar.AnchorageLengthStart : line.Lap,
        AnchorageLengthEnd = last ? bar.AnchorageLengthEnd : line.Lap,
        EndConditionStart = first ? bar.EndConditionStart : BarEndCondition.Straight,
        EndConditionEnd = last ? bar.EndConditionEnd : BarEndCondition.Straight,
        Mark = null,
        BarId = ""
      });
      if (!last)
      {
        laps.Add(new LapLink(
            zone,
            origin + i,
            origin + i + 1,
            AxisPosition(bar.Start, ux, uy, line.Joints[i]),
            line.Lap,
            line.Alpha));
      }
    }
  }

  private static List<(double From, double To)> Cuts(double length, IReadOnlyList<double> joints, double lap)
  {
    var cuts = new List<(double From, double To)>(joints.Count + 1);
    double from = 0;
    foreach (double center in joints)
    {
      cuts.Add((from, center + lap / 2.0));
      from = center - lap / 2.0;
    }

    cuts.Add((from, length));
    return cuts;
  }

  private static GroupPlan? Plan(
      ReinforcementZone zone,
      List<BarRef> group,
      string concreteClass,
      double topFactor,
      double maxPiece,
      IReadOnlyList<double> stock,
      double kerf,
      double jointRatioMax)
  {
    if (group.All(bar => bar.Segment.Start.DistanceTo(bar.Segment.End) <= maxPiece + 1e-6))
    {
      return new GroupPlan(
          group.Select(_ => new LinePlan([], 0, 0)).ToList(),
          FullSection: false);
    }

    var staggered = Place(
        zone,
        group,
        concreteClass,
        topFactor,
        AnchorageRules.LapSpliceShare.UpTo50,
        NormativeProfiles.Sp63_2018.LapUpTo50Alpha,
        maxPiece,
        stock,
        kerf,
        jointRatioMax,
        stagger: true);
    if (staggered is not null)
      return new GroupPlan(staggered, FullSection: false);

    var full = Place(
        zone,
        group,
        concreteClass,
        topFactor,
        AnchorageRules.LapSpliceShare.Full100,
        NormativeProfiles.Sp63_2018.LapFull100Alpha,
        maxPiece,
        stock,
        kerf,
        jointRatioMax,
        stagger: false);
    return full is null ? null : new GroupPlan(full, FullSection: true);
  }

  private static List<LinePlan>? Place(
      ReinforcementZone zone,
      List<BarRef> group,
      string concreteClass,
      double topFactor,
      AnchorageRules.LapSpliceShare share,
      double alpha,
      double maxPiece,
      IReadOnlyList<double> stock,
      double kerf,
      double jointRatioMax,
      bool stagger)
  {
    var sample = group[0].Segment;
    double lap = AnchorageRules.CalculateLapLength(
        sample.DiameterMm,
        zone.Spec.SteelClass,
        concreteClass,
        share,
        topBarAnchorageFactor: topFactor);
    if (lap > maxPiece + 1e-6)
      return null;

    double window = SectionLengthFactor * lap;
    double limit = Math.Min(0.5, jointRatioMax);
    var placed = new List<LinePlan>(group.Count);
    var centers = new List<List<double>>(group.Count);
    foreach (var bar in group)
    {
      double length = bar.Segment.Start.DistanceTo(bar.Segment.End);
      if (length <= maxPiece + 1e-6)
      {
        placed.Add(new LinePlan([], lap, alpha));
        centers.Add([]);
        continue;
      }

      var joints = Shortest(length, lap, maxPiece, stock, kerf, center =>
          !stagger || Allowed(center, centers, group.Count, window, limit));
      if (joints is null)
        return null;
      placed.Add(new LinePlan(joints, lap, alpha));
      centers.Add(joints);
    }

    return placed;
  }

  private static bool Allowed(
      double center,
      List<List<double>> placed,
      int lineCount,
      double window,
      double limit)
  {
    int occupied = 1;
    foreach (var line in placed)
    {
      if (line.Any(joint => Math.Abs(joint - center) <= window + 1e-6))
        occupied++;
    }

    return occupied / (double)lineCount <= limit + 1e-9;
  }

  private static List<double>? Shortest(
      double length,
      double lap,
      double maxPiece,
      IReadOnlyList<double> stock,
      double kerf,
      Func<double, bool> allowed)
  {
    var nodes = new List<Node> { new(0, false) };
    double first = Math.Ceiling(lap / 2.0 / JointStepMm - 1e-9) * JointStepMm;
    double last = length - lap / 2.0;
    for (double center = first; center <= last + 1e-6; center += JointStepMm)
    {
      if (allowed(center))
        nodes.Add(new Node(center, true));
    }

    nodes.Add(new Node(length, false));
    int count = nodes.Count;
    var dist = new double[count];
    var prev = new int[count];
    Array.Fill(dist, double.PositiveInfinity);
    Array.Fill(prev, -1);
    dist[0] = 0;

    for (int i = 0; i < count - 1; i++)
    {
      if (double.IsPositiveInfinity(dist[i]))
        continue;

      for (int j = i + 1; j < count - 1; j++)
      {
        if (!TryPiece(nodes[i], nodes[j], lap, maxPiece, out double piece))
        {
          if (piece > maxPiece)
            break;
          continue;
        }

        Relax(i, j, piece, stock, kerf, dist, prev);
      }

      if (TryPiece(nodes[i], nodes[count - 1], lap, maxPiece, out double endPiece))
        Relax(i, count - 1, endPiece, stock, kerf, dist, prev);
    }

    if (double.IsPositiveInfinity(dist[count - 1]))
      return null;

    var path = new List<int>();
    for (int cursor = count - 1; cursor >= 0; cursor = prev[cursor])
      path.Add(cursor);
    path.Reverse();
    return path
        .Where(index => nodes[index].Joint)
        .Select(index => nodes[index].Position)
        .ToList();
  }

  private static void Relax(
      int from,
      int to,
      double piece,
      IReadOnlyList<double> stock,
      double kerf,
      double[] dist,
      int[] prev)
  {
    double waste = Waste(piece, stock, kerf);
    if (double.IsPositiveInfinity(waste))
      return;
    double next = dist[from] + waste;
    if (next < dist[to])
    {
      dist[to] = next;
      prev[to] = from;
    }
  }

  private static bool TryPiece(Node from, Node to, double lap, double maxPiece, out double piece)
  {
    if (from.Joint && to.Joint && to.Position - from.Position < lap - 1e-6)
    {
      piece = 0;
      return false;
    }

    double start = from.Joint ? from.Position - lap / 2.0 : from.Position;
    double end = to.Joint ? to.Position + lap / 2.0 : to.Position;
    piece = end - start;
    if (piece < lap - 1e-6 && (from.Joint || to.Joint))
      return false;
    return piece <= maxPiece + 1e-6 && piece > 1e-6;
  }

  private static double Waste(double piece, IReadOnlyList<double> stock, double kerf)
  {
    double best = double.PositiveInfinity;
    foreach (double length in stock)
    {
      if (piece + kerf <= length + 1e-6)
        best = Math.Min(best, length - piece - kerf);
    }

    return best;
  }

  private static List<List<BarRef>> Groups(ReinforcementZone zone)
  {
    var groups = new Dictionary<(bool AlongX, long Start, long End, int Diameter), List<BarRef>>();
    for (int index = 0; index < zone.Rebars.Count; index++)
    {
      var bar = zone.Rebars[index];
      if (bar.Status == BarInstanceStatus.Discarded)
        continue;

      double dx = bar.End.X - bar.Start.X;
      double dy = bar.End.Y - bar.Start.Y;
      bool alongX = Math.Abs(dx) >= Math.Abs(dy);
      double start = alongX ? Math.Min(bar.Start.X, bar.End.X) : Math.Min(bar.Start.Y, bar.End.Y);
      double end = alongX ? Math.Max(bar.Start.X, bar.End.X) : Math.Max(bar.Start.Y, bar.End.Y);
      double station = alongX ? bar.Start.Y : bar.Start.X;
      var key = (alongX, Quantize(start), Quantize(end), bar.DiameterMm);
      if (!groups.TryGetValue(key, out var list))
      {
        list = [];
        groups[key] = list;
      }

      list.Add(new BarRef(index, station, bar));
    }

    return groups.Values
        .Select(list => list.OrderBy(bar => bar.Station).ToList())
        .ToList();
  }

  private static long Quantize(double value) => (long)Math.Round(value * 10.0, MidpointRounding.AwayFromZero);

  private static Point2D At(Point2D origin, double ux, double uy, double distance) =>
      new(origin.X + ux * distance, origin.Y + uy * distance);

  private static double AxisPosition(Point2D origin, double ux, double uy, double distance) =>
      Math.Abs(ux) >= Math.Abs(uy) ? origin.X + ux * distance : origin.Y + uy * distance;

  private readonly record struct Node(double Position, bool Joint);

  private readonly record struct BarRef(int Index, double Station, RebarSegment Segment);

  private readonly record struct LinePlan(IReadOnlyList<double> Joints, double Lap, double Alpha);

  private sealed record GroupPlan(List<LinePlan> Lines, bool FullSection);
}

/// <summary>One lap, addressed by the piece indexes in its zone after the split.</summary>
public sealed record LapLink(
    ReinforcementZone Zone,
    int Left,
    int Right,
    double PositionMm,
    double LengthMm,
    double Alpha);
