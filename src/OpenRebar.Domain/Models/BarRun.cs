namespace OpenRebar.Domain.Models;

/// <summary>
/// Bars of one class that share a length. The length is the outer extent of the merged lines.
/// </summary>
public sealed class BarRun
{
  public LayerKey? Layer { get; init; }
  public required RebarDirection Axis { get; init; }
  public required double StartCoord { get; init; }
  public required double EndCoord { get; init; }
  public required double FirstLine { get; init; }
  public required double LastLine { get; init; }
  public required int Count { get; init; }
  public required ReinforcementSpec Spec { get; init; }

  /// <summary>Station coordinates perpendicular to the bars.</summary>
  public IReadOnlyList<double> Lines { get; init; } = [];
}
