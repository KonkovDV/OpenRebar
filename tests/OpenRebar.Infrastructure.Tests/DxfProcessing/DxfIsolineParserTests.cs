using OpenRebar.Domain.Models;
using OpenRebar.Domain.Exceptions;
using OpenRebar.Infrastructure.DxfProcessing;
using FluentAssertions;
using IxMilia.Dxf;
using IxMilia.Dxf.Entities;
using System.Reflection;

namespace OpenRebar.Infrastructure.Tests.DxfProcessing;

public class DxfIsolineParserTests
{
  [Fact]
  public async Task ParseAsync_MissingFile_ShouldThrowInvalidIsolineFileException()
  {
    var parser = new DxfIsolineParser();
    var legend = CreateLegend(new IsolineColor(255, 0, 0));

    var act = async () => await parser.ParseAsync("missing-file.dxf", legend);
    await act.Should().ThrowAsync<InvalidIsolineFileException>();
  }

  [Fact]
  public async Task ParseAsync_LwPolylineWithBulge_ShouldApproximateArcSegments()
  {
    var parser = new DxfIsolineParser();
    var legend = CreateLegend(new IsolineColor(255, 0, 0));

    var polyline = new DxfLwPolyline([
        new DxfLwPolylineVertex { X = 0.0, Y = 0.5, Bulge = 1.0 },
            new DxfLwPolylineVertex { X = -1.5, Y = 0.5 },
            new DxfLwPolylineVertex { X = -1.5, Y = -0.5, Bulge = 1.0 },
            new DxfLwPolylineVertex { X = 0.0, Y = -0.5 }
    ])
    {
      IsClosed = true,
      Color = DxfColor.FromIndex(1)
    };

    var zones = await ParseEntityAsync(polyline, parser, legend);

    zones.Should().HaveCount(1);
    zones[0].Boundary.Vertices.Count.Should().BeGreaterThan(8,
        "bulged DXF segments should be discretized into an arc-following polygon");

    var bbox = zones[0].Boundary.GetBoundingBox();
    bbox.Width.Should().BeApproximately(1.5, 0.2);
    bbox.Height.Should().BeApproximately(2.5, 0.2);
  }

  [Fact]
  public void ExtractPolygonFromEntity_HatchWithCircularArcBoundary_ShouldProduceZone()
  {
    var legend = CreateLegend(new IsolineColor(0, 255, 0));

    var hatch = new DxfHatch
    {
      Color = DxfColor.ByLayer,
      FillColor = DxfColor.FromIndex(3),
      IsAssociative = true,
      PatternName = "SOLID",
      HatchStyle = DxfHatchStyle.EntireArea
    };

    var path = new DxfHatch.NonPolylineBoundaryPath(DxfHatch.BoundaryPathType.Textbox);
    path.Edges.Add(new DxfHatch.LineBoundaryPathEdge
    {
      StartPoint = new DxfPoint(-2.0, 0.0, 0.0),
      EndPoint = new DxfPoint(2.0, 0.0, 0.0)
    });
    path.Edges.Add(new DxfHatch.CircularArcBoundaryPathEdge
    {
      Center = new DxfPoint(0.0, 0.0, 0.0),
      Radius = 2.0,
      StartAngle = 0.0,
      EndAngle = 180.0,
      IsCounterClockwise = true
    });
    hatch.BoundaryPaths.Add(path);
    hatch.SeedPoints.Add(new DxfPoint(0.0, 1.0, 0.0));

    var dxfFile = new DxfFile();
    var (polygon, color) = ExtractEntityViaReflection(hatch, dxfFile);

    polygon.Should().NotBeNull();
    color.Should().NotBeNull();
    var legendEntry = legend.FindClosest(color!.Value);
    legendEntry.Should().NotBeNull();

    polygon!.Vertices.Count.Should().BeGreaterThan(6,
        "circular hatch edges should be sampled into a usable polygon");
    polygon.CalculateArea().Should().BeApproximately(Math.PI * 2.0, 0.8);
  }

  [Fact]
  public async Task ParseAsync_OpenPolyline_IsIgnored()
  {
    var parser = new DxfIsolineParser();
    var polyline = new DxfLwPolyline([
        new DxfLwPolylineVertex { X = 0, Y = 0 },
        new DxfLwPolylineVertex { X = 100, Y = 0 },
        new DxfLwPolylineVertex { X = 100, Y = 50 }
    ])
    {
      IsClosed = false,
      Color = DxfColor.FromIndex(1)
    };

    var zones = await ParseEntityAsync(polyline, parser, CreateLegend(new IsolineColor(255, 0, 0)));

    zones.Should().BeEmpty();
    parser.Stats.IgnoredByReason.Should().ContainKey("ignoredOpenPolylines");
  }

