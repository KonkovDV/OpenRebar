using FluentAssertions;
using OpenRebar.Domain.Models;

namespace OpenRebar.Domain.Tests.Models;

public sealed class SlabGeometryTests
{
  private static Polygon MakeRect() => new(
  [
    new Point2D(0, 0),
    new Point2D(1000, 0),
    new Point2D(1000, 1000),
    new Point2D(0, 1000)
  ]);

  private static SlabGeometry MakeSlab(double thicknessMm = 220, double coverMm = 30) =>
      new()
      {
        OuterBoundary = MakeRect(),
        ThicknessMm = thicknessMm,
        CoverMm = coverMm,
        ConcreteClass = "B25"
      };

  /// <summary>
  /// SP 63.13330.2018 §10.3: effective depth = h - cover - d/2.
  /// simple-slab etalon: h=220, cover=30, Ø20 → 180 mm.
  /// </summary>
  [Fact]
  public void EffectiveDepthFor_Diameter20_Returns180()
  {
    var slab = MakeSlab(thicknessMm: 220, coverMm: 30);
    slab.EffectiveDepthFor(20).Should().Be(180.0);
  }

  [Fact]
  public void EffectiveDepthFor_Diameter12_Returns169()
  {
    // h=200, cover=25, d=12 → 200-25-6=169
    var slab = MakeSlab(thicknessMm: 200, coverMm: 25);
    slab.EffectiveDepthFor(12).Should().Be(169.0);
  }

  /// <summary>
  /// EffectiveDepthMm (h-cover) is kept for BuildPartialReport where the bar
  /// diameter is not yet known. It must not be removed.
  /// </summary>
  [Fact]
  public void EffectiveDepthMm_ReturnsHMinusCover_AsConservativeFallback()
  {
    var slab = MakeSlab(thicknessMm: 220, coverMm: 30);
    slab.EffectiveDepthMm.Should().Be(190.0);
  }
}
