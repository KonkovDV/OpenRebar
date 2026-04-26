using OpenRebar.Domain.Models;
using OpenRebar.Domain.Ports;

namespace OpenRebar.RevitPlugin.Revit;

/// <summary>
/// Re-export of Infrastructure stub for backward compatibility.
/// Prefer <see cref="OpenRebar.Infrastructure.Stubs.StubRevitPlacer"/> for new code.
/// </summary>
public sealed class StubRevitPlacer : IRevitPlacer
{
  private readonly Infrastructure.Stubs.StubRevitPlacer _inner = new();

  public Task<PlacementResult> PlaceReinforcementAsync(
      IReadOnlyList<ReinforcementZone> zones,
      PlacementSettings settings,
      CancellationToken cancellationToken = default)
      => _inner.PlaceReinforcementAsync(zones, settings, cancellationToken);
}
