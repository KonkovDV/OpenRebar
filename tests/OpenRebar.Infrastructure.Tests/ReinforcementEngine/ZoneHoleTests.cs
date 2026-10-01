using FluentAssertions;
using OpenRebar.Domain.Models;
using OpenRebar.Infrastructure.Logging;
using OpenRebar.Infrastructure.ReinforcementEngine;

namespace OpenRebar.Infrastructure.Tests.ReinforcementEngine;

public class ZoneHoleTests
{
  [Fact]
  public void CalculateRebars_Hole_DoesNotCrossTheOpening()
  {
    var zone = new ReinforcementZone
    {
      Id = "Z",
      Boundary = Rectangle(0, 0, 1000, 1000),
      Holes = [Rectangle(400, 0, 600, 1000)],
      Spec = new ReinforcementSpec { DiameterMm = 12, SpacingMm = 200, SteelClass = "A500C" },
      Direction = RebarDirection.X,
      ZoneType = ZoneType.Simple
    };
    var slab = new SlabGeometry
    {
      OuterBoundary = Rectangle(0, 0, 1000, 1000),
      ThicknessMm = 220,
      CoverMm = 30,
      ConcreteClass = "B25"
    };

    var calculated = new StandardReinforcementCalculator(new ConsoleStructuredLogger()).CalculateRebars([zone], slab);

    calculated[0].Rebars.Should().NotBeEmpty();
    calculated[0].Rebars.Should().OnlyContain(bar => bar.End.X <= 400.1 || bar.Start.X >= 599.9);
  }

  private static Polygon Rectangle(double minX, double minY, double maxX, double maxY) => new(
  [
      new Point2D(minX, minY),
      new Point2D(maxX, minY),
      new Point2D(maxX, maxY),
      new Point2D(minX, maxY)
  ]);
}
