using OpenRebar.Domain.Rules;

namespace OpenRebar.Domain.Models;

/// <summary>
/// Metadata describing the execution context of the reinforcement pipeline.
/// Designed for downstream BIM / integration consumers that need stable identifiers.
/// </summary>
public sealed record PipelineExecutionMetadata
{
  public string ProjectCode { get; init; } = "UNSPECIFIED";
  public string SlabId { get; init; } = "SLAB-UNSPECIFIED";
  public string? LevelName { get; init; }
  public string SourceSystem { get; init; } = "OpenRebar.Reinforcement";
  public string TargetSystem { get; init; } = "AeroBIM";
  public string CountryCode { get; init; } = "RU";
  public string DesignCode { get; init; } = "SP 63.13330.2018";
  public string NormativeProfileId { get; init; } = NormativeProfiles.DefaultProfileId;
  public string NormativeTablesVersion { get; init; } = NormativeProfiles.DefaultTablesVersion;
}

/// <summary>
/// Machine-readable execution report for downstream persistence and BIM exchange.
/// </summary>
public sealed record ReinforcementExecutionReport
{
  public string ContractId { get; init; } = "OpenRebar.reinforcement.report.v2";
  public string SchemaVersion { get; init; } = "2.0.0";
  public required DateTimeOffset GeneratedAtUtc { get; init; }
  public required PipelineExecutionMetadata Metadata { get; init; }
  public required NormativeProfileExecutionReport NormativeProfile { get; init; }
  public required AnalysisProvenanceExecutionReport AnalysisProvenance { get; init; }
  public required string IsolineFileName { get; init; }
  public required string IsolineFileFormat { get; init; }
  public required SlabExecutionReport Slab { get; init; }
  public required IReadOnlyList<ZoneExecutionReport> Zones { get; init; }
  public required IReadOnlyList<DiameterOptimizationExecutionReport> OptimizationByDiameter { get; init; }
  public required PlacementExecutionReport Placement { get; init; }
  public required ExecutionSummaryReport Summary { get; init; }
  public IReadOnlyList<string> Warnings { get; init; } = [];
  public IReadOnlyList<PipelineFailureDiagnostic> Errors { get; init; } = [];
  public IReadOnlyList<UnoptimizedBarReport> UnoptimizedBars { get; init; } = [];
  public IReadOnlyList<PositionExecutionReport> Positions { get; init; } = [];
  public IReadOnlyList<StageExecutionReport> Stages { get; init; } = [];
  public ReinforcementVerificationResult? Verification { get; init; }
  public bool RequiresEngineerReview { get; init; } = true;
  public ProfileStamp Profile { get; init; } = ProfileStamp.Unspecified;
  public InputSourceStamp InputSource { get; init; } = InputSourceStamp.Unknown;
  public IReadOnlyList<LayerExecutionReport> Layers { get; init; } = LayerExecutionReport.NoneProvided;
  public IReadOnlyList<ParameterSourceReport> ParameterSources { get; init; } = [];
  public IReadOnlyList<ClashReport> Clashes { get; init; } = [];
  public bool PartialResult { get; init; }  // true if pipeline aborted early due to critical error
}

/// <summary>
/// One clearance or clash finding. Type is <c>hard</c> or <c>soft</c>.
/// </summary>
public sealed record ClashReport
{
  public required string Type { get; init; }
  public required string Kind { get; init; }
  public required double X { get; init; }
  public required double Y { get; init; }
  public required IReadOnlyList<string> BarIds { get; init; }
}

/// <summary>
/// Where one value that affected the run came from.
/// </summary>
public sealed record ParameterSourceReport
{
  public required string Path { get; init; }
  public required string Value { get; init; }
  public required string Source { get; init; }
}

/// <summary>
/// Company profile identity. Unspecified until a profile file is loaded.
/// </summary>
public sealed record ProfileStamp(string Id, string Version, string? Sha256)
{
  public static ProfileStamp Unspecified { get; } = new("unspecified", "0", null);
}

/// <summary>
/// Which adapter produced the drawing input, and the hash of that file.
/// </summary>
public sealed record InputSourceStamp(string AdapterId, string Version, string? Sha256)
{
  public static InputSourceStamp Unknown { get; } = new("unknown", "0", null);
}

