using FluentAssertions;
using IxMilia.Dxf;
using IxMilia.Dxf.Entities;
using OpenRebar.Application.UseCases;
using OpenRebar.Domain.Models;
using OpenRebar.Infrastructure.Catalog;
using OpenRebar.Infrastructure.DxfProcessing;
using OpenRebar.Infrastructure.Geometry;
using OpenRebar.Infrastructure.ImageProcessing;
using OpenRebar.Infrastructure.Logging;
using OpenRebar.Infrastructure.Optimization;
using OpenRebar.Infrastructure.ReinforcementEngine;
using OpenRebar.Infrastructure.Reporting;
using OpenRebar.Infrastructure.Stubs;
using OpenRebar.Infrastructure.ZoneProcessing;

namespace OpenRebar.Application.Tests;

public class MultiLayerDesignTests
{
  [Fact]
  public void Validate_RejectsMoreThanFourOrADuplicate()
  {
    var legend = Legend();
    LayerDesignInput Layer(LayerKey key) => new()
    {
      Layer = key,
      IsolineFilePath = "a.dxf",
      Legend = legend
    };

    LayerDesignRules.Validate(
    [
        Layer(LayerKey.BottomX),
        Layer(LayerKey.BottomY),
        Layer(LayerKey.TopX),
        Layer(LayerKey.TopY),
        Layer(LayerKey.BottomX)
    ]).Should().Contain("1 to 4");

    LayerDesignRules.Validate([Layer(LayerKey.BottomX), Layer(LayerKey.BottomX)])
        .Should().Contain("more than once");
  }

  [Fact]
  public void TryParse_ReadsLayerPathLegendAndBackground()
  {
    LayerInputParser.TryParse(
        "TopY=floor.dxf;legend=legend.json;bg=Ø10@200",
        out LayerInputDraft draft,
        out string? error).Should().BeTrue();

    error.Should().BeNull();
    draft.Layer.Should().Be(LayerKey.TopY);
    draft.FilePath.Should().Be("floor.dxf");
    draft.LegendPath.Should().Be("legend.json");
    draft.BackgroundDiameterMm.Should().Be(10);
    draft.BackgroundSpacingMm.Should().Be(200);

    LayerInputParser.TryParse("BottomX=floor.dxf;bg=wide", out _, out error).Should().BeFalse();
    error.Should().Contain("10@200");
  }

  [Fact]
  public async Task LoadAsync_ExampleProject_ResolvesTheDrawing()
  {
    string path = Path.Combine(RepositoryRoot(), "examples", "project", "simple-slab.project.json");
    var project = await ProjectDesignLoader.LoadAsync(path);

    project.Norm.Should().Be("ru.sp63.2018");
    project.Slab.WidthMm.Should().Be(6000);
    project.Layers.Should().ContainSingle();
    project.Layers[0].Layer.Should().Be("BottomX");
    File.Exists(project.Layers[0].File).Should().BeTrue();
    project.Layers[0].Background!.DiameterMm.Should().Be(10);
  }

