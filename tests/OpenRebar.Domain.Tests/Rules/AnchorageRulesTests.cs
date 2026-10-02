using OpenRebar.Domain.Rules;
using FluentAssertions;

namespace OpenRebar.Domain.Tests.Rules;

public class AnchorageRulesTests
{
  [Theory]
  [InlineData(10, 2.5, 1.0)]
  [InlineData(12, 2.5, 1.0)]
  [InlineData(16, 2.5, 1.0)]
  [InlineData(20, 2.5, 1.0)]
  [InlineData(25, 2.5, 1.0)]
  [InlineData(36, 2.5, 0.9)]
  public void BasicAnchorage_MatchesIndependentFormula(int diameterMm, double eta1, double eta2)
  {
    const double rs = 435.0;
    const double rbt = 1.05;
    double expected = rs * diameterMm / (4.0 * eta1 * eta2 * rbt);

    double actual = AnchorageRules.CalculateBasicAnchorageLength(diameterMm, "A500", "B25");

    actual.Should().BeApproximately(expected, 0.05);
  }

  [Theory]
  [InlineData(10, 1.0, 420)]
  [InlineData(12, 1.0, 500)]
  [InlineData(16, 1.0, 670)]
  [InlineData(20, 1.0, 830)]
  [InlineData(25, 1.0, 1040)]
  [InlineData(36, 0.9, 1660)]
  public void AnchorageLength_MatchesIndependentRounding(int diameterMm, double eta2, double publishedMm)
  {
    double basic = IndependentBasic(diameterMm, 2.5, eta2);
    double expected = RoundUp(Math.Max(basic, Math.Max(0.3 * basic, Math.Max(15.0 * diameterMm, 200.0))));

    expected.Should().Be(publishedMm);
    AnchorageRules.CalculateAnchorageLength(diameterMm, "A500", "B25")
        .Should().Be(expected);
  }

  [Theory]
  [InlineData(10, 1.2, 500)]
  [InlineData(12, 1.2, 600)]
  [InlineData(16, 1.2, 800)]
  [InlineData(20, 1.2, 1000)]
  [InlineData(25, 1.2, 1250)]
  [InlineData(36, 1.2, 1990)]
  [InlineData(10, 2.0, 830)]
  [InlineData(12, 2.0, 1000)]
  [InlineData(20, 2.0, 1660)]
  [InlineData(36, 2.0, 3320)]
  public void LapLength_MatchesIndependentFormula(int diameterMm, double alpha, double publishedMm)
  {
    double eta2 = diameterMm >= 36 ? 0.9 : 1.0;
    double basic = IndependentBasic(diameterMm, 2.5, eta2);
    double required = alpha * basic;
    double minimum = Math.Max(0.4 * alpha * basic, Math.Max(20.0 * diameterMm, 250.0));
    double expected = RoundUp(Math.Max(required, minimum));
    var share = Math.Abs(alpha - 1.2) < 1e-9
        ? AnchorageRules.LapSpliceShare.UpTo50
        : AnchorageRules.LapSpliceShare.Full100;

    expected.Should().Be(publishedMm);
    AnchorageRules.CalculateLapLength(diameterMm, "A500", "B25", share)
        .Should().Be(expected);
  }

  [Fact]
  public void ColdDeformedBar_UsesEta1OfTwo()
  {
    double rs = AnchorageRules.GetDesignStrength("B500");
    rs.Should().Be(415);
    double expected = rs * 12.0 / (4.0 * 2.0 * 1.0 * 1.05);

    AnchorageRules.CalculateBasicAnchorageLength(12, "B500", "B25")
        .Should().BeApproximately(expected, 0.05);
    RoundUp(Math.Max(expected, Math.Max(0.3 * expected, Math.Max(15.0 * 12, 200.0))))
        .Should().Be(600);
    AnchorageRules.CalculateAnchorageLength(12, "B500", "B25").Should().Be(600);
  }

  [Fact]
  public void A400BasicAnchorage_ScalesWithTheAmendedDesignStrength()
  {
    const double eta1 = 2.5;
    const double eta2 = 1.0;
    const double bond = 1.05;
    const int diameterMm = 12;
    double previous = 355.0 * diameterMm / (4.0 * eta1 * eta2 * bond);
    double amended = 340.0 * diameterMm / (4.0 * eta1 * eta2 * bond);

    double actual = AnchorageRules.CalculateBasicAnchorageLength(diameterMm, "A400", "B25");

    actual.Should().BeApproximately(amended, 0.05);
    (actual / previous).Should().BeApproximately(340.0 / 355.0, 1e-9);
    NormativeProfiles.GetDesignCompressionStrength("A400").Should().Be(350);
    NormativeProfiles.GetDesignCompressionStrength("A400", shortTerm: true).Should().Be(350);
    NormativeProfiles.GetDesignCompressionStrength("A500").Should().Be(435);
    NormativeProfiles.GetDesignCompressionStrength("A500", shortTerm: true).Should().Be(400);
    NormativeProfiles.GetDesignCompressionStrength("B500").Should().Be(415);
    NormativeProfiles.GetDesignCompressionStrength("B500", shortTerm: true).Should().Be(380);
  }

  [Fact]
  public void TopBarFactor_ScalesAnchorageOnly()
  {
    double plain = AnchorageRules.CalculateAnchorageLength(12, "A500C", "B25");
    double scaled = AnchorageRules.CalculateAnchorageLength(12, "A500C", "B25", topBarAnchorageFactor: 1.1);

    scaled.Should().BeGreaterThan(plain);
    NormativeProfiles.Sp63_2018.TopBarAnchorageFactor.Should().Be(1.0);
  }

  [Fact]
  public void CompressionLap_UsesAlpha09()
  {
    double basic = IndependentBasic(20, 2.5, 1.0);
    double required = 0.9 * basic;
    double minimum = Math.Max(0.4 * 0.9 * basic, Math.Max(20.0 * 20, 250.0));

    AnchorageRules.CalculateLapLength(20, "A500", "B25", inCompression: true)
        .Should().Be(RoundUp(Math.Max(required, minimum)));
  }

  private static double IndependentBasic(int diameterMm, double eta1, double eta2)
  {
    return 435.0 * diameterMm / (4.0 * eta1 * eta2 * 1.05);
  }

  private static double RoundUp(double lengthMm)
  {
    return Math.Ceiling(lengthMm / 10.0 - 1e-9) * 10.0;
  }
}
