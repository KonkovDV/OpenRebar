using OpenRebar.Application.UseCases;
using OpenRebar.Domain.Models;
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

public class FullPipelineIntegrationTests
{
  [Fact]
  public async Task ExecuteAsync_WithRealAdapters_ShouldParseOptimizePlaceAndPersistReport()
  {
    var tempDirectory = Path.Combine(Path.GetTempPath(), $"OpenRebar-e2e-{Guid.NewGuid():N}");
    Directory.CreateDirectory(tempDirectory);

    var dxfPath = Path.Combine(tempDirectory, "floor-03.dxf");
    var reportPath = Path.Combine(tempDirectory, "floor-03.result.json");
    CreateSampleDxf(dxfPath);

    var logger = new ConsoleStructuredLogger();
    var pipeline = new GenerateReinforcementPipeline(
        dxfParser: new DxfIsolineParser(),
        pngParser: new PngIsolineParser(),
        zoneDetector: new StandardZoneDetector(),
        calculator: new StandardReinforcementCalculator(logger),
        optimizer: new ColumnGenerationOptimizer(),
        catalogLoader: new FileSupplierCatalogLoader(),
        placer: new StubRevitPlacer(),
        reportStore: new JsonFileReportStore(),
        logger: logger);

    var input = new PipelineInput
    {
      IsolineFilePath = dxfPath,
      Legend = CreateLegend(),
      Slab = CreateSlab(),
      Metadata = new PipelineExecutionMetadata
      {
        ProjectCode = "OpenRebar-E2E",
        SlabId = "SLAB-03",
        LevelName = "Level 03"
      },
      PlaceInRevit = true,
      PersistReport = true,
      ReportOutputPath = reportPath
    };

    try
    {
      var result = await pipeline.ExecuteAsync(input);

      result.ParsedZoneCount.Should().Be(1);
      result.ClassifiedZones.Should().HaveCount(1);
      result.TotalRebarSegments.Should().BeGreaterThan(0);
      result.OptimizationResults.Should().ContainKey(12);
      result.TotalWastePercent.Should().BeLessThan(20.0);

      result.PlacementResult.Should().NotBeNull();
      result.PlacementResult!.TotalRebarsPlaced.Should().Be(result.TotalRebarSegments);
      result.PlacementResult.Warnings.Should().ContainSingle(warning => warning.Contains("StubRevitPlacer"));

      result.ClassifiedZones
          .SelectMany(zone => zone.Rebars)
          .Should()
          .OnlyContain(rebar =>
              rebar.AnchorageLengthStart >= 200 &&
              rebar.AnchorageLengthEnd >= 200);

      result.Report.Should().NotBeNull();
      result.Report!.ContractId.Should().Be("OpenRebar.reinforcement.report.v1");
      result.Report.NormativeProfile.ProfileId.Should().Be("ru.sp63.2018");
      result.Report.AnalysisProvenance.Geometry.DecompositionAlgorithm.Should().Be("adaptive-orthogonal-strip-or-grid/v3");
      result.Report.AnalysisProvenance.Optimization.OptimizerId.Should().Be("column-generation-relaxation-v1");
      result.Report.Summary.TotalRebarSegments.Should().Be(result.TotalRebarSegments);

      result.StoredReport.Should().NotBeNull();
      result.StoredReport!.OutputPath.Should().Be(Path.GetFullPath(reportPath));
      File.Exists(reportPath).Should().BeTrue();

      var persistedReport = await File.ReadAllTextAsync(reportPath);
      persistedReport.Should().Contain("\"contractId\": \"OpenRebar.reinforcement.report.v1\"");
      persistedReport.Should().Contain("\"parsedZoneCount\": 1");
    }
    finally
    {
      if (Directory.Exists(tempDirectory))
        Directory.Delete(tempDirectory, recursive: true);
    }
  }

  private static void CreateSampleDxf(string outputPath)
  {
    var dxfFile = new DxfFile();
    dxfFile.Header.Version = DxfAcadVersion.R2000;
    dxfFile.Entities.Add(new DxfLwPolyline([
        new DxfLwPolylineVertex { X = 0.0, Y = 0.0 },
            new DxfLwPolylineVertex { X = 2500.0, Y = 0.0 },
            new DxfLwPolylineVertex { X = 2500.0, Y = 2500.0 },
            new DxfLwPolylineVertex { X = 0.0, Y = 2500.0 }
    ])
    {
      IsClosed = true,
      Color = DxfColor.FromIndex(1)
    });

    dxfFile.Save(outputPath);
  }

  private static ColorLegend CreateLegend() => new([
      new LegendEntry(
            new IsolineColor(255, 0, 0),
            new ReinforcementSpec
            {
                DiameterMm = 12,
                SpacingMm = 200,
                SteelClass = "A500C"
            })
  ]);

  private static SlabGeometry CreateSlab() => new()
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
  };
}
