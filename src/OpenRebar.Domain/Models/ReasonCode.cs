namespace OpenRebar.Domain.Models;

/// <summary>
/// Stable reason codes written to the report as snake_case.
/// </summary>
public enum ReasonCode
{
  WorkBudget,
  TimeLimit,
  StructuralLimit,
  BarExceedsMaxStock,
  HoleIgnored,
  LayerNotSpecified,
  RasterNotCalibrated,
  LegendInvalid,
  CgNotConverged,
  FallbackUsed,
  VerificationFailed,
  NeedsHumanDecision
}

public static class ReasonCodeWire
{
  public static string ToWire(this ReasonCode code) => code switch
  {
    ReasonCode.WorkBudget => "work_budget",
    ReasonCode.TimeLimit => "time_limit",
    ReasonCode.StructuralLimit => "structural_limit",
    ReasonCode.BarExceedsMaxStock => "bar_exceeds_max_stock",
    ReasonCode.HoleIgnored => "hole_ignored",
    ReasonCode.LayerNotSpecified => "layer_not_specified",
    ReasonCode.RasterNotCalibrated => "raster_not_calibrated",
    ReasonCode.LegendInvalid => "legend_invalid",
    ReasonCode.CgNotConverged => "cg_not_converged",
    ReasonCode.FallbackUsed => "fallback_used",
    ReasonCode.VerificationFailed => "verification_failed",
    ReasonCode.NeedsHumanDecision => "needs_human_decision",
    _ => "structural_limit"
  };
}

public static class BoundStatuses
{
  public const string Proven = "Proven";
  public const string NotProven = "NotProven";
}
