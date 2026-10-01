using System.Globalization;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace OpenRebar.TestCorpus;

internal static class PngCorpusWriter
{
  private static readonly Rgb24 Background = new(255, 255, 255);
  private static readonly Rgb24 Panel = new(245, 245, 245);
  private static readonly Rgb24 Ink = new(0, 0, 0);

  private static readonly PngEncoder Encoder = new()
  {
    ColorType = PngColorType.Rgb,
    BitDepth = PngBitDepth.Bit8,
    FilterMethod = PngFilterMethod.None,
    InterlaceMethod = PngInterlaceMode.None,
    CompressionLevel = PngCompressionLevel.DefaultCompression,
    SkipMetadata = true
  };

  public static byte[] Write(ScenarioDefinition scenario, CorpusLayout layout)
  {
    int sample = CorpusLayout.Supersample;
    int sampleWidth = layout.Width * sample;
    int sampleHeight = layout.Height * sample;
    var coverage = new int[sampleWidth * sampleHeight];

    for (int zoneIndex = 0; zoneIndex < scenario.Zones.Length; zoneIndex++)
    {
      var zone = scenario.Zones[zoneIndex];
      Paint(coverage, sampleWidth, sampleHeight, sample, Rings(zone, layout), zone.ClassIndex + 1);
    }

    using var image = new Image<Rgb24>(layout.Width, layout.Height, Background);
    Downsample(image, coverage, sampleWidth, sample);
    DrawLegend(image, layout);

    using var stream = new MemoryStream();
    image.Save(stream, Encoder);
    return stream.ToArray();
  }

  private static PointD[][] Rings(ZoneDefinition zone, CorpusLayout layout)
  {
    var rings = new PointD[1 + zone.Holes.Length][];
    rings[0] = ToPixels(zone.Outer, layout);
    for (int i = 0; i < zone.Holes.Length; i++)
      rings[i + 1] = ToPixels(zone.Holes[i], layout);

    return rings;
  }

  private static PointD[] ToPixels(IReadOnlyList<Mm> ring, CorpusLayout layout)
  {
    var points = new PointD[ring.Count];
    for (int i = 0; i < ring.Count; i++)
    {
      points[i] = new PointD(
          layout.OriginX + ring[i].X * CorpusLayout.PxPerMm,
          layout.OriginY - ring[i].Y * CorpusLayout.PxPerMm);
    }

    return points;
  }

  /// <summary>
  /// Even-odd fill at <paramref name="sample"/>× resolution.
  /// A pixel is inside when its sample centre lies on a filled span.
  /// </summary>
  private static void Paint(
      int[] coverage,
      int sampleWidth,
      int sampleHeight,
      int sample,
      PointD[][] rings,
      int value)
  {
    var crossings = new List<double>(8);
    for (int row = 0; row < sampleHeight; row++)
    {
      double y = (row + 0.5) / sample;
      crossings.Clear();
      foreach (var ring in rings)
      {
        for (int i = 0; i < ring.Length; i++)
        {
          PointD a = ring[i];
          PointD b = ring[(i + 1) % ring.Length];
          if (a.Y == b.Y)
            continue;

          bool crosses = (a.Y <= y && y < b.Y) || (b.Y <= y && y < a.Y);
          if (!crosses)
            continue;

          double t = (y - a.Y) / (b.Y - a.Y);
          crossings.Add(a.X + t * (b.X - a.X));
        }
      }

      crossings.Sort();
      for (int i = 0; i + 1 < crossings.Count; i += 2)
      {
        int x0 = (int)Math.Ceiling(crossings[i] * sample - 0.5);
        int x1 = (int)Math.Floor(crossings[i + 1] * sample - 0.5 - 1e-9);
        if (x0 < 0)
          x0 = 0;
        if (x1 >= sampleWidth)
          x1 = sampleWidth - 1;

        int offset = row * sampleWidth;
        for (int x = x0; x <= x1; x++)
          coverage[offset + x] = value;
      }
    }
  }

  private static void Downsample(Image<Rgb24> image, int[] coverage, int sampleWidth, int sample)
  {
    int n = sample * sample;
    for (int y = 0; y < image.Height; y++)
    {
      for (int x = 0; x < image.Width; x++)
      {
        int r = 0;
        int g = 0;
        int b = 0;
        for (int sy = 0; sy < sample; sy++)
        {
          int row = (y * sample + sy) * sampleWidth + x * sample;
          for (int sx = 0; sx < sample; sx++)
          {
            int id = coverage[row + sx];
            if (id == 0)
            {
              r += 255;
              g += 255;
              b += 255;
            }
            else
            {
              var legendClass = LegendPalette.Get(id - 1);
              r += legendClass.R;
              g += legendClass.G;
              b += legendClass.B;
            }
          }
        }

        image[x, y] = new Rgb24((byte)(r / n), (byte)(g / n), (byte)(b / n));
      }
    }
  }

  private static void DrawLegend(Image<Rgb24> image, CorpusLayout layout)
  {
    int panelX = layout.Width - CorpusLayout.LegendWidth;
    Fill(image, panelX, 0, CorpusLayout.LegendWidth, layout.Height, Panel);
    BitmapFont.Draw(image, "10mm/px", panelX + 12, 4, Ink);

    foreach (var swatch in layout.Swatches)
    {
      Fill(image, swatch.X - 1, swatch.Y - 1, swatch.Width + 2, swatch.Height + 2, Ink);
      var legendClass = LegendPalette.Get(swatch.ClassIndex);
      Fill(image, swatch.X, swatch.Y, swatch.Width, swatch.Height, new Rgb24(legendClass.R, legendClass.G, legendClass.B));
      BitmapFont.Draw(
          image,
          string.Create(CultureInfo.InvariantCulture, $"{legendClass.DiameterMm}@{legendClass.SpacingMm}"),
          swatch.X + swatch.Width + 6,
          swatch.Y + 10,
          Ink);
    }
  }

  private static void Fill(Image<Rgb24> image, int x, int y, int width, int height, Rgb24 color)
  {
    int x1 = Math.Min(image.Width, x + width);
    int y1 = Math.Min(image.Height, y + height);
    int x0 = Math.Max(0, x);
    int y0 = Math.Max(0, y);
    for (int py = y0; py < y1; py++)
    {
      for (int px = x0; px < x1; px++)
        image[px, py] = color;
    }
  }

  private readonly record struct PointD(double X, double Y);
}
