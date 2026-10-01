using OpenRebar.Domain.Models;
using OpenRebar.Domain.Rules;
using FluentAssertions;

namespace OpenRebar.Domain.Tests.Rules;

public class BendRulesTests
{
  [Theory]
  [InlineData(12, "A240", 2.5)]
  [InlineData(20, "A240", 4)]
  [InlineData(12, "A500C", 5)]
  [InlineData(20, "A500C", 8)]
  [InlineData(16, "A400", 5)]
  [InlineData(25, "B500", 8)]
  public void MandrelDiameter_FollowsTheClauseSplit(int diameterMm, string steelClass, double factor)
  {
    BendRules.MandrelDiameterMm(diameterMm, steelClass).Should().BeApproximately(factor * diameterMm, 1e-9);
    BendRules.InnerRadiusMm(diameterMm, steelClass).Should().BeApproximately(factor * diameterMm / 2.0, 1e-9);
  }

  [Fact]
  public void TwoHooks_AddTheCenterlineArcAndNoTail()
  {
    const int diameter = 20;
    double centerline = BendRules.InnerRadiusMm(diameter, "A500C") + diameter / 2.0;
    double expected = 2 * Math.PI * centerline;

    var shape = BendRules.Describe(diameter, "A500C", BarEndCondition.NeedsHook, BarEndCondition.NeedsHook);

    shape.Shape.Should().Be(BarShape.Hooked);
    shape.Code.Should().Be("H");
    shape.ArcMm.Should().BeApproximately(expected, 1e-9);
    centerline.Should().BeApproximately(90, 1e-9);
  }

  [Fact]
  public void UnknownSteel_UsesTheLargerPeriodicMandrel()
  {
    BendRules.MandrelDiameterMm(12, "ZZZ").Should().BeApproximately(5 * 12, 1e-9);
  }

  [Fact]
  public void StraightEnds_StayCode00WithNoArc()
  {
    var shape = BendRules.Describe(12, "A500C", BarEndCondition.Straight, BarEndCondition.ShortenedAtEdge);

    shape.Shape.Should().Be(BarShape.Straight);
    shape.Code.Should().Be("00");
    shape.ArcMm.Should().Be(0);
    shape.InnerRadiusMm.Should().Be(0);
  }

  [Fact]
  public void LAndU_UseTheirOwnCodes()
  {
    BendRules.Describe(12, "A500C", BarEndCondition.NeedsLBar, BarEndCondition.Straight).Code.Should().Be("L");
    BendRules.Describe(12, "A500C", BarEndCondition.Straight, BarEndCondition.NeedsUBar).Code.Should().Be("U");
  }
}
