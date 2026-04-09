using A101.Domain.Models;
using A101.Domain.Ports;
using A101.Domain.Rules;

namespace A101.Application.UseCases;

/// <summary>
/// Standalone use case: optimize rebar cutting for a set of required lengths.
/// Can be invoked separately after manual edits to the layout.
/// </summary>
public sealed class OptimizeRebarCuttingUseCase
{
    private readonly IRebarOptimizer _optimizer;
    private readonly ISupplierCatalogLoader _catalogLoader;

    public OptimizeRebarCuttingUseCase(
        IRebarOptimizer optimizer,
        ISupplierCatalogLoader catalogLoader)
    {
        _optimizer = optimizer;
        _catalogLoader = catalogLoader;
    }

    /// <summary>
    /// Optimize cutting for given zones, grouped by diameter.
    /// Returns per-diameter optimization plus aggregate stats.
    /// </summary>
    public async Task<CuttingOptimizationReport> ExecuteAsync(
        IReadOnlyList<ReinforcementZone> zones,
        string? supplierCatalogPath,
        OptimizationSettings settings,
        CancellationToken cancellationToken = default)
    {
        var catalog = supplierCatalogPath is not null
            ? await _catalogLoader.LoadAsync(supplierCatalogPath, cancellationToken)
            : _catalogLoader.GetDefaultCatalog();

        var report = new CuttingOptimizationReport
        {
            SupplierName = catalog.SupplierName
        };

        var rebarsByDiameter = zones
            .SelectMany(z => z.Rebars)
            .GroupBy(r => r.DiameterMm)
            .OrderBy(g => g.Key);

        foreach (var group in rebarsByDiameter)
        {
            int diameter = group.Key;
            var lengths = group.Select(r => r.TotalLength).OrderDescending().ToList();

            var result = _optimizer.Optimize(lengths, catalog.AvailableLengths, settings);

            double linearMass = ReinforcementLimits.GetLinearMass(diameter);
            double totalLengthM = result.TotalRebarLengthMm / 1000.0;
            double totalMassKg = totalLengthM * linearMass;

            report.DiameterReports.Add(new DiameterOptimizationReport
            {
                DiameterMm = diameter,
                RebarCount = lengths.Count,
                OptimizationResult = result with { TotalMassKg = totalMassKg },
                LinearMassKgPerM = linearMass
            });
        }

        return report;
    }
}

/// <summary>
/// Full optimization report across all diameters.
/// </summary>
public sealed class CuttingOptimizationReport
{
    public required string SupplierName { get; init; }
    public List<DiameterOptimizationReport> DiameterReports { get; init; } = [];

    public double TotalMassKg => DiameterReports.Sum(r => r.OptimizationResult.TotalMassKg ?? 0);
    public int TotalStockBars => DiameterReports.Sum(r => r.OptimizationResult.TotalStockBarsNeeded);
    public double AverageWastePercent =>
        DiameterReports.Any() ? DiameterReports.Average(r => r.OptimizationResult.TotalWastePercent) : 0;
}

/// <summary>
/// Optimization report for a single rebar diameter.
/// </summary>
public sealed record DiameterOptimizationReport
{
    public required int DiameterMm { get; init; }
    public required int RebarCount { get; init; }
    public required OptimizationResult OptimizationResult { get; init; }
    public required double LinearMassKgPerM { get; init; }
}
