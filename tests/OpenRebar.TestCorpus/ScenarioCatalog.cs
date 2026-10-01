namespace OpenRebar.TestCorpus;

/// <summary>
/// A plan point in millimetres, rounded to 0.001 mm so DXF and JSON share one value.
/// </summary>
internal readonly record struct Mm(double X, double Y);

/// <summary>
/// One legend class. RGB and ACI match <c>JsonLegendLoader.GetDefaultLegend</c>,
/// so the current CLI can read the corpus without a legend file.
/// Class 4 (orange, Ø16) has no exact ACI entry and is not used by v0 scenarios.
/// </summary>
internal sealed record LegendClass(
    int Index,
    byte R,
    byte G,
    byte B,
    short? Aci,
    int DiameterMm,
    int SpacingMm)
{
  public double AreaPerMeterMm2 =>
      Math.PI * DiameterMm * DiameterMm / 4.0 * (1000.0 / SpacingMm);
}

internal static class LegendPalette
{
  /// <summary>Steel class of the built-in CLI legend. Fixture data, not a product rule.</summary>
  public const string SteelClass = "A500C";

  public static readonly LegendClass[] Classes =
  [
      new(0, 0, 0, 255, 5, 8, 200),
      new(1, 0, 255, 255, 4, 10, 200),
      new(2, 0, 255, 0, 3, 12, 200),
      new(3, 255, 255, 0, 2, 14, 200),
      new(4, 255, 165, 0, null, 16, 150),
      new(5, 255, 0, 0, 1, 20, 150),
      new(6, 255, 0, 255, 6, 25, 150)
  ];

  public static LegendClass Get(int index)
  {
    if (index < 0 || index >= Classes.Length)
      throw new ArgumentOutOfRangeException(nameof(index), index, "Unknown legend class.");

    return Classes[index];
  }
}

internal sealed record ZoneDefinition(int ClassIndex, Mm[] Outer, Mm[][] Holes);

/// <summary>
/// v0 scenarios are explicit geometry. <see cref="Seed"/> is part of the identity
/// and is reserved for future seeded perturbations; the generator does not draw random numbers.
/// Every outline sits in the first quadrant with its minimum corner at the origin,
/// because the current CLI slab rectangle starts at (0, 0).
/// </summary>
internal sealed record ScenarioDefinition(
    string Id,
    int Seed,
    string Description,
    Mm[] SlabOuter,
    Mm[][] Openings,
    ZoneDefinition[] Zones,
    double? NotableEdgeAngleDeg,
    double ThicknessMm,
    double CoverMm);

internal static class ScenarioCatalog
{
  public const double ThicknessMm = 200;
  public const double CoverMm = 25;

  public static IReadOnlyList<ScenarioDefinition> All { get; } = Build();

  private static ScenarioDefinition[] Build()
  {
    var rect = Rect(0, 0, 6000, 4000);
    var hole = Hole(2000, 1500, 4000, 2500);
    var shaft = Hole(3200, 2200, 4700, 3700);
    double run = Math.Round(4000.0 * Math.Sqrt(3.0), 3, MidpointRounding.AwayFromZero);
    double skewAngleDeg = Math.Atan2(4000.0, run) * 180.0 / Math.PI;
    var skew = new[]
    {
        P(0, 0),
        P(6000, 0),
        P(6000 + run, 4000),
        P(run, 4000)
    };

    return
    [
        S("rect-6x4", 1001,
            "Single rectangular zone, 6000×4000 mm, Ø12@200.",
            rect,
            [Zone(2, rect)]),
        S("l-zone", 1002,
            "L-shaped zone. Its area is well below the bounding box, so a rectangular shortcut does not apply.",
            [
                P(0, 0), P(6000, 0), P(6000, 2000),
                P(3000, 2000), P(3000, 4000), P(0, 4000)
            ],
            [Zone(3, [
                P(0, 0), P(6000, 0), P(6000, 2000),
                P(3000, 2000), P(3000, 4000), P(0, 4000)
            ])]),
        S("zone-with-hole", 1003,
            "Rectangular zone with an interior hole. The hole is part of the ground truth.",
            rect,
            [Zone(2, rect, hole)],
            [hole]),
        S("two-adjacent", 1004,
            "Two zones sharing the edge x = 3000 mm, Ø10@200 and Ø20@150.",
            rect,
            [
                Zone(1, Rect(0, 0, 3000, 4000)),
                Zone(5, Rect(3000, 0, 6000, 4000))
            ]),
        S("narrow-strip", 1005,
            "Vertical strip 400×4000 mm on layer BottomX, Ø20. Bars run along X, so the clear span is 400 mm, below the anchorage length. A correct layout must still emit them.",
            Rect(0, 0, 400, 4000),
            [Zone(5, Rect(0, 0, 400, 4000))]),
        S("span-14m", 1006,
            "Span 14000×3000 mm in Ø20. A bar along the long side is longer than a stock bar, so the layout needs a lap.",
            Rect(0, 0, 14000, 3000),
            [Zone(5, Rect(0, 0, 14000, 3000))]),
        S("shaft-opening", 1007,
            "Slab 8000×6000 mm with a 1500×1500 mm shaft opening.",
            Rect(0, 0, 8000, 6000),
            [Zone(1, Rect(0, 0, 8000, 6000), shaft)],
            [shaft]),
        S("skew-30", 1008,
            "Parallelogram whose non-horizontal edges meet the x axis at 30°.",
            skew,
            [Zone(2, skew)],
            openings: null,
            angle: skewAngleDeg)
    ];
  }

  private static ScenarioDefinition S(
      string id,
      int seed,
      string description,
      Mm[] slab,
      ZoneDefinition[] zones,
      Mm[][]? openings = null,
      double? angle = null) =>
      new(id, seed, description, slab, openings ?? [], zones, angle, ThicknessMm, CoverMm);

  private static ZoneDefinition Zone(int classIndex, Mm[] outer, params Mm[][] holes) =>
      new(classIndex, outer, holes);

  private static Mm P(double x, double y) => new(
      Math.Round(x, 3, MidpointRounding.AwayFromZero),
      Math.Round(y, 3, MidpointRounding.AwayFromZero));

  private static Mm[] Rect(double x0, double y0, double x1, double y1) =>
      [P(x0, y0), P(x1, y0), P(x1, y1), P(x0, y1)];

  /// <summary>Clockwise ring, so a hole is wound opposite to the outer contour.</summary>
  private static Mm[] Hole(double x0, double y0, double x1, double y1) =>
      [P(x0, y0), P(x0, y1), P(x1, y1), P(x1, y0)];
}
