using OpenRebar.Application.UseCases;
using OpenRebar.Domain.Exceptions;
using OpenRebar.Domain.Models;
using OpenRebar.Domain.Ports;
using FluentAssertions;
using NSubstitute;

namespace OpenRebar.Application.Tests;

public class GenerateReinforcementPipelineTests
{
    private readonly IIsolineParser _dxfParser = Substitute.For<IIsolineParser>();
    private readonly IIsolineParser _pngParser = Substitute.For<IIsolineParser>();
    private readonly IZoneDetector _zoneDetector = Substitute.For<IZoneDetector>();
    private readonly IReinforcementCalculator _calculator = Substitute.For<IReinforcementCalculator>();
    private readonly IRebarOptimizer _optimizer = Substitute.For<IRebarOptimizer>();
    private readonly ISupplierCatalogLoader _catalogLoader = Substitute.For<ISupplierCatalogLoader>();
    private readonly IRevitPlacer _placer = Substitute.For<IRevitPlacer>();
    private readonly IReportStore _reportStore = Substitute.For<IReportStore>();
    private readonly IStructuredLogger _logger = Substitute.For<IStructuredLogger>();

    public GenerateReinforcementPipelineTests()
    {
        _dxfParser.SupportedExtensions.Returns([".dxf"]);
        _pngParser.SupportedExtensions.Returns([".png", ".jpg", ".jpeg", ".bmp", ".tiff"]);
    }

    private GenerateReinforcementPipeline CreateSut() => new(
        _dxfParser,
        _pngParser,
        _zoneDetector,
        _calculator,
        _optimizer,
        _catalogLoader,
        _placer,
        _reportStore,
        _logger);

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
        result.Report.Should().NotBeNull();
        await _dxfParser.Received(1).ParseAsync(input.IsolineFilePath, input.Legend, Arg.Any<CancellationToken>());
        await _pngParser.DidNotReceive().ParseAsync(Arg.Any<string>(), Arg.Any<ColorLegend>(), Arg.Any<CancellationToken>());
        await _reportStore.DidNotReceive().SaveAsync(Arg.Any<ReinforcementExecutionReport>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        _logger.Received().Info("Starting reinforcement pipeline", Arg.Any<(string Key, object? Value)[]>());
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
    public async Task ExecuteAsync_UnsupportedInputFormat_ShouldReturnPartialResultWithError()
    {
        var sut = CreateSut();
        var input = CreateInput("plan.gif", placeInRevit: false);

        var result = await sut.ExecuteAsync(input);

        result.Report.Should().NotBeNull();
        result.Report!.PartialResult.Should().BeTrue();
        result.Report.Errors.Should().ContainSingle();
        result.Report.Errors[0].Stage.Should().Be("Parse");
        result.Report.Errors[0].IsCritical.Should().BeTrue();
        result.Report.Errors[0].ErrorMessage.Should().Contain("Unsupported isoline file format");
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
    public async Task ExecuteAsync_WhenReportPersistenceEnabled_ShouldPersistCanonicalReport()
    {
        var sut = CreateSut();
        var input = CreateInput("plan.dxf", placeInRevit: false) with
        {
            PersistReport = true,
            ReportOutputPath = "reports/plan.result.json",
            Metadata = new PipelineExecutionMetadata
            {
                ProjectCode = "OpenRebar-TST",
                SlabId = "SLAB-42",
                LevelName = "Level 12"
            }
        };

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
        _reportStore.SaveAsync(Arg.Any<ReinforcementExecutionReport>(), input.ReportOutputPath!, Arg.Any<CancellationToken>())
            .Returns(new StoredReportReference
            {
                OutputPath = input.ReportOutputPath!,
                MediaType = "application/json",
                Sha256 = "ABC123",
                ByteCount = 128
            });

        var result = await sut.ExecuteAsync(input);

        result.StoredReport.Should().NotBeNull();
        result.StoredReport!.OutputPath.Should().Be(input.ReportOutputPath);
        result.Report!.Metadata.ProjectCode.Should().Be("OpenRebar-TST");

        await _reportStore.Received(1).SaveAsync(
            Arg.Is<ReinforcementExecutionReport>(report =>
                report.Metadata.SlabId == "SLAB-42" &&
                report.Zones.Count == 1 &&
                report.Summary.TotalRebarSegments == 1),
            input.ReportOutputPath!,
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

    [Fact]
    public async Task ExecuteAsync_WhenRevitPlacementThrows_ShouldStillPersistReport()
    {
        var sut = CreateSut();
        var input = CreateInput("plan.dxf", placeInRevit: true) with
        {
            PersistReport = true,
            ReportOutputPath = "reports/plan.result.json"
        };
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
        _placer.PlaceReinforcementAsync(zones, input.PlacementSettings, Arg.Any<CancellationToken>())
            .Returns<Task<PlacementResult>>(_ => throw new InvalidOperationException("Revit refused transaction"));
        _reportStore.SaveAsync(Arg.Any<ReinforcementExecutionReport>(), input.ReportOutputPath!, Arg.Any<CancellationToken>())
            .Returns(new StoredReportReference
            {
                OutputPath = input.ReportOutputPath!,
                MediaType = "application/json",
                Sha256 = "ABC123",
                ByteCount = 128
            });

        var result = await sut.ExecuteAsync(input);

        result.StoredReport.Should().NotBeNull();
        result.PlacementResult.Should().NotBeNull();
        result.PlacementResult!.Success.Should().BeFalse();
        result.PlacementResult.Errors.Should().ContainSingle(error => error.Contains("Revit refused transaction"));
        await _reportStore.Received(1).SaveAsync(
            Arg.Any<ReinforcementExecutionReport>(),
            input.ReportOutputPath!,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WhenNoEstimatedCostExists_ShouldKeepSummaryEstimatedCostNull()
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
            .Returns(new OptimizationResult
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
                TotalMassKg = 6.39,
                EstimatedCost = null
            });

        var result = await sut.ExecuteAsync(input);

        result.Report.Should().NotBeNull();
        result.Report!.Summary.EstimatedCost.Should().BeNull();
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