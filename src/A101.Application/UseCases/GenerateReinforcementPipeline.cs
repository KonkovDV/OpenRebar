using A101.Domain.Models;
using A101.Domain.Ports;

namespace A101.Application.UseCases;

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

    public GenerateReinforcementPipeline(
        IIsolineParser dxfParser,
        IIsolineParser pngParser,
        IZoneDetector zoneDetector,
        IReinforcementCalculator calculator,
        IRebarOptimizer optimizer,
        ISupplierCatalogLoader catalogLoader,
        IRevitPlacer placer)
    {
        _dxfParser = dxfParser;
        _pngParser = pngParser;
        _zoneDetector = zoneDetector;
        _calculator = calculator;
        _optimizer = optimizer;
        _catalogLoader = catalogLoader;
        _placer = placer;
    }

    public async Task<PipelineResult> ExecuteAsync(
        PipelineInput input,
        CancellationToken cancellationToken = default)
    {
        var result = new PipelineResult();

        // 1. Parse isoline file
        var parser = GetParser(input.IsolineFilePath);
        var rawZones = await parser.ParseAsync(
            input.IsolineFilePath,
            input.Legend,
            cancellationToken);
        result.ParsedZoneCount = rawZones.Count;

        // 2. Classify zones and decompose complex ones
        var classifiedZones = _zoneDetector.ClassifyAndDecompose(rawZones, input.Slab);
        result.ClassifiedZones = classifiedZones;

        // 3. Calculate rebar layout per zone
        var zonesWithRebars = _calculator.CalculateRebars(classifiedZones, input.Slab);
        result.TotalRebarSegments = zonesWithRebars.Sum(z => z.Rebars.Count);

        // 4. Optimize cutting (group by diameter)
        var rebarsByDiameter = zonesWithRebars
            .SelectMany(z => z.Rebars)
            .GroupBy(r => r.DiameterMm);

        var catalog = input.SupplierCatalogPath is not null
            ? await _catalogLoader.LoadAsync(input.SupplierCatalogPath, cancellationToken)
            : _catalogLoader.GetDefaultCatalog();

        var optimizationResults = new Dictionary<int, OptimizationResult>();
        foreach (var group in rebarsByDiameter)
        {
            var lengths = group.Select(r => r.TotalLength).ToList();
            var optResult = _optimizer.Optimize(lengths, catalog.AvailableLengths, input.OptimizationSettings);
            optimizationResults[group.Key] = optResult;
        }
        result.OptimizationResults = optimizationResults;

        // 5. Place in Revit (if requested)
        if (input.PlaceInRevit)
        {
            var placementResult = await _placer.PlaceReinforcementAsync(
                zonesWithRebars,
                input.PlacementSettings,
                cancellationToken);
            result.PlacementResult = placementResult;
        }

        return result;
    }

    private IIsolineParser GetParser(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext switch
        {
            ".dxf" => _dxfParser,
            ".png" or ".jpg" or ".jpeg" or ".bmp" or ".tiff" => _pngParser,
            _ => throw new NotSupportedException($"Unsupported isoline file format: {ext}")
        };
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
    public string? SupplierCatalogPath { get; init; }
    public OptimizationSettings OptimizationSettings { get; init; } = new();
    public PlacementSettings PlacementSettings { get; init; } = new();
    public bool PlaceInRevit { get; init; } = true;
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

    public double TotalWastePercent =>
        OptimizationResults.Values.Any()
            ? OptimizationResults.Values.Average(o => o.TotalWastePercent)
            : 0;

    public double TotalMassKg =>
        OptimizationResults.Values
            .Where(o => o.TotalMassKg.HasValue)
            .Sum(o => o.TotalMassKg!.Value);
}
