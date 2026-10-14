namespace OpenRebar.Application.UseCases;

/// <summary>
/// Share of bars spliced in one section. The section is 1.3 lap lengths.
/// The denominator is every bar of the layer whose axis crosses that window.
/// </summary>
public static class LapSectionShare
{
  public readonly record struct Bar(double Start, double End, IReadOnlyList<double> Joints);

  public static double Ratio(IReadOnlyList<Bar> bars, double jointAt, double lapLength)
  {
    double window = LapPlanner.SectionLengthFactor * lapLength;
    int crossing = 0;
    int occupied = 0;
    double windowStart = jointAt - window;
    double windowEnd = jointAt + window;
    foreach (var bar in bars)
    {
      if (!Crosses(bar.Start, bar.End, windowStart, windowEnd))
        continue;

      crossing++;
      if (bar.Joints.Any(joint => Math.Abs(joint - jointAt) <= window + 1e-6))
        occupied++;
    }

    return crossing == 0 ? 0 : occupied / (double)crossing;
  }

  private static bool Crosses(double start, double end, double windowStart, double windowEnd)
  {
    double lo = Math.Min(start, end);
    double hi = Math.Max(start, end);
    return hi >= windowStart - 1e-6 && lo <= windowEnd + 1e-6;
  }
}