  [Fact]
  public async Task ExecuteAsync_TwoLayers_KeepsEachLayerAndBackgroundDelta()
  {
    var directory = Path.Combine(Path.GetTempPath(), $"OpenRebar-layers-{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    string bottom = Path.Combine(directory, "bottom.dxf");
    string top = Path.Combine(directory, "top.dxf");
    WriteRectangle(bottom);
    WriteRectangle(top);

    try
    {
      var logger = new ConsoleStructuredLogger();
      var pipeline = new GenerateReinforcementPipeline(
          new DxfIsolineParser(),
          new PngIsolineParser(),
          new StandardZoneDetector(),
          new StandardReinforcementCalculator(logger, new NtsPlanarGeometry()),
          new ColumnGenerationOptimizer(),
          new FileSupplierCatalogLoader(),
          new StubRevitPlacer(),
          new JsonFileReportStore(),
          logger);
      var legend = Legend();
      var result = await pipeline.ExecuteAsync(new PipelineInput
      {
        IsolineFilePath = bottom,
        Legend = legend,
        Slab = new SlabGeometry
        {
          OuterBoundary = new Polygon(
          [
              new Point2D(0, 0),
              new Point2D(2000, 0),
              new Point2D(2000, 2000),
              new Point2D(0, 2000)
          ]),
          ThicknessMm = 200,
          CoverMm = 25,
          ConcreteClass = "B25"
        },
        PlaceInRevit = false,
        RequireExplicitLayer = true,
        FieldDiametersMm = [10, 12, 16, 20, 25],
        Layers =
        [
            new LayerDesignInput
            {
              Layer = LayerKey.BottomX,
              IsolineFilePath = bottom,
              Legend = legend,
              Background = new BackgroundMesh(
                  LayerKey.BottomX,
                  new ReinforcementSpec { DiameterMm = 8, SpacingMm = 200, SteelClass = "A500C" },
                  0)
            },
            new LayerDesignInput
            {
              Layer = LayerKey.TopY,
              IsolineFilePath = top,
              Legend = legend
            }
        ]
      });

      result.Report!.PartialResult.Should().BeTrue();
      var bottomZone = result.ClassifiedZones.Single(zone => zone.DesignLayer == LayerKey.BottomX && !zone.IsBackgroundMesh);
      bottomZone.Id.Should().StartWith("BottomX-");
      bottomZone.Direction.Should().Be(RebarDirection.X);
      bottomZone.AsDeltaMm2PerM.Should().BeGreaterThan(0);
      bottomZone.Role.Should().Be(ZoneRole.Additional);
      bottomZone.EffectiveSpec.DiameterMm.Should().Be(10);
      bottomZone.EffectiveSpec.SpacingMm.Should().Be(200);
      var mesh = result.ClassifiedZones.Single(zone => zone.IsBackgroundMesh);
      mesh.DesignLayer.Should().Be(LayerKey.BottomX);
      mesh.Rebars.Should().NotBeEmpty();
      mesh.Rebars.Should().OnlyContain(bar => bar.DiameterMm == 8);
      result.ClassifiedZones.Single(zone => zone.DesignLayer == LayerKey.TopY).Direction.Should().Be(RebarDirection.Y);
      result.ClassifiedZones.Single(zone => zone.DesignLayer == LayerKey.TopY).EffectiveSpec.DiameterMm.Should().Be(12);
      result.Report.Verification!.Status.Should().Be(VerificationStatuses.Failed);
    }
    finally
    {
      if (Directory.Exists(directory))
        Directory.Delete(directory, recursive: true);
    }
  }

  private static ColorLegend Legend() => new(
  [
      new LegendEntry(
          new IsolineColor(255, 0, 0),
          new ReinforcementSpec { DiameterMm = 12, SpacingMm = 200, SteelClass = "A500C" })
  ]);

  private static void WriteRectangle(string path)
  {
    var dxf = new DxfFile();
    dxf.Header.Version = DxfAcadVersion.R2000;
    dxf.Entities.Add(new DxfLwPolyline(
    [
        new DxfLwPolylineVertex { X = 0, Y = 0 },
        new DxfLwPolylineVertex { X = 2000, Y = 0 },
        new DxfLwPolylineVertex { X = 2000, Y = 2000 },
        new DxfLwPolylineVertex { X = 0, Y = 2000 }
    ])
    {
      IsClosed = true,
      Color = DxfColor.FromIndex(1)
    });
    dxf.Save(path);
  }

  private static string RepositoryRoot()
  {
    var current = new DirectoryInfo(AppContext.BaseDirectory);
    while (current is not null)
    {
      if (File.Exists(Path.Combine(current.FullName, "OpenRebar.sln")))
        return current.FullName;
      current = current.Parent;
    }

    throw new DirectoryNotFoundException("Could not locate OpenRebar.sln.");
  }
}