/// <summary>
/// One of the four design layers. Missing drawings stay in the report as NotProvided.
/// </summary>
public sealed record LayerExecutionReport
{
  public static IReadOnlyList<LayerExecutionReport> NoneProvided { get; } =
  [
      Empty("BottomX"),
      Empty("BottomY"),
      Empty("TopX"),
      Empty("TopY")
  ];

  public required string Layer { get; init; }
  public required string Status { get; init; }
  public LayerBackgroundReport? Background { get; init; }
  public IReadOnlyList<string> Classes { get; init; } = [];
  public IReadOnlyList<string> ZoneIds { get; init; } = [];
  public IReadOnlyList<PositionExecutionReport> Positions { get; init; } = [];
  public double MassKg { get; init; }

  private static LayerExecutionReport Empty(string layer) => new()
  {
    Layer = layer,
    Status = "NotProvided"
  };
}

public sealed record LayerBackgroundReport
{
  public required int DiameterMm { get; init; }
  public required int SpacingMm { get; init; }
  public required string SteelClass { get; init; }
  public required double GridOriginMm { get; init; }
}

public sealed record StageExecutionReport
{
  public required string Name { get; init; }
  public required string Status { get; init; }
  public required bool Complete { get; init; }
  public bool BudgetExhausted { get; init; }
  public bool PartialResults { get; init; }
  public double DurationMs { get; init; }
  public string? AlgorithmId { get; init; }
  public required StageCounters Counters { get; init; }
  public IReadOnlyList<StageReasonReport> Reasons { get; init; } = [];
}

public sealed record StageCounters
{
  public int Processed { get; init; }
  public int? Max { get; init; }
}

public sealed record StageReasonReport
{
  public required string Code { get; init; }
  public bool Retryable { get; init; }
  public required string Message { get; init; }
  public string? Context { get; init; }
}

/// <summary>
/// One schedule position: bars that share steel class, diameter, shape, and rounded length.
/// Layer is reported but is not part of the default position key.
/// </summary>
public sealed record PositionExecutionReport
{
  public required string Mark { get; init; }
  public required int DiameterMm { get; init; }
  public required string SteelClass { get; init; }
  public required string ShapeCode { get; init; }
  public required double LengthMm { get; init; }
  public required int Quantity { get; init; }
  public required double MassPerPieceKg { get; init; }
  public required double TotalMassKg { get; init; }
  public required string Layer { get; init; }
}

/// <summary>
/// A bar that was detailed but excluded from cutting because it exceeds every in-stock length.
/// </summary>
public sealed record UnoptimizedBarReport
{
  public required int DiameterMm { get; init; }
  public required double LengthMm { get; init; }
  public required double MaxStockLengthMm { get; init; }
  public required string Reason { get; init; }
}

public sealed record NormativeProfileExecutionReport
{
  public required string ProfileId { get; init; }
  public string Id => ProfileId;
  public required string Jurisdiction { get; init; }
  public required string DesignCode { get; init; }
  public required string TablesVersion { get; init; }
  public string Version => TablesVersion;
  public double TopBarAnchorageFactor { get; init; } = 1.0;
}

public sealed record AnalysisProvenanceExecutionReport
{
  public required GeometryProcessingExecutionReport Geometry { get; init; }
  public required OptimizationProcessingExecutionReport Optimization { get; init; }
}

public sealed record GeometryProcessingExecutionReport
{
  public required string DecompositionAlgorithm { get; init; }
  public required double RectangularShortcutFillRatio { get; init; }
  public required double MinRectangleAreaMm2 { get; init; }
  public required int SamplingResolutionPerAxis { get; init; }
  public required double CellCoverageInclusionThreshold { get; init; }
  public double? MinCoverageRatioAcrossComplexZones { get; init; }
  public double? MaxOverCoverageRatioAcrossComplexZones { get; init; }
  public int ParsedEntityCount { get; init; }
  public IReadOnlyDictionary<string, int> IgnoredByReason { get; init; } = new Dictionary<string, int>();
}

public sealed record OptimizationProcessingExecutionReport
{
  public required string OptimizerId { get; init; }
  public required string MasterProblemStrategy { get; init; }
  public required string PricingStrategy { get; init; }
  public required string IntegerizationStrategy { get; init; }
  public required double DemandAggregationPrecisionMm { get; init; }
  public required string QualityFloor { get; init; }
  public required bool AnyFallbackMasterSolverUsed { get; init; }
  public bool FallbackUsed { get; init; }
}

