using OpenRebar.Domain.Models;
using OpenRebar.Domain.Exceptions;
using OpenRebar.Domain.Ports;
using OpenRebar.Infrastructure.Optimization;
using FluentAssertions;

namespace OpenRebar.Infrastructure.Tests.Optimization;

public class ColumnGenerationOptimizerTests
{
    private readonly IRebarOptimizer _optimizer = new ColumnGenerationOptimizer();

    private static readonly IReadOnlyList<StockLength> DefaultStock =
    [
        new StockLength { LengthMm = 11700, InStock = true },
    ];

    private static readonly OptimizationSettings DefaultSettings = new()
    {
        SawCutWidthMm = 3,
        MinScrapLengthMm = 300
    };

    [Fact]
    public void EmptyInput_ShouldReturnEmptyResult()
    {
        var result = _optimizer.Optimize([], DefaultStock, DefaultSettings);

        result.TotalStockBarsNeeded.Should().Be(0);
        result.TotalWasteMm.Should().Be(0);
        result.CuttingPlans.Should().BeEmpty();
    }

    [Fact]
    public void SingleRebarFitsInOneStock_ShouldUseOneBar()
    {
        var lengths = new List<double> { 5000 };

        var result = _optimizer.Optimize(lengths, DefaultStock, DefaultSettings);

        result.TotalStockBarsNeeded.Should().Be(1);
        result.CuttingPlans.Should().HaveCount(1);
    }

    [Fact]
    public void TwoRebarsFitInOneStock_ShouldPackTogether()
    {
        // 5000 + 5000 + 2×3mm sawcut = 10006, fits in 11700
        var lengths = new List<double> { 5000, 5000 };

        var result = _optimizer.Optimize(lengths, DefaultStock, DefaultSettings);

        result.TotalStockBarsNeeded.Should().Be(1);
    }

    [Fact]
    public void ManyIdenticalPieces_ShouldOptimizePacking()
    {
        // 10 pieces of 3000mm each. 11700/(3000+3) ≈ 3.89 → 3 per bar
        // 10/3 = ceil(3.33) = 4 bars
        var lengths = Enumerable.Repeat(3000.0, 10).ToList();
        var baseline = new FirstFitDecreasingOptimizer().Optimize(lengths, DefaultStock, DefaultSettings);

        var result = _optimizer.Optimize(lengths, DefaultStock, DefaultSettings);
        var allCuts = result.CuttingPlans.SelectMany(plan => plan.Cuts).ToList();

        result.TotalStockBarsNeeded.Should().BeLessThanOrEqualTo(4);
        allCuts.Should().HaveCount(lengths.Count,
            "column generation result should not materialize extra cuts beyond the original demand after integer rounding");
        result.TotalWastePercent.Should().BeApproximately(baseline.TotalWastePercent, 0.01,
            "with a single available stock length, the column-generation optimizer should match the practical FFD baseline");
    }

    [Fact]
    public void MixedLengths_ShouldBeatFFD()
    {
        // Gilmore-Gomory classic: lengths that benefit from column generation
        var lengths = new List<double>();
        lengths.AddRange(Enumerable.Repeat(5800.0, 4));  // long pieces
        lengths.AddRange(Enumerable.Repeat(3500.0, 6));  // medium
        lengths.AddRange(Enumerable.Repeat(2200.0, 10)); // short

        var result = _optimizer.Optimize(lengths, DefaultStock, DefaultSettings);

        // 4×5800 + 6×3500 + 10×2200 = 23200 + 21000 + 22000 = 66200mm
        // 66200/11700 = 5.66 → at least 6 bars
        result.TotalStockBarsNeeded.Should().BeGreaterThanOrEqualTo(6);
        result.TotalStockBarsNeeded.Should().BeLessThanOrEqualTo(8);
        result.TotalWastePercent.Should().BeLessThan(20);
    }

    [Fact]
    public void AllCutsPresentInResult()
    {
        var lengths = new List<double> { 5000, 3000, 2000, 1500 };

        var result = _optimizer.Optimize(lengths, DefaultStock, DefaultSettings);

        var allCuts = result.CuttingPlans.SelectMany(p => p.Cuts).ToList();
        allCuts.Should().Contain(5000);
        allCuts.Should().Contain(3000);
        allCuts.Should().Contain(2000);
        allCuts.Should().Contain(1500);
    }

    [Fact]
    public void WasteCalculation_ShouldBeConsistent()
    {
        var lengths = new List<double> { 10000 };

        var result = _optimizer.Optimize(lengths, DefaultStock, DefaultSettings);

        result.TotalRebarLengthMm.Should().Be(10000);
        result.CuttingPlans.Should().OnlyContain(plan => plan.SawCutWidthMm == DefaultSettings.SawCutWidthMm);
        result.TotalWasteMm.Should().BeApproximately(
            result.TotalStockBarsNeeded * 11700.0 - 10000 - DefaultSettings.SawCutWidthMm, 0.001);
    }

