using OpenRebar.Domain.Models;

namespace OpenRebar.Domain.Ports;

/// <summary>
/// Detects and classifies reinforcement zones on the slab.
/// Handles zone decomposition for complex polygons.
/// </summary>
public interface IZoneDetector
{
  /// <summary>
  /// Classify zones and decompose complex polygons into rectangles.
  /// </summary>
  /// <param name="zones">Raw zones from isoline parser.</param>
  /// <param name="slab">Slab geometry for context.</param>
  /// <returns>Zones with classification and decomposition applied.</returns>
  IReadOnlyList<ReinforcementZone> ClassifyAndDecompose(
      IReadOnlyList<ReinforcementZone> zones,
      SlabGeometry slab);
}
