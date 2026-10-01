using System.Text.Json;
using FluentAssertions;
using OpenRebar.Application.UseCases;
using OpenRebar.Domain.Exceptions;
using OpenRebar.Domain.Models;

namespace OpenRebar.Application.Tests;

public class AsFieldZoneBuilderTests
{
  [Fact]
  public void Field_UniformGrid_MergesAndKeepsOutwardMargin()
  {
    var field = Field(
        Element("a", 0, 0, 1000, 1000, 200),
        Element("b", 1000, 0, 2000, 1000, 200));
    var layout = AsFieldZoneBuilder.Build(field, Slab(4000, 4000));

    layout.Warnings.Should().BeEmpty();
    layout.Zones.Should().ContainSingle();
    var box = layout.Zones[0].Boundary.GetBoundingBox();
    box.Min.X.Should().Be(0);
    box.Min.Y.Should().Be(0);
    box.Max.X.Should().Be(2500);
    box.Max.Y.Should().Be(1500);
    layout.Zones[0].Spec.DiameterMm.Should().Be(10);
    layout.Zones[0].Spec.SpacingMm.Should().Be(300);
  }

  [Fact]
  public void Field_KnownArea_SelectsDiameter20At150()
  {
    var field = Field(Element("slab", 0, 0, 6000, 4000, 2094));
    var zone = AsFieldZoneBuilder.Build(field, Slab(6000, 4000)).Zones.Single();

    zone.Spec.DiameterMm.Should().Be(20);
    zone.Spec.SpacingMm.Should().Be(150);
    zone.DesignLayer.Should().Be(LayerKey.BottomX);
  }

  [Fact]
  public void Field_ElementOutsideSlab_Fails()
  {
    var field = Field(Element("out", 0, 0, 1000, 5000, 200));
    var act = () => AsFieldZoneBuilder.Build(field, Slab(4000, 4000));
    act.Should().Throw<AsFieldReadException>().Which.ErrorCode.Should().Be("FIELD_OUTSIDE");
  }

  [Fact]
  public void Field_SplitRegions_Warns()
  {
    var field = Field(
        Element("a", 0, 0, 1000, 1000, 200),
        Element("b", 2500, 2500, 3500, 3500, 200));
    var layout = AsFieldZoneBuilder.Build(field, Slab(4000, 4000));

    layout.Warnings.Should().Contain(AsFieldZoneBuilder.SplitWarning);
    layout.Zones.Should().HaveCount(2);
  }

  [Fact]
  public void LiraMappingFile_MatchesThePreset()
  {
    string path = Path.Combine(RepoRoot(), "adapters", "lira-sapr.plates.json");
    using var document = JsonDocument.Parse(File.ReadAllText(path));
    var columns = document.RootElement.GetProperty("columns");
    columns.GetProperty("AS1").GetString().Should().Be(AsFieldMapping.LiraPlates.Columns["AS1"]);
    columns.GetProperty("AS2").GetString().Should().Be("TopX");
    columns.GetProperty("AS3").GetString().Should().Be("BottomY");
    columns.GetProperty("AS4").GetString().Should().Be("TopY");
    document.RootElement.GetProperty("units").GetString().Should().Be("cm2_per_m");
  }

  [Fact]
  public async Task Field_FourLayers_VerificationPassed()
  {
    string source = Path.Combine(RepoRoot(), "examples", "fe-field", "uniform-slab.csv");
    string directory = Path.Combine(Path.GetTempPath(), $"openrebar-field-{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    string input = Path.Combine(directory, "uniform-slab.csv");
    File.Copy(source, input);

    int exit = await global::OpenRebar.Cli.Program.Main(
    [
        input,
        "--thickness", "220",
        "--cover", "30",
        "--slab-width", "6000",
        "--slab-height", "4000"
    ]);

    exit.Should().Be(2);
    using var report = JsonDocument.Parse(await File.ReadAllTextAsync(Path.ChangeExtension(input, ".result.json")));
    report.RootElement.GetProperty("verification").GetProperty("status").GetString().Should().Be("Failed");
    var layers = report.RootElement.GetProperty("layers").EnumerateArray().ToList();
    layers.Should().OnlyContain(layer => layer.GetProperty("status").GetString() == "Provided");
    var bottomX = layers.Single(layer => layer.GetProperty("layer").GetString() == "BottomX");
    var position = bottomX.GetProperty("positions").EnumerateArray().Single();
    position.GetProperty("diameterMm").GetInt32().Should().Be(20);
    position.GetProperty("quantity").GetInt32().Should().Be(27);
    bottomX.GetProperty("massKg").GetDouble().Should().BeApproximately(437.82, 0.1);
  }

  private static AsField Field(params AsFieldElement[] elements) => new()
  {
    AdapterId = "test",
    Units = "mm2_per_m",
    Elements = elements
  };

  private static AsFieldElement Element(string id, double minX, double minY, double maxX, double maxY, double bottomX) => new()
  {
    Id = id,
    MinX = minX,
    MinY = minY,
    MaxX = maxX,
    MaxY = maxY,
    AsMm2PerM = new Dictionary<string, double> { ["BottomX"] = bottomX }
  };

  private static SlabGeometry Slab(double width, double height) => new()
  {
    OuterBoundary = new Polygon(
    [
        new Point2D(0, 0),
        new Point2D(width, 0),
        new Point2D(width, height),
        new Point2D(0, height)
    ]),
    ThicknessMm = 220,
    CoverMm = 30,
    ConcreteClass = "B25"
  };

  private static string RepoRoot()
  {
    var current = new DirectoryInfo(AppContext.BaseDirectory);
    while (current is not null)
    {
      if (File.Exists(Path.Combine(current.FullName, "OpenRebar.sln")))
        return current.FullName;
      current = current.Parent;
    }

    throw new InvalidOperationException("Repository root was not found.");
  }
}
