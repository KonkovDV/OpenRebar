using OpenRebar.Domain.Models;

namespace OpenRebar.Infrastructure.ReinforcementEngine;

/// <summary>
/// Extends a run by the required anchorage, stopping at the working-area interval that contains it.
/// </summary>
public static class AnchorageExtender
{
  public const double FitToleranceMm = 0.1;

  public static (double Start, double End, double AchievedStart, double AchievedEnd) Extend(
      double runStart,
      double runEnd,
      double requiredMm,
      IReadOnlyList<(double Start, double End)> allowed)
  {
    double mid = (runStart + runEnd) / 2.0;
    (double Start, double End) host = default;
    bool found = false;
    foreach (var interval in allowed)
    {
      if (interval.Start - 1e-6 <= mid && mid <= interval.End + 1e-6)
      {
        host = interval;
        found = true;
        break;
      }
    }

    if (!found)
      return (runStart, runEnd, 0, 0);

    double roomStart = Math.Max(0, runStart - host.Start);
    double roomEnd = Math.Max(0, host.End - runEnd);
    double required = Math.Max(0, requiredMm);
    double achievedStart = Math.Min(required, roomStart);
    double achievedEnd = Math.Min(required, roomEnd);
    return (runStart - achievedStart, runEnd + achievedEnd, achievedStart, achievedEnd);
  }

  public static BarEndCondition Condition(double achievedMm, double requiredMm, string requested)
  {
    if (achievedMm + FitToleranceMm >= Math.Max(0, requiredMm))
      return BarEndCondition.Straight;

    return requested switch
    {
      "Straight" => BarEndCondition.ShortenedAtEdge,
      "LBar" => BarEndCondition.NeedsLBar,
      "UBar" => BarEndCondition.NeedsUBar,
      _ => BarEndCondition.NeedsHook
    };
  }
}
