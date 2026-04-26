using OpenRebar.Domain.Models;
using OpenRebar.Domain.Exceptions;
using OpenRebar.Domain.Ports;
using OpenRebar.Domain.Rules;

namespace OpenRebar.Infrastructure.Optimization;

/// <summary>
/// First Fit Decreasing bin-packing optimizer for rebar cutting.
/// Sorts required lengths descending, then packs them with a single-stock or mixed-stock constructive heuristic.
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

        var plans = inStockLengths
            .Select(stock => stock.LengthMm)
            .Distinct()
            .Count() == 1
            ? BuildSingleStockPlans(sorted, inStockLengths[0].LengthMm, settings.SawCutWidthMm)
            : BuildMixedStockPlans(sorted, inStockLengths, settings);

        double totalRequired = requiredLengths.Sum();
        double totalStock = plans.Sum(plan => plan.StockLengthMm);
        double totalWaste = plans.Sum(plan => plan.WasteMm);

        return new OptimizationResult
        {
            CuttingPlans = plans,
            TotalStockBarsNeeded = plans.Count,
            TotalWasteMm = totalWaste,
            TotalWastePercent = totalStock > 0 ? totalWaste / totalStock * 100 : 0,
            TotalRebarLengthMm = totalRequired,
            Provenance = BuildFfdProvenance()
        };
    }

    private static List<CuttingPlan> BuildSingleStockPlans(
        IReadOnlyList<double> sortedLengths,
        double stockLength,
        double sawCutWidthMm)
    {
        var bins = new List<(double Remaining, List<double> Cuts)>();

        foreach (var length in sortedLengths)
        {
            double effectiveLength = length + sawCutWidthMm;
            int bestBin = -1;
            double bestRemaining = double.MaxValue;

            for (int i = 0; i < bins.Count; i++)
            {
                if (bins[i].Remaining < effectiveLength)
                    continue;

                double candidateRemaining = bins[i].Remaining - effectiveLength;
                if (candidateRemaining >= bestRemaining)
                    continue;

                bestRemaining = candidateRemaining;
                bestBin = i;
            }

            if (bestBin >= 0)
            {
                var bin = bins[bestBin];
                bin.Cuts.Add(length);
                bins[bestBin] = (bin.Remaining - effectiveLength, bin.Cuts);
                continue;
            }

            bins.Add((stockLength - effectiveLength, [length]));
        }

        return bins.Select(bin => new CuttingPlan
        {
            StockLengthMm = stockLength,
            Cuts = bin.Cuts,
            SawCutWidthMm = sawCutWidthMm
        }).ToList();
    }

    private static List<CuttingPlan> BuildMixedStockPlans(
        IReadOnlyList<double> sortedLengths,
        IReadOnlyList<StockLength> inStockLengths,
        OptimizationSettings settings)
    {
        var remaining = sortedLengths.ToList();
        var plans = new List<CuttingPlan>();

        while (remaining.Count > 0)
        {
            var nextPlan = SelectBestMixedStockPlan(remaining, inStockLengths, settings);
            plans.Add(nextPlan);

            foreach (double cut in nextPlan.Cuts)
            {
                int removeIndex = remaining.FindIndex(length => Math.Abs(length - cut) <= 1e-6);
                if (removeIndex >= 0)
                    remaining.RemoveAt(removeIndex);
            }
        }

        return plans;
    }

    private static CuttingPlan SelectBestMixedStockPlan(
        IReadOnlyList<double> remaining,
        IReadOnlyList<StockLength> inStockLengths,
        OptimizationSettings settings)
    {
        double longestRemaining = remaining[0];
        var feasibleStocks = inStockLengths
            .Where(stock => stock.LengthMm + 1e-6 >= longestRemaining + settings.SawCutWidthMm)
            .GroupBy(stock => stock.LengthMm)
            .Select(group => group
                .OrderBy(stock => stock.PricePerTon ?? double.PositiveInfinity)
                .First())
            .ToList();

        var candidatePlans = feasibleStocks
            .Select(stock =>
            {
                var cuts = FillBarGreedily(remaining, stock.LengthMm, settings.SawCutWidthMm);
                var plan = new CuttingPlan
                {
                    StockLengthMm = stock.LengthMm,
                    Cuts = cuts,
                    SawCutWidthMm = settings.SawCutWidthMm
                };

                double? barCost = stock.PricePerTon.HasValue
                    ? stock.LengthMm * stock.PricePerTon.Value
                    : null;

                return new MixedStockCandidate(plan, barCost);
            })
            .Where(candidate => candidate.Plan.Cuts.Count > 0)
            .ToList();

        double minCost = candidatePlans
            .Where(candidate => candidate.CostProxy.HasValue)
            .Select(candidate => candidate.CostProxy!.Value)
            .DefaultIfEmpty(0)
            .Min();

        double maxCost = candidatePlans
            .Where(candidate => candidate.CostProxy.HasValue)
            .Select(candidate => candidate.CostProxy!.Value)
            .DefaultIfEmpty(0)
            .Max();

        return candidatePlans
            .OrderBy(candidate => ScoreMixedStockCandidate(candidate, settings, minCost, maxCost))
            .ThenBy(candidate => candidate.Plan.WasteMm)
            .ThenByDescending(candidate => candidate.Plan.Cuts.Count)
            .ThenBy(candidate => candidate.Plan.StockLengthMm)
            .First()
            .Plan;
    }

    private static List<double> FillBarGreedily(
        IReadOnlyList<double> remaining,
        double stockLength,
        double sawCutWidthMm)
    {
        double remainingCapacity = stockLength;
        var cuts = new List<double>();

        foreach (double length in remaining)
        {
            double effectiveLength = length + sawCutWidthMm;
            if (effectiveLength > remainingCapacity + 1e-6)
                continue;

            cuts.Add(length);
            remainingCapacity -= effectiveLength;
        }

        return cuts;
    }

    private static double ScoreMixedStockCandidate(
        MixedStockCandidate candidate,
        OptimizationSettings settings,
        double minCost,
        double maxCost)
    {
        double wasteScore = candidate.Plan.WastePercent / 100.0;
        double installScore = 1.0 / candidate.Plan.Cuts.Count;

        double costScore = 0;
        if (candidate.CostProxy.HasValue && maxCost > minCost)
            costScore = (candidate.CostProxy.Value - minCost) / (maxCost - minCost);

        return settings.WasteWeight * wasteScore
             + settings.InstallationWeight * installScore
             + settings.CostWeight * costScore;
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

    private sealed record MixedStockCandidate(CuttingPlan Plan, double? CostProxy);
}
