using OpenRebar.Domain.Models;
using OpenRebar.Domain.Ports;
using OpenRebar.Domain.Exceptions;
using OpenRebar.Domain.Rules;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace OpenRebar.Infrastructure.ImageProcessing;

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

  public IsolineParseStats Stats { get; private set; } = IsolineParseStats.Empty;

  public RasterCalibration? Calibration { get; set; }

  /// <summary>Components smaller than this are dropped. Default is 0.1 m².</summary>
  public double MinZoneAreaMm2 { get; set; } = 100_000;

  public async Task<IReadOnlyList<ReinforcementZone>> ParseAsync(
      string filePath,
      ColorLegend legend,
      CancellationToken cancellationToken = default)
  {
    if (!File.Exists(filePath))
      throw new InvalidIsolineFileException(filePath, "File not found.");

    if (Calibration is null || Calibration.PixelsPerMillimetre <= 0)
      throw new RasterNotCalibratedException(filePath);

    Stats = new IsolineParseStats { RoiAssumedFullImage = !Calibration.HasRoi };

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
    catch (OpenRebarDomainException)
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
        Boundary = ToMillimetres(boundary),
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
    var calibration = Calibration ?? throw new RasterNotCalibratedException(filePath);
    using var image = await Image.LoadAsync<Rgba32>(filePath, cancellationToken);
    int width = image.Width;
    int height = image.Height;

    if ((long)width * height > MaxImagePixels)
      throw new InvalidIsolineFileException(
          filePath,
          $"Image is too large for in-process parsing: {width}x{height} pixels. " +
          $"Limit: {MaxImagePixels} pixels.");

    var entries = legend.Entries.ToList();
    var lut = BuildColorLut(entries);
    var ignored = new Dictionary<string, int>(StringComparer.Ordinal);
    int labeledPixels = 0;

    // Exact RGB lookup. Blended edge pixels stay unlabeled so antialiasing
    // does not invent a second class.
    int[,] labels = new int[width, height];
    for (int y = 0; y < height; y++)
    {
      for (int x = 0; x < width; x++)
      {
        cancellationToken.ThrowIfCancellationRequested();
        if (!calibration.ContainsPixel(x, y))
        {
          labels[x, y] = -1;
          continue;
        }

        var pixel = image[x, y];
        int key = (pixel.R << 16) | (pixel.G << 8) | pixel.B;
        if (!lut.TryGetValue(key, out int classIndex))
        {
          labels[x, y] = -1;
          continue;
        }

        labels[x, y] = classIndex;
        labeledPixels++;
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

        var contour = ExtractBoundaryPolygon(component, ignored);
        if (contour is null) continue;

        var polygon = ToMillimetres(contour.Outer);
        if (polygon.CalculateArea() < MinZoneAreaMm2) continue;

        var entry = entries[label];
        zones.Add(new ReinforcementZone
        {
          Id = $"PNG-{++zoneIndex:D4}",
          Boundary = polygon,
          Holes = contour.Holes.Select(ToMillimetres).ToList(),
          Spec = entry.Spec,
          Direction = RebarDirection.X,
          ZoneType = ZoneType.Simple
        });
      }
    }

    double pixelAreaMm2 = 1.0 / (calibration.PixelsPerMillimetre * calibration.PixelsPerMillimetre);
    if (zones.Count == 0 && labeledPixels * pixelAreaMm2 >= MinZoneAreaMm2)
    {
      throw new InvalidIsolineFileException(
          filePath,
          "Raster contained a legend-colored region but produced no zone.");
    }

    Stats = new IsolineParseStats
    {
      ParsedEntityCount = labeledPixels == 0 ? 0 : zones.Count,
      IgnoredByReason = ignored,
      RoiAssumedFullImage = !calibration.HasRoi
    };

    return zones;
  }

  private static Dictionary<int, int> BuildColorLut(IReadOnlyList<LegendEntry> entries)
  {
    var lut = new Dictionary<int, int>(entries.Count);
    for (int i = 0; i < entries.Count; i++)
    {
      var color = entries[i].Color;
      lut[(color.R << 16) | (color.G << 8) | color.B] = i;
    }

    return lut;
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

  private sealed record RasterContour(Polygon Outer, IReadOnlyList<Polygon> Holes);

  private static RasterContour? ExtractBoundaryPolygon(
      List<(int X, int Y)> pixels,
      Dictionary<string, int> ignored)
  {
    if (pixels.Count < 3) return null;

    var pixelSet = pixels.ToHashSet();
    var outgoing = new Dictionary<(int X, int Y), List<(int X, int Y)>>();

    foreach (var (x, y) in pixels)
    {
      if (!pixelSet.Contains((x, y - 1)))
        AddEdge(outgoing, (x, y), (x + 1, y));
      if (!pixelSet.Contains((x + 1, y)))
        AddEdge(outgoing, (x + 1, y), (x + 1, y + 1));
      if (!pixelSet.Contains((x, y + 1)))
        AddEdge(outgoing, (x + 1, y + 1), (x, y + 1));
      if (!pixelSet.Contains((x - 1, y)))
        AddEdge(outgoing, (x, y + 1), (x, y));
    }

    var loops = new List<List<(int X, int Y)>>();
    int guard = outgoing.Sum(pair => pair.Value.Count) + 2;
    while (outgoing.Count > 0 && loops.Count < guard)
    {
      var start = outgoing.Keys.OrderBy(point => point.Y).ThenBy(point => point.X).First();
      var loop = WalkLoop(outgoing, start);
      if (loop.Count >= 4)
        loops.Add(loop);
    }

    if (loops.Count == 0)
      return null;

    int outerIndex = 0;
    double outerArea = LoopArea(loops[0]);
    for (int i = 1; i < loops.Count; i++)
    {
      double area = LoopArea(loops[i]);
      if (area > outerArea)
      {
        outerArea = area;
        outerIndex = i;
      }
    }

    var outer = LoopPolygon(loops[outerIndex]);
    if (outer is null)
      return null;

    var holes = new List<Polygon>();
    for (int i = 0; i < loops.Count; i++)
    {
      if (i == outerIndex)
        continue;

      var hole = LoopPolygon(loops[i]);
      if (hole is null)
        continue;

      if (PolygonDecomposition.IsPointInPolygon(hole.Vertices[0], outer))
        holes.Add(hole);
      else
      {
        ignored.TryGetValue("holeIgnored", out int current);
        ignored["holeIgnored"] = current + 1;
      }
    }

    return new RasterContour(outer, holes);
  }

  private static Polygon? LoopPolygon(List<(int X, int Y)> loop)
  {
    var simplified = SimplifyOrthogonalLoop(loop);
    if (simplified.Count < 3)
      return null;

    return new Polygon(simplified.Select(point => new Point2D(point.X, point.Y)).ToList());
  }

  private static void AddEdge(
      Dictionary<(int X, int Y), List<(int X, int Y)>> outgoing,
      (int X, int Y) from,
      (int X, int Y) to)
  {
    if (!outgoing.TryGetValue(from, out var list))
    {
      list = [];
      outgoing[from] = list;
    }

    list.Add(to);
  }

  private static List<(int X, int Y)> WalkLoop(
      Dictionary<(int X, int Y), List<(int X, int Y)>> outgoing,
      (int X, int Y) start)
  {
    var first = outgoing[start][0];
    var incoming = (first.X - start.X, first.Y - start.Y);
    var current = start;
    var loop = new List<(int X, int Y)>();
    int guard = 0;
    int limit = outgoing.Sum(pair => pair.Value.Count) + 2;

    do
    {
      loop.Add(current);
      if (!outgoing.TryGetValue(current, out var options) || options.Count == 0)
        break;

      var next = ChooseNext(incoming, options, current);
      options.Remove(next);
      if (options.Count == 0)
        outgoing.Remove(current);

      incoming = (next.X - current.X, next.Y - current.Y);
      current = next;
      guard++;
    }
    while (current != start && guard < limit);

    return loop;
  }

  /// <summary>
  /// Boundary edges keep the filled pixel on their right in image coordinates.
  /// At a shared corner the smallest clockwise turn stays on that face, so a
  /// one-pixel neck does not jump to the other side.
  /// </summary>
  private static (int X, int Y) ChooseNext(
      (int X, int Y) incoming,
      List<(int X, int Y)> options,
      (int X, int Y) here)
  {
    int best = 0;
    int bestDelta = int.MaxValue;
    for (int i = 0; i < options.Count; i++)
    {
      var step = (options[i].X - here.X, options[i].Y - here.Y);
      int delta = ClockwiseDelta(incoming, step);
      if (delta < bestDelta)
      {
        bestDelta = delta;
        best = i;
      }
    }

    return options[best];
  }

  private static int ClockwiseDelta((int X, int Y) incoming, (int X, int Y) outgoing)
  {
    int from = DirectionIndex(incoming.X, incoming.Y);
    int to = DirectionIndex(outgoing.X, outgoing.Y);
    return (to - from + 4) % 4;
  }

  private static int DirectionIndex(int x, int y) => (x, y) switch
  {
    (1, 0) => 0,
    (0, 1) => 1,
    (-1, 0) => 2,
    (0, -1) => 3,
    _ => 0
  };

  private static double LoopArea(List<(int X, int Y)> loop)
  {
    double area = 0;
    for (int i = 0; i < loop.Count; i++)
    {
      var current = loop[i];
      var next = loop[(i + 1) % loop.Count];
      area += current.X * next.Y - next.X * current.Y;
    }

    return Math.Abs(area) / 2.0;
  }

  private Polygon ToMillimetres(Polygon polygon)
  {
    var calibration = Calibration ?? throw new InvalidOperationException("Raster calibration is required.");
    return new Polygon(polygon.Vertices
        .Select(point => calibration.ToMillimetres(point.X, point.Y))
        .ToList());
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
