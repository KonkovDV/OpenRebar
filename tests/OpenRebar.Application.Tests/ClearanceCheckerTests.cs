using FluentAssertions;
using OpenRebar.Application.UseCases;
using OpenRebar.Domain.Models;
using OpenRebar.Domain.Rules;
using OpenRebar.Infrastructure.Geometry;

namespace OpenRebar.Application.Tests;

public class ClearanceCheckerTests
{
  [Fact]
  public void BarOnTheBoundary_IsInside_AndJustPastTheTolerance_IsOutside()
  {
    var onEdge = Check(Bar("a", 0, 50, 80, 50));
    var atTolerance = Check(Bar("b", -ClearanceChecker.InsideToleranceMm, 50, 80, 50));
    var pastTolerance = Check(Bar("c", -ClearanceChecker.InsideToleranceMm - 0.05, 50, 80, 50));

    onEdge.Should().NotContain(clash => clash.Kind == "barOutsideWorkingArea");
    atTolerance.Should().NotContain(clash => clash.Kind == "barOutsideWorkingArea");
    pastTolerance.Should().ContainSingle(clash => clash.Kind == "barOutsideWorkingArea" && clash.Type == "hard");
  }

  [Fact]
  public void RequiredAnchorage_DoesNotPushTheBarOutside()
  {
    var clashes = Check(Bar("a", 0, 50, 100, 50, anchorage: 20));

    clashes.Should().NotContain(clash => clash.Kind == "barOutsideWorkingArea");
  }

  [Fact]
  public void BarThroughAnOpening_IsAHardClash()
  {
    var slab = Slab();
    slab = new SlabGeometry
    {
      OuterBoundary = slab.OuterBoundary,
      Openings = [Rectangle(40, 40, 60, 60)],
      ThicknessMm = slab.ThicknessMm,
      CoverMm = slab.CoverMm,
      ConcreteClass = slab.ConcreteClass
    };
    var clashes = ClearanceChecker.Check([Zone(Bar("a", 0, 50, 100, 50))], slab);

    clashes.Should().Contain(clash => clash.Kind == "barInOpening" && clash.Type == "hard");
  }

  [Theory]
  [InlineData(37, false)]
  [InlineData(36.9, true)]
  public void SideGap_UsesTheRequiredClearDistance(double spacing, bool clash)
  {
    var clashes = Check(
        Bar("a", 0, 0, 200, 0),
        Bar("b", 0, spacing, 200, spacing));

    clashes.Any(item => item.Kind == "layerClearance").Should().Be(clash);
  }

  [Fact]
  public void CrossingDiametersThatFit_AreNotAClash()
  {
    var clashes = ClearanceChecker.Check(
    [
        Zone([Bar("x", 0, 50, 100, 50, diameter: 20)], LayerKey.BottomX),
        Zone([Bar("y", 50, 0, 50, 100, diameter: 20)], LayerKey.BottomY)
    ], Slab());

    clashes.Should().NotContain(clash => clash.Kind == "crossingStack");
  }

  [Fact]
  public void CrossingDiametersThatDoNotFit_AreHard()
  {
    var slab = new SlabGeometry
    {
      OuterBoundary = Rectangle(0, 0, 1000, 1000),
      ThicknessMm = 50,
      CoverMm = 20,
      ConcreteClass = "B25"
    };
    var clashes = ClearanceChecker.Check(
    [
        Zone([Bar("x", 0, 50, 100, 50, diameter: 20)], LayerKey.BottomX),
        Zone([Bar("y", 50, 0, 50, 100, diameter: 20)], LayerKey.BottomY)
    ], slab);

    clashes.Should().Contain(clash => clash.Kind == "crossingStack" && clash.Type == "hard");
  }

  [Fact]
  public void LapShareAboveTheProfileLimit_IsSoft()
  {
    var clashes = Check(
        Bar("a", 0, 10, 80, 10),
        Bar("b", 40, 10, 120, 10));

    clashes.Should().Contain(clash => clash.Kind == "lapOverload" && clash.Type == "soft");
  }

  [Theory]
  [InlineData(40, true)]
  [InlineData(60, false)]
  public void AdjacentLaps_CloserThanTwoDiameters_AreHard(double lateral, bool clash)
  {
    var clashes = Check(
        Bar("a1", 0, 100, 600, 100),
        Bar("a2", 300, 100, 900, 100),
        Bar("b1", 0, 100 + lateral, 600, 100 + lateral),
        Bar("b2", 300, 100 + lateral, 900, 100 + lateral));

    clashes.Any(item => item.Kind == "lapClearance" && item.Type == "hard").Should().Be(clash);
    clashes.Should().NotContain(item => item.Kind == "layerClearance");
  }

  [Fact]
  public void LapsOnFarApartLines_AreNotALapClearanceClash()
  {
    var clashes = Check(
        Bar("a1", 0, 100, 600, 100),
        Bar("a2", 300, 100, 900, 100),
        Bar("b1", 0, 300, 600, 300),
        Bar("b2", 300, 300, 900, 300));

    clashes.Should().NotContain(item => item.Kind == "lapClearance");
  }

  [Fact]
  public void TopBars_NeedThirtyMillimetreClearDistance()
  {
    var bottom = ClearanceChecker.Check(
        [Zone([Bar("a", 0, 0, 200, 0), Bar("b", 0, 40, 200, 40)], LayerKey.BottomX)],
        Slab());
    var top = ClearanceChecker.Check(
        [Zone([Bar("a", 0, 0, 200, 0), Bar("b", 0, 40, 200, 40)], LayerKey.TopX)],
        Slab());

    bottom.Should().NotContain(item => item.Kind == "layerClearance");
    top.Should().Contain(item => item.Kind == "layerClearance" && item.Type == "hard");
  }

