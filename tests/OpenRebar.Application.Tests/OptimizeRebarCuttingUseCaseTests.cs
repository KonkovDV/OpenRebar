using OpenRebar.Application.UseCases;
using OpenRebar.Domain.Models;
using OpenRebar.Domain.Ports;
using FluentAssertions;
using NSubstitute;

namespace OpenRebar.Application.Tests;

public class OptimizeRebarCuttingUseCaseTests
{
  private readonly IRebarOptimizer _optimizer = Substitute.For<IRebarOptimizer>();
  private readonly ISupplierCatalogLoader _catalogLoader = Substitute.For<ISupplierCatalogLoader>();
  private readonly IStructuredLogger _logger = Substitute.For<IStructuredLogger>();

  [Fact]
  public async Task ExecuteAsync_ShouldEstimateCostFromPurchasedStock()
  {
    var useCase = new OptimizeRebarCuttingUseCase(_optimizer, _catalogLoader, _logger);
    IReadOnlyList<ReinforcementZone> zones =
    [
        new ReinforcementZone
            {
                Id = "Z-1",
                Boundary = new Polygon([
                    new Point2D(0, 0),
                    new Point2D(1000, 0),
                    new Point2D(1000, 1000),
                    new Point2D(0, 1000)
                ]),
                Spec = new ReinforcementSpec { DiameterMm = 12, SpacingMm = 200, SteelClass = "A500C" },
                Direction = RebarDirection.X,
                ZoneType = ZoneType.Simple,
                Rebars =
                [
                    new RebarSegment
                    {
                        Start = new Point2D(0, 0),
                        End = new Point2D(1000, 0),
                        DiameterMm = 12,
                        AnchorageLengthStart = 200,
                        AnchorageLengthEnd = 200,
                        Mark = "1"
                    }
                ]
            }
    ];

    var catalog = new SupplierCatalog
    {
      SupplierName = "Supplier",
      AvailableLengths =
        [
            new StockLength { LengthMm = 6000, PricePerTon = 50000, InStock = true }
        ]
    };

    _catalogLoader.GetDefaultCatalog().Returns(catalog);
    _optimizer.Optimize(Arg.Any<IReadOnlyList<double>>(), Arg.Any<IReadOnlyList<StockLength>>(), Arg.Any<OptimizationSettings>())
        .Returns(new OptimizationResult
        {
          CuttingPlans =
            [
                new CuttingPlan
                    {
                        StockLengthMm = 6000,
                        Cuts = [1400]
                    }
            ],
          TotalStockBarsNeeded = 1,
          TotalWasteMm = 4600,
          TotalWastePercent = 76.67,
          TotalRebarLengthMm = 1400
        });

    var report = await useCase.ExecuteAsync(zones, null, new OptimizationSettings());

    report.DiameterReports.Should().HaveCount(1);
    report.DiameterReports[0].OptimizationResult.EstimatedCost.Should().HaveValue();
    report.DiameterReports[0].OptimizationResult.EstimatedCost!.Value.Should().BeGreaterThan(0);
  }

