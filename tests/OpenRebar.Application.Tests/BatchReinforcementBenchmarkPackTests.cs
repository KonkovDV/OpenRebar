using OpenRebar.Application.UseCases;
using OpenRebar.Domain.Models;
using OpenRebar.Domain.Ports;
using OpenRebar.Infrastructure.Catalog;
using OpenRebar.Infrastructure.DxfProcessing;
using OpenRebar.Infrastructure.ImageProcessing;
using OpenRebar.Infrastructure.Logging;
using OpenRebar.Infrastructure.Optimization;
using OpenRebar.Infrastructure.ReinforcementEngine;
using OpenRebar.Infrastructure.Reporting;
using OpenRebar.Infrastructure.Stubs;
using OpenRebar.Infrastructure.ZoneProcessing;
using FluentAssertions;
using IxMilia.Dxf;
using IxMilia.Dxf.Entities;

namespace OpenRebar.Application.Tests;

public class BatchReinforcementBenchmarkPackTests
{
    private static readonly OptimizationSettings BenchmarkOptimizationSettings = new()
    {
        SawCutWidthMm = 3,
        MinScrapLengthMm = 300
    };

    [Fact]
    public async Task ExecuteAsync_WithRealAdapters_ShouldStayWithinBatchQualityEnvelope()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"OpenRebar-batch-bench-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);

