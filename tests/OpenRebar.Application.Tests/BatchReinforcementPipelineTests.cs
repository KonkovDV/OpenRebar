using OpenRebar.Application.UseCases;
using OpenRebar.Domain.Models;
using OpenRebar.Domain.Ports;
using FluentAssertions;
using NSubstitute;

namespace OpenRebar.Application.Tests;

public class BatchReinforcementPipelineTests
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

    public BatchReinforcementPipelineTests()
    {
        _dxfParser.SupportedExtensions.Returns([".dxf"]);
        _pngParser.SupportedExtensions.Returns([".png", ".jpg", ".jpeg", ".bmp", ".tiff"]);
    }

    [Fact]
    public async Task ExecuteAsync_ThreeInputs_ShouldAggregateResults()
    {
        var batch = CreateSut();
        ConfigureSuccessfulPipeline();

        var inputs = new[]
        {
            CreateInput("slab-1.dxf", "SLAB-1"),
            CreateInput("slab-2.dxf", "SLAB-2"),
            CreateInput("slab-3.dxf", "SLAB-3")
        };

        var result = await batch.ExecuteAsync(inputs);

        result.SlabResults.Should().HaveCount(3);
        result.Failures.Should().BeEmpty();
        result.TotalStockBars.Should().Be(3);
        result.TotalMassKg.Should().BeApproximately(3 * 6.39, 0.01);
        result.AverageWastePercent.Should().BeApproximately(38.46, 0.01);
    }

    [Fact]
    public async Task ExecuteAsync_PreCancelledToken_ShouldThrowOperationCanceledException()
    {
        var batch = CreateSut();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await batch.ExecuteAsync([CreateInput("slab-1.dxf", "SLAB-1")], cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ExecuteAsync_WhenOneInputFails_ShouldCaptureFailureAndContinue()
    {
        var batch = CreateSut();
        ConfigureSuccessfulPipeline();

        _dxfParser.ParseAsync("broken.dxf", Arg.Any<ColorLegend>(), Arg.Any<CancellationToken>())
            .Returns<Task<IReadOnlyList<ReinforcementZone>>>(_ => throw new InvalidOperationException("Broken DXF"));

        var inputs = new[]
        {
            CreateInput("slab-1.dxf", "SLAB-1"),
            CreateInput("broken.dxf", "SLAB-BROKEN"),
            CreateInput("slab-2.dxf", "SLAB-2")
        };

        var result = await batch.ExecuteAsync(inputs);

        result.SlabResults.Should().HaveCount(2);
        result.Failures.Should().ContainSingle();
        result.Failures[0].SlabId.Should().Be("SLAB-BROKEN");
        result.Failures[0].ErrorMessage.Should().Contain("Broken DXF");
    }

    [Fact]
    public void AverageWastePercent_ShouldUseWeightedPurchasedLengthInsteadOfPlainMean()
    {
        var batch = new BatchResult
        {
            SlabResults =
            [
                new BatchSlabResult
                {
                    SlabId = "SMALL-HIGH-WASTE",
                    Result = new PipelineResult
                    {
                        OptimizationResults = new Dictionary<int, OptimizationResult>
                        {
                            [12] = new OptimizationResult
                            {
                                CuttingPlans =
                                [
                                    new CuttingPlan
                                    {
                                        StockLengthMm = 10000,
                                        Cuts = [1000]
                                    }
                                ],
                                TotalStockBarsNeeded = 1,
                                TotalWasteMm = 9000,
                                TotalWastePercent = 90,
                                TotalRebarLengthMm = 1000
                            }
                        }
                    }
                },
                new BatchSlabResult
                {
                    SlabId = "LARGE-LOW-WASTE",
                    Result = new PipelineResult
                    {
                        OptimizationResults = new Dictionary<int, OptimizationResult>
                        {
                            [12] = new OptimizationResult
                            {
                                CuttingPlans = Enumerable.Range(0, 10)
                                    .Select(_ => new CuttingPlan
                                    {
                                        StockLengthMm = 10000,
                                        Cuts = [10000]
                                    })
                                    .ToList(),
                                TotalStockBarsNeeded = 10,
                                TotalWasteMm = 0,
                                TotalWastePercent = 0,
                                TotalRebarLengthMm = 100000
                            }
                        }
                    }
                }
            ]
        };

        batch.AverageWastePercent.Should().BeApproximately(8.18, 0.01,
            "batch waste should be weighted by purchased stock length, so a tiny bad slab cannot dominate the aggregate KPI");
    }

    private BatchReinforcementPipeline CreateSut()
    {
        var pipeline = new GenerateReinforcementPipeline(
            _dxfParser,
            _pngParser,
            _zoneDetector,
            _calculator,
            _optimizer,
            _catalogLoader,
            _placer,
            _reportStore,
            _logger);

        return new BatchReinforcementPipeline(pipeline);
    }

    private void ConfigureSuccessfulPipeline()
    {
        _dxfParser.ParseAsync(Arg.Any<string>(), Arg.Any<ColorLegend>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var slabId = Path.GetFileNameWithoutExtension(callInfo.ArgAt<string>(0)).ToUpperInvariant();
                IReadOnlyList<ReinforcementZone> zones = [CreateZone(slabId)];
                return Task.FromResult(zones);
            });

        _zoneDetector.ClassifyAndDecompose(Arg.Any<IReadOnlyList<ReinforcementZone>>(), Arg.Any<SlabGeometry>())
            .Returns(callInfo => callInfo.ArgAt<IReadOnlyList<ReinforcementZone>>(0));

        _calculator.CalculateRebars(Arg.Any<IReadOnlyList<ReinforcementZone>>(), Arg.Any<SlabGeometry>())
            .Returns(callInfo => callInfo.ArgAt<IReadOnlyList<ReinforcementZone>>(0));

        _catalogLoader.GetDefaultCatalog().Returns(new SupplierCatalog
        {
            SupplierName = "Default",
            AvailableLengths = [new StockLength { LengthMm = 11700, InStock = true }]
        });

        _optimizer.Optimize(Arg.Any<IReadOnlyList<double>>(), Arg.Any<IReadOnlyList<StockLength>>(), Arg.Any<OptimizationSettings>())
            .Returns(new OptimizationResult
            {
                CuttingPlans = [new CuttingPlan { StockLengthMm = 11700, Cuts = [2400, 2400, 2400] }],
                TotalStockBarsNeeded = 1,
                TotalWasteMm = 4500,
                TotalWastePercent = 38.46,
                TotalRebarLengthMm = 7200,
                TotalMassKg = 6.39
            });
    }

    private static PipelineInput CreateInput(string filePath, string slabId) => new()
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
        Metadata = new PipelineExecutionMetadata
        {
            ProjectCode = "OpenRebar-BATCH",
            SlabId = slabId,
            LevelName = "Batch"
        },
        PlaceInRevit = false,
        PersistReport = false
    };

    private static ReinforcementZone CreateZone(string id) => new()
    {
        Id = id,
        Boundary = new Polygon([
            new Point2D(0, 0),
            new Point2D(3000, 0),
            new Point2D(3000, 3000),
            new Point2D(0, 3000)
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
    };
}