using FluentAssertions;
using OpenRebar.Domain.Models;
using OpenRebar.Domain.Rules;

namespace OpenRebar.Domain.Tests.Rules;

public class LayerElevationsTests
{
  [Theory]
  [InlineData(RebarLayer.Bottom, RebarDirection.X, 33)]
  [InlineData(RebarLayer.Bottom, RebarDirection.Y, 47)]
  [InlineData(RebarLayer.Top, RebarDirection.Y, 173)]
  [InlineData(RebarLayer.Top, RebarDirection.X, 187)]
  public void AxisElevations_KeepTheCoverToTheBarSurface(RebarLayer face, RebarDirection direction, double expected)
  {
    double elevation = LayerElevations.AxisElevationMm(face, direction, 220, 25, 25, 16, 12);

    elevation.Should().BeApproximately(expected, 1e-9);
  }

  [Fact]
  public void CrossingLayersOfOneFace_DoNotOverlap()
  {
    double outer = LayerElevations.AxisElevationMm(RebarLayer.Bottom, RebarDirection.X, 220, 25, 25, 16, 12);
    double inner = LayerElevations.AxisElevationMm(RebarLayer.Bottom, RebarDirection.Y, 220, 25, 25, 16, 12);

    (inner - outer).Should().BeApproximately((16 + 12) / 2.0, 1e-9);
    (outer - 16 / 2.0).Should().BeApproximately(25, 1e-9);
  }

  [Fact]
  public void TopCover_IsReadSeparatelyFromBottomCover()
  {
    double top = LayerElevations.AxisElevationMm(RebarLayer.Top, RebarDirection.X, 200, 20, 35, 12, 12);

    (200 - top - 12 / 2.0).Should().BeApproximately(35, 1e-9);
  }

  [Fact]
  public void LayersThatDoNotFit_AreReported()
  {
    LayerElevations.LayersFit(100, 25, 25, 16, 16, 16, 16).Should().BeFalse();
    LayerElevations.LayersFit(220, 25, 25, 16, 12, 16, 12).Should().BeTrue();
  }
}
