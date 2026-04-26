using OpenRebar.Domain.Rules;
using FluentAssertions;

namespace OpenRebar.Domain.Tests.Rules;

public class AnchorageRulesTests
{
  [Theory(DisplayName = "SP 63 §9.2.1 — Anchorage Length Minimum Constraints")]
  [InlineData(12, "A500C", "B25", 200)]    // min(15*12=180, 200, calc) → at least 200mm
  [InlineData(16, "A500C", "B25", 240)]    // min(15*16=240, 200, calc)
  [InlineData(20, "A500C", "B25", 300)]    // min(15*20=300, 200, calc)
  [InlineData(25, "A500C", "B25", 375)]    // min(15*25=375, 200, calc)
  public void AnchorageLength_ShouldRespectMinimumConstraints(
      int diameter, string steelClass, string concreteClass, double minExpected)
  {
    var result = AnchorageRules.CalculateAnchorageLength(diameter, steelClass, concreteClass);
    result.Should().BeGreaterThanOrEqualTo(minExpected);
  }

  [Fact(DisplayName = "SP 63 §9.2.1 — Anchorage Length Golden Case (d12 A500C B25)")]
  public void AnchorageLength_A500C_B25_d12_ShouldBeReasonable()
  {
    // Current domain formula includes the periodic-profile bond coefficient η1 = 2.5
    // and good-placement η2 = 1.0, so:
    // l_an = Rs*d / (4*η1*η2*Rbt) = 435*12 / (4*2.5*1.05) ≈ 497mm → rounded to 500mm
    var result = AnchorageRules.CalculateAnchorageLength(12, "A500C", "B25");

    result.Should().BeInRange(450, 550,
        "anchorage for d12 A500C B25 should be about 500mm with the current η1·η2 bond model");
  }

  [Theory(DisplayName = "SP 63 §9.2.2 — Lap Length Must Exceed Anchorage")]
  [InlineData(12, "A500C", "B25")]
  [InlineData(16, "A400", "B30")]
  [InlineData(20, "A500C", "B20")]
  public void LapLength_ShouldBeGreaterThanAnchorage(
      int diameter, string steelClass, string concreteClass)
  {
    var anchorage = AnchorageRules.CalculateAnchorageLength(diameter, steelClass, concreteClass);
    var lap = AnchorageRules.CalculateLapLength(diameter, steelClass, concreteClass);

    lap.Should().BeGreaterThan(anchorage, "lap splice length must exceed anchorage length");
  }

  [Theory(DisplayName = "SP 63 §9.2.2 — Lap Length Minimum (20·d or 250mm)")]
  [InlineData(12)]
  [InlineData(16)]
  [InlineData(20)]
  public void LapLength_ShouldRespectMinimum20d(int diameter)
  {
    var lap = AnchorageRules.CalculateLapLength(diameter, "A500C", "B25");
    double minimum = Math.Max(20.0 * diameter, 250.0);

    lap.Should().BeGreaterThanOrEqualTo(minimum);
  }
}
