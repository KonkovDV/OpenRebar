using OpenRebar.Domain.Models;

namespace OpenRebar.Domain.Ports;

/// <summary>
/// Calculates reinforcement layout within zones:
/// rebar positions, anchorage lengths, lap lengths, total rebar lengths.
/// </summary>
public interface IReinforcementCalculator
{
    /// <summary>
    /// Generate individual rebar segments for all zones.
    /// Applies anchorage and lap splice rules per design code.
    /// </summary>
    /// <param name="zones">Classified zones with geometry.</param>
    /// <param name="slab">Slab geometry for context (cover, thickness, concrete class).</param>
    /// <returns>Zones with computed rebar segments populated.</returns>
    IReadOnlyList<ReinforcementZone> CalculateRebars(
        IReadOnlyList<ReinforcementZone> zones,
        SlabGeometry slab);
}
