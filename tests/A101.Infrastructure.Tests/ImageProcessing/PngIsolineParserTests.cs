using A101.Domain.Models;
using A101.Domain.Exceptions;
using A101.Domain.Ports;
using A101.Infrastructure.ImageProcessing;
using FluentAssertions;
using NSubstitute;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace A101.Infrastructure.Tests.ImageProcessing;

public class PngIsolineParserTests
{
    [Fact]
    public async Task ParseAsync_MissingFile_ShouldThrowInvalidIsolineFileException()
    {
        var parser = new PngIsolineParser();
        var legend = new ColorLegend([
            new LegendEntry(new IsolineColor(255, 0, 0), new ReinforcementSpec
            {
                DiameterMm = 12,
                SpacingMm = 200,
                SteelClass = "A500C"
            })
        ]);

        var act = async () => await parser.ParseAsync("missing-image.png", legend);
        await act.Should().ThrowAsync<InvalidIsolineFileException>();
    }

    [Fact]
    public async Task ParseAsync_LShapedRegion_ShouldNotCollapseToBoundingBox()
    {
        var parser = new PngIsolineParser();
        var legend = new ColorLegend([
            new LegendEntry(new IsolineColor(255, 0, 0), new ReinforcementSpec
            {
                DiameterMm = 12,
                SpacingMm = 200,
                SteelClass = "A500C"
            })
        ]);

        var tempFile = Path.Combine(Path.GetTempPath(), $"a101-l-shape-{Guid.NewGuid():N}.png");

        try
        {
            using (var image = new Image<Rgba32>(30, 30, new Rgba32(255, 255, 255, 255)))
            {
                for (int y = 2; y < 17; y++)
                {
                    for (int x = 2; x < 17; x++)
                    {
                        if (x < 7 || y >= 12)
                            image[x, y] = new Rgba32(255, 0, 0, 255);
                    }
                }

                await image.SaveAsPngAsync(tempFile);
            }

            var zones = await parser.ParseAsync(tempFile, legend);

            zones.Should().HaveCount(1);
            var area = zones[0].Boundary.CalculateArea();

            area.Should().BeGreaterThan(110);
            area.Should().BeLessThan(180, "the extracted polygon should follow the L-shape, not its 15x15 bounding box");
            zones[0].Boundary.Vertices.Count.Should().BeGreaterThan(4);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ParseAsync_WithMlService_ShouldDelegateToSegmentationService()
    {
        var mlService = Substitute.For<IImageSegmentationService>();
        var parser = new PngIsolineParser(mlService);
        var legend = new ColorLegend([
            new LegendEntry(new IsolineColor(255, 0, 0), new ReinforcementSpec
            {
                DiameterMm = 12,
                SpacingMm = 200,
                SteelClass = "A500C"
            })
        ]);

        var boundary = new Polygon([
            new Point2D(0, 0),
            new Point2D(10, 0),
            new Point2D(10, 10),
            new Point2D(0, 10)
        ]);

        mlService.SegmentAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([(boundary, new IsolineColor(255, 0, 0))]);

        var tempFile = Path.Combine(Path.GetTempPath(), $"a101-ml-{Guid.NewGuid():N}.png");
        await File.WriteAllBytesAsync(tempFile, []);

        try
        {
            var zones = await parser.ParseAsync(tempFile, legend);

            zones.Should().HaveCount(1);
            zones[0].Id.Should().StartWith("PNG-ML-");
            zones[0].Spec.DiameterMm.Should().Be(12);

            await mlService.Received(1).SegmentAsync(tempFile, Arg.Any<CancellationToken>());
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }
}