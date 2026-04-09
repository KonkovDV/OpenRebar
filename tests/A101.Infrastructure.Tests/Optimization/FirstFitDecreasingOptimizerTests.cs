using A101.Domain.Models;
using A101.Domain.Ports;
using A101.Infrastructure.Optimization;
using FluentAssertions;

namespace A101.Infrastructure.Tests.Optimization;

public class FirstFitDecreasingOptimizerTests
{
    private readonly IRebarOptimizer _optimizer = new FirstFitDecreasingOptimizer();

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
        result.CuttingPlans[0].Cuts.Should().Contain(5000);
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
    public void RebarExceedsSingleStock_ShouldRequireMultipleBars()
    {
        // 3 × 6000 = 18000, needs at least 2 bars of 11700
        var lengths = new List<double> { 6000, 6000, 6000 };

        var result = _optimizer.Optimize(lengths, DefaultStock, DefaultSettings);

        result.TotalStockBarsNeeded.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public void WastePercentage_ShouldBeCalculatedCorrectly()
    {
        var lengths = new List<double> { 10000 }; // Uses 1 bar of 11700

        var result = _optimizer.Optimize(lengths, DefaultStock, DefaultSettings);

        result.TotalRebarLengthMm.Should().Be(10000);
        result.TotalWasteMm.Should().BeApproximately(1700, 10);
        result.TotalWastePercent.Should().BeApproximately(14.5, 1);
    }

    [Fact]
    public void ManySmallRebars_ShouldPackEfficiently()
    {
        // 20 rebars of 1000mm each = 20000mm total
        // 11700 / (1000+3) ≈ 11 per bar → need ~2 bars
        var lengths = Enumerable.Repeat(1000.0, 20).ToList();

        var result = _optimizer.Optimize(lengths, DefaultStock, DefaultSettings);

        result.TotalStockBarsNeeded.Should().BeLessThanOrEqualTo(3);
        result.TotalWastePercent.Should().BeLessThan(50,
            "efficient packing should waste less than 50%");
    }
}
