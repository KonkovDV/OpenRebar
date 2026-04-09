namespace A101.Domain.Models;

/// <summary>
/// Available rebar stock length from a supplier.
/// </summary>
public sealed record StockLength
{
    /// <summary>Standard bar length in mm (e.g. 11700, 12000).</summary>
    public required double LengthMm { get; init; }

    /// <summary>Price per ton in currency units.</summary>
    public double? PricePerTon { get; init; }

    /// <summary>Whether currently available from the supplier.</summary>
    public bool InStock { get; init; } = true;
}

/// <summary>
/// A supplier's catalog of available rebar stock.
/// </summary>
public sealed class SupplierCatalog
{
    public required string SupplierName { get; init; }
    public required IReadOnlyList<StockLength> AvailableLengths { get; init; }
}

/// <summary>
/// A cutting instruction: how to cut one stock bar into segments.
/// </summary>
public sealed record CuttingPlan
{
    /// <summary>The stock bar length used (mm).</summary>
    public required double StockLengthMm { get; init; }

    /// <summary>Segments cut from this bar (lengths in mm).</summary>
    public required IReadOnlyList<double> Cuts { get; init; }

    /// <summary>Remaining waste (mm).</summary>
    public double WasteMm => StockLengthMm - Cuts.Sum();

    /// <summary>Waste percentage.</summary>
    public double WastePercent => WasteMm / StockLengthMm * 100;
}

/// <summary>
/// Optimization result: full set of cutting plans + summary stats.
/// </summary>
public sealed class OptimizationResult
{
    public required IReadOnlyList<CuttingPlan> CuttingPlans { get; init; }
    public required int TotalStockBarsNeeded { get; init; }
    public required double TotalWasteMm { get; init; }
    public required double TotalWastePercent { get; init; }
    public required double TotalRebarLengthMm { get; init; }

    /// <summary>Total mass in kg (calculated from diameter and total length).</summary>
    public double? TotalMassKg { get; init; }

    /// <summary>Estimated cost based on supplier prices.</summary>
    public double? EstimatedCost { get; init; }
}
