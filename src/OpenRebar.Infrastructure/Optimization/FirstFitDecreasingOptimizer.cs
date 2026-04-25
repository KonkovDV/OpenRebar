using OpenRebar.Domain.Models;
using OpenRebar.Domain.Exceptions;
using OpenRebar.Domain.Ports;
using OpenRebar.Domain.Rules;

namespace OpenRebar.Infrastructure.Optimization;

/// <summary>
/// First Fit Decreasing bin-packing optimizer for rebar cutting.
/// Sorts required lengths descending, then packs each into the first bin where it fits.
/// </summary>
public sealed class FirstFitDecreasingOptimizer : IRebarOptimizer
{
    public OptimizationResult Optimize(
        IReadOnlyList<double> requiredLengths,
        IReadOnlyList<StockLength> stockLengths,
        OptimizationSettings settings)
    {
        if (requiredLengths.Count == 0)
        {
            return new OptimizationResult
            {
                CuttingPlans = [],
                TotalStockBarsNeeded = 0,
                TotalWasteMm = 0,
                TotalWastePercent = 0,
                TotalRebarLengthMm = 0,
                Provenance = BuildFfdProvenance()
            };
        }

        // Sort required lengths descending (largest first for better packing)
        var sorted = requiredLengths.OrderDescending().ToList();

        // Choose the best stock length (prefer longest available for less waste)
        var inStockLengths = stockLengths
            .Where(s => s.InStock)
            .ToList();

        if (inStockLengths.Count == 0)
            throw new OptimizationException("No in-stock bar lengths are available for optimization.");

        EnsureAllCutsFitStock(requiredLengths, inStockLengths, settings.SawCutWidthMm);

        var preferredStock = inStockLengths
            .OrderByDescending(s => s.LengthMm)
            .First();

        double stockLength = preferredStock.LengthMm;

        // Bins: each bin tracks remaining capacity and placed items
        var bins = new List<(double Remaining, List<double> Cuts)>();

        foreach (var length in sorted)
        {
            double effectiveLength = length + settings.SawCutWidthMm;

            // Find first bin where this piece fits
            int bestBin = -1;
            double bestRemaining = double.MaxValue;

            for (int i = 0; i < bins.Count; i++)
            {
                if (bins[i].Remaining >= effectiveLength)
                {
                    // Best Fit variant: choose bin with least remaining space
                    if (bins[i].Remaining - effectiveLength < bestRemaining)
                    {
                        bestRemaining = bins[i].Remaining - effectiveLength;
                        bestBin = i;
                    }
                }
            }

            if (bestBin >= 0)
            {
                var bin = bins[bestBin];
                bin.Cuts.Add(length);
                bins[bestBin] = (bin.Remaining - effectiveLength, bin.Cuts);
            }
            else
            {
                // Need a new stock bar
                var newCuts = new List<double> { length };
                bins.Add((stockLength - effectiveLength, newCuts));
            }
        }

        // Build cutting plans
        var plans = bins.Select(b => new CuttingPlan
        {
            StockLengthMm = stockLength,
            Cuts = b.Cuts
        }).ToList();

        double totalRequired = requiredLengths.Sum();
        double totalStock = bins.Count * stockLength;
        double totalWaste = totalStock - totalRequired;

        return new OptimizationResult
        {
            CuttingPlans = plans,
            TotalStockBarsNeeded = bins.Count,
            TotalWasteMm = totalWaste,
            TotalWastePercent = totalStock > 0 ? totalWaste / totalStock * 100 : 0,
            TotalRebarLengthMm = totalRequired,
            Provenance = BuildFfdProvenance()
        };
    }

    private static void EnsureAllCutsFitStock(
        IReadOnlyList<double> requiredLengths,
        IReadOnlyList<StockLength> inStockLengths,
        double sawCutWidthMm)
    {
        foreach (double length in requiredLengths)
        {
            double requiredEffectiveLength = length + sawCutWidthMm;
            bool hasCompatibleStock = inStockLengths.Any(stock => stock.LengthMm + 1e-6 >= requiredEffectiveLength);
            if (hasCompatibleStock)
                continue;

            double maxStock = inStockLengths.Max(stock => stock.LengthMm);
            throw new OptimizationException(
                $"Required cut {length:F1} mm (effective {requiredEffectiveLength:F1} mm with saw cut) exceeds all available stock lengths. Max in-stock length: {maxStock:F1} mm.");
        }
    }

    private static OptimizationProvenance BuildFfdProvenance()
    {
        return new OptimizationProvenance
        {
            OptimizerId = "first-fit-decreasing-v1",
            MasterProblemStrategy = "heuristic-bin-packing",
            PricingStrategy = "not-applicable",
            IntegerizationStrategy = "direct-constructive-heuristic",
            DemandAggregationPrecisionMm = 0,
            QualityFloor = "none",
            UsedFallbackMasterSolver = false
        };
    }
}
