using FluentAssertions;
using OpenRebar.Domain.Exceptions;
using OpenRebar.Domain.Models;
using OpenRebar.Domain.Ports;
using OpenRebar.Domain.Rules;

namespace OpenRebar.Domain.Tests.Models;

public class FailClosedValidationTests
{
  [Theory]
  [InlineData(double.NaN)]
  [InlineData(double.PositiveInfinity)]
  [InlineData(double.NegativeInfinity)]
  public void SlabGeometry_NonFinitePhysicalDimensions_AreRejected(double value)
  {
    var badThickness = () => new SlabGeometry
    {
      OuterBoundary = Square(),
      ThicknessMm = value,
      CoverMm = 30,
      ConcreteClass = "B25"
    };
    var badCover = () => new SlabGeometry
    {
      OuterBoundary = Square(),
      ThicknessMm = 200,
      CoverMm = value,
      ConcreteClass = "B25"
    };

    badThickness.Should().Throw<ArgumentOutOfRangeException>();
    badCover.Should().Throw<ArgumentOutOfRangeException>();
  }

  [Theory]
  [InlineData(double.NaN, 100)]
  [InlineData(0, double.PositiveInfinity)]
  public void AsLegend_NonFiniteIntervals_AreRejected(double lower, double upper)
  {
    var act = () => new AsLegend(LayerKey.BottomX,
    [
        new AsLegendClass("bad", new IsolineColor(255, 0, 0), new AsInterval(lower, upper))
    ]);

    act.Should().Throw<LegendValidationException>()
        .Which.ErrorCode.Should().Be(AsLegend.IntervalInvalid);
  }

  [Fact]
  public void AdditionalBarSelector_NaNRequirement_NeedsHumanDecision()
  {
    var request = new AdditionalBarRequest(
        double.NaN,
        new BackgroundMesh(
            LayerKey.BottomX,
            new ReinforcementSpec { DiameterMm = 12, SpacingMm = 200, SteelClass = "A500C" },
            0),
        [10, 12, 14],
        new AdditionalSpacingPolicy(AdditionalSpacingMode.InterleaveSameAsBackground));

    new AdditionalBarSelector().Select(request).Status
        .Should().Be(AdditionalBarSelector.NeedsHumanDecision);
  }

  private static Polygon Square() => new(
  [
      new Point2D(0, 0),
      new Point2D(1000, 0),
      new Point2D(1000, 1000),
      new Point2D(0, 1000)
  ]);
}
