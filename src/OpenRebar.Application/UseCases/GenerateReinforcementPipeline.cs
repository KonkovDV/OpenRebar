using OpenRebar.Domain.Exceptions;
using OpenRebar.Domain.Models;
using OpenRebar.Domain.Ports;
using OpenRebar.Domain.Rules;

namespace OpenRebar.Application.UseCases;

/// <summary>
/// Full pipeline: isoline file → parsed zones → classified zones → rebar layout → optimization → placement.
/// Orchestrates all domain ports to execute the complete workflow.
/// </summary>
public sealed class GenerateReinforcementPipeline
{
    private readonly IIsolineParser _dxfParser;
    private readonly IIsolineParser _pngParser;
    private readonly IZoneDetector _zoneDetector;
    private readonly IReinforcementCalculator _calculator;
    private readonly IRebarOptimizer _optimizer;
    private readonly ISupplierCatalogLoader _catalogLoader;
    private readonly IRevitPlacer _placer;
    private readonly IReportStore _reportStore;
    private readonly IStructuredLogger _logger;

    public GenerateReinforcementPipeline(
        IIsolineParser dxfParser,
        IIsolineParser pngParser,
        IZoneDetector zoneDetector,
        IReinforcementCalculator calculator,
        IRebarOptimizer optimizer,
        ISupplierCatalogLoader catalogLoader,
        IRevitPlacer placer,
        IReportStore reportStore,
        IStructuredLogger logger)
    {
        _dxfParser = dxfParser;
        _pngParser = pngParser;
        _zoneDetector = zoneDetector;
        _calculator = calculator;
        _optimizer = optimizer;
        _catalogLoader = catalogLoader;
        _placer = placer;
        _reportStore = reportStore;
        _logger = logger;
    }

