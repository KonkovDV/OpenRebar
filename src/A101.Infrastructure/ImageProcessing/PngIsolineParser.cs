using A101.Domain.Models;
using A101.Domain.Ports;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace A101.Infrastructure.ImageProcessing;

/// <summary>
/// Parses PNG isoline images using color-based segmentation.
/// For MVP: uses connected-component analysis on color regions.
/// For production: delegates to IImageSegmentationService (Python ML).
/// </summary>
public sealed class PngIsolineParser : IIsolineParser
{
    private readonly IImageSegmentationService? _mlService;

    public PngIsolineParser(IImageSegmentationService? mlService = null)
    {
        _mlService = mlService;
    }

    public IReadOnlyList<string> SupportedExtensions => [".png", ".jpg", ".jpeg", ".bmp", ".tiff"];

    public async Task<IReadOnlyList<ReinforcementZone>> ParseAsync(
        string filePath,
        ColorLegend legend,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Image file not found: {filePath}");

        // If ML service available, delegate to it
        if (_mlService is not null)
        {
            return await ParseWithMlAsync(filePath, legend, cancellationToken);
        }

        // Otherwise: basic color quantization + connected components
        return await ParseWithColorQuantizationAsync(filePath, legend, cancellationToken);
    }

    private async Task<IReadOnlyList<ReinforcementZone>> ParseWithMlAsync(
        string filePath,
        ColorLegend legend,
        CancellationToken cancellationToken)
    {
        var segmented = await _mlService!.SegmentAsync(filePath, cancellationToken);
        var zones = new List<ReinforcementZone>();
        int zoneIndex = 0;

        foreach (var (boundary, dominantColor) in segmented)
        {
            var legendEntry = legend.FindClosest(dominantColor);
            if (legendEntry is null) continue;

            zones.Add(new ReinforcementZone
            {
                Id = $"PNG-ML-{++zoneIndex:D4}",
                Boundary = boundary,
                Spec = legendEntry.Spec,
                Direction = RebarDirection.X,
                ZoneType = ZoneType.Simple
            });
        }

        return zones;
    }

    private async Task<IReadOnlyList<ReinforcementZone>> ParseWithColorQuantizationAsync(
        string filePath,
        ColorLegend legend,
        CancellationToken cancellationToken)
    {
        using var image = await Image.LoadAsync<Rgba32>(filePath, cancellationToken);
        int width = image.Width;
        int height = image.Height;

        // Build a label map: each pixel → legend entry index (or -1)
        int[,] labels = new int[width, height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var pixel = image[x, y];
                var isoColor = new IsolineColor(pixel.R, pixel.G, pixel.B);
                var entry = legend.FindClosest(isoColor);

                labels[x, y] = entry is not null
                    ? legend.Entries.ToList().IndexOf(entry)
                    : -1;
            }
        }

        // Connected component analysis per label
        var zones = new List<ReinforcementZone>();
        bool[,] visited = new bool[width, height];
        int zoneIndex = 0;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (visited[x, y] || labels[x, y] < 0) continue;

                int label = labels[x, y];
                var component = FloodFill(labels, visited, x, y, label, width, height);

                if (component.Count < 100) continue; // Skip tiny regions

                var polygon = ExtractBoundaryPolygon(component);
                if (polygon is null) continue;

                var entry = legend.Entries[label];
                zones.Add(new ReinforcementZone
                {
                    Id = $"PNG-{++zoneIndex:D4}",
                    Boundary = polygon,
                    Spec = entry.Spec,
                    Direction = RebarDirection.X,
                    ZoneType = ZoneType.Simple
                });
            }
        }

        return zones;
    }

    private static List<(int X, int Y)> FloodFill(
        int[,] labels, bool[,] visited, int startX, int startY,
        int targetLabel, int width, int height)
    {
        var result = new List<(int, int)>();
        var stack = new Stack<(int X, int Y)>();
        stack.Push((startX, startY));

        while (stack.Count > 0)
        {
            var (x, y) = stack.Pop();
            if (x < 0 || x >= width || y < 0 || y >= height) continue;
            if (visited[x, y] || labels[x, y] != targetLabel) continue;

            visited[x, y] = true;
            result.Add((x, y));

            stack.Push((x + 1, y));
            stack.Push((x - 1, y));
            stack.Push((x, y + 1));
            stack.Push((x, y - 1));
        }

        return result;
    }

    private static Polygon? ExtractBoundaryPolygon(List<(int X, int Y)> pixels)
    {
        if (pixels.Count < 3) return null;

        // Simple approach: convex hull of pixel coordinates
        int minX = pixels.Min(p => p.X);
        int minY = pixels.Min(p => p.Y);
        int maxX = pixels.Max(p => p.X);
        int maxY = pixels.Max(p => p.Y);

        // For MVP: return bounding box as polygon
        var vertices = new List<Point2D>
        {
            new(minX, minY),
            new(maxX, minY),
            new(maxX, maxY),
            new(minX, maxY)
        };

        return new Polygon(vertices);
    }
}