  [Fact]
  public void IndexAndShift_FindTheSameClearanceClash()
  {
    var local = ClearanceChecker.Check(
        [Zone(Bar("a", 0, 0, 200, 0), Bar("b", 0, 30, 200, 30))],
        Slab(),
        new NtsPlanarGeometry());
    var shifted = ClearanceChecker.Check(
        [Zone(Bar("a", 1_000_000, 1_000_000, 1_000_200, 1_000_000), Bar("b", 1_000_000, 1_000_030, 1_000_200, 1_000_030))],
        Slab(1_000_000, 1_000_000),
        new NtsPlanarGeometry());

    local.Should().Contain(clash => clash.Kind == "layerClearance");
    shifted.Select(clash => clash.Kind).Should().Equal(local.Select(clash => clash.Kind));
  }

  [Fact]
  public void CombinedSection_FlagsLapsThatExceedTheHalfShare()
  {
    double lap = AnchorageRules.CalculateLapLength(12, "A500C", "B25", AnchorageRules.LapSpliceShare.UpTo50);
    var clashes = Check(
        Piece("a1", 0, 0, 5000 + lap / 2),
        Piece("a2", 5000 - lap / 2, 0, 10000),
        Piece("plain", 0, 200, 10000),
        Piece("b1", 0, 400, 5000 + lap / 2),
        Piece("b2", 5000 - lap / 2, 400, 9000),
        Piece("c1", 0, 600, 5000 + lap / 2),
        Piece("c2", 5000 - lap / 2, 600, 9000));

    clashes.Should().Contain(clash => clash.Kind == "lapShareExceeded" && clash.Type == "hard");
  }

  [Fact]
  public void CombinedSection_AcceptsLapsOutsideTheWindow()
  {
    double lap = AnchorageRules.CalculateLapLength(12, "A500C", "B25", AnchorageRules.LapSpliceShare.UpTo50);
    double far = 5000 + LapPlanner.SectionLengthFactor * lap + 10;
    var clashes = Check(
        Piece("a1", 0, 0, 5000 + lap / 2),
        Piece("a2", 5000 - lap / 2, 0, 10000),
        Piece("plain", 0, 200, 10000),
        Piece("b1", 0, 400, far + lap / 2),
        Piece("b2", far - lap / 2, 400, 9000),
        Piece("c1", 0, 600, far + lap / 2),
        Piece("c2", far - lap / 2, 600, 9000));

    clashes.Should().NotContain(clash => clash.Kind == "lapShareExceeded");
  }

  [Fact]
  public void CombinedLayer_FlagsAGapWiderThanTheLimit()
  {
    var clashes = ClearanceChecker.Check(
        [Zone([Bar("a", 0, 0, 200, 0), Bar("b", 0, 400, 200, 400)])],
        Slab(),
        new NtsPlanarGeometry());

    clashes.Should().ContainSingle(clash =>
        clash.Kind == "layerSpacing" && clash.Type == "hard"
        && clash.BarIds.Count == 2 && clash.BarIds[0] == "a" && clash.BarIds[1] == "b");
  }

  [Fact]
  public void CombinedLayer_AcceptsAnAdditionalBarBetweenTheMesh()
  {
    var background = Zone([Bar("a", 0, 0, 200, 0), Bar("b", 0, 400, 200, 400)]);
    var additional = Zone([Bar("c", 0, 200, 200, 200)]);
    var clashes = ClearanceChecker.Check([background, additional], Slab(), new NtsPlanarGeometry());

    clashes.Should().NotContain(clash => clash.Kind == "layerSpacing");
  }

  private static IReadOnlyList<ClashReport> Check(params RebarSegment[] bars) =>
      ClearanceChecker.Check([Zone(bars)], Slab());

  private static ReinforcementZone Zone(params RebarSegment[] bars) => Zone(bars, LayerKey.BottomX);

  private static ReinforcementZone Zone(RebarSegment[] bars, LayerKey layer)
  {
    var first = bars[0];
    return new ReinforcementZone
    {
      Id = layer.ToString(),
      Boundary = Rectangle(0, 0, 1000, 1000),
      Spec = new ReinforcementSpec { DiameterMm = first.DiameterMm, SpacingMm = 200, SteelClass = "A500C" },
      Direction = layer is LayerKey.BottomY or LayerKey.TopY ? RebarDirection.Y : RebarDirection.X,
      ZoneType = ZoneType.Simple,
      DesignLayer = layer,
      Rebars = bars
    };
  }

  private static RebarSegment Piece(string id, double x0, double y, double x1) =>
      Bar(id, x0, y, x1, y);

  private static RebarSegment Bar(
      string id,
      double x0,
      double y0,
      double x1,
      double y1,
      double anchorage = 0,
      int diameter = 12) => new()
      {
        Start = new Point2D(x0, y0),
        End = new Point2D(x1, y1),
        DiameterMm = diameter,
        AnchorageLengthStart = anchorage,
        AnchorageLengthEnd = anchorage,
        BarId = id
      };

  private static SlabGeometry Slab(double originX = 0, double originY = 0) => new()
  {
    OuterBoundary = Rectangle(originX, originY, originX + 1000, originY + 1000),
    ThicknessMm = 220,
    CoverMm = 30,
    ConcreteClass = "B25"
  };

  private static Polygon Rectangle(double minX, double minY, double maxX, double maxY) => new(
  [
      new Point2D(minX, minY),
      new Point2D(maxX, minY),
      new Point2D(maxX, maxY),
      new Point2D(minX, maxY)
  ]);
}
