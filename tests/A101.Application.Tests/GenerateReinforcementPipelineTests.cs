using A101.Application.UseCases;
using A101.Domain.Models;
using A101.Domain.Ports;
using FluentAssertions;
using NSubstitute;

namespace A101.Application.Tests;

public class GenerateReinforcementPipelineTests
{
    private readonly IIsolineParser _dxfParser = Substitute.For<IIsolineParser>();
    private readonly IIsolineParser _pngParser = Substitute.For<IIsolineParser>();
    private readonly IZoneDetector _zoneDetector = Substitute.For<IZoneDetector>();
    private readonly IReinforcementCalculator _calculator = Substitute.For<IReinforcementCalculator>();
    private readonly IRebarOptimizer _optimizer = Substitute.For<IRebarOptimizer>();
    private readonly ISupplierCatalogLoader _catalogLoader = Substitute.For<ISupplierCatalogLoader>();
    private readonly IRevitPlacer _placer = Substitute.For<IRevitPlacer>();

    private GenerateReinforcementPipeline CreateSut() => new(
        _dxfParser,
        _pngParser,
        _zoneDetector,
        _calculator,
        _optimizer,
        _catalogLoader,
        _placer);

    [Fact]
    public async Task ExecuteAsync_DxfInput_ShouldUseDxfParser()
    {
        var sut = CreateSut();
        var input = CreateInput("plan.dxf", placeInRevit: false);
        var rawZone = CreateZone("Z-1");
        var classifiedZones = new[] { rawZone };
        var optimized = CreateOptimizationResult();

        _dxfParser.ParseAsync(input.IsolineFilePath, input.Legend, Arg.Any<CancellationToken>())
            .Returns([rawZone]);
        _zoneDetector.ClassifyAndDecompose(Arg.Any<IReadOnlyList<ReinforcementZone>>(), input.Slab)
            .Returns(classifiedZones);
        _calculator.CalculateRebars(classifiedZones, input.Slab)
            .Returns(classifiedZones);
        _catalogLoader.GetDefaultCatalog().Returns(new SupplierCatalog
        {
            SupplierName = "Default",
            AvailableLengths = [new StockLength { LengthMm = 11700, InStock = true }]
        });
        _optimizer.Optimize(Arg.Any<IReadOnlyList<double>>(), Arg.Any<IReadOnlyList<StockLength>>(), input.OptimizationSettings)
            .Returns(optimized);

        var result = await sut.ExecuteAsync(input);

        result.ParsedZoneCount.Should().Be(1);
        await _dxfParser.Received(1).ParseAsync(input.IsolineFilePath, input.Legend, Arg.Any<CancellationToken>());
        await _pngParser.DidNotReceive().ParseAsync(Arg.Any<string>(), Arg.Any<ColorLegend>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_PngInput_ShouldUsePngParser()
    {
        var sut = CreateSut();
        var input = CreateInput("plan.png", placeInRevit: false);
        var rawZone = CreateZone("Z-1");
        var classifiedZones = new[] { rawZone };

        _pngParser.ParseAsync(input.IsolineFilePath, input.Legend, Arg.Any<CancellationToken>())
            .Returns([rawZone]);
        _zoneDetector.ClassifyAndDecompose(Arg.Any<IReadOnlyList<ReinforcementZone>>(), input.Slab)
            .Returns(classifiedZones);
        _calculator.CalculateRebars(classifiedZones, input.Slab)
            .Returns(classifiedZones);
        _catalogLoader.GetDefaultCatalog().Returns(new SupplierCatalog
        {
            SupplierName = "Default",
            AvailableLengths = [new StockLength { LengthMm = 11700, InStock = true }]
        });
        _optimizer.Optimize(Arg.Any<IReadOnlyList<double>>(), Arg.Any<IReadOnlyList<StockLength>>(), input.OptimizationSettings)
            .Returns(CreateOptimizationResult());

        await sut.ExecuteAsync(input);

        await _pngParser.Received(1).ParseAsync(input.IsolineFilePath, input.Legend, Arg.Any<CancellationToken>());
        await _dxfParser.DidNotReceive().ParseAsync(Arg.Any<string>(), Arg.Any<ColorLegend>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WhenPlacementDisabled_ShouldNotCallRevitPlacer()
    {
        var sut = CreateSut();
        var input = CreateInput("plan.dxf", placeInRevit: false);
        var zone = CreateZone("Z-1");
        zone.Rebars =
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
        ];
        var zones = new[] { zone };

        _dxfParser.ParseAsync(input.IsolineFilePath, input.Legend, Arg.Any<CancellationToken>())
            .Returns(zones);
        _zoneDetector.ClassifyAndDecompose(Arg.Any<IReadOnlyList<ReinforcementZone>>(), input.Slab)
            .Returns(zones);
        _calculator.CalculateRebars(zones, input.Slab).Returns(zones);
        _catalogLoader.GetDefaultCatalog().Returns(new SupplierCatalog
        {
            SupplierName = "Default",
            AvailableLengths = [new StockLength { LengthMm = 11700, InStock = true }]
        });
        _optimizer.Optimize(Arg.Any<IReadOnlyList<double>>(), Arg.Any<IReadOnlyList<StockLength>>(), input.OptimizationSettings)
            .Returns(CreateOptimizationResult());

        var result = await sut.ExecuteAsync(input);

        result.PlacementResult.Should().BeNull();
        await _placer.DidNotReceive().PlaceReinforcementAsync(
            Arg.Any<IReadOnlyList<ReinforcementZone>>(),
            Arg.Any<PlacementSettings>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WhenPlacementEnabled_ShouldCallRevitPlacer()
    {
        var sut = CreateSut();
        var input = CreateInput("plan.dxf", placeInRevit: true);
        var zone = CreateZone("Z-1");
        zone.Rebars =
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
        ];
        var zones = new[] { zone };
        var placement = new PlacementResult
        {
            TotalRebarsPlaced = 1,
            TotalTagsCreated = 1,
            TotalBendingDetails = 1
        };

        _dxfParser.ParseAsync(input.IsolineFilePath, input.Legend, Arg.Any<CancellationToken>())
            .Returns(zones);
        _zoneDetector.ClassifyAndDecompose(Arg.Any<IReadOnlyList<ReinforcementZone>>(), input.Slab)
            .Returns(zones);
        _calculator.CalculateRebars(zones, input.Slab).Returns(zones);
        _catalogLoader.GetDefaultCatalog().Returns(new SupplierCatalog
        {
            SupplierName = "Default",
            AvailableLengths = [new StockLength { LengthMm = 11700, InStock = true }]
        });
        _optimizer.Optimize(Arg.Any<IReadOnlyList<double>>(), Arg.Any<IReadOnlyList<StockLength>>(), input.OptimizationSettings)
            .Returns(CreateOptimizationResult());
        _placer.PlaceReinforcementAsync(zones, input.PlacementSettings, Arg.Any<CancellationToken>())
            .Returns(placement);

        var result = await sut.ExecuteAsync(input);

        result.PlacementResult.Should().NotBeNull();
        result.PlacementResult!.TotalRebarsPlaced.Should().Be(1);
        await _placer.Received(1).PlaceReinforcementAsync(
            zones,
            input.PlacementSettings,
            Arg.Any<CancellationToken>());
    }

    private static PipelineInput CreateInput(string filePath, bool placeInRevit)
    {
        return new PipelineInput
        {
            IsolineFilePath = filePath,
            Legend = new ColorLegend([
                new LegendEntry(new IsolineColor(255, 0, 0), new ReinforcementSpec
                {
                    DiameterMm = 12,
                    SpacingMm = 200,
                    SteelClass = "A500C"
                })
            ]),
            Slab = new SlabGeometry
            {
                OuterBoundary = new Polygon([
                    new Point2D(0, 0),
                    new Point2D(6000, 0),
                    new Point2D(6000, 6000),
                    new Point2D(0, 6000)
                ]),
                ThicknessMm = 200,
                CoverMm = 25,
                ConcreteClass = "B25"
            },
            PlaceInRevit = placeInRevit
        };
    }

    private static ReinforcementZone CreateZone(string id)
    {
        return new ReinforcementZone
        {
            Id = id,
            Boundary = new Polygon([
                new Point2D(0, 0),
                new Point2D(3000, 0),
                new Point2D(3000, 3000),
                new Point2D(0, 3000)
            ]),
            Spec = new ReinforcementSpec
            {
                DiameterMm = 12,
                SpacingMm = 200,
                SteelClass = "A500C"
            },
            Direction = RebarDirection.X,
            ZoneType = ZoneType.Simple
        };
    }

    private static OptimizationResult CreateOptimizationResult()
    {
        return new OptimizationResult
        {
            CuttingPlans =
            [
                new CuttingPlan
                {
                    StockLengthMm = 11700,
                    Cuts = [2400, 2400, 2400]
                }
            ],
            TotalStockBarsNeeded = 1,
            TotalWasteMm = 4500,
            TotalWastePercent = 38.46,
            TotalRebarLengthMm = 7200,
            TotalMassKg = 6.39
        };
    }
}