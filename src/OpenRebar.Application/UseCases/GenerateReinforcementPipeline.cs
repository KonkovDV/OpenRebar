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
  private readonly IReinforcementVerifier _verifier;
  private readonly IPlanarGeometry? _planar;

  public GenerateReinforcementPipeline(
      IIsolineParser dxfParser,
      IIsolineParser pngParser,
      IZoneDetector zoneDetector,
      IReinforcementCalculator calculator,
      IRebarOptimizer optimizer,
      ISupplierCatalogLoader catalogLoader,
      IRevitPlacer placer,
      IReportStore reportStore,
      IStructuredLogger logger,
      IReinforcementVerifier? verifier = null,
      IPlanarGeometry? planar = null)
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
    _verifier = verifier ?? new GridReinforcementVerifier();
    _planar = planar;
  }

  public async Task<PipelineResult> ExecuteAsync(
      PipelineInput input,
      CancellationToken cancellationToken = default)
  {
    var result = new PipelineResult();
    var failures = new List<PipelineFailureDiagnostic>();
    var stages = new StageRecorder();
    PipelineResult Finish()
    {
      result.Stages = stages.Seal();
      if (result.Report is not null)
        result.Report = result.Report with { Stages = result.Stages };
      return result;
    }

    ReinforcementExecutionReport Publish(ReinforcementExecutionReport report)
    {
      result.Stages = stages.Seal();
      return report with { Stages = result.Stages };
    }

    _logger.Info(
        "Starting reinforcement pipeline",
        ("projectCode", input.Metadata.ProjectCode),
        ("slabId", input.Metadata.SlabId),
        ("levelName", input.Metadata.LevelName),
        ("isolineFilePath", input.IsolineFilePath));

    // 1. Parse isoline file (CRITICAL - abort on failure)
    IReadOnlyList<ReinforcementZone> rawZones;
    bool layersAlreadyAssigned = input.Layers.Count > 0;
    try
    {
      if (input.AsField is not null)
      {
        string steel = input.Legend.Entries.FirstOrDefault()?.Spec.SteelClass ?? "A500C";
        var layout = AsFieldZoneBuilder.Build(
            input.AsField,
            input.Slab,
            new AsFieldLayoutOptions
            {
              SteelClass = steel,
              DiametersMm = input.FieldDiametersMm,
              SpacingsMm = input.FieldSpacingsMm
            });
        rawZones = layout.Zones;
        result.ParsedZoneCount = rawZones.Count;
        result.ParseStats = IsolineParseStats.Empty;
        result.FieldWarnings = input.AsField.Warnings.Concat(layout.Warnings).ToList();
      }
      else if (layersAlreadyAssigned)
      {
        rawZones = await ParseDesignLayersAsync(input, result, cancellationToken);
      }
      else
      {
        var parser = GetParser(input.IsolineFilePath);
        rawZones = await parser.ParseAsync(
            input.IsolineFilePath,
            input.Legend,
            cancellationToken);
        result.ParsedZoneCount = rawZones.Count;
        result.ParseStats = parser.Stats ?? IsolineParseStats.Empty;
      }
      var parseReasons = new List<StageReasonReport>();
      if (Count(result.ParseStats ?? IsolineParseStats.Empty, "holeIgnored") > 0)
      {
        parseReasons.Add(StageRecorder.Reason(
            ReasonCode.HoleIgnored,
            "Hole loops were counted and left out of the zone geometry."));
      }

      stages.Complete(
          "Parse",
          Path.GetExtension(input.IsolineFilePath).TrimStart('.').ToLowerInvariant(),
          processed: rawZones.Count,
          reasons: parseReasons,
          partial: parseReasons.Count > 0);
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
        StackTrace = input.IncludeDiagnostics ? ex.StackTrace : null,
        IsCritical = true
      };
      failures.Add(diagnostic);
      stages.Fail("Parse", StageRecorder.Reason(ParseFailureCode(ex), ex.Message, context: ex.GetType().Name));
      _logger.Error("Failed to parse isoline file; aborting pipeline", ex, ("filePath", input.IsolineFilePath));

      result.Report = Publish(BuildPartialReport(input, failures, result));
      if (input.PersistReport)
      {
        var outputPath = ResolveReportOutputPath(input);
        result.StoredReport = await _reportStore.SaveAsync(result.Report, outputPath, cancellationToken);
        _logger.Info("Stored partial reinforcement report after parse failure", ("outputPath", result.StoredReport.OutputPath));
      }
      return Finish();
    }

    if (!layersAlreadyAssigned && !TryAssignLayers(rawZones, input, out string? layerError))
    {
      var diagnostic = new PipelineFailureDiagnostic
      {
        Stage = "Layer",
        ErrorMessage = layerError ?? "Layer is not specified.",
        ExceptionType = "LayerNotSpecified",
        OccurredAtUtc = DateTimeOffset.UtcNow,
        IsCritical = true
      };
      failures.Add(diagnostic);
      stages.Fail("Layer", StageRecorder.Reason(ReasonCode.LayerNotSpecified, layerError ?? "Layer is not specified."));
      _logger.Warn("Layer was not specified", ("filePath", input.IsolineFilePath));
      result.Report = Publish(BuildPartialReport(input, failures, result));
      if (input.PersistReport)
      {
        var outputPath = ResolveReportOutputPath(input);
        result.StoredReport = await _reportStore.SaveAsync(result.Report, outputPath, cancellationToken);
      }

      return Finish();
    }

    stages.Complete("Layer", processed: rawZones.Count);

    // 2. Classify zones and decompose complex ones
    IReadOnlyList<ReinforcementZone> classifiedZones;
    try
    {
      classifiedZones = _zoneDetector.ClassifyAndDecompose(
          rawZones,
          input.Slab,
          input.UseLegacyDirectionHeuristic && input.Layer is null);
      result.ClassifiedZones = classifiedZones;
      foreach (var zone in classifiedZones.Where(zone => zone.DirectionInferredFromGeometry))
      {
        failures.Add(new PipelineFailureDiagnostic
        {
          Stage = "Layer",
          ErrorMessage = $"Direction of zone {zone.Id} was inferred from its bounding box.",
          ExceptionType = "DirectionInferredFromGeometry",
          OccurredAtUtc = DateTimeOffset.UtcNow,
          IsCritical = false
        });
      }

      _logger.Info("Classified reinforcement zones", ("classifiedZoneCount", classifiedZones.Count));

      if (_planar is not null)
      {
        var overlay = LayerClassOverlay.Apply(classifiedZones, _planar);
        classifiedZones = overlay.Zones;
        result.ClassifiedZones = classifiedZones;
        result.OverlayWarnings = overlay.Warnings;
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
        StackTrace = input.IncludeDiagnostics ? ex.StackTrace : null,
        IsCritical = true
      };
      failures.Add(diagnostic);
      stages.Fail("ZoneDetection", StageRecorder.Reason(ReasonCode.StructuralLimit, ex.Message, context: ex.GetType().Name));
      _logger.Error("Zone classification failed; aborting pipeline", ex);

      result.Report = Publish(BuildPartialReport(input, failures, result));
      if (input.PersistReport)
      {
        var outputPath = ResolveReportOutputPath(input);
        result.StoredReport = await _reportStore.SaveAsync(result.Report, outputPath, cancellationToken);
      }

      return Finish();
    }

    stages.Complete("ZoneDetection", "bar-runs/v1", processed: classifiedZones.Count);

    var layoutPlan = BackgroundLayoutPlanner.Apply(
        classifiedZones,
        input.Layers,
        input.Slab,
        input.FieldDiametersMm,
        input.AdditionalSpacingMode);
    classifiedZones = layoutPlan.Zones;
    result.ClassifiedZones = classifiedZones;
    result.LayoutWarnings = layoutPlan.Warnings;
    foreach (string warning in layoutPlan.Warnings)
    {
      failures.Add(new PipelineFailureDiagnostic
      {
        Stage = "RebarCalculation",
        ErrorMessage = warning,
        ExceptionType = "NeedsHumanDecision",
        OccurredAtUtc = DateTimeOffset.UtcNow,
        IsCritical = true
      });
    }

    // 3. Load the catalog before detailing so a long bar is cut to stock.
    SupplierCatalog catalog;
    try
    {
      catalog = input.SupplierCatalogPath is not null
          ? await _catalogLoader.LoadAsync(input.SupplierCatalogPath, cancellationToken)
          : input.SupplierCatalog ?? _catalogLoader.GetDefaultCatalog();
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
      failures.Add(new PipelineFailureDiagnostic
      {
        Stage = "CatalogLoading",
        ErrorMessage = ex.Message,
        ExceptionType = ex.GetType().Name,
        OccurredAtUtc = DateTimeOffset.UtcNow,
        StackTrace = input.IncludeDiagnostics ? ex.StackTrace : null,
        IsCritical = false
      });
      _logger.Warn("Failed to load supplier catalog; using default", ("catalogPath", input.SupplierCatalogPath));
      catalog = _catalogLoader.GetDefaultCatalog();
    }

    // 4. Calculate rebar layout per zone
    IReadOnlyList<ReinforcementZone> zonesWithRebars;
    try
    {
      foreach (var zone in classifiedZones)
        zone.RequestedEndCondition = string.IsNullOrWhiteSpace(input.EndCondition) ? "NeedsHook" : input.EndCondition;
      zonesWithRebars = _calculator.CalculateRebars(classifiedZones, input.Slab);
      var lapLinks = new List<LapLink>();
      var lapWarnings = new List<string>();
      if (input.Couplers)
        lapWarnings.Add("Couplers are selected and lap splices are not modeled.");
      else
      {
        var stock = catalog.AvailableLengths.Where(length => length.InStock).Select(length => (double)length.LengthMm).ToList();
        LapPlanner.Apply(
            zonesWithRebars,
            input.Slab.ConcreteClass,
            stock,
            input.OptimizationSettings.SawCutWidthMm,
            input.JointRatioMax,
            lapLinks,
            lapWarnings);
      }

      result.DetailingWarnings = DetailingNotes(zonesWithRebars)
          .Concat(SlabEdges.Warnings(input.Slab))
          .Concat(lapWarnings)
          .ToList();
      result.Positions = PositionAssigner.Assign(zonesWithRebars);
      result.Laps = lapLinks
          .Select((link, index) => new LapJointReport
          {
            LapId = (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
            BarIds = [link.Zone.Rebars[link.Left].BarId, link.Zone.Rebars[link.Right].BarId],
            PositionMm = link.PositionMm,
            LengthMm = link.LengthMm,
            Alpha = link.Alpha
          })
          .ToList();
      result.Clashes = ClearanceChecker.Check(zonesWithRebars, input.Slab, _planar, input.JointRatioMax);
      result.TotalRebarSegments = zonesWithRebars.Sum(z => z.Rebars.Count);
      _logger.Info(
          "Calculated reinforcement layout",
          ("totalRebarSegments", result.TotalRebarSegments),
          ("zoneCount", zonesWithRebars.Count));
      stages.Complete(
          "RebarCalculation",
          processed: result.TotalRebarSegments,
          partial: layoutPlan.NeedsHumanDecision,
          reasons: layoutPlan.Warnings
              .Select(warning => StageRecorder.Reason(ReasonCode.NeedsHumanDecision, warning))
              .ToList());
      result.Verification = _verifier.Verify(
          zonesWithRebars,
          new VerificationSettings { Field = input.AsField, Slab = input.Slab });
      if (result.Verification.Status == VerificationStatuses.Failed)
      {
        var first = result.Verification.DeficitRegions.FirstOrDefault();
        string message = first is null
            ? "Placed bars do not cover the required reinforcement area."
            : $"Under-reinforced region in {first.ZoneId} at ({first.MinX:F0}, {first.MinY:F0})-({first.MaxX:F0}, {first.MaxY:F0}) mm.";
        failures.Add(new PipelineFailureDiagnostic
        {
          Stage = "Verification",
          ErrorMessage = message,
          ExceptionType = "VerificationFailed",
          OccurredAtUtc = DateTimeOffset.UtcNow,
          IsCritical = true
        });
        stages.Fail(
            "Verification",
            StageRecorder.Reason(ReasonCode.VerificationFailed, message),
            algorithmId: "grid-as-v0");
        _logger.Warn("Reinforcement coverage check failed", ("status", result.Verification.Status), ("underReinforcedAreaM2", result.Verification.UnderReinforcedAreaM2));
      }
      else
      {
        stages.Complete(
            "Verification",
            algorithmId: "grid-as-v0",
            processed: zonesWithRebars.Count,
            partial: result.Verification.Status != VerificationStatuses.Passed);
      }
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
      var diagnostic = new PipelineFailureDiagnostic
      {
        Stage = "RebarCalculation",
        ErrorMessage = ex.Message,
        ExceptionType = ex.GetType().Name,
        OccurredAtUtc = DateTimeOffset.UtcNow,
        StackTrace = input.IncludeDiagnostics ? ex.StackTrace : null,
        IsCritical = true
      };
      failures.Add(diagnostic);
      stages.Fail("RebarCalculation", StageRecorder.Reason(ReasonCode.StructuralLimit, ex.Message, context: ex.GetType().Name));
      _logger.Error("Rebar calculation failed; aborting pipeline", ex);

      result.Report = Publish(BuildPartialReport(input, failures, result));
      if (input.PersistReport)
      {
        var outputPath = ResolveReportOutputPath(input);
        result.StoredReport = await _reportStore.SaveAsync(result.Report, outputPath, cancellationToken);
        _logger.Info("Stored partial reinforcement report after calculation failure", ("outputPath", result.StoredReport.OutputPath));
      }
      return Finish();
    }

    // 5. Optimize cutting (group by diameter) - try to optimize each diameter
    var rebarsByDiameter = zonesWithRebars
        .SelectMany(z => z.Rebars)
        .GroupBy(r => r.DiameterMm);

    var optimizationResults = new Dictionary<int, OptimizationResult>();
    var unoptimizedBars = new List<UnoptimizedBarReport>();
    foreach (var group in rebarsByDiameter)
    {
      var cuts = group.Select(r => r.TotalLength).ToList();
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
        stages.Fail("Optimization", StageRecorder.Reason(ReasonCode.StructuralLimit, fallbackDiagnostic.ErrorMessage));

        _logger.Warn(
            "Fallback optimization cannot start: no in-stock stock lengths",
            ("diameterMm", group.Key));

        result.UnoptimizedBars = unoptimizedBars;
        result.OptimizationResults = optimizationResults;
        result.Report = Publish(BuildPartialReport(input, failures, result));
        if (input.PersistReport)
        {
          var outputPath = ResolveReportOutputPath(input);
          result.StoredReport = await _reportStore.SaveAsync(result.Report, outputPath, cancellationToken);
          _logger.Info("Stored partial reinforcement report after fallback stock availability failure", ("outputPath", result.StoredReport.OutputPath));
        }

        return Finish();
      }

      double maxStockLength = inStockLengths.Max(s => s.LengthMm);
      double sawCutWidthMm = input.OptimizationSettings.SawCutWidthMm;
      var fittable = new List<double>();
      var oversized = new List<double>();
      foreach (double length in cuts)
      {
        if (length + sawCutWidthMm > maxStockLength + 1e-6)
          oversized.Add(length);
        else
          fittable.Add(length);
      }

      if (oversized.Count > 0)
      {
        foreach (double length in oversized)
        {
          unoptimizedBars.Add(new UnoptimizedBarReport
          {
            DiameterMm = group.Key,
            LengthMm = length,
            MaxStockLengthMm = maxStockLength,
            Reason = "bar_exceeds_max_stock"
          });
        }

        failures.Add(new PipelineFailureDiagnostic
        {
          Stage = "Detailing",
          ErrorMessage = $"{oversized.Count} bar(s) of d{group.Key} mm exceed max in-stock length {maxStockLength:F0} mm and were excluded from cutting.",
          ExceptionType = "BarExceedsMaxStock",
          OccurredAtUtc = DateTimeOffset.UtcNow,
          IsCritical = false
        });
      }

      if (fittable.Count == 0)
        continue;

      try
      {
        var optResult = _optimizer.Optimize(fittable, catalog.AvailableLengths, input.OptimizationSettings);
        optimizationResults[group.Key] = EnrichOptimizationResultWithMassAndCost(optResult, group.Key, catalog);
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
          StackTrace = input.IncludeDiagnostics ? ex.StackTrace : null,
          IsCritical = false
        };
        failures.Add(diagnostic);
        _logger.Warn(
            "Optimization failed for diameter; using max-stock fallback",
            ("diameterMm", group.Key),
            ("message", ex.Message));

        if (!TryBuildFallbackCuttingPlans(
                fittable,
                maxStockLength,
                sawCutWidthMm,
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
          stages.Fail("Optimization", StageRecorder.Reason(ReasonCode.StructuralLimit, fallbackDiagnostic.ErrorMessage));

          _logger.Warn(
              "Fallback optimization became infeasible; aborting pipeline",
              ("diameterMm", group.Key),
              ("reason", fallbackDiagnostic.ErrorMessage));

          result.UnoptimizedBars = unoptimizedBars;
          result.OptimizationResults = optimizationResults;
          result.Report = Publish(BuildPartialReport(input, failures, result));
          if (input.PersistReport)
          {
            var outputPath = ResolveReportOutputPath(input);
            result.StoredReport = await _reportStore.SaveAsync(result.Report, outputPath, cancellationToken);
            _logger.Info("Stored partial reinforcement report after fallback optimization failure", ("outputPath", result.StoredReport.OutputPath));
          }

          return Finish();
        }

        var fallback = new OptimizationResult
        {
          CuttingPlans = cuttingPlans,
          TotalStockBarsNeeded = cuttingPlans.Count,
          TotalWasteMm = cuttingPlans.Sum(p => p.WasteMm),
          TotalWastePercent = cuttingPlans.Sum(p => p.StockLengthMm) > 0
                ? cuttingPlans.Sum(p => p.WasteMm) / cuttingPlans.Sum(p => p.StockLengthMm) * 100.0
                : 0,
          TotalRebarLengthMm = fittable.Sum(),
          Provenance = BuildPipelineFallbackProvenance()
        };
        optimizationResults[group.Key] = EnrichOptimizationResultWithMassAndCost(fallback, group.Key, catalog);
      }
    }
    result.UnoptimizedBars = unoptimizedBars;
    result.OptimizationResults = optimizationResults;
    var optimizationReasons = OptimizationReasons(optimizationResults.Values, unoptimizedBars);
    stages.Complete(
        "Optimization",
        optimizationResults.Values.Select(item => item.Provenance?.OptimizerId).FirstOrDefault(id => !string.IsNullOrWhiteSpace(id)),
        processed: optimizationResults.Count,
        reasons: optimizationReasons,
        partial: optimizationReasons.Count > 0);

    // 6. Place in Revit (if requested)
    if (!input.PlaceInRevit)
    {
      stages.Skip("Placement");
    }
    else
    {
      try
      {
        var placementResult = await _placer.PlaceReinforcementAsync(
            zonesWithRebars,
            input.PlacementSettings,
            cancellationToken);
        result.PlacementResult = placementResult;
        stages.Complete("Placement", processed: placementResult.TotalRebarsPlaced, partial: !placementResult.Success);

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
          StackTrace = input.IncludeDiagnostics ? ex.StackTrace : null,
          IsCritical = false
        };
        failures.Add(diagnostic);
        stages.Fail("Placement", StageRecorder.Reason(ReasonCode.StructuralLimit, ex.Message, context: ex.GetType().Name));

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

    result.Report = Publish(BuildReport(input, catalog.SupplierName, zonesWithRebars, result, failures));

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

    return Finish();
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

  private static bool TryAssignLayers(
      IReadOnlyList<ReinforcementZone> zones,
      PipelineInput input,
      out string? error)
  {
    if (input.Layer is LayerKey explicitLayer)
    {
      foreach (var zone in zones)
        LayerKeyParser.Apply(zone, explicitLayer);

      error = null;
      return true;
    }

    if (input.UseLegacyDirectionHeuristic)
    {
      error = null;
      return true;
    }

    var unresolved = new List<string>();
    foreach (var zone in zones)
    {
      if (ReinforcementLayerMapper.TryMap(zone.SourceLayerName, out LayerKey mapped))
      {
        LayerKeyParser.Apply(zone, mapped);
        continue;
      }

      unresolved.Add(zone.SourceLayerName ?? zone.Id);
    }

    if (unresolved.Count == 0 || !input.RequireExplicitLayer)
    {
      error = null;
      return true;
    }

    error = "Layer is not specified. Pass --layer BottomX|BottomY|TopX|TopY, or use a DXF layer name that maps to one. Unresolved: "
        + string.Join(", ", unresolved);
    return false;
  }

  private static string ResolveReportOutputPath(PipelineInput input)
  {
    if (!string.IsNullOrWhiteSpace(input.ReportOutputPath))
      return input.ReportOutputPath;

    return Path.ChangeExtension(input.IsolineFilePath, ".result.json");
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
        TablesVersion = input.Metadata.NormativeTablesVersion,
        TopBarAnchorageFactor = NormativeProfiles.Sp63_2018.TopBarAnchorageFactor
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
          DiameterMm = zone.EffectiveSpec.DiameterMm,
          SpacingMm = zone.EffectiveSpec.SpacingMm,
          RebarCount = zone.Rebars.Count,
          TotalClearSpanMm = zone.Rebars.Sum(r => r.ClearSpan),
          TotalLengthMm = zone.Rebars.Sum(r => r.TotalLength),
          BoundingBox = ToBoundingBoxReport(zoneBox),
          SubRectangleCount = zone.SubRectangles?.Count,
          DecompositionCoverageRatio = zone.DecompositionMetrics?.CoverageRatio,
          DecompositionOverCoverageRatio = zone.DecompositionMetrics?.OverCoverageRatio,
          ExtendedBeyondZoneCount = zone.ExtendedBeyondZoneCount
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
              BoundStatus = kv.Value.BoundStatus,
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
        MassInstalledKg = SummarizeMass(result).InstalledKg,
        MassPurchasedKg = SummarizeMass(result).PurchasedKg,
        EstimatedCost = estimatedCosts.Count > 0 ? estimatedCosts.Sum() : null
      },
      UnoptimizedBars = result.UnoptimizedBars,
      Positions = result.Positions,
      Stages = result.Stages,
      Verification = result.Verification,
      Profile = LayerReportBuilder.ProfileFrom(input),
      InputSource = LayerReportBuilder.InputFrom(input),
      Layers = LayerReportBuilder.Build(input, zonesWithRebars),
      ParameterSources = input.ParameterSources,
      Clashes = result.Clashes,
      Laps = result.Laps,
      Warnings = MergeWarnings(placement?.Warnings, result.ParseStats)
          .Concat(result.FieldWarnings)
          .Concat(result.OverlayWarnings)
          .Concat(result.LayoutWarnings)
          .Concat(result.DetailingWarnings)
          .ToList(),
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
        TablesVersion = input.Metadata.NormativeTablesVersion,
        TopBarAnchorageFactor = NormativeProfiles.Sp63_2018.TopBarAnchorageFactor
      },
      AnalysisProvenance = new AnalysisProvenanceExecutionReport
      {
        Geometry = new GeometryProcessingExecutionReport
        {
          DecompositionAlgorithm = "n/a",
          RectangularShortcutFillRatio = 0,
          MinRectangleAreaMm2 = 1,
          SamplingResolutionPerAxis = 1,
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
        MassInstalledKg = SummarizeMass(result).InstalledKg,
        MassPurchasedKg = SummarizeMass(result).PurchasedKg,
        EstimatedCost = null
      },
      UnoptimizedBars = result.UnoptimizedBars,
      Positions = result.Positions,
      Profile = LayerReportBuilder.ProfileFrom(input),
      InputSource = LayerReportBuilder.InputFrom(input),
      Layers = LayerReportBuilder.Build(input, []),
      ParameterSources = input.ParameterSources,
      Clashes = result.Clashes,
      Laps = result.Laps,
      Warnings = result.OverlayWarnings.Concat(result.LayoutWarnings).Concat(result.DetailingWarnings).ToList(),
      Errors = failures,
      PartialResult = true
    };
  }

  private static IReadOnlyList<string> DetailingNotes(IReadOnlyList<ReinforcementZone> zones)
  {
    var ends = zones
        .SelectMany(zone => zone.Rebars)
        .Where(bar => bar.Status != BarInstanceStatus.Discarded)
        .SelectMany(bar => new[] { bar.EndConditionStart, bar.EndConditionEnd })
        .Where(condition => condition != BarEndCondition.Straight)
        .GroupBy(condition => condition)
        .OrderBy(group => group.Key.ToString(), StringComparer.Ordinal)
        .ToList();
    if (ends.Count == 0)
      return [];

    string counts = string.Join(", ", ends.Select(group => $"{group.Key} {group.Count()}"));
    return [$"Anchorage stays inside the working area. Ends that cannot take the full length: {counts}."];
  }

  private static ReasonCode ParseFailureCode(Exception exception) => exception switch
  {
    RasterNotCalibratedException => ReasonCode.RasterNotCalibrated,
    LegendLoadException => ReasonCode.LegendInvalid,
    _ => ReasonCode.StructuralLimit
  };

  private static List<StageReasonReport> OptimizationReasons(
      IEnumerable<OptimizationResult> results,
      IReadOnlyList<UnoptimizedBarReport> unoptimizedBars)
  {
    var reasons = new List<StageReasonReport>();
    var items = results.ToList();
    if (unoptimizedBars.Count > 0)
    {
      reasons.Add(StageRecorder.Reason(
          ReasonCode.BarExceedsMaxStock,
          $"{unoptimizedBars.Count} bars exceed every in-stock length."));
    }

    if (items.Any(item => item.Provenance?.FallbackUsed == true))
    {
      reasons.Add(StageRecorder.Reason(
          ReasonCode.FallbackUsed,
          "Cutting used the max-stock fallback.",
          retryable: true));
    }

    if (items.Any(item =>
            item.BoundStatus == BoundStatuses.NotProven
            && item.Provenance?.OptimizerId?.Contains("column-generation", StringComparison.Ordinal) == true))
    {
      reasons.Add(StageRecorder.Reason(
          ReasonCode.CgNotConverged,
          "Column generation did not prove a dual bound.",
          retryable: true));
    }

    return reasons;
  }

  private static List<string> MergeWarnings(
      IReadOnlyList<string>? placementWarnings,
      IsolineParseStats? stats)
  {
    var warnings = new List<string>(placementWarnings ?? []);
    if (stats is null)
      return warnings;

    if (stats.UnitsAssumed)
      warnings.Add("DXF $INSUNITS is unset; coordinates were interpreted as millimetres.");

    if (Count(stats, "holeIgnored") > 0)
      warnings.Add("Hatch hole loops were ignored; only the outer boundary was kept.");

    if (Count(stats, "unsupportedBoundaryEdge") > 0 || Count(stats, "unsupportedCurve") > 0)
      warnings.Add("Spline or ellipse geometry was not approximated.");

    int openPolylines = Count(stats, "ignoredOpenPolylines");
    if (openPolylines > 0)
      warnings.Add($"{openPolylines} open polylines were ignored.");

    if (stats.RoiAssumedFullImage)
      warnings.Add("PNG region of interest was not set; the full image was used.");

    return warnings;
  }

  private static int Count(IsolineParseStats stats, string reason)
      => stats.IgnoredByReason.TryGetValue(reason, out int count) ? count : 0;

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
        DecompositionAlgorithm = "bar-runs/v1",
        RectangularShortcutFillRatio = PolygonDecomposition.RectangularFillRatioThreshold,
        MinRectangleAreaMm2 = PolygonDecomposition.DefaultMinRectangleAreaMm2,
        SamplingResolutionPerAxis = PolygonDecomposition.CoverageSamplingResolutionPerAxis,
        CellCoverageInclusionThreshold = PolygonDecomposition.CellCoverageInclusionThreshold,
        MinCoverageRatioAcrossComplexZones = decompositionMetrics.Count > 0
                ? decompositionMetrics.Min(m => m.CoverageRatio)
                : null,
        MaxOverCoverageRatioAcrossComplexZones = decompositionMetrics.Count > 0
                ? decompositionMetrics.Max(m => m.OverCoverageRatio)
                : null,
        ParsedEntityCount = result.ParseStats?.ParsedEntityCount ?? 0,
        IgnoredByReason = result.ParseStats?.IgnoredByReason ?? new Dictionary<string, int>()
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
        AnyFallbackMasterSolverUsed = optimizationProvenances.Any(p => p.UsedFallbackMasterSolver),
        FallbackUsed = optimizationProvenances.Any(p => p.FallbackUsed)
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

  private static OptimizationResult EnrichOptimizationResultWithMassAndCost(
      OptimizationResult result,
      int diameterMm,
      SupplierCatalog catalog)
  {
    double linearMass = ReinforcementLimits.GetLinearMass(diameterMm);
    double totalLengthM = result.TotalRebarLengthMm / 1000.0;
    double totalMassKg = totalLengthM * linearMass;
    double purchasedLengthM = result.CuttingPlans.Sum(plan => plan.StockLengthMm) / 1000.0;
    double? estimatedCost = EstimatePurchasedStockCost(result, catalog, linearMass);

    return new OptimizationResult
    {
      CuttingPlans = result.CuttingPlans,
      TotalStockBarsNeeded = result.TotalStockBarsNeeded,
      TotalWasteMm = result.TotalWasteMm,
      TotalWastePercent = result.TotalWastePercent,
      TotalRebarLengthMm = result.TotalRebarLengthMm,
      TotalMassKg = totalMassKg,
      MassInstalledKg = totalMassKg,
      MassPurchasedKg = purchasedLengthM * linearMass,
      EstimatedCost = estimatedCost,
      DualBound = result.DualBound,
      Gap = result.Gap,
      BoundStatus = result.BoundStatus,
      Provenance = result.Provenance
    };
  }

  private static (double InstalledKg, double PurchasedKg) SummarizeMass(PipelineResult result)
  {
    double installed = result.OptimizationResults.Values.Sum(item => item.MassInstalledKg ?? item.TotalMassKg ?? 0);
    installed += result.UnoptimizedBars.Sum(bar =>
        bar.LengthMm / 1000.0 * ReinforcementLimits.GetLinearMass(bar.DiameterMm));
    double purchased = result.OptimizationResults.Values.Sum(item => item.MassPurchasedKg ?? 0);
    return (installed, purchased);
  }

  private static OptimizationProvenance BuildPipelineFallbackProvenance()
  {
    return new OptimizationProvenance
    {
      OptimizerId = "fallback-max-stock-ffd-v1",
      MasterProblemStrategy = "not-applicable",
      PricingStrategy = "not-applicable",
      IntegerizationStrategy = "first-fit-decreasing",
      DemandAggregationPrecisionMm = 0,
      QualityFloor = "none",
      UsedFallbackMasterSolver = false,
      FallbackUsed = true
    };
  }

  private static double? EstimatePurchasedStockCost(
      OptimizationResult result,
      SupplierCatalog catalog,
      double linearMassKgPerM)
  {
    if (result.CuttingPlans.Count == 0)
      return null;

    double totalCost = 0;

    foreach (var plan in result.CuttingPlans)
    {
      var stock = catalog.AvailableLengths.FirstOrDefault(s => Math.Abs(s.LengthMm - plan.StockLengthMm) < 0.1);
      if (stock?.PricePerTon is null)
        return null;

      double purchasedMassKg = (plan.StockLengthMm / 1000.0) * linearMassKgPerM;
      totalCost += purchasedMassKg / 1000.0 * stock.PricePerTon.Value;
    }

    return totalCost;
  }

  private async Task<IReadOnlyList<ReinforcementZone>> ParseDesignLayersAsync(
      PipelineInput input,
      PipelineResult result,
      CancellationToken cancellationToken)
  {
    string? designError = LayerDesignRules.Validate(input.Layers);
    if (designError is not null)
      throw new InvalidOperationException(designError);

    var merged = new List<ReinforcementZone>();
    var ignored = new Dictionary<string, int>(StringComparer.Ordinal);
    int parsedEntities = 0;
    bool unitsAssumed = false;
    string? assumedUnits = null;
    bool roiAssumed = false;

    foreach (var layer in input.Layers)
    {
      var parser = GetParser(layer.IsolineFilePath);
      var zones = await parser.ParseAsync(layer.IsolineFilePath, layer.Legend, cancellationToken);
      var stats = parser.Stats ?? IsolineParseStats.Empty;
      parsedEntities += stats.ParsedEntityCount;
      unitsAssumed |= stats.UnitsAssumed;
      assumedUnits ??= stats.AssumedUnits;
      roiAssumed |= stats.RoiAssumedFullImage;
      foreach (var pair in stats.IgnoredByReason)
        ignored[pair.Key] = ignored.GetValueOrDefault(pair.Key) + pair.Value;

      foreach (var zone in zones)
      {
        if (input.Layers.Count > 1)
          zone.Id = $"{layer.Layer}-{zone.Id}";

        LayerKeyParser.Apply(zone, layer.Layer);
        zone.DesignLayer = layer.Layer;
        zone.AsRequiredMm2PerM = zone.Spec.AreaPerMeterMm2;
        if (layer.Background is not null)
        {
          zone.AsBackgroundMm2PerM = layer.Background.Spec.AreaPerMeterMm2;
          zone.AsDeltaMm2PerM = Math.Max(0, zone.AsRequiredMm2PerM - zone.AsBackgroundMm2PerM);
          zone.BackgroundSpacingMm = layer.Background.Spec.SpacingMm;
          zone.GridOriginMm = layer.Background.GridOriginMm;
          zone.Role = zone.AsDeltaMm2PerM <= 1e-6 ? ZoneRole.Background : ZoneRole.Additional;
        }

        merged.Add(zone);
      }
    }

    result.ParsedZoneCount = merged.Count;
    result.ParseStats = new IsolineParseStats
    {
      ParsedEntityCount = parsedEntities,
      IgnoredByReason = ignored,
      UnitsAssumed = unitsAssumed,
      AssumedUnits = assumedUnits,
      RoiAssumedFullImage = roiAssumed
    };
    return merged;
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
  public LayerKey? Layer { get; init; }
  public IReadOnlyList<LayerDesignInput> Layers { get; init; } = [];
  public string? CompanyProfilePath { get; init; }
  public string? CompanyProfileId { get; init; }
  public string CompanyProfileVersion { get; init; } = "0";
  public SupplierCatalog? SupplierCatalog { get; init; }
  public IReadOnlyList<ParameterSourceReport> ParameterSources { get; init; } = [];
  public bool UseLegacyDirectionHeuristic { get; init; }
  public bool RequireExplicitLayer { get; init; }
  public bool IncludeDiagnostics { get; init; }
  public AsField? AsField { get; init; }
  public IReadOnlyList<int> FieldDiametersMm { get; init; } = [];
  public IReadOnlyList<int> FieldSpacingsMm { get; init; } = [];
  public string AdditionalSpacingMode { get; init; } = "interleave";
  public double JointRatioMax { get; init; } = 0.5;

  /// <summary>Profile end rule. NeedsHook when the profile does not say otherwise.</summary>
  public string EndCondition { get; init; } = "NeedsHook";

  /// <summary>When true, long bars are not cut with a lap. Coupler length is not in the profile.</summary>
  public bool Couplers { get; init; }
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
  public IsolineParseStats? ParseStats { get; set; }
  public IReadOnlyList<ReinforcementZone> ClassifiedZones { get; set; } = [];
  public int TotalRebarSegments { get; set; }
  public Dictionary<int, OptimizationResult> OptimizationResults { get; set; } = new();
  public IReadOnlyList<UnoptimizedBarReport> UnoptimizedBars { get; set; } = [];
  public IReadOnlyList<PositionExecutionReport> Positions { get; set; } = [];
  public PlacementResult? PlacementResult { get; set; }
  public ReinforcementExecutionReport? Report { get; set; }
  public IReadOnlyList<StageExecutionReport> Stages { get; set; } = [];
  public ReinforcementVerificationResult? Verification { get; set; }
  public StoredReportReference? StoredReport { get; set; }
  public IReadOnlyList<string> FieldWarnings { get; set; } = [];
  public IReadOnlyList<string> OverlayWarnings { get; set; } = [];
  public IReadOnlyList<string> LayoutWarnings { get; set; } = [];
  public IReadOnlyList<string> DetailingWarnings { get; set; } = [];
  public IReadOnlyList<ClashReport> Clashes { get; set; } = [];
  public IReadOnlyList<LapJointReport> Laps { get; set; } = [];

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