  [Fact]
  public async Task ParseAsync_CoincidentEndpoints_AreClosed()
  {
    var parser = new DxfIsolineParser();
    var polyline = new DxfLwPolyline([
        new DxfLwPolylineVertex { X = 0, Y = 0 },
        new DxfLwPolylineVertex { X = 100, Y = 0 },
        new DxfLwPolylineVertex { X = 100, Y = 50 },
        new DxfLwPolylineVertex { X = 0, Y = 50 },
        new DxfLwPolylineVertex { X = 0, Y = 0 }
    ])
    {
      IsClosed = false,
      Color = DxfColor.FromIndex(1)
    };

    var zones = await ParseEntityAsync(polyline, parser, CreateLegend(new IsolineColor(255, 0, 0)));

    zones.Should().HaveCount(1);
    zones[0].Boundary.CalculateArea().Should().BeApproximately(5000, 1);
  }

  [Fact]
  public async Task ParseAsync_CentimetreUnits_ScaleToMillimetres()
  {
    var parser = new DxfIsolineParser();
    var polyline = ClosedRectangle(0, 0, 600, 400);
    var zones = await ParseEntityAsync(
        polyline,
        parser,
        CreateLegend(new IsolineColor(255, 0, 0)),
        file => file.Header.DefaultDrawingUnits = DxfUnits.Centimeters);

    zones.Should().HaveCount(1);
    var box = zones[0].Boundary.GetBoundingBox();
    box.Width.Should().BeApproximately(6000, 0.1);
    box.Height.Should().BeApproximately(4000, 0.1);
    parser.Stats.UnitsAssumed.Should().BeFalse();
  }

  [Fact]
  public async Task ParseAsync_UnsetUnits_UsesCallerUnit()
  {
    var parser = new DxfIsolineParser();
    parser.TrySetUnitsWhenUnset("cm").Should().BeTrue();
    var zones = await ParseEntityAsync(
        ClosedRectangle(0, 0, 10, 4),
        parser,
        CreateLegend(new IsolineColor(255, 0, 0)));

    var box = zones[0].Boundary.GetBoundingBox();
    box.Width.Should().BeApproximately(100, 0.1);
    box.Height.Should().BeApproximately(40, 0.1);
    parser.Stats.UnitsAssumed.Should().BeTrue();
  }

  [Fact]
  public async Task ParseAsync_HatchHole_KeepsOuterLoopAndCountsHole()
  {
    var parser = new DxfIsolineParser();
    var hatch = new DxfHatch
    {
      Color = DxfColor.FromIndex(1),
      PatternName = "SOLID"
    };
    hatch.BoundaryPaths.Add(RectangleLoop(0, 0, 100, 80));
    hatch.BoundaryPaths.Add(RectangleLoop(10, 10, 20, 20));

    var zones = await ParseEntityAsync(hatch, parser, CreateLegend(new IsolineColor(255, 0, 0)));

    zones.Should().HaveCount(1);
    zones[0].Boundary.CalculateArea().Should().BeApproximately(8000, 1);
    zones[0].Holes.Should().ContainSingle();
    zones[0].Holes[0].CalculateArea().Should().BeApproximately(400, 1);
    parser.Stats.IgnoredByReason.Should().NotContainKey("holeIgnored");
  }

  [Fact]
  public async Task ParseAsync_DuplicatePolylineAndHatch_CollapsesToOneZone()
  {
    var parser = new DxfIsolineParser();
    var legend = CreateLegend(new IsolineColor(255, 0, 0));
    var polyline = ClosedRectangle(0, 0, 100, 40);
    var hatch = new DxfHatch
    {
      Color = DxfColor.FromIndex(1),
      PatternName = "SOLID"
    };
    hatch.BoundaryPaths.Add(RectangleLoop(0, 0, 100, 40));

    var zones = await ParseEntitiesAsync([polyline, hatch], parser, legend);

    zones.Should().HaveCount(1);
    parser.Stats.IgnoredByReason["duplicateZone"].Should().Be(1);
  }

  [Fact]
  public async Task ParseAsync_Insert_PlacesBlockGeometry()
  {
    var parser = new DxfIsolineParser();
    var block = new IxMilia.Dxf.Blocks.DxfBlock
    {
      Name = "ZONE"
    };
    block.Entities.Add(ClosedRectangle(0, 0, 10, 5));
    var insert = new DxfInsert
    {
      Name = "ZONE",
      Location = new DxfPoint(100, 200, 0),
      Color = DxfColor.FromIndex(1)
    };

    var zones = await ParseEntitiesAsync(
        [insert],
        parser,
        CreateLegend(new IsolineColor(255, 0, 0)),
        file => file.Blocks.Add(block));

    zones.Should().HaveCount(1);
    var box = zones[0].Boundary.GetBoundingBox();
    box.Min.X.Should().BeApproximately(100, 0.1);
    box.Min.Y.Should().BeApproximately(200, 0.1);
    box.Width.Should().BeApproximately(10, 0.1);
  }

  [Fact]
  public async Task ParseAsync_InsertArray_ExpandsWithinBudget()
  {
    var parser = new DxfIsolineParser();
    var block = new IxMilia.Dxf.Blocks.DxfBlock
    {
      Name = "ZONE"
    };
    block.Entities.Add(ClosedRectangle(0, 0, 10, 5));
    var insert = new DxfInsert
    {
      Name = "ZONE",
      RowCount = 2,
      ColumnCount = 3,
      RowSpacing = 20,
      ColumnSpacing = 20,
      Color = DxfColor.FromIndex(1)
    };

    var zones = await ParseEntitiesAsync(
        [insert],
        parser,
        CreateLegend(new IsolineColor(255, 0, 0)),
        file => file.Blocks.Add(block));

    zones.Should().HaveCount(6);
  }

