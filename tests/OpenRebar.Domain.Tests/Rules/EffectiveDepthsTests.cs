using FluentAssertions;
using OpenRebar.Domain.Models;
using OpenRebar.Domain.Rules;

namespace OpenRebar.Domain.Tests.Rules;

public class EffectiveDepthsTests
{
  [Fact]
  public void BottomOuterBar_IsThicknessMinusCoverMinusHalfDiameter()
  {
    var slab = Slab(thicknessMm: 220, coverMm: 30);
    var zones = new[]
    {
      Zone(RebarLayer.Bottom, RebarDirection.X, 20),
      Zone(RebarLayer.Bottom, RebarDirection.Y, 12, bars: false)
    };

    var depths = EffectiveDepths.ByLayer(slab, zones);

    depths.Should().Equal(new Dictionary<string, double> { ["BottomX"] = 180 });
  }

  [Fact]
  public void EachLayer_MeasuresToItsOwnAxis()
  {
    var slab = Slab(thicknessMm: 220, coverMm: 25);
    var zones = new[]
    {
      Zone(RebarLayer.Bottom, RebarDirection.X, 16),
      Zone(RebarLayer.Bottom, RebarDirection.Y, 12),
      Zone(RebarLayer.Top, RebarDirection.X, 16),
      Zone(RebarLayer.Top, RebarDirection.Y, 12)
    };

    var depths = EffectiveDepths.ByLayer(slab, zones);

    depths["BottomX"].Should().BeApproximately(187, 1e-9);
    depths["BottomY"].Should().BeApproximately(173, 1e-9);
    depths["TopX"].Should().BeApproximately(187, 1e-9);
    depths["TopY"].Should().BeApproximately(173, 1e-9);
    depths.Keys.Should().ContainInOrder("BottomX", "BottomY", "TopX", "TopY");
  }

  private static SlabGeometry Slab(double thicknessMm, double coverMm) => new()
  {
    OuterBoundary = new Polygon([new Point2D(0, 0), new Point2D(1000, 0), new Point2D(0, 1000)]),
    ThicknessMm = thicknessMm,
    CoverMm = coverMm,
    ConcreteClass = "B25"
  };

  private static ReinforcementZone Zone(RebarLayer face, RebarDirection direction, int diameterMm, bool bars = true) => new()
  {
    Id = $"{face}-{direction}",
    Boundary = new Polygon([new Point2D(0, 0), new Point2D(1000, 0), new Point2D(0, 1000)]),
    Spec = new ReinforcementSpec { DiameterMm = diameterMm, SpacingMm = 200, SteelClass = "A500C" },
    Direction = direction,
    ZoneType = ZoneType.Simple,
    Layer = face,
    Rebars = bars
        ?
        [
            new RebarSegment
            {
              Start = new Point2D(0, 0),
              End = new Point2D(1000, 0),
              DiameterMm = diameterMm,
              AnchorageLengthStart = 0,
              AnchorageLengthEnd = 0
            }
        ]
        : []
  };
}
