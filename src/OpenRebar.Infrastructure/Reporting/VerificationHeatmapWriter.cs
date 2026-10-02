using System.Text.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using OpenRebar.Domain.Models;

namespace OpenRebar.Infrastructure.Reporting;

/// <summary>
/// Writes the coverage heatmap and a sparse JSON grid of cells that are short.
/// </summary>
public static class VerificationHeatmapWriter
{
  private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };

  public static async Task WriteAsync(
      ReinforcementVerificationResult verification,
      string pngPath,
      string jsonPath,
      CancellationToken cancellationToken = default)
  {
    var shortfall = verification.Cells
        .Where(cell => cell.MarginMm2PerM < -1e-6)
        .ToList();
    var payload = new
    {
      status = verification.Status,
      cellSizeMm = verification.CellSizeMm,
      cells = shortfall.Select(cell => new
      {
        layer = cell.Layer,
        x = cell.X,
        y = cell.Y,
        marginMm2PerM = cell.MarginMm2PerM,
        status = cell.Status
      })
    };
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(jsonPath)) ?? ".");
    await File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(payload, Options), cancellationToken);
    WritePng(verification, pngPath);
  }

  private static void WritePng(ReinforcementVerificationResult verification, string pngPath)
  {
    if (verification.Cells.Count == 0 || verification.CellSizeMm <= 0)
    {
      using var empty = new Image<Rgba32>(1, 1, new Rgba32(0, 0, 0, 0));
      empty.SaveAsPng(pngPath);
      return;
    }

    double cell = verification.CellSizeMm;
    double minX = verification.Cells.Min(item => item.X) - cell / 2.0;
    double minY = verification.Cells.Min(item => item.Y) - cell / 2.0;
    int width = verification.Cells.Max(item => (int)Math.Floor((item.X - minX) / cell)) + 1;
    int height = verification.Cells.Max(item => (int)Math.Floor((item.Y - minY) / cell)) + 1;
    width = Math.Max(1, width);
    height = Math.Max(1, height);

    using var image = new Image<Rgba32>(width, height, new Rgba32(240, 240, 240));
    foreach (var sample in verification.Cells)
    {
      int px = (int)Math.Floor((sample.X - minX) / cell);
      int py = (int)Math.Floor((sample.Y - minY) / cell);
      if (px < 0 || py < 0 || px >= width || py >= height)
        continue;
      image[px, height - 1 - py] = sample.MarginMm2PerM < -1e-6
          ? new Rgba32(220, 40, 40)
          : new Rgba32(40, 160, 70);
    }

    image.SaveAsPng(pngPath);
  }
}
