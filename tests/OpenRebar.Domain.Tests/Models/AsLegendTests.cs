using FluentAssertions;
using OpenRebar.Domain.Exceptions;
using OpenRebar.Domain.Models;

namespace OpenRebar.Domain.Tests.Models;

public class AsLegendTests
{
  [Fact]
  public void Constructor_MonotonicTouchingIntervals_IsAccepted()
  {
    var legend = new AsLegend(LayerKey.BottomX,
    [
        new AsLegendClass("a", new IsolineColor(255, 0, 0), new AsInterval(0, 100)),
        new AsLegendClass("b", new IsolineColor(0, 180, 0), new AsInterval(100, 250))
    ]);

    legend.Convention.Should().Be(AsValueConvention.UpperBound);
    legend.Classes[1].As.Resolve(legend.Convention).Should().Be(250);
    legend.Classes[1].As.Resolve(AsValueConvention.Midpoint).Should().Be(175);
  }

  [Fact]
  public void Constructor_OutOfOrderInterval_IsNotMonotonic()
  {
    var act = () => new AsLegend(LayerKey.BottomX,
    [
        new AsLegendClass("b", new IsolineColor(0, 180, 0), new AsInterval(100, 250)),
        new AsLegendClass("a", new IsolineColor(255, 0, 0), new AsInterval(0, 100))
    ]);

    act.Should().Throw<LegendValidationException>().Which.ErrorCode.Should().Be(AsLegend.NotMonotonic);
  }

  [Fact]
  public void Constructor_OverlappingInterval_IsRejected()
  {
    var act = () => new AsLegend(LayerKey.TopX,
    [
        new AsLegendClass("a", new IsolineColor(255, 0, 0), new AsInterval(0, 120)),
        new AsLegendClass("b", new IsolineColor(0, 0, 255), new AsInterval(80, 200))
    ]);

    act.Should().Throw<LegendValidationException>().Which.ErrorCode.Should().Be(AsLegend.IntervalOverlap);
  }

  [Fact]
  public void Constructor_Gap_IsRejectedUnlessAllowed()
  {
    AsLegendClass[] classes =
    [
        new AsLegendClass("a", new IsolineColor(255, 0, 0), new AsInterval(0, 100)),
        new AsLegendClass("b", new IsolineColor(0, 0, 255), new AsInterval(150, 250))
    ];

    var rejected = () => new AsLegend(LayerKey.BottomY, classes);
    rejected.Should().Throw<LegendValidationException>().Which.ErrorCode.Should().Be(AsLegend.IntervalGap);

    var allowed = new AsLegend(LayerKey.BottomY, classes, allowGaps: true);
    allowed.AllowGaps.Should().BeTrue();
  }

  [Fact]
  public void Constructor_CloseColors_AreRejected()
  {
    var act = () => new AsLegend(LayerKey.TopY,
    [
        new AsLegendClass("a", new IsolineColor(255, 0, 0), new AsInterval(0, 100)),
        new AsLegendClass("b", new IsolineColor(250, 5, 5), new AsInterval(100, 200))
    ]);

    act.Should().Throw<LegendValidationException>().Which.ErrorCode.Should().Be(AsLegend.ColorTooClose);
  }

  [Fact]
  public void Constructor_DuplicateClassOrColor_IsRejected()
  {
    var duplicateId = () => new AsLegend(LayerKey.BottomX,
    [
        new AsLegendClass("a", new IsolineColor(255, 0, 0), new AsInterval(0, 100)),
        new AsLegendClass("a", new IsolineColor(0, 0, 255), new AsInterval(100, 200))
    ]);
    duplicateId.Should().Throw<LegendValidationException>().Which.ErrorCode.Should().Be(AsLegend.DuplicateClass);

    var duplicateColor = () => new AsLegend(LayerKey.BottomX,
    [
        new AsLegendClass("a", new IsolineColor(255, 0, 0), new AsInterval(0, 100)),
        new AsLegendClass("b", new IsolineColor(255, 0, 0), new AsInterval(100, 200))
    ]);
    duplicateColor.Should().Throw<LegendValidationException>().Which.ErrorCode.Should().Be(AsLegend.DuplicateColor);
  }
}
