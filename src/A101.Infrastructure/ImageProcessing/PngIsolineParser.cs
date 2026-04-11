using A101.Domain.Models;
using A101.Domain.Ports;
using A101.Domain.Exceptions;
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
    private const long MaxImagePixels = 25_000_000;

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
            throw new InvalidIsolineFileException(filePath, "File not found.");

        try
        {
            // If ML service available, delegate to it
            if (_mlService is not null)
            {
                return await ParseWithMlAsync(filePath, legend, cancellationToken);
            }

            // Otherwise: basic color quantization + connected components
            return await ParseWithColorQuantizationAsync(filePath, legend, cancellationToken);
        }
        catch (InvalidIsolineFileException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidIsolineFileException(filePath, ex.Message);
        }
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

        if ((long)width * height > MaxImagePixels)
            throw new InvalidIsolineFileException(
                filePath,
                $"Image is too large for in-process parsing: {width}x{height} pixels. " +
                $"Limit: {MaxImagePixels} pixels.");

        var entries = legend.Entries.ToList();

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
                    ? entries.IndexOf(entry)
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

                var entry = entries[label];
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

        var pixelSet = pixels.ToHashSet();
        var edges = new Dictionary<(int X, int Y), (int X, int Y)>();

        foreach (var (x, y) in pixels)
        {
            if (!pixelSet.Contains((x, y - 1)))
                edges[(x, y)] = (x + 1, y);

            if (!pixelSet.Contains((x + 1, y)))
                edges[(x + 1, y)] = (x + 1, y + 1);

            if (!pixelSet.Contains((x, y + 1)))
                edges[(x + 1, y + 1)] = (x, y + 1);

            if (!pixelSet.Contains((x - 1, y)))
                edges[(x, y + 1)] = (x, y);
        }

        if (edges.Count == 0)
            return null;

        var start = edges.Keys
            .OrderBy(p => p.Y)
            .ThenBy(p => p.X)
            .First();

        var outline = new List<(int X, int Y)>();
        var current = start;
        int safety = 0;

        do
        {
            outline.Add(current);

            if (!edges.TryGetValue(current, out var next))
                return null;

            current = next;
            safety++;
        }
        while (current != start && safety <= edges.Count + 1);

        if (outline.Count < 3 || safety > edges.Count + 1)
            return null;

        var simplified = SimplifyOrthogonalLoop(outline);
        if (simplified.Count < 3)
            return null;

        return new Polygon(simplified.Select(p => new Point2D(p.X, p.Y)).ToList());
    }

    private static List<(int X, int Y)> SimplifyOrthogonalLoop(List<(int X, int Y)> outline)
    {
        var simplified = new List<(int X, int Y)>();

        for (int i = 0; i < outline.Count; i++)
        {
            var prev = outline[(i - 1 + outline.Count) % outline.Count];
            var current = outline[i];
            var next = outline[(i + 1) % outline.Count];

            if (!AreCollinear(prev, current, next))
                simplified.Add(current);
        }

        return simplified;
    }

    private static bool AreCollinear((int X, int Y) a, (int X, int Y) b, (int X, int Y) c)
    {
        return (a.X == b.X && b.X == c.X) || (a.Y == b.Y && b.Y == c.Y);
    }
}
