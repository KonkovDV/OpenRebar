using FluentAssertions;
using OpenRebar.Application.UseCases;
using OpenRebar.Domain.Models;
using OpenRebar.Domain.Rules;

namespace OpenRebar.Application.Tests;

public class LapPlannerTests
{
  private static readonly double[] Stock = [6000, 9000, 11700, 12000];
  private const double Kerf = 3;

  [Fact]
  public void BarThatFitsStock_IsNotCut()
  {
    var zone = Zone(Bar(0, 10000));
    var laps = new List<LapLink>();
    var warnings = new List<string>();

    LapPlanner.Apply([zone], "B25", Stock, Kerf, 0.5, laps, warnings);

    zone.Rebars.Should().ContainSingle();
    zone.Rebars[0].TotalLength.Should().BeApproximately(10000, 0.1);
    laps.Should().BeEmpty();
    warnings.Should().BeEmpty();
  }

  [Fact]
  public void HookedEnds_KeepTheirArcOnTheEndPiecesOnly()
  {
    var bar = Bar(0, 20000);
    bar = bar with
    {
      EndConditionStart = BarEndCondition.NeedsHook,
      EndConditionEnd = BarEndCondition.NeedsHook,
      Shape = BarShape.Hooked,
      ShapeCode = "H",
      BendArcMm = BendRules.Describe(12, "A500C", BarEndCondition.NeedsHook, BarEndCondition.NeedsHook).ArcMm
    };
    var zone = Zone(bar);
    var laps = new List<LapLink>();
    var warnings = new List<string>();

    LapPlanner.Apply([zone], "B25", Stock, Kerf, 0.5, laps, warnings);

    double oneHook = BendRules.Describe(12, "A500C", BarEndCondition.NeedsHook, BarEndCondition.Straight).ArcMm;
    zone.Rebars.First().BendArcMm.Should().BeApproximately(oneHook, 1e-6);
    zone.Rebars.Last().BendArcMm.Should().BeApproximately(oneHook, 1e-6);
    zone.Rebars.Skip(1).SkipLast(1).Should().OnlyContain(piece => piece.BendArcMm == 0 && piece.ShapeCode == "00");
    zone.Rebars.Should().OnlyContain(piece => piece.TotalLength + Kerf <= Stock.Max() + 0.1);
    warnings.Should().Contain(warning => warning.Contains("full-section", StringComparison.Ordinal));
  }

  [Fact]
  public void OneLongBar_UsesTheFullSectionFactorAndFitsStock()
  {
    var zone = Zone(Bar(0, 20000));
    var laps = new List<LapLink>();
    var warnings = new List<string>();

    LapPlanner.Apply([zone], "B25", Stock, Kerf, 0.5, laps, warnings);

    double lap = AnchorageRules.CalculateLapLength(12, "A500C", "B25", AnchorageRules.LapSpliceShare.Full100);
    zone.Rebars.Should().HaveCountGreaterThan(1);
    zone.Rebars.Should().OnlyContain(bar => bar.TotalLength + Kerf <= Stock.Max() + 0.1);
    laps.Should().NotBeEmpty();
    laps.Should().OnlyContain(link =>
        Math.Abs(link.LengthMm - lap) < 0.1 &&
        Math.Abs(link.Alpha - NormativeProfiles.Sp63_2018.LapFull100Alpha) < 1e-9);
    warnings.Should().Contain(warning => warning.Contains("full-section", StringComparison.Ordinal));
    Overlaps(zone.Rebars).Should().OnlyContain(overlap => Math.Abs(overlap - lap) < 0.1);
  }

  [Fact]
  public void TwoLongBars_StaggerJointsInsideTheSectionWindow()
  {
    var zone = Zone(Bar(0, 20000), Bar(200, 20000));
    var laps = new List<LapLink>();
    var warnings = new List<string>();

    LapPlanner.Apply([zone], "B25", Stock, Kerf, 0.5, laps, warnings);

    double lap = AnchorageRules.CalculateLapLength(12, "A500C", "B25", AnchorageRules.LapSpliceShare.UpTo50);
    warnings.Should().BeEmpty();
    laps.Should().NotBeEmpty();
    laps.Should().OnlyContain(link => Math.Abs(link.Alpha - NormativeProfiles.Sp63_2018.LapUpTo50Alpha) < 1e-9);
    zone.Rebars.Should().OnlyContain(bar => bar.TotalLength + Kerf <= Stock.Max() + 0.1);

    var onFirstLine = laps.Where(link => zone.Rebars[link.Left].Start.Y < 100).Select(link => link.PositionMm).ToList();
    var onSecondLine = laps.Where(link => zone.Rebars[link.Left].Start.Y > 100).Select(link => link.PositionMm).ToList();
    onFirstLine.Should().NotBeEmpty();
    onSecondLine.Should().NotBeEmpty();
    foreach (double left in onFirstLine)
    {
      foreach (double right in onSecondLine)
        Math.Abs(left - right).Should().BeGreaterThan(LapPlanner.SectionLengthFactor * lap);
    }
  }

  private static IEnumerable<double> Overlaps(IReadOnlyList<RebarSegment> bars)
  {
    for (int i = 0; i < bars.Count - 1; i++)
    {
      double start = Math.Max(bars[i].Start.X, bars[i + 1].Start.X);
      double end = Math.Min(bars[i].End.X, bars[i + 1].End.X);
      yield return end - start;
    }
  }

  private static ReinforcementZone Zone(params RebarSegment[] bars) => new()
  {
    Id = "Z",
    Boundary = new Polygon(
    [
        new Point2D(0, 0),
        new Point2D(20000, 0),
        new Point2D(20000, 1000),
        new Point2D(0, 1000)
    ]),
    Spec = new ReinforcementSpec { DiameterMm = 12, SpacingMm = 200, SteelClass = "A500C" },
    Direction = RebarDirection.X,
    ZoneType = ZoneType.Simple,
    Rebars = bars
  };

  private static RebarSegment Bar(double y, double length) => new()
  {
    Start = new Point2D(0, y),
    End = new Point2D(length, y),
    DiameterMm = 12,
    AnchorageLengthStart = 200,
    AnchorageLengthEnd = 200
  };
}
