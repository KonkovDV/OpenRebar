using FluentAssertions;
using OpenRebar.Domain.Models;

namespace OpenRebar.Domain.Tests.Models;

public class NormativeCheckTests
{
  [Fact]
  public void Build_WithoutVerificationOrLaps_MarksCrackWidthAndDeflection()
  {
    var checks = NormativeCheck.Build(verification: null, hasLaps: false);

    checks.Select(check => check.Id).Should().Equal("providedArea", "crackWidth", "deflection");
    checks[0].Status.Should().Be(CheckStatus.NotEvaluated);
    checks[0].Reason.Should().Be("noVerification");
    checks[1].Status.Should().Be(CheckStatus.NotEvaluated);
    checks[1].Reason.Should().Be("inputIsRequiredAreaOnly");
    checks[2].Reason.Should().Be("inputIsRequiredAreaOnly");
  }

  [Theory]
  [InlineData(VerificationStatuses.Passed)]
  [InlineData(VerificationStatuses.PassedWithSmoothing)]
  [InlineData(VerificationStatuses.PassedWithUnderCoverage)]
  public void Build_TreatsAPassingAreaCheckAsPassed(string status)
  {
    var checks = NormativeCheck.Build(Verification(status), hasLaps: false);

    checks[0].Status.Should().Be(CheckStatus.Passed);
    checks[0].Reason.Should().BeNull();
  }

  [Fact]
  public void Build_TreatsAFailingAreaCheckAsFailed()
  {
    var checks = NormativeCheck.Build(Verification(VerificationStatuses.Failed), hasLaps: false);

    checks[0].Status.Should().Be(CheckStatus.Failed);
  }

  [Fact]
  public void Build_AddsTransverseSteelOnlyWhenALapExists()
  {
    var without = NormativeCheck.Build(Verification(VerificationStatuses.Passed), hasLaps: false);
    var withLap = NormativeCheck.Build(Verification(VerificationStatuses.Passed), hasLaps: true);

    without.Should().NotContain(check => check.Id == "transverseInLap");
    withLap.Should().ContainSingle(check =>
        check.Id == "transverseInLap"
        && check.Status == CheckStatus.NotEvaluated
        && check.Reason == "Q-2b");
  }

  private static ReinforcementVerificationResult Verification(string status) => new()
  {
    Status = status,
    UnderReinforcedAreaM2 = 0,
    CheckedAreaM2 = 0,
    MinMarginMm2PerM = 0,
    MinProvisionRatio = 0,
    CellSizeMm = 50,
    UnderCoverageRatio = 0,
    DeficitRegions = []
  };
}
