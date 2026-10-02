using OpenRebar.Application.UseCases;
using OpenRebar.Domain.Models;
using OpenRebar.Domain.Ports;
using OpenRebar.Infrastructure.Catalog;
using OpenRebar.Infrastructure.Geometry;
using OpenRebar.Infrastructure.Logging;
using OpenRebar.Infrastructure.Optimization;
using OpenRebar.Infrastructure.ReinforcementEngine;
using OpenRebar.Infrastructure.Reporting;
using OpenRebar.Infrastructure.Stubs;
using OpenRebar.Infrastructure.ZoneProcessing;
using FluentAssertions;
using NSubstitute;

namespace OpenRebar.Application.Tests;

public class LayerAssignmentTests
{
  [Theory]
  [InlineData("BottomX", LayerKey.BottomX)]
  [InlineData("bot x", LayerKey.BottomX)]
  [InlineData("низ_X", LayerKey.BottomX)]
  [InlineData("TopY", LayerKey.TopY)]
  [InlineData("верх-y", LayerKey.TopY)]
  [InlineData("0", null)]
  public void DxfLayerMapping_RecognizesNames(string sourceLayer, LayerKey? expected)
  {
    bool mapped = ReinforcementLayerMapper.TryMap(sourceLayer, out LayerKey layer);
    if (expected is null)
    {
      mapped.Should().BeFalse();
      return;
    }

    mapped.Should().BeTrue();
    layer.Should().Be(expected.Value);
  }

  [Fact]
  public async Task NarrowVerticalZone_WithBottomX_PlacesHorizontalBars()
  {
    var zone = new ReinforcementZone
    {
      Id = "NARROW",
      Boundary = new Polygon([
          new Point2D(0, 0), new Point2D(400, 0),
          new Point2D(400, 4000), new Point2D(0, 4000)
      ]),
      Spec = new ReinforcementSpec { DiameterMm = 20, SpacingMm = 150, SteelClass = "A500C" },
      Direction = RebarDirection.Y,
      ZoneType = ZoneType.Simple,
      SourceLayerName = "0"
    };
    var parser = Substitute.For<IIsolineParser>();
    parser.SupportedExtensions.Returns([".dxf"]);
    parser.ParseAsync(Arg.Any<string>(), Arg.Any<ColorLegend>(), Arg.Any<CancellationToken>())
        .Returns([zone]);
    var logger = new ConsoleStructuredLogger();
    var pipeline = new GenerateReinforcementPipeline(
        parser,
        parser,
        new StandardZoneDetector(),
        new StandardReinforcementCalculator(logger, new NtsPlanarGeometry()),
        new ColumnGenerationOptimizer(),
        new FileSupplierCatalogLoader(),
        new StubRevitPlacer(),
        new JsonFileReportStore(),
        logger);

    var result = await pipeline.ExecuteAsync(new PipelineInput
    {
      IsolineFilePath = "narrow.dxf",
      Legend = new ColorLegend([
          new LegendEntry(new IsolineColor(255, 0, 0), new ReinforcementSpec
          {
            DiameterMm = 20,
            SpacingMm = 150,
            SteelClass = "A500C"
          })
      ]),
      Slab = new SlabGeometry
      {
        OuterBoundary = new Polygon([
            new Point2D(0, 0), new Point2D(400, 0),
            new Point2D(400, 4000), new Point2D(0, 4000)
        ]),
        ThicknessMm = 220,
        CoverMm = 30,
        ConcreteClass = "B25"
      },
      Layer = LayerKey.BottomX,
      RequireExplicitLayer = true,
      PlaceInRevit = false
    });

    result.Report!.PartialResult.Should().BeTrue();
    result.ClassifiedZones.Should().ContainSingle();
    result.ClassifiedZones[0].Direction.Should().Be(RebarDirection.X);
    result.ClassifiedZones[0].Layer.Should().Be(RebarLayer.Bottom);
    result.TotalRebarSegments.Should().BeGreaterThan(0);
    result.ClassifiedZones[0].Rebars.Should().OnlyContain(rebar => rebar.Start.Y == rebar.End.Y);
    result.ClassifiedZones[0].ExtendedBeyondZoneCount.Should().Be(result.TotalRebarSegments);
  }

}
