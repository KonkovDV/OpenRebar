using OpenRebar.Domain.Models;
using OpenRebar.Domain.Ports;

namespace OpenRebar.Application.Tests;

/// <summary>
/// Used by pipeline tests whose bars are stubs, not a real layout.
/// </summary>
internal sealed class PassingReinforcementVerifier : IReinforcementVerifier
{
  public ReinforcementVerificationResult Verify(
      IReadOnlyList<ReinforcementZone> zones,
      VerificationSettings? settings = null) => new()
      {
        Status = VerificationStatuses.Passed,
        UnderReinforcedAreaM2 = 0,
        CheckedAreaM2 = 0,
        MinMarginMm2PerM = 0,
        MinProvisionRatio = 1,
        CellSizeMm = settings?.CellSizeMm ?? 50,
        UnderCoverageRatio = settings?.UnderCoverageRatio ?? 0,
        DeficitRegions = []
      };
}
