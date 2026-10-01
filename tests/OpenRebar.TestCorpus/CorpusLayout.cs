namespace OpenRebar.TestCorpus;

/// <summary>
/// Shared frame for the PNG and the ground truth.
/// World millimetres map to pixels by
/// <c>px = originX + x·pxPerMm</c>, <c>py = originY − y·pxPerMm</c>.
/// </summary>
internal sealed record CorpusLayout(
    int Width,
    int Height,
    double OriginX,
    double OriginY,
    double MinX,
    double MinY,
    double MaxX,
    double MaxY,
    int[] UsedClassIndexes,
    CorpusLayout.Swatch[] Swatches)
{
  public const double PxPerMm = 0.1;
  public const int Margin = 24;
  public const int LegendWidth = 160;
  public const int Supersample = 4;

  internal readonly record struct Swatch(int ClassIndex, int X, int Y, int Width, int Height);

  public static CorpusLayout Create(ScenarioDefinition scenario)
  {
    double minX = scenario.SlabOuter.Min(p => p.X);
    double minY = scenario.SlabOuter.Min(p => p.Y);
    double maxX = scenario.SlabOuter.Max(p => p.X);
    double maxY = scenario.SlabOuter.Max(p => p.Y);

    if (minX != 0 || minY != 0)
    {
      throw new InvalidOperationException(
          $"Scenario '{scenario.Id}' must start at the origin so it fits the CLI slab rectangle.");
    }

    int contentWidth = (int)Math.Ceiling((maxX - minX) * PxPerMm);
    int contentHeight = (int)Math.Ceiling((maxY - minY) * PxPerMm);

    var used = new List<int>();
    foreach (var zone in scenario.Zones)
    {
      if (!used.Contains(zone.ClassIndex))
        used.Add(zone.ClassIndex);
    }

    int swatchBlock = 16 + used.Count * 44;
    int width = Margin + contentWidth + Margin + LegendWidth;
    int height = Math.Max(Margin + contentHeight + Margin, swatchBlock + 8);
    double originX = Margin - minX * PxPerMm;
    double originY = (height - Margin) + minY * PxPerMm;

    int panelX = width - LegendWidth;
    var swatches = new Swatch[used.Count];
    for (int i = 0; i < used.Count; i++)
      swatches[i] = new Swatch(used[i], panelX + 13, 17 + i * 44, 28, 28);

    return new CorpusLayout(
        width,
        height,
        originX,
        originY,
        minX,
        minY,
        maxX,
        maxY,
        used.ToArray(),
        swatches);
  }
}
