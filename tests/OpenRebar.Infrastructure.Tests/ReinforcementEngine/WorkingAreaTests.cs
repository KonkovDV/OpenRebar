using FluentAssertions;
using OpenRebar.Application.UseCases;
using OpenRebar.Domain.Models;
using OpenRebar.Domain.Rules;
using OpenRebar.Infrastructure.Geometry;
using OpenRebar.Infrastructure.Logging;
using OpenRebar.Infrastructure.ReinforcementEngine;

namespace OpenRebar.Infrastructure.Tests.ReinforcementEngine;

public class WorkingAreaTests
{
  [Fact]
  public void ZeroCover_InsetsTheHookCrownByHalfTheDiameter()
  {
    var calculated = Calculate(edgeCoverMm: 0, openingClearanceMm: 0, opening: null, coverMm: 0);
    double joint = 6 + BendRules.CenterlineRadiusMm(12, "A500C");

    calculated[0].Rebars.Should().OnlyContain(bar =>
        Math.Abs(bar.Start.X - joint) < 0.2
        && Math.Abs(bar.End.X - (2000 - joint)) < 0.2);
  }

  [Fact]
  public void FreeEdge_PutsTheHookCrownOnTheCoverLine()
  {
    var calculated = Calculate(edgeCoverMm: 0, openingClearanceMm: 0, opening: null);
    double joint = 36 + BendRules.CenterlineRadiusMm(12, "A500C");

    calculated[0].Rebars.Should().NotBeEmpty();
    calculated[0].Rebars.Should().OnlyContain(bar =>
        Math.Abs(bar.Start.X - joint) < 0.2
        && Math.Abs(bar.End.X - (2000 - joint)) < 0.2);
  }

  [Fact]
  public void EdgeCover_PullsBarsInsideTheInset()
  {
    var calculated = Calculate(edgeCoverMm: 40, openingClearanceMm: 0, opening: null);
    double joint = 40 + BendRules.CenterlineRadiusMm(12, "A500C");

    calculated[0].Rebars.Should().NotBeEmpty();
    calculated[0].Rebars.Should().OnlyContain(bar =>
        Math.Abs(bar.Start.X - joint) < 0.2
        && Math.Abs(bar.End.X - (2000 - joint)) < 0.2
        && bar.Start.Y >= 39.9 && bar.Start.Y <= 960.1);
  }

  [Fact]
  public void SupportedEnds_ExtendByAnchorage_AndStayInsideTheSupport()
  {
    var edges = new SlabEdge[]
    {
      new() { Segment = "minX", Kind = SlabEdgeKind.Supported, SupportDepthMm = 600 },
      new() { Segment = "maxX", Kind = SlabEdgeKind.Supported, SupportDepthMm = 600 }
    };
    var calculated = Calculate(edgeCoverMm: 0, openingClearanceMm: 0, opening: null, edges);

    calculated[0].Rebars.Should().NotBeEmpty();
    calculated[0].Rebars.Should().OnlyContain(bar =>
        bar.AnchorageLengthStart == 500
        && bar.Start.X >= -500.1 && bar.Start.X <= -499.9
        && bar.End.X >= 2499.9 && bar.End.X <= 2500.1
        && bar.EndConditionStart == BarEndCondition.Straight
        && bar.EndConditionEnd == BarEndCondition.Straight);

    var slab = Slab(edgeCoverMm: 0, openingClearanceMm: 0, opening: null, edges);
    ClearanceChecker.Check(calculated, slab, new NtsPlanarGeometry())
        .Should().NotContain(clash => clash.Kind == "barOutsideWorkingArea");
  }

  [Fact]
  public void FreeEnds_StayOnTheSlab_AndAskForAHook()
  {
    var calculated = Calculate(edgeCoverMm: 0, openingClearanceMm: 0, opening: null);
    double joint = 36 + BendRules.CenterlineRadiusMm(12, "A500C");

    calculated[0].Rebars.Should().OnlyContain(bar =>
        Math.Abs(bar.Start.X - joint) < 0.2
        && Math.Abs(bar.End.X - (2000 - joint)) < 0.2
        && bar.EndConditionStart == BarEndCondition.NeedsHook
        && bar.EndConditionEnd == BarEndCondition.NeedsHook
        && bar.AnchorageLengthStart == 500);
  }

  [Fact]
  public void OpeningClearance_WidensTheGapAroundTheOpening()
  {
    var calculated = Calculate(
        edgeCoverMm: 0,
        openingClearanceMm: 50,
        opening: Rectangle(800, 0, 1200, 1000));

    double reach = BendRules.CenterlineRadiusMm(12, "A500C");
    calculated[0].Rebars.Should().NotBeEmpty();
    calculated[0].Rebars.Should().OnlyContain(bar => bar.End.X <= 750.1 || bar.Start.X >= 1249.9);
    calculated[0].Rebars.Should().Contain(bar => Math.Abs(bar.End.X - (750 - reach)) < 0.2);
    calculated[0].Rebars.Should().Contain(bar => Math.Abs(bar.Start.X - (1250 + reach)) < 0.2);
  }

  private static IReadOnlyList<ReinforcementZone> Calculate(
      double edgeCoverMm,
      double openingClearanceMm,
      Polygon? opening,
      IReadOnlyList<SlabEdge>? edges = null,
      double coverMm = 30)
  {
    var zone = new ReinforcementZone
    {
      Id = "Z",
      Boundary = Rectangle(0, 0, 2000, 1000),
      Spec = new ReinforcementSpec { DiameterMm = 12, SpacingMm = 200, SteelClass = "A500C" },
      Direction = RebarDirection.X,
      ZoneType = ZoneType.Simple
    };
    var slab = Slab(edgeCoverMm, openingClearanceMm, opening, edges, coverMm);

    return new StandardReinforcementCalculator(new ConsoleStructuredLogger(), new NtsPlanarGeometry())
        .CalculateRebars([zone], slab);
  }

  private static SlabGeometry Slab(
      double edgeCoverMm,
      double openingClearanceMm,
      Polygon? opening,
      IReadOnlyList<SlabEdge>? edges,
      double coverMm = 30) => new()
      {
        OuterBoundary = Rectangle(0, 0, 2000, 1000),
        Openings = opening is null ? [] : [opening],
        ThicknessMm = 220,
        CoverMm = coverMm,
        EdgeCoverMm = edgeCoverMm,
        OpeningClearanceMm = openingClearanceMm,
        ConcreteClass = "B25",
        Edges = edges ?? []
      };

  private static Polygon Rectangle(double minX, double minY, double maxX, double maxY) => new(
  [
      new Point2D(minX, minY),
      new Point2D(maxX, minY),
      new Point2D(maxX, maxY),
      new Point2D(minX, maxY)
  ]);
}
