using FluentAssertions;
using OpenRebar.Application.UseCases;
using OpenRebar.Domain.Models;
using OpenRebar.Infrastructure.Geometry;
using OpenRebar.Infrastructure.Logging;
using OpenRebar.Infrastructure.ReinforcementEngine;

namespace OpenRebar.Application.Tests;

public class LayerClassOverlayTests
{
  [Fact]
  public void OverlappingClasses_SplitIntoThreeFaces_AndReinforceOnce()
  {
    var lighter = Zone("A", 0, 200, diameterMm: 12);
    var heavier = Zone("B", 100, 300, diameterMm: 16);

    var overlay = LayerClassOverlay.Apply([lighter, heavier], new NtsPlanarGeometry());

    overlay.Warnings.Should().ContainSingle().Which.Should().StartWith(LayerClassOverlay.OverlapWarning);
    overlay.Zones.Should().HaveCount(3);

    var onlyA = overlay.Zones.Should().ContainSingle(zone => zone.Id == "A").Subject;
    var onlyB = overlay.Zones.Should().ContainSingle(zone => zone.Id == "B").Subject;
    var both = overlay.Zones.Should().ContainSingle(zone => zone.Id == "A+B").Subject;
    onlyA.SourceIds.Should().Equal("A");
    onlyB.SourceIds.Should().Equal("B");
    both.SourceIds.Should().Equal("A", "B");

    onlyA.Spec.DiameterMm.Should().Be(12);
    onlyB.Spec.DiameterMm.Should().Be(16);
    both.Spec.DiameterMm.Should().Be(16);
    onlyA.Boundary.CalculateArea().Should().BeApproximately(10_000, 1);
    onlyB.Boundary.CalculateArea().Should().BeApproximately(10_000, 1);
    both.Boundary.CalculateArea().Should().BeApproximately(10_000, 1);

    var bars = new StandardReinforcementCalculator(new ConsoleStructuredLogger())
        .CalculateRebars(overlay.Zones, Slab())
        .SelectMany(zone => zone.Rebars)
        .ToList();

    bars.Where(bar => bar.DiameterMm == 12).Should().ContainSingle().Which.Should().Match<RebarSegment>(bar =>
        bar.Start.X <= 100.1 && bar.End.X - 100.0 <= bar.AnchorageLengthEnd + 0.1);
    bars.Where(bar => bar.DiameterMm == 16).Should().HaveCount(2);
  }

  [Fact]
  public void DisjointClasses_StayUntouched()
  {
    var left = Zone("A", 0, 100, diameterMm: 12);
    var right = Zone("B", 200, 300, diameterMm: 16);

    var overlay = LayerClassOverlay.Apply([left, right], new NtsPlanarGeometry());

    overlay.Warnings.Should().BeEmpty();
    overlay.Zones.Select(zone => zone.Id).Should().Equal("A", "B");
  }

  private static ReinforcementZone Zone(string id, double minX, double maxX, int diameterMm) => new()
  {
    Id = id,
    Boundary = new Polygon(
    [
        new Point2D(minX, 0),
        new Point2D(maxX, 0),
        new Point2D(maxX, 100),
        new Point2D(minX, 100)
    ]),
    Spec = new ReinforcementSpec { DiameterMm = diameterMm, SpacingMm = 200, SteelClass = "A500C" },
    Direction = RebarDirection.X,
    ZoneType = ZoneType.Simple,
    DesignLayer = LayerKey.BottomX
  };

  private static SlabGeometry Slab() => new()
  {
    OuterBoundary = new Polygon(
    [
        new Point2D(0, 0),
        new Point2D(300, 0),
        new Point2D(300, 100),
        new Point2D(0, 100)
    ]),
    ThicknessMm = 220,
    CoverMm = 30,
    ConcreteClass = "B25"
  };
}
