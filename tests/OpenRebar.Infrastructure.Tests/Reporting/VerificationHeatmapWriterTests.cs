using System.Text.Json;
using FluentAssertions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using OpenRebar.Domain.Models;
using OpenRebar.Infrastructure.Reporting;

namespace OpenRebar.Infrastructure.Tests.Reporting;

public class VerificationHeatmapWriterTests
{
  [Fact]
  public async Task WriteAsync_PaintsShortCellsRed_AndOmitsCoveredCellsFromJson()
  {
    var directory = Path.Combine(Path.GetTempPath(), $"OpenRebar-heatmap-{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    var png = Path.Combine(directory, "slab.verification.png");
    var json = Path.Combine(directory, "slab.verification.json");

    try
    {
      var verification = new ReinforcementVerificationResult
      {
        Status = VerificationStatuses.Failed,
        UnderReinforcedAreaM2 = 0.0025,
        CheckedAreaM2 = 0.005,
        MinMarginMm2PerM = -100,
        MinProvisionRatio = 0,
        CellSizeMm = 50,
        UnderCoverageRatio = 0,
        DeficitRegions = [],
        Cells =
        [
            new VerificationCellReport { Layer = "BottomX", X = 25, Y = 25, MarginMm2PerM = -100 },
            new VerificationCellReport { Layer = "BottomX", X = 75, Y = 25, MarginMm2PerM = 20 }
        ]
      };

      await VerificationHeatmapWriter.WriteAsync(verification, png, json);

      using var image = Image.Load<Rgba32>(png);
      image[0, image.Height - 1].R.Should().BeGreaterThan(image[0, image.Height - 1].G);
      image[1, image.Height - 1].G.Should().BeGreaterThan(image[1, image.Height - 1].R);

      using var document = JsonDocument.Parse(await File.ReadAllTextAsync(json));
      var cells = document.RootElement.GetProperty("cells");
      cells.GetArrayLength().Should().Be(1);
      cells[0].GetProperty("marginMm2PerM").GetDouble().Should().Be(-100);
    }
    finally
    {
      if (Directory.Exists(directory))
        Directory.Delete(directory, recursive: true);
    }
  }
}
