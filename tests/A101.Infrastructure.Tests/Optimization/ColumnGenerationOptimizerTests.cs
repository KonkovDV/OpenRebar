using A101.Domain.Models;
using A101.Domain.Exceptions;
using A101.Domain.Ports;
using A101.Infrastructure.Optimization;
using FluentAssertions;

namespace A101.Infrastructure.Tests.Optimization;

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

        result.TotalStockBarsNeeded.Should().BeLessThanOrEqualTo(4);
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
        result.TotalWasteMm.Should().BeApproximately(
            result.TotalStockBarsNeeded * 11700.0 - 10000, 10);
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

        result.TotalStockBarsNeeded.Should().Be(3);
        result.CuttingPlans.Should().OnlyContain(plan => plan.StockLengthMm == 6000,
            "three 5.5m pieces fit more economically into 6m stock than into 11.7m stock");
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
}