    [Fact]
    public void TypicalSlabScenario_ShouldComplete()
    {
        // Realistic: 50 rebars of varying lengths for a slab zone
        var rng = new Random(42);
        var lengths = Enumerable.Range(0, 50)
            .Select(_ => 1500.0 + rng.NextDouble() * 8000) // 1500–9500mm
            .ToList();
        var baseline = new FirstFitDecreasingOptimizer().Optimize(lengths, DefaultStock, DefaultSettings);

        var result = _optimizer.Optimize(lengths, DefaultStock, DefaultSettings);

        result.TotalStockBarsNeeded.Should().BeGreaterThan(0);
        result.CuttingPlans.Should().NotBeEmpty();
        result.TotalStockBarsNeeded.Should().BeLessThanOrEqualTo(baseline.TotalStockBarsNeeded,
            "column generation should not use more stock bars than the simpler FFD baseline on a deterministic scenario");
        result.TotalWastePercent.Should().BeLessThanOrEqualTo(baseline.TotalWastePercent + 0.01,
            "column generation should not regress versus the simpler FFD baseline on a deterministic scenario");
    }

    [Fact]
    public void MultipleStockLengths_ShouldUseAvailable()
    {
        var stock = new List<StockLength>
        {
            new() { LengthMm = 11700, InStock = true },
            new() { LengthMm = 6000, InStock = true },
        };

        var lengths = new List<double> { 5500, 5500, 5500 };

        var result = _optimizer.Optimize(lengths, stock, DefaultSettings);

        result.TotalStockBarsNeeded.Should().Be(2);
        result.CuttingPlans.Should().Contain(plan => plan.StockLengthMm == 11700,
            "the exact optimum packs two 5.5m pieces into one 11.7m bar");
        result.CuttingPlans.Should().Contain(plan => plan.StockLengthMm == 6000,
            "the remaining 5.5m piece is then served by one 6m bar");
    }

    [Fact]
    public void LargeMixedStockBatch_ShouldUseMixedStockLengthsBeyondExactSearchEnvelope()
    {
        var stock = new List<StockLength>
        {
            new() { LengthMm = 11700, InStock = true },
            new() { LengthMm = 6000, InStock = true },
        };

        var lengths = Enumerable.Repeat(5500.0, 9).ToList();

        var result = _optimizer.Optimize(lengths, stock, DefaultSettings);

        result.TotalStockBarsNeeded.Should().Be(5,
            "four 11.7m bars can cover eight pieces and one 6m bar should cover the remainder");
        result.CuttingPlans.Should().Contain(plan => plan.StockLengthMm == 11700);
        result.CuttingPlans.Should().Contain(plan => plan.StockLengthMm == 6000);
        result.TotalWasteMm.Should().BeApproximately(3273, 0.001,
            "the heterogeneous catalog should reduce waste versus any single-stock strategy on this batch");
    }

    [Fact]
    public void CostWeightedOptimization_ShouldNotBeOverriddenByBaselineGuard()
    {
        var stock = new List<StockLength>
        {
            new() { LengthMm = 12000, InStock = true, PricePerTon = 100 },
            new() { LengthMm = 6000, InStock = true, PricePerTon = 1 },
        };

        var settings = new OptimizationSettings
        {
            SawCutWidthMm = 3,
            MinScrapLengthMm = 300,
            WasteWeight = 0,
            InstallationWeight = 0,
            CostWeight = 1,
        };

        var result = _optimizer.Optimize([5500, 5500], stock, settings);

        result.CuttingPlans.Should().OnlyContain(plan => plan.StockLengthMm == 6000,
            "with CostWeight=1, optimizer should keep the cheaper stock strategy and avoid baseline override");
    }

    [Fact]
    public void NoInStockLengths_ShouldThrowOptimizationException()
    {
        var stock = new List<StockLength>
        {
            new() { LengthMm = 11700, InStock = false }
        };

        var act = () => _optimizer.Optimize([5000], stock, DefaultSettings);
        act.Should().Throw<OptimizationException>();
    }

    [Fact]
    public void PieceLongerThanAnyStock_ShouldThrowOptimizationException()
    {
        var act = () => _optimizer.Optimize([12_000], DefaultStock, DefaultSettings);

        act.Should().Throw<OptimizationException>()
            .WithMessage("*exceeds all available stock lengths*");
    }

    [Fact]
    public void SmallExactInstance_ShouldMatchExactMinimumBarCount()
    {
        var lengths = new List<double> { 5800, 5800, 3500, 3500, 2200, 2200 };

        var result = _optimizer.Optimize(lengths, DefaultStock, DefaultSettings);
        int exact = SolveExactMinimumBars(lengths, DefaultStock[0].LengthMm, DefaultSettings.SawCutWidthMm);

        result.TotalStockBarsNeeded.Should().Be(exact);
    }

