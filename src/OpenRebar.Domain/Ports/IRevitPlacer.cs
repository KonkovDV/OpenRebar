using OpenRebar.Domain.Models;

namespace OpenRebar.Domain.Ports;

/// <summary>
/// Places reinforcement elements into the Revit model.
/// Creates RebarInSystem, tags, bending details, and annotations.
/// </summary>
public interface IRevitPlacer
{
  /// <summary>
  /// Place all rebar elements from zones into the active Revit view.
  /// Creates rebar groups, tags, and bending details.
  /// </summary>
  /// <param name="zones">Zones with computed rebar segments.</param>
  /// <param name="settings">Placement settings.</param>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>Summary of placed elements.</returns>
  Task<PlacementResult> PlaceReinforcementAsync(
      IReadOnlyList<ReinforcementZone> zones,
      PlacementSettings settings,
      CancellationToken cancellationToken = default);
}

/// <summary>
/// Settings for Revit placement.
/// </summary>
public sealed record PlacementSettings
{
  /// <summary>Whether to create rebar tags.</summary>
  public bool CreateTags { get; init; } = true;

  /// <summary>Whether to create bending details.</summary>
  public bool CreateBendingDetails { get; init; } = true;

  /// <summary>Whether to group rebars by zone.</summary>
  public bool GroupByZone { get; init; } = true;

  /// <summary>Custom parameter name for zone origin ID.</summary>
  public string ZoneParameterName { get; init; } = "OpenRebar_ZoneId";

  /// <summary>Revit host element id serialized as string to keep domain isolation.</summary>
  public string? HostElementId { get; init; }

  /// <summary>Host slab elevation offset in Revit internal feet.</summary>
  public double ElevationOffsetFeet { get; init; }

  /// <summary>Maximum number of rebars committed per Revit transaction batch.</summary>
  public int MaxRebarsPerTransaction
  {
    get => _maxRebarsPerTransaction;
    init
    {
      if (value <= 0)
        throw new ArgumentOutOfRangeException(nameof(MaxRebarsPerTransaction), value, "Maximum rebars per transaction must be positive.");
      _maxRebarsPerTransaction = value;
    }
  }
  private readonly int _maxRebarsPerTransaction = 5000;
}

/// <summary>
/// Result of the placement operation.
/// </summary>
public sealed record PlacementResult
{
  public required int TotalRebarsPlaced { get; init; }
  public required int TotalTagsCreated { get; init; }
  public required int TotalBendingDetails { get; init; }
  public IReadOnlyList<string> Warnings { get; init; } = [];
  public IReadOnlyList<string> Errors { get; init; } = [];
  public bool Success => Errors.Count == 0;
}
