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
    public string NormativeProfileId { get; init; } = "ru.sp63.2018";
    public string NormativeTablesVersion { get; init; } = "embedded.sp63.v1";
}

/// <summary>
/// Machine-readable execution report for downstream persistence and BIM exchange.
/// </summary>
public sealed record ReinforcementExecutionReport
{
    public string ContractId { get; init; } = "OpenRebar.reinforcement.report.v1";
    public string SchemaVersion { get; init; } = "1.0.0";
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
}

public sealed record NormativeProfileExecutionReport
{
    public required string ProfileId { get; init; }
    public required string Jurisdiction { get; init; }
    public required string DesignCode { get; init; }
    public required string TablesVersion { get; init; }
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
    public required IReadOnlyList<CuttingPlanExecutionReport> CuttingPlans { get; init; }
}

public sealed record CuttingPlanExecutionReport
{
    public required double StockLengthMm { get; init; }
    public required IReadOnlyList<double> CutsMm { get; init; }
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
    public double? EstimatedCost { get; init; }
}