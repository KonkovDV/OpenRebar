using OpenRebar.Domain.Models;

namespace OpenRebar.Domain.Ports;

/// <summary>
/// Parses isoline files (DXF or PNG) and extracts reinforcement zones.
/// </summary>
public interface IIsolineParser
{
  /// <summary>Supported file extensions (e.g. ".dxf", ".png").</summary>
  IReadOnlyList<string> SupportedExtensions { get; }

  /// <summary>
  /// Parse the isoline file and extract raw zone polygons with their colors.
  /// </summary>
  /// <param name="filePath">Path to the isoline file.</param>
  /// <param name="legend">Color legend for interpreting the isoline.</param>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>List of reinforcement zones with basic geometry.</returns>
  Task<IReadOnlyList<ReinforcementZone>> ParseAsync(
      string filePath,
      ColorLegend legend,
      CancellationToken cancellationToken = default);
}
