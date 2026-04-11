using A101.Domain.Models;
using A101.Domain.Exceptions;
using A101.Infrastructure.DxfProcessing;
using FluentAssertions;
using IxMilia.Dxf;
using IxMilia.Dxf.Entities;
using System.Reflection;

namespace A101.Infrastructure.Tests.DxfProcessing;

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

    private static async Task<IReadOnlyList<ReinforcementZone>> ParseEntityAsync(
        DxfEntity entity,
        DxfIsolineParser parser,
        ColorLegend legend)
    {
        var file = new DxfFile();
        file.Header.Version = DxfAcadVersion.R2000;
        file.Entities.Add(entity);

        var tempFile = Path.Combine(Path.GetTempPath(), $"a101-dxf-{Guid.NewGuid():N}.dxf");

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