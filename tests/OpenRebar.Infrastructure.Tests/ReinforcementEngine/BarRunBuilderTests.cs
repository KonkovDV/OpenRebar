using FluentAssertions;
using OpenRebar.Domain.Models;
using OpenRebar.Infrastructure.Geometry;
using OpenRebar.Infrastructure.Logging;
using OpenRebar.Infrastructure.ReinforcementEngine;

namespace OpenRebar.Infrastructure.Tests.ReinforcementEngine;

public class BarRunBuilderTests
{
  [Fact]
  public void Rectangle_IsOneRunOfTheFullSpan()
  {
    var runs = BarRunBuilder.Build(Zone(Rectangle(0, 0, 6000, 4000)), Slab(6000, 4000));

    runs.Should().ContainSingle();
    runs[0].StartCoord.Should().BeApproximately(0, 0.1);
    runs[0].EndCoord.Should().BeApproximately(6000, 0.1);
    runs[0].Count.Should().BeGreaterThan(1);
  }

  [Fact]
  public void StepWithinSnap_LengthensEveryBarToTheOuterEnd()
  {
    var zone = Zone(new Polygon(
    [
        new Point2D(0, 0),
        new Point2D(1000, 0),
        new Point2D(1000, 400),
        new Point2D(1040, 400),
        new Point2D(1040, 800),
        new Point2D(0, 800)
    ]));

    var runs = BarRunBuilder.Build(zone, Slab(1040, 800));

    runs.Should().ContainSingle();
    runs[0].EndCoord.Should().BeApproximately(1040, 0.1);
    runs[0].Count.Should().BeGreaterThan(2);

    var bars = new StandardReinforcementCalculator(new ConsoleStructuredLogger(), new NtsPlanarGeometry())
        .CalculateRebars([zone], Slab(1040, 800));
    bars[0].Rebars.Should().OnlyContain(bar => bar.End.X >= 1003.9 && bar.End.X <= 1004.1);
    bars[0].Rebars.Should().Contain(bar => bar.Start.Y < 400);
  }

  [Fact]
  public void ObliqueEdge_QuantizesLengthUpward_AndKeepsOneLengthPerRun()
  {
    var zone = Zone(new Polygon(
    [
        new Point2D(0, 0),
        new Point2D(80, 0),
        new Point2D(0, 4000)
    ]));

    var runs = BarRunBuilder.Build(zone, Slab(80, 4000));
    var bars = new StandardReinforcementCalculator(new ConsoleStructuredLogger(), new NtsPlanarGeometry())
        .CalculateRebars([zone], Slab(80, 4000))[0].Rebars;

    runs.Should().NotBeEmpty();
    bars.Should().NotBeEmpty();
    foreach (var run in runs)
    {
      run.Lines.Should().NotBeEmpty();
      double length = run.EndCoord - run.StartCoord;
      length.Should().BeApproximately(Math.Round(length / BarRunBuilder.LengthStepMm) * BarRunBuilder.LengthStepMm, 0.1);
    }

    bars.Should().OnlyContain(bar => bar.Start.X >= -0.1 && bar.End.X <= 80.1);
  }

  [Fact]
  public void StepBeyondSnap_StaysTwoRuns()
  {
    var zone = Zone(new Polygon(
    [
        new Point2D(0, 0),
        new Point2D(1000, 0),
        new Point2D(1000, 400),
        new Point2D(1080, 400),
        new Point2D(1080, 800),
        new Point2D(0, 800)
    ]));

    var runs = BarRunBuilder.Build(zone, Slab(1080, 800));

    runs.Should().HaveCount(2);
    runs.Select(run => run.EndCoord).OrderBy(end => end).Should().ContainInOrder(1000, 1080);
  }

  private static ReinforcementZone Zone(Polygon boundary) => new()
  {
    Id = "Z",
    Boundary = boundary,
    Spec = new ReinforcementSpec { DiameterMm = 12, SpacingMm = 200, SteelClass = "A500C" },
    Direction = RebarDirection.X,
    ZoneType = ZoneType.Simple
  };

  private static SlabGeometry Slab(double width, double height) => new()
  {
    OuterBoundary = Rectangle(0, 0, width, height),
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
