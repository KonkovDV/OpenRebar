using FluentAssertions;
using OpenRebar.Domain.Models;
using OpenRebar.Infrastructure.Geometry;
using OpenRebar.Infrastructure.Logging;
using OpenRebar.Infrastructure.ReinforcementEngine;

namespace OpenRebar.Infrastructure.Tests.ReinforcementEngine;

public class WorkingAreaTests
{
  [Fact]
  public void ZeroInsets_KeepTheFullSpan()
  {
    var calculated = Calculate(edgeCoverMm: 0, openingClearanceMm: 0, opening: null);

    calculated[0].Rebars.Should().Contain(bar => bar.Start.X <= 0.1 && bar.End.X >= 1999.9);
  }

  [Fact]
  public void EdgeCover_PullsBarsInsideTheInset()
  {
    var calculated = Calculate(edgeCoverMm: 40, openingClearanceMm: 0, opening: null);

    calculated[0].Rebars.Should().NotBeEmpty();
    calculated[0].Rebars.Should().OnlyContain(bar =>
        bar.Start.X >= 39.9 && bar.End.X <= 1960.1 && bar.Start.Y >= 39.9 && bar.Start.Y <= 960.1);
  }

  [Fact]
  public void OpeningClearance_WidensTheGapAroundTheOpening()
  {
    var calculated = Calculate(
        edgeCoverMm: 0,
        openingClearanceMm: 50,
        opening: Rectangle(800, 0, 1200, 1000));

    calculated[0].Rebars.Should().NotBeEmpty();
    calculated[0].Rebars.Should().OnlyContain(bar => bar.End.X <= 750.1 || bar.Start.X >= 1249.9);
    calculated[0].Rebars.Should().Contain(bar => bar.End.X >= 749.9 && bar.End.X <= 750.1);
    calculated[0].Rebars.Should().Contain(bar => bar.Start.X >= 1249.9 && bar.Start.X <= 1250.1);
  }

  private static IReadOnlyList<ReinforcementZone> Calculate(
      double edgeCoverMm,
      double openingClearanceMm,
      Polygon? opening)
  {
    var zone = new ReinforcementZone
    {
      Id = "Z",
      Boundary = Rectangle(0, 0, 2000, 1000),
      Spec = new ReinforcementSpec { DiameterMm = 12, SpacingMm = 200, SteelClass = "A500C" },
      Direction = RebarDirection.X,
      ZoneType = ZoneType.Simple
    };
    var slab = new SlabGeometry
    {
      OuterBoundary = Rectangle(0, 0, 2000, 1000),
      Openings = opening is null ? [] : [opening],
      ThicknessMm = 220,
      CoverMm = 30,
      EdgeCoverMm = edgeCoverMm,
      OpeningClearanceMm = openingClearanceMm,
      ConcreteClass = "B25"
    };

    return new StandardReinforcementCalculator(new ConsoleStructuredLogger(), new NtsPlanarGeometry())
        .CalculateRebars([zone], slab);
  }

  private static Polygon Rectangle(double minX, double minY, double maxX, double maxY) => new(
  [
      new Point2D(minX, minY),
      new Point2D(maxX, minY),
      new Point2D(maxX, maxY),
      new Point2D(minX, maxY)
  ]);
}