    [Fact]
    public void SmallExactInstance_ShouldExposeExactSearchProvenance()
    {
        var lengths = new List<double> { 3000, 3000, 3000, 3000, 1500, 1500 };

        var result = _optimizer.Optimize(lengths, DefaultStock, DefaultSettings);

        result.Provenance.Should().NotBeNull();
        result.Provenance!.OptimizerId.Should().Be("exact-small-instance-search-v1");
        result.Provenance.PricingStrategy.Should().Be("not-applicable");
        result.Provenance.IntegerizationStrategy.Should().Be("exact-discrete-search");
        result.Provenance.QualityFloor.Should().Be("exact-small-instance-optimum");
    }

    [Fact]
    public void ForcedFallbackMasterSolver_ShouldSuppressUnreliableBounds()
    {
        using var _ = ColumnGenerationOptimizer.PushForceFallbackMasterSolverOverrideForTesting();

        var lengths = Enumerable.Repeat(3000.0, 10).ToList();

        var result = _optimizer.Optimize(lengths, DefaultStock, DefaultSettings);

        result.Provenance.Should().NotBeNull();
        result.Provenance!.UsedFallbackMasterSolver.Should().BeTrue();
        result.Provenance.MasterProblemStrategy.Should().Be("restricted-master-lp-highs-with-fallback");
        result.DualBound.Should().BeNull(
            "heuristic fallback master solve does not provide an audit-grade LP lower bound");
        result.Gap.Should().BeNull(
            "gap depends on a valid LP lower bound and must be suppressed for heuristic fallback master solve");
        result.Provenance.QualityGapPercent.Should().BeNull(
            "provenance should not claim a quality gap when the underlying bound is not mathematically reliable");
    }

    [Fact]
    public void HighsMasterSolver_ShouldExposeFormulaConsistentReliableBounds()
    {
        var lengths = Enumerable.Repeat(3000.0, 10).ToList();

        var result = _optimizer.Optimize(lengths, DefaultStock, DefaultSettings);

        result.Provenance.Should().NotBeNull();
        result.Provenance!.UsedFallbackMasterSolver.Should().BeFalse(
            "this deterministic scenario is expected to use the HiGHS LP master path");

        result.DualBound.Should().HaveValue();
        result.Gap.Should().HaveValue();
        result.Provenance.QualityGapPercent.Should().HaveValue();

        double dualBound = result.DualBound!.Value;
        double expectedGap = (result.TotalStockBarsNeeded - dualBound) / dualBound * 100.0;

        dualBound.Should().BeGreaterThan(0,
            "LP lower bound must be positive for a non-empty demand instance");
        dualBound.Should().BeLessThanOrEqualTo(result.TotalStockBarsNeeded + 1e-6,
            "LP lower bound cannot exceed the integer number of purchased bars");
        result.Gap!.Value.Should().BeGreaterThanOrEqualTo(-1e-6,
            "primal-vs-dual gap should be non-negative on a mathematically reliable master LP solve");
        result.Gap.Value.Should().BeApproximately(expectedGap, 1e-6,
            "reported gap must match (primal - dual) / dual * 100");
        result.Provenance.QualityGapPercent!.Value.Should().BeApproximately(result.Gap.Value, 1e-6,
            "provenance quality gap should be exactly aligned with result-level gap for reliable LP bounds");
    }

    [Fact]
    public void FractionalLengthInput_ShouldKeepEveryPlanPhysicallyFeasible()
    {
        var lengths = new List<double> { 500.06, 500.06, 500.06 };
        var stock = new List<StockLength>
        {
            new() { LengthMm = 1000, InStock = true }
        };

        var result = _optimizer.Optimize(lengths, stock, DefaultSettings with { SawCutWidthMm = 0 });

        result.CuttingPlans.Should().OnlyContain(plan =>
            plan.ConsumedLengthMm <= plan.StockLengthMm + 1e-6,
            "each emitted cutting plan must be physically feasible even for fractional-length demands");
    }

    private static int SolveExactMinimumBars(
        IReadOnlyList<double> lengths,
        double stockLength,
        double sawCutWidthMm)
    {
        var effective = lengths
            .Select(length => length + sawCutWidthMm)
            .OrderByDescending(length => length)
            .ToArray();

        int best = effective.Length;
        var bins = new List<double>();

        void Search(int index)
        {
            if (bins.Count >= best)
                return;

            if (index == effective.Length)
            {
                best = Math.Min(best, bins.Count);
                return;
            }

            double piece = effective[index];
            var seen = new HashSet<int>();

            for (int i = 0; i < bins.Count; i++)
            {
                int rounded = (int)Math.Round(bins[i], MidpointRounding.AwayFromZero);
                if (!seen.Add(rounded))
                    continue;

                if (bins[i] + piece <= stockLength + 1e-6)
                {
                    bins[i] += piece;
                    Search(index + 1);
                    bins[i] -= piece;
                }
            }

            bins.Add(piece);
            Search(index + 1);
            bins.RemoveAt(bins.Count - 1);
        }

        Search(0);
        return best;
    }
}
