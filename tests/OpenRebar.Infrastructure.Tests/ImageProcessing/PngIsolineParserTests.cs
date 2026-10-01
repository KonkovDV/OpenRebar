using OpenRebar.Domain.Models;
using OpenRebar.Domain.Exceptions;
using OpenRebar.Domain.Ports;
using OpenRebar.Infrastructure.ImageProcessing;
using FluentAssertions;
using NSubstitute;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace OpenRebar.Infrastructure.Tests.ImageProcessing;

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
  public async Task ParseAsync_WithoutCalibration_Throws()
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
    var tempFile = Path.Combine(Path.GetTempPath(), $"OpenRebar-uncalibrated-{Guid.NewGuid():N}.png");
    await File.WriteAllBytesAsync(tempFile, []);

    try
    {
      var act = async () => await parser.ParseAsync(tempFile, legend);
      await act.Should().ThrowAsync<RasterNotCalibratedException>();
    }
    finally
    {
      if (File.Exists(tempFile))
        File.Delete(tempFile);
    }
  }

  [Fact]
  public async Task ParseAsync_LShapedRegion_ShouldNotCollapseToBoundingBox()
  {
    var parser = new PngIsolineParser
    {
      Calibration = new RasterCalibration(1, 0, 0),
      MinZoneAreaMm2 = 1
    };
    var legend = new ColorLegend([
        new LegendEntry(new IsolineColor(255, 0, 0), new ReinforcementSpec
            {
                DiameterMm = 12,
                SpacingMm = 200,
                SteelClass = "A500C"
            })
    ]);

    var tempFile = Path.Combine(Path.GetTempPath(), $"OpenRebar-l-shape-{Guid.NewGuid():N}.png");

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
    var parser = new PngIsolineParser(mlService)
    {
      Calibration = new RasterCalibration(1, 0, 0)
    };
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

    var tempFile = Path.Combine(Path.GetTempPath(), $"OpenRebar-ml-{Guid.NewGuid():N}.png");
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

  [Fact]
  public async Task ParseAsync_WithMlServiceDomainFailure_ShouldPropagateDomainException()
  {
    var mlService = Substitute.For<IImageSegmentationService>();
    var parser = new PngIsolineParser(mlService)
    {
      Calibration = new RasterCalibration(1, 0, 0)
    };
    var legend = new ColorLegend([
        new LegendEntry(new IsolineColor(255, 0, 0), new ReinforcementSpec
            {
                DiameterMm = 12,
                SpacingMm = 200,
                SteelClass = "A500C"
            })
    ]);

    var expected = new ImageSegmentationServiceException("Segmentation backend is offline.");
    mlService.SegmentAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
        .Returns<Task<IReadOnlyList<(Polygon Boundary, IsolineColor DominantColor)>>>(_ => throw expected);

    var tempFile = Path.Combine(Path.GetTempPath(), $"OpenRebar-ml-error-{Guid.NewGuid():N}.png");
    await File.WriteAllBytesAsync(tempFile, []);

    try
    {
      var act = async () => await parser.ParseAsync(tempFile, legend);
      await act.Should().ThrowAsync<ImageSegmentationServiceException>()
          .Where(ex => ReferenceEquals(ex, expected));
    }
    finally
    {
      if (File.Exists(tempFile))
        File.Delete(tempFile);
    }
  }

  [Fact]
  public async Task ParseAsync_BlendedPixel_IsNotALegendClass()
  {
    var parser = Calibrated();
    var tempFile = Path.Combine(Path.GetTempPath(), $"OpenRebar-blend-{Guid.NewGuid():N}.png");

    try
    {
      using (var image = new Image<Rgba32>(40, 40, new Rgba32(255, 255, 255, 255)))
      {
        for (int y = 5; y < 35; y++)
          for (int x = 5; x < 35; x++)
            image[x, y] = new Rgba32(200, 40, 40, 255);

        await image.SaveAsPngAsync(tempFile);
      }

      var zones = await parser.ParseAsync(tempFile, RedLegend());
      zones.Should().BeEmpty();
    }
    finally
    {
      if (File.Exists(tempFile))
        File.Delete(tempFile);
    }
  }

  [Fact]
  public async Task ParseAsync_Hole_KeepsOuterLoopAndCountsHole()
  {
    var parser = Calibrated();
    var tempFile = Path.Combine(Path.GetTempPath(), $"OpenRebar-hole-{Guid.NewGuid():N}.png");

    try
    {
      using (var image = new Image<Rgba32>(50, 40, new Rgba32(255, 255, 255, 255)))
      {
        Fill(image, 2, 2, 40, 30, new Rgba32(255, 0, 0, 255));
        Fill(image, 12, 10, 10, 8, new Rgba32(255, 255, 255, 255));
        await image.SaveAsPngAsync(tempFile);
      }

      var zones = await parser.ParseAsync(tempFile, RedLegend());
      zones.Should().HaveCount(1);
      zones[0].Boundary.CalculateArea().Should().BeApproximately(40 * 30, 1);
      zones[0].Holes.Should().ContainSingle();
      parser.Stats.IgnoredByReason.Should().NotContainKey("holeIgnored");
    }
    finally
    {
      if (File.Exists(tempFile))
        File.Delete(tempFile);
    }
  }

  [Fact]
  public async Task ParseAsync_OnePixelNeck_StaysOneZone()
  {
    var parser = Calibrated();
    var tempFile = Path.Combine(Path.GetTempPath(), $"OpenRebar-neck-{Guid.NewGuid():N}.png");

    try
    {
      using (var image = new Image<Rgba32>(30, 30, new Rgba32(255, 255, 255, 255)))
      {
        Fill(image, 2, 2, 10, 10, new Rgba32(255, 0, 0, 255));
        image[11, 12] = new Rgba32(255, 0, 0, 255);
        Fill(image, 12, 12, 10, 10, new Rgba32(255, 0, 0, 255));
        await image.SaveAsPngAsync(tempFile);
      }

      var zones = await parser.ParseAsync(tempFile, RedLegend());
      zones.Should().HaveCount(1);
      zones[0].Boundary.CalculateArea().Should().BeApproximately(201, 1);
      var box = zones[0].Boundary.GetBoundingBox();
      zones[0].Boundary.CalculateArea().Should().BeLessThan(box.Width * box.Height);
    }
    finally
    {
      if (File.Exists(tempFile))
        File.Delete(tempFile);
    }
  }

  [Fact]
  public async Task ParseAsync_CalibratedRectangle_MatchesMillimetreArea()
  {
    var parser = new PngIsolineParser
    {
      Calibration = new RasterCalibration(0.1, 0, 80),
      MinZoneAreaMm2 = 1
    };
    var tempFile = Path.Combine(Path.GetTempPath(), $"OpenRebar-rect-{Guid.NewGuid():N}.png");

    try
    {
      using (var image = new Image<Rgba32>(100, 80, new Rgba32(255, 255, 255, 255)))
      {
        Fill(image, 0, 0, 100, 80, new Rgba32(255, 0, 0, 255));
        await image.SaveAsPngAsync(tempFile);
      }

      var zones = await parser.ParseAsync(tempFile, RedLegend());
      zones.Should().HaveCount(1);
      zones[0].Boundary.CalculateArea().Should().BeApproximately(1000 * 800, 1000 * 800 * 0.01);
    }
    finally
    {
      if (File.Exists(tempFile))
        File.Delete(tempFile);
    }
  }

  private static PngIsolineParser Calibrated() => new()
  {
    Calibration = new RasterCalibration(1, 0, 0),
    MinZoneAreaMm2 = 1
  };

  private static ColorLegend RedLegend() => new([
      new LegendEntry(new IsolineColor(255, 0, 0), new ReinforcementSpec
      {
        DiameterMm = 20,
        SpacingMm = 150,
        SteelClass = "A500C"
      })
  ]);

  private static void Fill(Image<Rgba32> image, int x, int y, int width, int height, Rgba32 color)
  {
    for (int py = y; py < y + height; py++)
      for (int px = x; px < x + width; px++)
        image[px, py] = color;
  }
}
