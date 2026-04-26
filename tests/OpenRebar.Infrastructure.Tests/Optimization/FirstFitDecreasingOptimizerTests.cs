using OpenRebar.Domain.Models;
using OpenRebar.Domain.Exceptions;
using OpenRebar.Domain.Ports;
using OpenRebar.Infrastructure.Optimization;
using FluentAssertions;

namespace OpenRebar.Infrastructure.Tests.Optimization;

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
    result.CuttingPlans[0].WasteMm.Should().BeApproximately(1697, 0.001);
    result.TotalWasteMm.Should().BeApproximately(1697, 0.001);
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

  [Fact]
  public void PieceLongerThanAnyStock_ShouldThrowOptimizationException()
  {
    var lengths = new List<double> { 12_000 }; // 12000 + 3mm saw cut > 11700 stock

    var act = () => _optimizer.Optimize(lengths, DefaultStock, DefaultSettings);

    act.Should().Throw<OptimizationException>()
        .WithMessage("*exceeds all available stock lengths*");
  }

  [Fact]
  public void MultipleStockLengths_ShouldMixStockWhenItImprovesOverallPacking()
  {
    var stock = new List<StockLength>
        {
            new() { LengthMm = 11700, InStock = true },
            new() { LengthMm = 6000, InStock = true },
        };

    var lengths = Enumerable.Repeat(5500.0, 9).ToList();

    var result = _optimizer.Optimize(lengths, stock, DefaultSettings);

    result.TotalStockBarsNeeded.Should().Be(5);
    result.CuttingPlans.Should().Contain(plan => plan.StockLengthMm == 11700);
    result.CuttingPlans.Should().Contain(plan => plan.StockLengthMm == 6000);
  }
}
