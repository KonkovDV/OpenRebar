using OpenRebar.Domain.Models;

namespace OpenRebar.Domain.Rules;

/// <summary>
/// Centerline of a bar in millimetres. A hook is the semicircle already counted in
/// <see cref="RebarSegment.BendArcMm"/>. A straight tail is added only when one is set.
/// </summary>
public static class HookCenterline
{
  public readonly record struct Point(double X, double Y, double Z);

  /// <summary>A line, or an arc when <see cref="Through"/> is set.</summary>
  public readonly record struct Span(Point Start, Point End, Point? Through);

  public static bool TryBuild(
      RebarSegment segment,
      double elevationMm,
      double tailMm,
      bool bendUp,
      out IReadOnlyList<Span> spans,
      out string? skip)
  {
    spans = [];
    skip = null;
    if (tailMm < 0)
      throw new ArgumentOutOfRangeException(nameof(tailMm), tailMm, "Hook tail cannot be negative.");

    if (segment.Shape is BarShape.L or BarShape.U)
    {
      skip = "L and U bars are not drawn. They stay in the schedule and IFC.";
      return false;
    }

    var start = new Point(segment.Start.X, segment.Start.Y, elevationMm);
    var end = new Point(segment.End.X, segment.End.Y, elevationMm);
    double dx = end.X - start.X;
    double dy = end.Y - start.Y;
    double length = Math.Sqrt(dx * dx + dy * dy);
    if (length < 1e-6)
    {
      skip = "Bar length is zero.";
      return false;
    }

    if (segment.Shape == BarShape.Straight)
    {
      if (segment.BendArcMm > 1e-6)
      {
        skip = "A straight bar has a bend arc.";
        return false;
      }

      spans = [new Span(start, end, null)];
      return true;
    }

    double radius = segment.BendRadiusMm + segment.DiameterMm / 2.0;
    if (radius <= 1e-6)
    {
      skip = "Hook radius is missing.";
      return false;
    }

    double ux = dx / length;
    double uy = dy / length;
    double hz = bendUp ? 1.0 : -1.0;
    var curves = new List<Span>();
    if (segment.EndConditionStart == BarEndCondition.NeedsHook)
      curves.AddRange(Hook(start, ux, uy, hz, radius, tailMm, atStart: true));
    curves.Add(new Span(start, end, null));
    if (segment.EndConditionEnd == BarEndCondition.NeedsHook)
      curves.AddRange(Hook(end, ux, uy, hz, radius, tailMm, atStart: false));

    if (curves.Count == 1)
    {
      skip = "A hooked bar has no hook end.";
      return false;
    }

    spans = curves;
    return true;
  }

  private static IEnumerable<Span> Hook(
      Point joint,
      double forwardX,
      double forwardY,
      double hz,
      double radius,
      double tailMm,
      bool atStart)
  {
    var rise = new Point(0, 0, hz);
    var along = new Point(forwardX, forwardY, 0);
    var free = Add(joint, Scale(rise, 2.0 * radius));
    double crown = atStart ? -1.0 : 1.0;
    var mid = Add(joint, Add(Scale(along, crown * radius), Scale(rise, radius)));
    if (tailMm > 1e-6)
    {
      var tip = Add(free, Scale(along, atStart ? tailMm : -tailMm));
      yield return atStart
          ? new Span(tip, free, null)
          : new Span(free, tip, null);
    }

    yield return atStart
        ? new Span(free, joint, mid)
        : new Span(joint, free, mid);
  }

  private static Point Add(Point left, Point right) =>
      new(left.X + right.X, left.Y + right.Y, left.Z + right.Z);

  private static Point Scale(Point point, double factor) =>
      new(point.X * factor, point.Y * factor, point.Z * factor);
}
