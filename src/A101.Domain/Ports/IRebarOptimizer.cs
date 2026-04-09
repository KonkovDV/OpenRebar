using A101.Domain.Models;

namespace A101.Domain.Ports;

/// <summary>
/// Optimizes rebar cutting to minimize waste and cost.
/// Solves the bin-packing (cutting stock) problem for rebar lengths.
/// </summary>
public interface IRebarOptimizer
{
    /// <summary>
    /// Find the optimal cutting plan to minimize waste.
    /// </summary>
    /// <param name="requiredLengths">All required rebar lengths in mm.</param>
    /// <param name="stockLengths">Available stock bar lengths.</param>
    /// <param name="settings">Optimization settings.</param>
    /// <returns>Optimal cutting plan with waste statistics.</returns>
    OptimizationResult Optimize(
        IReadOnlyList<double> requiredLengths,
        IReadOnlyList<StockLength> stockLengths,
        OptimizationSettings settings);
}

/// <summary>
/// Settings for the rebar optimization algorithm.
/// </summary>
public sealed record OptimizationSettings
{
    /// <summary>Minimum usable scrap length in mm (shorter pieces are waste).</summary>
    public double MinScrapLengthMm { get; init; } = 300;

    /// <summary>Saw cut width in mm.</summary>
    public double SawCutWidthMm { get; init; } = 3;

    /// <summary>Weight for waste minimization (0..1).</summary>
    public double WasteWeight { get; init; } = 0.5;

    /// <summary>Weight for cost minimization (0..1).</summary>
    public double CostWeight { get; init; } = 0.3;

    /// <summary>Weight for ease of installation (fewer pieces, 0..1).</summary>
    public double InstallationWeight { get; init; } = 0.2;

    /// <summary>Maximum computation time.</summary>
    public TimeSpan MaxComputationTime { get; init; } = TimeSpan.FromSeconds(30);
}