    public async Task<PipelineResult> ExecuteAsync(
        PipelineInput input,
        CancellationToken cancellationToken = default)
    {
        var result = new PipelineResult();
        var failures = new List<PipelineFailureDiagnostic>();

        _logger.Info(
            "Starting reinforcement pipeline",
            ("projectCode", input.Metadata.ProjectCode),
            ("slabId", input.Metadata.SlabId),
            ("levelName", input.Metadata.LevelName),
            ("isolineFilePath", input.IsolineFilePath));

        // 1. Parse isoline file (CRITICAL - abort on failure)
        IReadOnlyList<ReinforcementZone> rawZones;
        try
        {
            var parser = GetParser(input.IsolineFilePath);
            rawZones = await parser.ParseAsync(
                input.IsolineFilePath,
                input.Legend,
                cancellationToken);
            result.ParsedZoneCount = rawZones.Count;
            _logger.Info("Parsed reinforcement zones", ("parsedZoneCount", result.ParsedZoneCount));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var diagnostic = new PipelineFailureDiagnostic
            {
                Stage = "Parse",
                ErrorMessage = ex.Message,
                ExceptionType = ex.GetType().Name,
                OccurredAtUtc = DateTimeOffset.UtcNow,
                StackTrace = ex.StackTrace,
                IsCritical = true
            };
            failures.Add(diagnostic);
            _logger.Error("Failed to parse isoline file; aborting pipeline", ex, ("filePath", input.IsolineFilePath));
            
            result.Report = BuildPartialReport(input, failures, result);
            if (input.PersistReport)
            {
                var outputPath = ResolveReportOutputPath(input);
                result.StoredReport = await _reportStore.SaveAsync(result.Report, outputPath, cancellationToken);
                _logger.Info("Stored partial reinforcement report after parse failure", ("outputPath", result.StoredReport.OutputPath));
            }
            return result;
        }

        // 2. Classify zones and decompose complex ones
        IReadOnlyList<ReinforcementZone> classifiedZones;
        try
        {
            classifiedZones = _zoneDetector.ClassifyAndDecompose(rawZones, input.Slab);
            result.ClassifiedZones = classifiedZones;
            _logger.Info("Classified reinforcement zones", ("classifiedZoneCount", classifiedZones.Count));

            var qualityDiagnostics = EvaluateDecompositionQuality(classifiedZones, input.DecompositionQualityGate);
            foreach (var diagnostic in qualityDiagnostics)
            {
                failures.Add(diagnostic);
                _logger.Warn(
                    "Geometry quality gate violation detected",
                    ("stage", diagnostic.Stage),
                    ("message", diagnostic.ErrorMessage),
                    ("isCritical", diagnostic.IsCritical));
            }

            if (qualityDiagnostics.Any(d => d.IsCritical))
            {
                _logger.Error("Critical geometry quality gate violation; aborting pipeline");
                result.Report = BuildPartialReport(input, failures, result);
                if (input.PersistReport)
                {
                    var outputPath = ResolveReportOutputPath(input);
                    result.StoredReport = await _reportStore.SaveAsync(result.Report, outputPath, cancellationToken);
                    _logger.Info("Stored partial reinforcement report after geometry quality gate failure", ("outputPath", result.StoredReport.OutputPath));
                }
                return result;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var diagnostic = new PipelineFailureDiagnostic
            {
                Stage = "ZoneDetection",
                ErrorMessage = ex.Message,
                ExceptionType = ex.GetType().Name,
                OccurredAtUtc = DateTimeOffset.UtcNow,
                StackTrace = ex.StackTrace,
                IsCritical = true
            };
            failures.Add(diagnostic);
            _logger.Error("Zone classification failed; aborting pipeline", ex);
            
            // Use raw zones as fallback
            classifiedZones = rawZones;
        }

        // 3. Calculate rebar layout per zone
        IReadOnlyList<ReinforcementZone> zonesWithRebars;
        try
        {
            zonesWithRebars = _calculator.CalculateRebars(classifiedZones, input.Slab);
            result.TotalRebarSegments = zonesWithRebars.Sum(z => z.Rebars.Count);
            _logger.Info(
                "Calculated reinforcement layout",
                ("totalRebarSegments", result.TotalRebarSegments),
                ("zoneCount", zonesWithRebars.Count));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var diagnostic = new PipelineFailureDiagnostic
            {
                Stage = "RebarCalculation",
                ErrorMessage = ex.Message,
                ExceptionType = ex.GetType().Name,
                OccurredAtUtc = DateTimeOffset.UtcNow,
                StackTrace = ex.StackTrace,
                IsCritical = true
            };
            failures.Add(diagnostic);
            _logger.Error("Rebar calculation failed; aborting pipeline", ex);
            
            result.Report = BuildPartialReport(input, failures, result);
            if (input.PersistReport)
            {
                var outputPath = ResolveReportOutputPath(input);
                result.StoredReport = await _reportStore.SaveAsync(result.Report, outputPath, cancellationToken);
                _logger.Info("Stored partial reinforcement report after calculation failure", ("outputPath", result.StoredReport.OutputPath));
            }
            return result;
        }

        // 4. Load supplier catalog (recoverable failure)
        SupplierCatalog catalog;
        try
        {
            catalog = input.SupplierCatalogPath is not null
                ? await _catalogLoader.LoadAsync(input.SupplierCatalogPath, cancellationToken)
                : _catalogLoader.GetDefaultCatalog();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var diagnostic = new PipelineFailureDiagnostic
            {
                Stage = "CatalogLoading",
                ErrorMessage = ex.Message,
                ExceptionType = ex.GetType().Name,
                OccurredAtUtc = DateTimeOffset.UtcNow,
                StackTrace = ex.StackTrace,
                IsCritical = false
            };
            failures.Add(diagnostic);
            _logger.Warn("Failed to load supplier catalog; using default", ("catalogPath", input.SupplierCatalogPath));
            
            catalog = _catalogLoader.GetDefaultCatalog();
        }

        // 5. Optimize cutting (group by diameter) - try to optimize each diameter
        var rebarsByDiameter = zonesWithRebars
            .SelectMany(z => z.Rebars)
            .GroupBy(r => r.DiameterMm);

        var optimizationResults = new Dictionary<int, OptimizationResult>();
        foreach (var group in rebarsByDiameter)
        {
            try
            {
                var lengths = group.Select(r => r.TotalLength).ToList();
                var optResult = _optimizer.Optimize(lengths, catalog.AvailableLengths, input.OptimizationSettings);
                optimizationResults[group.Key] = optResult;
                _logger.Info(
                    "Optimized cutting plan",
                    ("diameterMm", group.Key),
                    ("stockBarsNeeded", optResult.TotalStockBarsNeeded),
                    ("wastePercent", Math.Round(optResult.TotalWastePercent, 2)));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var diagnostic = new PipelineFailureDiagnostic
                {
                    Stage = $"Optimization(d{group.Key}mm)",
                    ErrorMessage = ex.Message,
                    ExceptionType = ex.GetType().Name,
                    OccurredAtUtc = DateTimeOffset.UtcNow,
                    StackTrace = ex.StackTrace,
                    IsCritical = false
                };
                failures.Add(diagnostic);
                _logger.Warn(
                    "Optimization failed for diameter; skipping",
                    ("diameterMm", group.Key),
                    ("message", ex.Message));
                
                // Create a fallback optimization result (no optimization, use all lengths from single in-stock stock).
                var inStockLengths = catalog.AvailableLengths
                    .Where(s => s.InStock)
                    .ToList();

                if (inStockLengths.Count == 0)
                {
                    var fallbackDiagnostic = new PipelineFailureDiagnostic
                    {
                        Stage = $"OptimizationFallback(d{group.Key}mm)",
                        ErrorMessage = "Fallback requires at least one in-stock bar length.",
                        ExceptionType = nameof(OptimizationException),
                        OccurredAtUtc = DateTimeOffset.UtcNow,
                        IsCritical = true
                    };
                    failures.Add(fallbackDiagnostic);

                    _logger.Warn(
                        "Fallback optimization cannot start: no in-stock stock lengths",
                        ("diameterMm", group.Key));

                    result.Report = BuildPartialReport(input, failures, result);
                    if (input.PersistReport)
                    {
                        var outputPath = ResolveReportOutputPath(input);
                        result.StoredReport = await _reportStore.SaveAsync(result.Report, outputPath, cancellationToken);
                        _logger.Info("Stored partial reinforcement report after fallback stock availability failure", ("outputPath", result.StoredReport.OutputPath));
                    }

                    return result;
                }

                var maxStockLength = inStockLengths.MaxBy(s => s.LengthMm)!.LengthMm;
                var cuts = group.Select(r => r.TotalLength).ToList();
                var totalLength = cuts.Sum();
                if (!TryBuildFallbackCuttingPlans(
                        cuts,
                        maxStockLength,
                        input.OptimizationSettings.SawCutWidthMm,
                        out var cuttingPlans,
                        out var infeasibleReason))
                {
                    var fallbackDiagnostic = new PipelineFailureDiagnostic
                    {
                        Stage = $"OptimizationFallback(d{group.Key}mm)",
                        ErrorMessage = infeasibleReason ?? "Fallback packing failed.",
                        ExceptionType = nameof(OptimizationException),
                        OccurredAtUtc = DateTimeOffset.UtcNow,
                        IsCritical = true
                    };
                    failures.Add(fallbackDiagnostic);

                    _logger.Warn(
                        "Fallback optimization became infeasible; aborting pipeline",
                        ("diameterMm", group.Key),
                        ("reason", fallbackDiagnostic.ErrorMessage));

                    result.Report = BuildPartialReport(input, failures, result);
                    if (input.PersistReport)
                    {
                        var outputPath = ResolveReportOutputPath(input);
                        result.StoredReport = await _reportStore.SaveAsync(result.Report, outputPath, cancellationToken);
                        _logger.Info("Stored partial reinforcement report after fallback optimization failure", ("outputPath", result.StoredReport.OutputPath));
                    }

                    return result;
                }

                var stocksNeeded = cuttingPlans.Count;
                
                optimizationResults[group.Key] = new OptimizationResult
                {
                    CuttingPlans = cuttingPlans,
                    TotalStockBarsNeeded = stocksNeeded,
                    TotalWasteMm = cuttingPlans.Sum(p => p.WasteMm),
                    TotalWastePercent = cuttingPlans.Sum(p => p.StockLengthMm) > 0
                        ? cuttingPlans.Sum(p => p.WasteMm) / cuttingPlans.Sum(p => p.StockLengthMm) * 100.0
                        : 0,
                    TotalRebarLengthMm = totalLength,
                    TotalMassKg = null,
                    EstimatedCost = null,
                    Provenance = null
                };
            }
        }
        result.OptimizationResults = optimizationResults;

        // 6. Place in Revit (if requested)
        if (input.PlaceInRevit)
        {
            try
            {
                var placementResult = await _placer.PlaceReinforcementAsync(
                    zonesWithRebars,
                    input.PlacementSettings,
                    cancellationToken);
                result.PlacementResult = placementResult;

                if (!placementResult.Success)
                {
                    _logger.Warn(
                        "Revit placement completed with warnings",
                        ("errorCount", placementResult.Errors.Count),
                        ("warningCount", placementResult.Warnings.Count));
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var diagnostic = new PipelineFailureDiagnostic
                {
                    Stage = "RevitPlacement",
                    ErrorMessage = ex.Message,
                    ExceptionType = ex.GetType().Name,
                    OccurredAtUtc = DateTimeOffset.UtcNow,
                    StackTrace = ex.StackTrace,
                    IsCritical = false
                };
                failures.Add(diagnostic);
                
                result.PlacementResult = new PlacementResult
                {
                    TotalRebarsPlaced = 0,
                    TotalTagsCreated = 0,
                    TotalBendingDetails = 0,
                    Errors = [$"Revit placement failed: {ex.Message}"]
                };

                _logger.Error(
                    "Revit placement failed; report persistence will continue",
                    ex,
                    ("projectCode", input.Metadata.ProjectCode),
                    ("slabId", input.Metadata.SlabId));
            }
        }

        result.Report = BuildReport(input, catalog.SupplierName, zonesWithRebars, result, failures);

        if (input.PersistReport)
        {
            var outputPath = ResolveReportOutputPath(input);
            result.StoredReport = await _reportStore.SaveAsync(result.Report, outputPath, cancellationToken);
            _logger.Info("Stored reinforcement report", ("outputPath", result.StoredReport.OutputPath), ("hasErrors", failures.Any()));
        }

        _logger.Info(
            "Pipeline completed",
            ("totalWastePercent", Math.Round(result.TotalWastePercent, 2)),
            ("totalMassKg", Math.Round(result.TotalMassKg, 2)),
            ("failureCount", failures.Count));

        return result;
    }

    private static bool TryBuildFallbackCuttingPlans(
        IReadOnlyList<double> cuts,
        double stockLengthMm,
        double sawCutWidthMm,
        out List<CuttingPlan> cuttingPlans,
        out string? infeasibleReason)
    {
        var bins = new List<(double UsedLengthMm, List<double> Cuts)>();

        foreach (double cut in cuts.OrderByDescending(length => length))
        {
            double effectiveLength = cut + sawCutWidthMm;
            if (effectiveLength > stockLengthMm + 1e-6)
            {
                cuttingPlans = [];
                infeasibleReason =
                    $"Fallback infeasible for cut {cut:F1} mm (effective {effectiveLength:F1} mm with saw cut) and stock length {stockLengthMm:F1} mm.";
                return false;
            }

            int binIndex = bins.FindIndex(bin => bin.UsedLengthMm + effectiveLength <= stockLengthMm + 1e-6);
            if (binIndex >= 0)
            {
                var bin = bins[binIndex];
                bin.UsedLengthMm += effectiveLength;
                bin.Cuts.Add(cut);
                bins[binIndex] = bin;
            }
            else
            {
                bins.Add((effectiveLength, [cut]));
            }
        }

        cuttingPlans = bins
            .Select(bin => new CuttingPlan
            {
                StockLengthMm = stockLengthMm,
                Cuts = bin.Cuts,
                SawCutWidthMm = sawCutWidthMm
            })
            .ToList();
        infeasibleReason = null;
        return true;
    }

    private static string ResolveReportOutputPath(PipelineInput input)
    {
        if (!string.IsNullOrWhiteSpace(input.ReportOutputPath))
            return input.ReportOutputPath;

        return Path.ChangeExtension(input.IsolineFilePath, ".result.json");
    }

    private static IReadOnlyList<PipelineFailureDiagnostic> EvaluateDecompositionQuality(
        IReadOnlyList<ReinforcementZone> zones,
        DecompositionQualityGateSettings gate)
    {
        if (!gate.Enabled)
            return [];

        var diagnostics = new List<PipelineFailureDiagnostic>();

        foreach (var zone in zones)
        {
            if (zone.ZoneType != ZoneType.Complex || zone.DecompositionMetrics is null)
                continue;

            var metrics = zone.DecompositionMetrics;
            var reasons = new List<string>();

            if (metrics.CoverageRatio < gate.MinCoverageRatio)
            {
                reasons.Add(
                    $"coverage ratio {metrics.CoverageRatio:F3} is below minimum {gate.MinCoverageRatio:F3}");
            }

            if (metrics.OverCoverageRatio > gate.MaxOverCoverageRatio)
            {
                reasons.Add(
                    $"over-coverage ratio {metrics.OverCoverageRatio:F3} exceeds maximum {gate.MaxOverCoverageRatio:F3}");
            }

            if (reasons.Count == 0)
                continue;

            diagnostics.Add(new PipelineFailureDiagnostic
            {
                Stage = "GeometryQualityGate",
                ErrorMessage = $"Zone {zone.Id}: {string.Join("; ", reasons)}",
                ExceptionType = "DecompositionQualityViolation",
                OccurredAtUtc = DateTimeOffset.UtcNow,
                IsCritical = gate.TreatViolationsAsCritical
            });
        }

        return diagnostics;
    }

    private static ReinforcementExecutionReport BuildReport(
        PipelineInput input,
        string supplierName,
        IReadOnlyList<ReinforcementZone> zonesWithRebars,
        PipelineResult result,
        IReadOnlyList<PipelineFailureDiagnostic> failures)
    {
        var slabBox = input.Slab.OuterBoundary.GetBoundingBox();
        var placement = result.PlacementResult;
        var estimatedCosts = result.OptimizationResults.Values
            .Where(o => o.EstimatedCost.HasValue)
            .Select(o => o.EstimatedCost!.Value)
            .ToList();

        return new ReinforcementExecutionReport
        {
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            Metadata = input.Metadata,
            NormativeProfile = new NormativeProfileExecutionReport
            {
                ProfileId = input.Metadata.NormativeProfileId,
                Jurisdiction = input.Metadata.CountryCode,
                DesignCode = input.Metadata.DesignCode,
                TablesVersion = input.Metadata.NormativeTablesVersion
            },
            AnalysisProvenance = BuildAnalysisProvenance(zonesWithRebars, result),
            IsolineFileName = Path.GetFileName(input.IsolineFilePath),
            IsolineFileFormat = Path.GetExtension(input.IsolineFilePath).TrimStart('.').ToLowerInvariant(),
            Slab = new SlabExecutionReport
            {
                ConcreteClass = input.Slab.ConcreteClass,
                ThicknessMm = input.Slab.ThicknessMm,
                CoverMm = input.Slab.CoverMm,
                EffectiveDepthMm = input.Slab.EffectiveDepthMm,
                AreaMm2 = input.Slab.OuterBoundary.CalculateArea(),
                OpeningCount = input.Slab.Openings.Count,
                BoundingBox = ToBoundingBoxReport(slabBox)
            },
            Zones = zonesWithRebars.Select(zone =>
            {
                var zoneBox = zone.Boundary.GetBoundingBox();
                return new ZoneExecutionReport
                {
                    ZoneId = zone.Id,
                    ZoneType = zone.ZoneType.ToString(),
                    Direction = zone.Direction.ToString(),
                    Layer = zone.Layer.ToString(),
                    DiameterMm = zone.Spec.DiameterMm,
                    SpacingMm = zone.Spec.SpacingMm,
                    RebarCount = zone.Rebars.Count,
                    TotalClearSpanMm = zone.Rebars.Sum(r => r.ClearSpan),
                    TotalLengthMm = zone.Rebars.Sum(r => r.TotalLength),
                    BoundingBox = ToBoundingBoxReport(zoneBox),
                    SubRectangleCount = zone.SubRectangles?.Count,
                    DecompositionCoverageRatio = zone.DecompositionMetrics?.CoverageRatio,
                    DecompositionOverCoverageRatio = zone.DecompositionMetrics?.OverCoverageRatio
                };
            }).ToList(),
            OptimizationByDiameter = result.OptimizationResults
                .OrderBy(kv => kv.Key)
                .Select(kv => new DiameterOptimizationExecutionReport
                {
                    DiameterMm = kv.Key,
                    SupplierName = supplierName,
                    RebarCount = zonesWithRebars.SelectMany(z => z.Rebars).Count(r => r.DiameterMm == kv.Key),
                    StockBarsNeeded = kv.Value.TotalStockBarsNeeded,
                    TotalWasteMm = kv.Value.TotalWasteMm,
                    TotalWastePercent = kv.Value.TotalWastePercent,
                    TotalRebarLengthMm = kv.Value.TotalRebarLengthMm,
                    TotalMassKg = kv.Value.TotalMassKg,
                    EstimatedCost = kv.Value.EstimatedCost,
                    DualBound = kv.Value.DualBound,
                    Gap = kv.Value.Gap,
                    CuttingPlans = kv.Value.CuttingPlans.Select(plan => new CuttingPlanExecutionReport
                    {
                        StockLengthMm = plan.StockLengthMm,
                        CutsMm = plan.Cuts,
                        SawCutWidthMm = plan.SawCutWidthMm,
                        WasteMm = plan.WasteMm,
                        WastePercent = plan.WastePercent
                    }).ToList()
                })
                .ToList(),
            Placement = new PlacementExecutionReport
            {
                Requested = input.PlaceInRevit,
                Executed = placement is not null,
                Success = placement?.Success ?? !input.PlaceInRevit,
                TotalRebarsPlaced = placement?.TotalRebarsPlaced ?? 0,
                TotalTagsCreated = placement?.TotalTagsCreated ?? 0,
                TotalBendingDetails = placement?.TotalBendingDetails ?? 0,
                Warnings = placement?.Warnings ?? [],
                Errors = placement?.Errors ?? []
            },
            Summary = new ExecutionSummaryReport
            {
                ParsedZoneCount = result.ParsedZoneCount,
                ClassifiedZoneCount = result.ClassifiedZones.Count,
                TotalRebarSegments = result.TotalRebarSegments,
                TotalWastePercent = result.TotalWastePercent,
                TotalWasteMm = result.OptimizationResults.Values.Sum(o => o.TotalWasteMm),
                TotalMassKg = result.TotalMassKg,
                EstimatedCost = estimatedCosts.Count > 0 ? estimatedCosts.Sum() : null
            },
            Warnings = placement?.Warnings ?? [],
            Errors = failures,
            PartialResult = failures.Any(f => f.IsCritical)
        };
    }

    /// <summary>
    /// Build a minimal report when pipeline aborts early due to critical failure.
    /// Includes diagnostic information but minimal execution details.
    /// </summary>
    private static ReinforcementExecutionReport BuildPartialReport(
        PipelineInput input,
        IReadOnlyList<PipelineFailureDiagnostic> failures,
        PipelineResult result)
    {
        return new ReinforcementExecutionReport
        {
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            Metadata = input.Metadata,
            NormativeProfile = new NormativeProfileExecutionReport
            {
                ProfileId = input.Metadata.NormativeProfileId,
                Jurisdiction = input.Metadata.CountryCode,
                DesignCode = input.Metadata.DesignCode,
                TablesVersion = input.Metadata.NormativeTablesVersion
            },
            AnalysisProvenance = new AnalysisProvenanceExecutionReport
            {
                Geometry = new GeometryProcessingExecutionReport
                {
                    DecompositionAlgorithm = "n/a",
                    RectangularShortcutFillRatio = 0,
                    MinRectangleAreaMm2 = 0,
                    SamplingResolutionPerAxis = 0,
                    CellCoverageInclusionThreshold = 0
                },
                Optimization = new OptimizationProcessingExecutionReport
                {
                    OptimizerId = "n/a",
                    MasterProblemStrategy = "n/a",
                    PricingStrategy = "n/a",
                    IntegerizationStrategy = "n/a",
                    DemandAggregationPrecisionMm = 0,
                    QualityFloor = "n/a",
                    AnyFallbackMasterSolverUsed = false
                }
            },
            IsolineFileName = Path.GetFileName(input.IsolineFilePath),
            IsolineFileFormat = Path.GetExtension(input.IsolineFilePath).TrimStart('.').ToLowerInvariant(),
            Slab = new SlabExecutionReport
            {
                ConcreteClass = input.Slab.ConcreteClass,
                ThicknessMm = input.Slab.ThicknessMm,
                CoverMm = input.Slab.CoverMm,
                EffectiveDepthMm = input.Slab.EffectiveDepthMm,
                AreaMm2 = input.Slab.OuterBoundary.CalculateArea(),
                OpeningCount = input.Slab.Openings.Count,
                BoundingBox = ToBoundingBoxReport(input.Slab.OuterBoundary.GetBoundingBox())
            },
            Zones = [],
            OptimizationByDiameter = [],
            Placement = new PlacementExecutionReport
            {
                Requested = false,
                Executed = false,
                Success = false,
                TotalRebarsPlaced = 0,
                TotalTagsCreated = 0,
                TotalBendingDetails = 0,
                Warnings = [],
                Errors = []
            },
            Summary = new ExecutionSummaryReport
            {
                ParsedZoneCount = result.ParsedZoneCount,
                ClassifiedZoneCount = 0,
                TotalRebarSegments = 0,
                TotalWastePercent = 0,
                TotalWasteMm = 0,
                TotalMassKg = 0,
                EstimatedCost = null
            },
            Warnings = [],
            Errors = failures,
            PartialResult = true
        };
    }

    private static BoundingBoxExecutionReport ToBoundingBoxReport(BoundingBox bbox) => new()
    {
        MinX = bbox.Min.X,
        MinY = bbox.Min.Y,
        MaxX = bbox.Max.X,
        MaxY = bbox.Max.Y,
        Width = bbox.Width,
        Height = bbox.Height
    };

    private static AnalysisProvenanceExecutionReport BuildAnalysisProvenance(
        IReadOnlyList<ReinforcementZone> zonesWithRebars,
        PipelineResult result)
    {
        var decompositionMetrics = zonesWithRebars
            .Select(zone => zone.DecompositionMetrics)
            .Where(metrics => metrics is not null)
            .Cast<PolygonDecompositionMetrics>()
            .ToList();

        var optimizationProvenances = result.OptimizationResults.Values
            .Select(o => o.Provenance)
            .Where(p => p is not null)
            .Cast<OptimizationProvenance>()
            .ToList();

        return new AnalysisProvenanceExecutionReport
        {
            Geometry = new GeometryProcessingExecutionReport
            {
                DecompositionAlgorithm = "adaptive-orthogonal-strip-or-grid/v3",
                RectangularShortcutFillRatio = PolygonDecomposition.RectangularFillRatioThreshold,
                MinRectangleAreaMm2 = PolygonDecomposition.DefaultMinRectangleAreaMm2,
                SamplingResolutionPerAxis = PolygonDecomposition.CoverageSamplingResolutionPerAxis,
                CellCoverageInclusionThreshold = PolygonDecomposition.CellCoverageInclusionThreshold,
                MinCoverageRatioAcrossComplexZones = decompositionMetrics.Count > 0
                    ? decompositionMetrics.Min(m => m.CoverageRatio)
                    : null,
                MaxOverCoverageRatioAcrossComplexZones = decompositionMetrics.Count > 0
                    ? decompositionMetrics.Max(m => m.OverCoverageRatio)
                    : null
            },
            Optimization = new OptimizationProcessingExecutionReport
            {
                OptimizerId = ResolveProvenanceString(optimizationProvenances.Select(p => p.OptimizerId), fallback: "none"),
                MasterProblemStrategy = ResolveProvenanceString(optimizationProvenances.Select(p => p.MasterProblemStrategy), fallback: "none"),
                PricingStrategy = ResolveProvenanceString(optimizationProvenances.Select(p => p.PricingStrategy), fallback: "none"),
                IntegerizationStrategy = ResolveProvenanceString(optimizationProvenances.Select(p => p.IntegerizationStrategy), fallback: "none"),
                DemandAggregationPrecisionMm = optimizationProvenances.Count > 0
                    ? optimizationProvenances.Max(p => p.DemandAggregationPrecisionMm)
                    : 0,
                QualityFloor = ResolveProvenanceString(optimizationProvenances.Select(p => p.QualityFloor), fallback: "none"),
                AnyFallbackMasterSolverUsed = optimizationProvenances.Any(p => p.UsedFallbackMasterSolver)
            }
        };
    }

    private static string ResolveProvenanceString(IEnumerable<string> values, string fallback)
    {
        var distinct = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return distinct.Count switch
        {
            0 => fallback,
            1 => distinct[0],
            _ => "mixed"
        };
    }

    private IIsolineParser GetParser(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        if (SupportsExtension(_dxfParser, ext))
            return _dxfParser;

        if (SupportsExtension(_pngParser, ext))
            return _pngParser;

        throw new InvalidIsolineFileException(filePath, $"Unsupported isoline file format: {ext}");
    }

    private static bool SupportsExtension(IIsolineParser parser, string ext)
    {
        foreach (var supportedExtension in parser.SupportedExtensions)
        {
            if (string.Equals(supportedExtension, ext, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}

/// <summary>
/// Input data for the reinforcement generation pipeline.
/// </summary>
public sealed record PipelineInput
{
    public required string IsolineFilePath { get; init; }
    public required ColorLegend Legend { get; init; }
    public required SlabGeometry Slab { get; init; }
    public PipelineExecutionMetadata Metadata { get; init; } = new();
    public string? SupplierCatalogPath { get; init; }
    public string? ReportOutputPath { get; init; }
    public OptimizationSettings OptimizationSettings { get; init; } = new();
    public DecompositionQualityGateSettings DecompositionQualityGate { get; init; } = new();
    public PlacementSettings PlacementSettings { get; init; } = new();
    public bool PlaceInRevit { get; init; } = true;
    public bool PersistReport { get; init; }
}

/// <summary>
/// Acceptance gate for polygon decomposition quality metrics.
/// Allows warning-only operation or fail-fast behavior for strict lanes.
/// </summary>
public sealed record DecompositionQualityGateSettings
{
    public bool Enabled { get; init; } = true;
    public double MinCoverageRatio { get; init; } = 0.94;
    public double MaxOverCoverageRatio { get; init; } = 0.25;
    public bool TreatViolationsAsCritical { get; init; }
}

/// <summary>
/// Pipeline execution result with all intermediate data.
/// </summary>
public sealed class PipelineResult
{
    public int ParsedZoneCount { get; set; }
    public IReadOnlyList<ReinforcementZone> ClassifiedZones { get; set; } = [];
    public int TotalRebarSegments { get; set; }
    public Dictionary<int, OptimizationResult> OptimizationResults { get; set; } = new();
    public PlacementResult? PlacementResult { get; set; }
    public ReinforcementExecutionReport? Report { get; set; }
    public StoredReportReference? StoredReport { get; set; }

    public double TotalWastePercent =>
        OptimizationResults.Values.Any()
            ? CalculateWeightedWastePercent(OptimizationResults.Values)
            : 0;

    public double TotalMassKg =>
        OptimizationResults.Values
            .Where(o => o.TotalMassKg.HasValue)
            .Sum(o => o.TotalMassKg!.Value);

    private static double CalculateWeightedWastePercent(IEnumerable<OptimizationResult> results)
    {
        double totalWaste = results.Sum(o => o.TotalWasteMm);
        double totalPurchasedLength = results
            .SelectMany(o => o.CuttingPlans)
            .Sum(p => p.StockLengthMm);

        return totalPurchasedLength > 0
            ? totalWaste / totalPurchasedLength * 100.0
            : 0;
    }
}