public sealed record SlabExecutionReport
{
  public required string ConcreteClass { get; init; }
  public required double ThicknessMm { get; init; }
  public required double CoverMm { get; init; }
  public required double EffectiveDepthMm { get; init; }
  public required double AreaMm2 { get; init; }
  public required int OpeningCount { get; init; }
  public required BoundingBoxExecutionReport BoundingBox { get; init; }
}

public sealed record ZoneExecutionReport
{
  public required string ZoneId { get; init; }
  public required string ZoneType { get; init; }
  public required string Direction { get; init; }
  public required string Layer { get; init; }
  public required int DiameterMm { get; init; }
  public required int SpacingMm { get; init; }
  public required int RebarCount { get; init; }
  public required double TotalClearSpanMm { get; init; }
  public required double TotalLengthMm { get; init; }
  public required BoundingBoxExecutionReport BoundingBox { get; init; }
  public int? SubRectangleCount { get; init; }
  public double? DecompositionCoverageRatio { get; init; }
  public double? DecompositionOverCoverageRatio { get; init; }
  public int ExtendedBeyondZoneCount { get; init; }
}

public sealed record BoundingBoxExecutionReport
{
  public required double MinX { get; init; }
  public required double MinY { get; init; }
  public required double MaxX { get; init; }
  public required double MaxY { get; init; }
  public required double Width { get; init; }
  public required double Height { get; init; }
}

public sealed record DiameterOptimizationExecutionReport
{
  public required int DiameterMm { get; init; }
  public required string SupplierName { get; init; }
  public required int RebarCount { get; init; }
  public required int StockBarsNeeded { get; init; }
  public required double TotalWasteMm { get; init; }
  public required double TotalWastePercent { get; init; }
  public required double TotalRebarLengthMm { get; init; }
  public double? TotalMassKg { get; init; }
  public double? EstimatedCost { get; init; }
  public double? DualBound { get; init; }
  public double? Gap { get; init; }
  public string BoundStatus { get; init; } = BoundStatuses.NotProven;
  public required IReadOnlyList<CuttingPlanExecutionReport> CuttingPlans { get; init; }
}

public sealed record CuttingPlanExecutionReport
{
  public required double StockLengthMm { get; init; }
  public required IReadOnlyList<double> CutsMm { get; init; }
  public required double SawCutWidthMm { get; init; }
  public required double WasteMm { get; init; }
  public required double WastePercent { get; init; }
}

public sealed record PlacementExecutionReport
{
  public required bool Requested { get; init; }
  public required bool Executed { get; init; }
  public required bool Success { get; init; }
  public required int TotalRebarsPlaced { get; init; }
  public required int TotalTagsCreated { get; init; }
  public required int TotalBendingDetails { get; init; }
  public IReadOnlyList<string> Warnings { get; init; } = [];
  public IReadOnlyList<string> Errors { get; init; } = [];
}

public sealed record ExecutionSummaryReport
{
  public required int ParsedZoneCount { get; init; }
  public required int ClassifiedZoneCount { get; init; }
  public required int TotalRebarSegments { get; init; }
  public required double TotalWastePercent { get; init; }
  public required double TotalWasteMm { get; init; }
  public required double TotalMassKg { get; init; }

  /// <summary>Mass of detailed pieces, including bars excluded from cutting.</summary>
  public double MassInstalledKg { get; init; }

  /// <summary>Mass of purchased stock bars.</summary>
  public double MassPurchasedKg { get; init; }

  public double? EstimatedCost { get; init; }
}

/// <summary>
/// Diagnostic entry for pipeline failures and recoverable errors.
/// Allows partial results when non-critical stages fail.
/// </summary>
public sealed record PipelineFailureDiagnostic
{
  public required string Stage { get; init; }  // e.g., "Parse", "ZoneDetection", "Optimization"
  public required string ErrorMessage { get; init; }
  public required string ExceptionType { get; init; }
  public required DateTimeOffset OccurredAtUtc { get; init; }
  public string? StackTrace { get; init; }
  public bool IsCritical { get; init; }  // true = pipeline aborted, false = skipped stage
}
