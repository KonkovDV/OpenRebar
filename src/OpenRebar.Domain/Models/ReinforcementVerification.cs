using System.Text.Json.Serialization;

namespace OpenRebar.Domain.Models;

/// <summary>
/// Coverage check settings. The generic profile allows no under-coverage and no smoothing.
/// </summary>
public sealed record VerificationSettings
{
  public double CellSizeMm { get; init; } = 50;

  /// <summary>
  /// Fraction of checked area that may stay below the specification before the status is Failed.
  /// Zero means any deficit fails.
  /// </summary>
  public double UnderCoverageRatio { get; init; }

  /// <summary>
  /// When set, a local deficit can be waived by taking the peak provision inside this window.
  /// The raw deficit is still reported, and the status becomes PassedWithSmoothing.
  /// </summary>
  public double? PeakSmoothingWindowMm { get; init; }

  /// <summary>
  /// When set, a sample cell takes the greatest element As that the cell touches.
  /// </summary>
  public AsField? Field { get; init; }

  /// <summary>Slab outline used to tell a free-edge development shortfall from a real gap.</summary>
  public SlabGeometry? Slab { get; init; }
}

public static class VerificationStatuses
{
  public const string Passed = "Passed";
  public const string Failed = "Failed";
  public const string PassedWithUnderCoverage = "PassedWithUnderCoverage";
  public const string PassedWithSmoothing = "PassedWithSmoothing";
}

/// <summary>
/// Cell-grid certificate for provided versus required reinforcement area.
/// </summary>
public sealed record ReinforcementVerificationResult
{
  public required string Status { get; init; }
  public required double UnderReinforcedAreaM2 { get; init; }

  /// <summary>Failed area that is short only because development does not reach a free edge.</summary>
  public double EdgeDevelopmentAreaM2 { get; init; }

  /// <summary>Failed area that is not explained by a free edge.</summary>
  public double RealDeficitAreaM2 { get; init; }

  /// <summary>Ways to remove an edge-development shortfall. Empty when that area is zero.</summary>
  public IReadOnlyList<string> Remedy { get; init; } = [];

  public required double CheckedAreaM2 { get; init; }
  public required double MinMarginMm2PerM { get; init; }
  public required double MinProvisionRatio { get; init; }
  public required double CellSizeMm { get; init; }
  public required double UnderCoverageRatio { get; init; }
  public double? PeakSmoothingWindowMm { get; init; }
  public int UnderReinforcedCells { get; init; }
  public double ExcessSteelKg { get; init; }
  public IReadOnlyList<LayerVerificationReport> Layers { get; init; } = [];

  /// <summary>Sample cells for the heatmap. Omitted from the execution report.</summary>
  [JsonIgnore]
  public IReadOnlyList<VerificationCellReport> Cells { get; init; } = [];

  public required IReadOnlyList<DeficitRegionReport> DeficitRegions { get; init; }
}

/// <summary>One sampled cell. Margin is provided area minus required area, mm²/m.</summary>
public sealed record VerificationCellReport
{
  public required string Layer { get; init; }
  public required double X { get; init; }
  public required double Y { get; init; }
  public required double MarginMm2PerM { get; init; }

  /// <summary>EdgeDevelopmentShort or RealDeficit when the cell is short. Empty when it is covered.</summary>
  public string? Status { get; init; }
}

/// <summary>
/// Coverage certificate for one design layer.
/// </summary>
public sealed record LayerVerificationReport
{
  public required string Layer { get; init; }
  public required string Status { get; init; }
  public required double UnderReinforcedAreaM2 { get; init; }
  public required int UnderReinforcedCells { get; init; }
  public required double MinMarginMm2PerM { get; init; }
  public required double ExcessSteelKg { get; init; }
}

/// <summary>
/// Axis-aligned extent of one connected under-reinforced region.
/// </summary>
public sealed record DeficitRegionReport
{
  public required string ZoneId { get; init; }
  public required double MinX { get; init; }
  public required double MinY { get; init; }
  public required double MaxX { get; init; }
  public required double MaxY { get; init; }
}