  [Fact]
  public async Task AverageWastePercent_ShouldBeWeightedByPurchasedStock()
  {
    var useCase = new OptimizeRebarCuttingUseCase(_optimizer, _catalogLoader, _logger);
    IReadOnlyList<ReinforcementZone> zones =
    [
        MakeZone("Z-12", 12),
            MakeZone("Z-16", 16)
    ];

    var catalog = new SupplierCatalog
    {
      SupplierName = "Supplier",
      AvailableLengths =
        [
            new StockLength { LengthMm = 6000, InStock = true },
                new StockLength { LengthMm = 12000, InStock = true }
        ]
    };

    _catalogLoader.GetDefaultCatalog().Returns(catalog);
    _optimizer.Optimize(Arg.Any<IReadOnlyList<double>>(), Arg.Any<IReadOnlyList<StockLength>>(), Arg.Any<OptimizationSettings>())
        .Returns(
            new OptimizationResult
            {
              CuttingPlans = [new CuttingPlan { StockLengthMm = 6000, Cuts = [1400] }],
              TotalStockBarsNeeded = 1,
              TotalWasteMm = 4600,
              TotalWastePercent = 76.67,
              TotalRebarLengthMm = 1400
            },
            new OptimizationResult
            {
              CuttingPlans = [new CuttingPlan { StockLengthMm = 12000, Cuts = [5400] }],
              TotalStockBarsNeeded = 1,
              TotalWasteMm = 6600,
              TotalWastePercent = 55.00,
              TotalRebarLengthMm = 5400
            });

    var report = await useCase.ExecuteAsync(zones, null, new OptimizationSettings());

    report.AverageWastePercent.Should().BeApproximately((4600.0 + 6600.0) / (6000.0 + 12000.0) * 100.0, 0.01);
    _logger.Received().Info("Starting cutting optimization", Arg.Any<(string Key, object? Value)[]>());
  }

  [Fact]
  public async Task ExecuteAsync_ShouldPreserveOptimizerQualityMetadata()
  {
    var useCase = new OptimizeRebarCuttingUseCase(_optimizer, _catalogLoader, _logger);
    IReadOnlyList<ReinforcementZone> zones = [MakeZone("Z-12", 12)];

    var catalog = new SupplierCatalog
    {
      SupplierName = "Supplier",
      AvailableLengths =
        [
            new StockLength { LengthMm = 6000, InStock = true }
        ]
    };

    var provenance = new OptimizationProvenance
    {
      OptimizerId = "column-generation-relaxation-v1",
      MasterProblemStrategy = "restricted-master-lp-highs",
      PricingStrategy = "bounded-knapsack-dp",
      IntegerizationStrategy = "largest-remainder-plus-repair",
      DemandAggregationPrecisionMm = 0.1,
      QualityFloor = "ffd-non-regression-floor",
      UsedFallbackMasterSolver = false,
      QualityGapPercent = 1.25
    };

    _catalogLoader.GetDefaultCatalog().Returns(catalog);
    _optimizer.Optimize(Arg.Any<IReadOnlyList<double>>(), Arg.Any<IReadOnlyList<StockLength>>(), Arg.Any<OptimizationSettings>())
        .Returns(new OptimizationResult
        {
          CuttingPlans = [new CuttingPlan { StockLengthMm = 6000, Cuts = [1400] }],
          TotalStockBarsNeeded = 1,
          TotalWasteMm = 4600,
          TotalWastePercent = 76.67,
          TotalRebarLengthMm = 1400,
          DualBound = 0.95,
          Gap = 5.26,
          Provenance = provenance
        });

    var report = await useCase.ExecuteAsync(zones, null, new OptimizationSettings());

    var result = report.DiameterReports.Single().OptimizationResult;
    result.DualBound.Should().Be(0.95);
    result.Gap.Should().Be(5.26);
    result.Provenance.Should().BeEquivalentTo(provenance);
  }

  private static ReinforcementZone MakeZone(string id, int diameterMm) =>
      new()
      {
        Id = id,
        Boundary = new Polygon([
              new Point2D(0, 0),
                new Point2D(1000, 0),
                new Point2D(1000, 1000),
                new Point2D(0, 1000)
          ]),
        Spec = new ReinforcementSpec { DiameterMm = diameterMm, SpacingMm = 200, SteelClass = "A500C" },
        Direction = RebarDirection.X,
        ZoneType = ZoneType.Simple,
        Rebars =
          [
              new RebarSegment
                {
                    Start = new Point2D(0, 0),
                    End = new Point2D(1000, 0),
                    DiameterMm = diameterMm,
                    AnchorageLengthStart = 200,
                    AnchorageLengthEnd = 200,
                    Mark = "1"
                }
          ]
      };
}
