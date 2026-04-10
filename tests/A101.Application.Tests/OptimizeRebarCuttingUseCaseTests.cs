using A101.Application.UseCases;
using A101.Domain.Models;
using A101.Domain.Ports;
using FluentAssertions;
using NSubstitute;

namespace A101.Application.Tests;

public class OptimizeRebarCuttingUseCaseTests
{
    private readonly IRebarOptimizer _optimizer = Substitute.For<IRebarOptimizer>();
    private readonly ISupplierCatalogLoader _catalogLoader = Substitute.For<ISupplierCatalogLoader>();

    [Fact]
    public async Task ExecuteAsync_ShouldEstimateCostFromPurchasedStock()
    {
        var useCase = new OptimizeRebarCuttingUseCase(_optimizer, _catalogLoader);
        var zones =
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
}