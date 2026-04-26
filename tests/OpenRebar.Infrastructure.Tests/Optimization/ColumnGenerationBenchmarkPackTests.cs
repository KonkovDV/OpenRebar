using FluentAssertions;
using OpenRebar.Domain.Models;
using OpenRebar.Domain.Ports;
using OpenRebar.Infrastructure.Optimization;

namespace OpenRebar.Infrastructure.Tests.Optimization;

public class ColumnGenerationBenchmarkPackTests
{
    private readonly IRebarOptimizer _optimizer = new ColumnGenerationOptimizer();

    private static readonly OptimizationSettings DefaultSettings = new()
    {
        SawCutWidthMm = 3,
        MinScrapLengthMm = 300
    };

    [Fact]
    public void ExactBenchmarkPack_ShouldStayWithinGapEnvelope()
    {
        var cases = new[]
        {
            new BenchmarkCase(
                "single-stock-balanced-pairs",
                [5800, 5800, 3500, 3500, 2200, 2200],
                [new StockLength { LengthMm = 11700, InStock = true }]),
            new BenchmarkCase(
                "single-stock-mixed-short-and-medium",
                [5000, 3000, 2000, 1500, 1500],
                [new StockLength { LengthMm = 11700, InStock = true }]),
            new BenchmarkCase(
                "dual-stock-prefer-shorter-bars",
                [5500, 5500, 5500],
                [
                    new StockLength { LengthMm = 11700, InStock = true },
                    new StockLength { LengthMm = 6000, InStock = true }
                ]),
            new BenchmarkCase(
                "dual-stock-zero-waste-pairing",
                [4000, 4000, 2000, 2000],
                [
                    new StockLength { LengthMm = 11700, InStock = true },
                    new StockLength { LengthMm = 6000, InStock = true }
                ]),
            new BenchmarkCase(
                "dual-stock-long-plus-short-mixture",
                [5900, 5900, 2500, 2500, 2500],
                [
                    new StockLength { LengthMm = 11700, InStock = true },
                    new StockLength { LengthMm = 6000, InStock = true }
                ])
        };

        var outcomes = cases.Select(RunBenchmarkCase).ToList();

        outcomes.Max(outcome => outcome.ScoreGap).Should().BeLessThanOrEqualTo(0.08,
            "the production-oriented optimizer should stay close to the exact weighted objective on the small benchmark pack");

        outcomes.Max(outcome => outcome.BarGap).Should().BeLessThanOrEqualTo(0,
            "the optimizer should not use more bars than the exact weighted-optimal reference on the benchmark pack");

        outcomes.Average(outcome => outcome.ScoreGap).Should().BeLessThanOrEqualTo(0.02,
            "average score drift across the exact benchmark pack should remain low");

        outcomes.Max(outcome => outcome.WasteGapMm).Should().BeLessThanOrEqualTo(1200,
            "the benchmark pack should keep absolute waste drift small even when stock-length choice differs");

        outcomes.Average(outcome => outcome.WasteGapPercentOfExactStock).Should().BeLessThanOrEqualTo(3.0,
            "average waste gap across the exact benchmark pack should remain low");

        Percentile95(outcomes.Select(outcome => outcome.WasteGapPercentOfExactStock).ToList())
            .Should().BeLessThanOrEqualTo(8.0,
                "the upper-tail waste gap across the benchmark pack should remain controlled");
    }

    private BenchmarkOutcome RunBenchmarkCase(BenchmarkCase testCase)
    {
        var actual = _optimizer.Optimize(testCase.Lengths, testCase.StockLengths, DefaultSettings);
        var exact = SolveExactReference(testCase.Lengths, testCase.StockLengths, DefaultSettings);

        double actualScore = ComputeObjectiveScore(actual, testCase.Lengths.Count, DefaultSettings);

        return new BenchmarkOutcome(
            testCase.Name,
            actualScore - exact.Score,
            actual.TotalStockBarsNeeded - exact.BarCount,
            actual.TotalWasteMm - exact.TotalWasteMm,
            exact.TotalStockLengthMm > 0
                ? (actual.TotalWasteMm - exact.TotalWasteMm) / exact.TotalStockLengthMm * 100.0
                : 0);
    }

