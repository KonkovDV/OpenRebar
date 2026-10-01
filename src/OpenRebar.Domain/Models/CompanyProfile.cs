namespace OpenRebar.Domain.Models;

/// <summary>
/// Organization settings. Behavior comes from these fields, never from the profile id.
/// </summary>
public sealed record CompanyProfile
{
  public required string Id { get; init; }
  public required string Version { get; init; }
  public string? Extends { get; init; }
  public bool Example { get; init; }
  public string? Description { get; init; }
  public required string SteelClass { get; init; }
  public required string ConcreteClass { get; init; }
  public required AdditionalReinforcementProfile Additional { get; init; }
  public required EndConditionProfile Ends { get; init; }
  public required LapProfile Laps { get; init; }
  public required PositionProfile Positions { get; init; }
  public required ScheduleProfile Schedule { get; init; }
  public required SupplyProfile Supply { get; init; }
  public required SmoothingProfile Smoothing { get; init; }
  public required VerificationProfile Verification { get; init; }
  public required SafetyProfile Safety { get; init; }
  public required IReadOnlyList<LegendSwatch> Legend { get; init; }
}

public sealed record AdditionalReinforcementProfile
{
  public required IReadOnlyList<int> DiametersMm { get; init; }
  public required string SpacingMode { get; init; }
  public int? MaxDiameterCount { get; init; }
}

public sealed record EndConditionProfile
{
  public required string Condition { get; init; }

  /// <summary>internal keeps codes 00, H, L, and U. Other catalogs are not loaded.</summary>
  public string ShapeStandard { get; init; } = "internal";
}

public sealed record LapProfile
{
  public required double JointRatioMax { get; init; }
  public required bool Couplers { get; init; }
}

public sealed record PositionProfile
{
  public required bool IncludeLayerInKey { get; init; }
}

public sealed record ScheduleProfile
{
  public required string Culture { get; init; }

  /// <summary>gost-21.501 is the specification plus the steel-mass sheet. Other templates are not loaded.</summary>
  public string Template { get; init; } = "gost-21.501";
}

public sealed record SupplyProfile
{
  public required string SupplierName { get; init; }
  public required IReadOnlyList<int> StockLengthsMm { get; init; }
  public required bool SpecialLengths { get; init; }
  public required bool Offcuts { get; init; }
}

public sealed record SmoothingProfile
{
  public required bool Allowed { get; init; }
}

public sealed record VerificationProfile
{
  public required double UnderCoverageRatio { get; init; }
}

public sealed record SafetyProfile
{
  public required double Factor { get; init; }
  public required double AnchorageLengthFactor { get; init; }
  public required double LapLengthFactor { get; init; }
  public double? MaxSpacingMm { get; init; }
}

public sealed record LegendSwatch
{
  public required IReadOnlyList<int> Color { get; init; }
  public required int DiameterMm { get; init; }
  public required int SpacingMm { get; init; }
}