        try
        {
            var logger = new ConsoleStructuredLogger();
            var catalogLoader = new FileSupplierCatalogLoader();
            var pipeline = new GenerateReinforcementPipeline(
                dxfParser: new DxfIsolineParser(),
                pngParser: new PngIsolineParser(),
                zoneDetector: new StandardZoneDetector(),
                calculator: new StandardReinforcementCalculator(logger),
                optimizer: new ColumnGenerationOptimizer(),
                catalogLoader: catalogLoader,
                placer: new StubRevitPlacer(),
                reportStore: new JsonFileReportStore(),
                logger: logger);

            var batchPipeline = new BatchReinforcementPipeline(pipeline);
            var inputs = CreateBenchmarkInputs(tempDirectory);
            var defaultCatalog = catalogLoader.GetDefaultCatalog();
            var ffdOptimizer = new FirstFitDecreasingOptimizer();

            var result = await batchPipeline.ExecuteAsync(inputs);

            result.Failures.Should().BeEmpty();
            result.SlabResults.Should().HaveCount(inputs.Count);
            result.TotalStockBars.Should().Be(result.SlabResults.Sum(slab =>
                slab.Result.OptimizationResults.Values.Sum(opt => opt.TotalStockBarsNeeded)));
            result.TotalMassKg.Should().BeApproximately(
                result.SlabResults.Sum(slab => slab.Result.TotalMassKg),
                0.001);
            result.AverageWastePercent.Should().BeApproximately(
                CalculateWeightedWastePercent(result.SlabResults),
                0.0001);

            foreach (var slabResult in result.SlabResults)
            {
                slabResult.Result.StoredReport.Should().NotBeNull();
                File.Exists(slabResult.Result.StoredReport!.OutputPath).Should().BeTrue();
                slabResult.Result.Report.Should().NotBeNull();
                slabResult.Result.Report!.AnalysisProvenance.Optimization.OptimizerId
                    .Should().BeOneOf(
                        "column-generation-relaxation-v1",
                        "first-fit-decreasing-v1",
                        "exact-small-instance-search-v1");

                var requiredLengths = slabResult.Result.ClassifiedZones
                    .SelectMany(zone => zone.Rebars)
                    .Select(rebar => rebar.TotalLength)
                    .ToList();

                requiredLengths.Should().NotBeEmpty();

                var actual = slabResult.Result.OptimizationResults.Values.Single();
                var baseline = ffdOptimizer.Optimize(
                    requiredLengths,
                    defaultCatalog.AvailableLengths,
                    BenchmarkOptimizationSettings);

                actual.TotalStockBarsNeeded.Should().BeLessThanOrEqualTo(
                    baseline.TotalStockBarsNeeded,
                    "column generation should not use more bars than the FFD baseline for slab {0}",
                    slabResult.SlabId);
                actual.TotalWastePercent.Should().BeLessThanOrEqualTo(
                    baseline.TotalWastePercent + 0.01,
                    "column generation should not regress waste versus FFD for slab {0}",
                    slabResult.SlabId);
            }
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
                Directory.Delete(tempDirectory, recursive: true);
        }
    }

    private static List<PipelineInput> CreateBenchmarkInputs(string tempDirectory)
    {
        var cases = new[]
        {
            new BenchmarkCase(
                FileName: "floor-batch-a.dxf",
                SlabId: "BATCH-A",
                SlabWidthMm: 6000,
                SlabHeightMm: 6000,
                Zones:
                [
                    new RectangleSpec(0, 0, 2500, 2500)
                ]),
            new BenchmarkCase(
                FileName: "floor-batch-b.dxf",
                SlabId: "BATCH-B",
                SlabWidthMm: 9000,
                SlabHeightMm: 6000,
                Zones:
                [
                    new RectangleSpec(500, 500, 4200, 1800),
                    new RectangleSpec(5200, 900, 1800, 1200)
                ]),
            new BenchmarkCase(
                FileName: "floor-batch-c.dxf",
                SlabId: "BATCH-C",
                SlabWidthMm: 12000,
                SlabHeightMm: 8000,
                Zones:
                [
                    new RectangleSpec(800, 800, 4800, 2400),
                    new RectangleSpec(6500, 1200, 3200, 2200),
                    new RectangleSpec(2400, 4200, 2600, 1800)
                ])
        };

        return cases.Select(@case =>
        {
            var dxfPath = Path.Combine(tempDirectory, @case.FileName);
            var reportPath = Path.ChangeExtension(dxfPath, ".result.json");
            CreateSampleDxf(dxfPath, @case.Zones);

            return new PipelineInput
            {
                IsolineFilePath = dxfPath,
                Legend = CreateLegend(),
                Slab = CreateSlab(@case.SlabWidthMm, @case.SlabHeightMm),
                Metadata = new PipelineExecutionMetadata
                {
                    ProjectCode = "OpenRebar-BATCH-BENCH",
                    SlabId = @case.SlabId,
                    LevelName = "Benchmark"
                },
                OptimizationSettings = BenchmarkOptimizationSettings,
                PlaceInRevit = false,
                PersistReport = true,
                ReportOutputPath = reportPath
            };
        }).ToList();
    }

    private static double CalculateWeightedWastePercent(IReadOnlyList<BatchSlabResult> slabResults)
    {
        double totalWaste = slabResults.Sum(result =>
            result.Result.OptimizationResults.Values.Sum(optimization => optimization.TotalWasteMm));

        double totalPurchasedLength = slabResults.Sum(result =>
            result.Result.OptimizationResults.Values
                .SelectMany(optimization => optimization.CuttingPlans)
                .Sum(plan => plan.StockLengthMm));

        return totalPurchasedLength > 0
            ? totalWaste / totalPurchasedLength * 100.0
            : 0;
    }

    private static void CreateSampleDxf(string outputPath, IReadOnlyList<RectangleSpec> zones)
    {
        var dxfFile = new DxfFile();
        dxfFile.Header.Version = DxfAcadVersion.R2000;

        foreach (var zone in zones)
        {
            dxfFile.Entities.Add(new DxfLwPolyline(
            [
                new DxfLwPolylineVertex { X = zone.X, Y = zone.Y },
                new DxfLwPolylineVertex { X = zone.X + zone.Width, Y = zone.Y },
                new DxfLwPolylineVertex { X = zone.X + zone.Width, Y = zone.Y + zone.Height },
                new DxfLwPolylineVertex { X = zone.X, Y = zone.Y + zone.Height }
            ])
            {
                IsClosed = true,
                Color = DxfColor.FromIndex(1)
            });
        }

        dxfFile.Save(outputPath);
    }

    private static ColorLegend CreateLegend() => new(
    [
        new LegendEntry(
            new IsolineColor(255, 0, 0),
            new ReinforcementSpec
            {
                DiameterMm = 12,
                SpacingMm = 200,
                SteelClass = "A500C"
            })
    ]);

    private static SlabGeometry CreateSlab(double widthMm, double heightMm) => new()
    {
        OuterBoundary = new Polygon(
        [
            new Point2D(0, 0),
            new Point2D(widthMm, 0),
            new Point2D(widthMm, heightMm),
            new Point2D(0, heightMm)
        ]),
        ThicknessMm = 200,
        CoverMm = 25,
        ConcreteClass = "B25"
    };

    private sealed record BenchmarkCase(
        string FileName,
        string SlabId,
        double SlabWidthMm,
        double SlabHeightMm,
        IReadOnlyList<RectangleSpec> Zones);

    private sealed record RectangleSpec(double X, double Y, double Width, double Height);
}