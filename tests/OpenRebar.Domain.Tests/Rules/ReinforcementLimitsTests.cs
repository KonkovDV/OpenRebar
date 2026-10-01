using OpenRebar.Domain.Rules;
using FluentAssertions;

namespace OpenRebar.Domain.Tests.Rules;

public class ReinforcementLimitsTests
{
  [Theory]
  [InlineData(100, 200)]
  [InlineData(150, 200)]
  [InlineData(200, 300)]
  [InlineData(300, 400)]
  public void MaxSpacing_FollowsThicknessBands(double thicknessMm, double expectedMm)
  {
    double working = ReinforcementLimits.MaxSpacing(thicknessMm, ReinforcementLimits.SlabReinforcementRole.Working);
    double distribution = ReinforcementLimits.MaxSpacing(thicknessMm, ReinforcementLimits.SlabReinforcementRole.Distribution);

    working.Should().Be(expectedMm);
    distribution.Should().Be(expectedMm);
  }

  [Fact]
  public void MinReinforcementArea_UsesEffectiveDepth()
  {
    double fromEffectiveDepth = ReinforcementLimits.MinReinforcementArea(effectiveDepthMm: 180, widthMm: 1000);
    double fromFullThickness = 0.001 * 220 * 1000;

    fromEffectiveDepth.Should().Be(180);
    fromEffectiveDepth.Should().BeLessThan(fromFullThickness);
  }
}
