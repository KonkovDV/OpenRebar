using OpenRebar.Domain.Models;

namespace OpenRebar.Domain.Ports;

/// <summary>
/// Checks that placed bars provide the zone specification over the zone area.
/// Independent of the calculator that produced the bars.
/// </summary>
public interface IReinforcementVerifier
{
  ReinforcementVerificationResult Verify(
      IReadOnlyList<ReinforcementZone> zones,
      VerificationSettings? settings = null);
}