    private static ExactOptimizationReference SolveExactReference(
        IReadOnlyList<double> lengths,
        IReadOnlyList<StockLength> stockLengths,
        OptimizationSettings settings)
    {
        var effectiveLengths = lengths
            .OrderByDescending(length => length)
            .Select(length => length + settings.SawCutWidthMm)
            .ToArray();

        var candidateStocks = stockLengths
            .Where(stock => stock.InStock)
            .Select(stock => stock.LengthMm)
            .Distinct()
            .OrderBy(length => length)
            .ToArray();

        double bestScore = double.PositiveInfinity;
        int bestBarCount = int.MaxValue;
        double bestTotalStockLength = double.PositiveInfinity;
        var bins = new List<ExactBin>();

        void Search(int index)
        {
            if (index == effectiveLengths.Length)
            {
                double currentTotalStockLength = bins.Sum(bin => bin.StockLengthMm);
                double currentWaste = currentTotalStockLength - lengths.Sum() - lengths.Count * settings.SawCutWidthMm;
                double currentWastePercent = currentTotalStockLength > 0
                    ? currentWaste / currentTotalStockLength * 100.0
                    : 0;
                double currentScore = settings.WasteWeight * (currentWastePercent / 100.0)
                    + settings.InstallationWeight * ((double)bins.Count / lengths.Count);

                if (currentScore < bestScore - 1e-9 ||
                    (Math.Abs(currentScore - bestScore) <= 1e-9 && currentTotalStockLength < bestTotalStockLength - 1e-6) ||
                    (Math.Abs(currentScore - bestScore) <= 1e-9 && Math.Abs(currentTotalStockLength - bestTotalStockLength) <= 1e-6 && bins.Count < bestBarCount))
                {
                    bestScore = currentScore;
                    bestBarCount = bins.Count;
                    bestTotalStockLength = currentTotalStockLength;
                }

                return;
            }

            double piece = effectiveLengths[index];
            var seenStates = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < bins.Count; i++)
            {
                if (bins[i].UsedEffectiveLengthMm + piece > bins[i].StockLengthMm + 1e-6)
                    continue;

                string stateKey = $"{bins[i].StockLengthMm:F3}:{bins[i].UsedEffectiveLengthMm:F3}";
                if (!seenStates.Add(stateKey))
                    continue;

                bins[i] = bins[i] with { UsedEffectiveLengthMm = bins[i].UsedEffectiveLengthMm + piece };
                Search(index + 1);
                bins[i] = bins[i] with { UsedEffectiveLengthMm = bins[i].UsedEffectiveLengthMm - piece };
            }

            foreach (double stockLength in candidateStocks)
            {
                if (piece > stockLength + 1e-6)
                    continue;

                bins.Add(new ExactBin(stockLength, piece));
                Search(index + 1);
                bins.RemoveAt(bins.Count - 1);
            }
        }

        Search(0);

        double totalRequiredLength = lengths.Sum();
        return new ExactOptimizationReference(
            bestScore,
            bestBarCount,
            bestTotalStockLength - totalRequiredLength - lengths.Count * settings.SawCutWidthMm,
            bestTotalStockLength);
    }

    private static double ComputeObjectiveScore(
        OptimizationResult result,
        int itemCount,
        OptimizationSettings settings)
    {
        double wasteScore = result.TotalWastePercent / 100.0;
        double installScore = itemCount > 0
            ? (double)result.TotalStockBarsNeeded / itemCount
            : 0;

        return settings.WasteWeight * wasteScore
             + settings.InstallationWeight * installScore;
    }

    private static double Percentile95(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
            return 0;

        var sorted = values.OrderBy(value => value).ToList();
        int index = (int)Math.Ceiling(sorted.Count * 0.95) - 1;
        index = Math.Clamp(index, 0, sorted.Count - 1);
        return sorted[index];
    }

    private sealed record BenchmarkCase(
        string Name,
        IReadOnlyList<double> Lengths,
        IReadOnlyList<StockLength> StockLengths);

    private sealed record ExactOptimizationReference(
        double Score,
        int BarCount,
        double TotalWasteMm,
        double TotalStockLengthMm);

    private sealed record BenchmarkOutcome(
        string Name,
        double ScoreGap,
        int BarGap,
        double WasteGapMm,
        double WasteGapPercentOfExactStock);

    private sealed record ExactBin(double StockLengthMm, double UsedEffectiveLengthMm);
}