  [Fact]
  public async Task ParseAsync_InsertArrayBeyondBudget_IsRejectedBeforeExpansion()
  {
    var parser = new DxfIsolineParser();
    var block = new IxMilia.Dxf.Blocks.DxfBlock
    {
      Name = "ZONE"
    };
    block.Entities.Add(ClosedRectangle(0, 0, 10, 5));
    var insert = new DxfInsert
    {
      Name = "ZONE",
      RowCount = 1_001,
      ColumnCount = 101,
      RowSpacing = 20,
      ColumnSpacing = 20,
      Color = DxfColor.FromIndex(1)
    };

    Func<Task> act = async () => await ParseEntitiesAsync(
        [insert],
        parser,
        CreateLegend(new IsolineColor(255, 0, 0)),
        file => file.Blocks.Add(block));

    await act.Should()
        .ThrowAsync<InvalidIsolineFileException>()
        .WithMessage("*100000*zone candidate limit*");
  }

  [Fact]
  public async Task ParseAsync_Spline_IsCountedInsteadOfSkippedSilently()
  {
    var parser = new DxfIsolineParser();
    var spline = new DxfSpline { Color = DxfColor.FromIndex(1) };

    var zones = await ParseEntityAsync(spline, parser, CreateLegend(new IsolineColor(255, 0, 0)));

    zones.Should().BeEmpty();
    parser.Stats.IgnoredByReason.Should().ContainKey("unsupportedCurve");
    parser.Stats.ParsedEntityCount.Should().Be(1);
  }

  private static ColorLegend CreateLegend(IsolineColor color)
  {
    return new ColorLegend([
        new LegendEntry(color, new ReinforcementSpec
            {
                DiameterMm = 12,
                SpacingMm = 200,
                SteelClass = "A500C"
            })
    ]);
  }

  private static Task<IReadOnlyList<ReinforcementZone>> ParseEntityAsync(
      DxfEntity entity,
      DxfIsolineParser parser,
      ColorLegend legend,
      Action<DxfFile>? configure = null)
      => ParseEntitiesAsync([entity], parser, legend, configure);

  private static async Task<IReadOnlyList<ReinforcementZone>> ParseEntitiesAsync(
      IReadOnlyList<DxfEntity> entities,
      DxfIsolineParser parser,
      ColorLegend legend,
      Action<DxfFile>? configure = null)
  {
    var file = new DxfFile();
    file.Header.Version = DxfAcadVersion.R2000;
    foreach (var entity in entities)
      file.Entities.Add(entity);
    configure?.Invoke(file);

    var tempFile = Path.Combine(Path.GetTempPath(), $"OpenRebar-dxf-{Guid.NewGuid():N}.dxf");

    try
    {
      file.Save(tempFile);
      return await parser.ParseAsync(tempFile, legend);
    }
    finally
    {
      if (File.Exists(tempFile))
        File.Delete(tempFile);
    }
  }

  private static DxfLwPolyline ClosedRectangle(double x, double y, double width, double height)
  {
    return new DxfLwPolyline([
        new DxfLwPolylineVertex { X = x, Y = y },
        new DxfLwPolylineVertex { X = x + width, Y = y },
        new DxfLwPolylineVertex { X = x + width, Y = y + height },
        new DxfLwPolylineVertex { X = x, Y = y + height }
    ])
    {
      IsClosed = true,
      Color = DxfColor.FromIndex(1)
    };
  }

  private static DxfHatch.PolylineBoundaryPath RectangleLoop(
      double x, double y, double width, double height)
  {
    var path = new DxfHatch.PolylineBoundaryPath
    {
      IsClosed = true
    };
    path.Vertices.Add(new DxfVertex { Location = new DxfPoint(x, y, 0) });
    path.Vertices.Add(new DxfVertex { Location = new DxfPoint(x + width, y, 0) });
    path.Vertices.Add(new DxfVertex { Location = new DxfPoint(x + width, y + height, 0) });
    path.Vertices.Add(new DxfVertex { Location = new DxfPoint(x, y + height, 0) });
    return path;
  }

  private static (Polygon? Polygon, IsolineColor? Color) ExtractEntityViaReflection(DxfEntity entity, DxfFile file)
  {
    var method = typeof(DxfIsolineParser).GetMethod(
        "ExtractPolygonFromEntity",
        BindingFlags.NonPublic | BindingFlags.Static);

    method.Should().NotBeNull();

    var result = method!.Invoke(null, [entity, file]);
    result.Should().NotBeNull();

    var resultType = result!.GetType();
    var polygon = (Polygon?)resultType.GetField("Item1")!.GetValue(result);
    var color = (IsolineColor?)resultType.GetField("Item2")!.GetValue(result);
    return (polygon, color);
  }
}
