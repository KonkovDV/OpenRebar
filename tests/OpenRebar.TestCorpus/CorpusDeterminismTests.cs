using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace OpenRebar.TestCorpus;

public class CorpusDeterminismTests
{
  [Fact]
  public void Catalog_ListsEightScenarios()
  {
    CorpusGenerator.ScenarioIds.Should().Equal(
        "rect-6x4",
        "l-zone",
        "zone-with-hole",
        "two-adjacent",
        "narrow-strip",
        "span-14m",
        "shaft-opening",
        "skew-30");
  }

  [Fact]
  public void CorpusA0_HashFile_IsWrittenWhenRequested()
  {
    using var buffer = new MemoryStream();
    foreach (string id in CorpusGenerator.ScenarioIds)
    {
      var files = CorpusGenerator.Generate(id);
      buffer.Write(files.Dxf.AsSpan());
      buffer.Write(files.Png.AsSpan());
      buffer.Write(files.GroundTruthJson.AsSpan());
    }

    string hash = Sha256(buffer.ToArray());
    hash.Should().HaveLength(64);
    WriteHashIfRequested("corpus-a0.sha256", hash);
  }

  [Fact]
  public void Generate_IsByteForByteStable()
  {
    foreach (string id in CorpusGenerator.ScenarioIds)
    {
      var first = CorpusGenerator.Generate(id);
      var second = CorpusGenerator.Generate(id);

      Sha256(first.Dxf).Should().Be(Sha256(second.Dxf), $"{id} DXF");
      Sha256(first.Png).Should().Be(Sha256(second.Png), $"{id} PNG");
      Sha256(first.GroundTruthJson).Should().Be(Sha256(second.GroundTruthJson), $"{id} ground truth");

      first.Dxf.Length.Should().BeGreaterThan(100);
      first.Png.Length.Should().BeGreaterThan(8);
      first.Png[..8].Should().Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
    }
  }

  [Fact]
  public void Dxf_NamesTheReinforcementLayer()
  {
    string text = Encoding.ASCII.GetString(CorpusGenerator.Generate("rect-6x4").Dxf);
    text.Should().Contain("LWPOLYLINE");
    text.Should().Contain("BottomX");

    string holed = Encoding.ASCII.GetString(CorpusGenerator.Generate("zone-with-hole").Dxf);
    holed.Should().Contain("HATCH");
  }

  [Fact]
  public void GroundTruth_AreasAndAsMatchIndependentFormulas()
  {
    using var rect = JsonDocument.Parse(CorpusGenerator.Generate("rect-6x4").GroundTruthJson);
    var zone = rect.RootElement.GetProperty("zones")[0];
    zone.GetProperty("areaMm2").GetDouble().Should().Be(24_000_000);
    double asReq = rect.RootElement.GetProperty("classes")[0].GetProperty("areaPerMeterMm2").GetDouble();
    asReq.Should().BeApproximately(Math.PI * 12 * 12 / 4.0 * (1000.0 / 200.0), 1e-9);

    using var elbow = JsonDocument.Parse(CorpusGenerator.Generate("l-zone").GroundTruthJson);
    elbow.RootElement.GetProperty("zones")[0].GetProperty("areaMm2").GetDouble().Should().Be(18_000_000);

    foreach (string id in CorpusGenerator.ScenarioIds)
    {
      using var document = JsonDocument.Parse(CorpusGenerator.Generate(id).GroundTruthJson);
      foreach (var item in document.RootElement.GetProperty("zones").EnumerateArray())
      {
        double shoelace = Shoelace(item.GetProperty("outer")) - item.GetProperty("holes").EnumerateArray().Sum(Shoelace);
        item.GetProperty("areaMm2").GetDouble().Should().BeApproximately(shoelace, 1e-6, id);
      }
    }
  }

  [Fact]
  public void GroundTruth_NarrowStripAndSkewEdge()
  {
    using var narrow = JsonDocument.Parse(CorpusGenerator.Generate("narrow-strip").GroundTruthJson);
    var slab = narrow.RootElement.GetProperty("slab");
    slab.GetProperty("maxX").GetDouble().Should().Be(400);
    slab.GetProperty("maxY").GetDouble().Should().Be(4000);

    using var skew = JsonDocument.Parse(CorpusGenerator.Generate("skew-30").GroundTruthJson);
    skew.RootElement.GetProperty("notableEdgeAngleDeg").GetDouble().Should().BeApproximately(30, 0.01);
  }

  [Fact]
  public void Png_InteriorPixelsMatchTheZones()
  {
    AssertPixel("rect-6x4", 3000, 2000, 0, 255, 0);
    AssertPixel("zone-with-hole", 3000, 2000, 255, 255, 255);
    AssertPixel("two-adjacent", 1500, 2000, 0, 255, 255);
    AssertPixel("two-adjacent", 4500, 2000, 255, 0, 0);
    AssertPixel("shaft-opening", 3950, 2950, 255, 255, 255);
    AssertPixel("shaft-opening", 1000, 1000, 0, 255, 255);
  }

  private static void AssertPixel(string scenarioId, double xMm, double yMm, byte r, byte g, byte b)
  {
    var files = CorpusGenerator.Generate(scenarioId);
    using var document = JsonDocument.Parse(files.GroundTruthJson);
    var root = document.RootElement;
    double k = root.GetProperty("pxPerMm").GetDouble();
    double ox = root.GetProperty("originPx")[0].GetDouble();
    double oy = root.GetProperty("originPx")[1].GetDouble();
    int px = (int)Math.Round(ox + xMm * k);
    int py = (int)Math.Round(oy - yMm * k);

    using var image = Image.Load<Rgb24>(new MemoryStream(files.Png));
    var pixel = image[px, py];
    pixel.R.Should().Be(r, $"{scenarioId} at ({xMm},{yMm}) mm");
    pixel.G.Should().Be(g, $"{scenarioId} at ({xMm},{yMm}) mm");
    pixel.B.Should().Be(b, $"{scenarioId} at ({xMm},{yMm}) mm");
  }

  private static double Shoelace(JsonElement ring)
  {
    var points = ring.EnumerateArray().Select(point => (X: point[0].GetDouble(), Y: point[1].GetDouble())).ToArray();
    double sum = 0;
    for (int i = 0; i < points.Length; i++)
    {
      var a = points[i];
      var b = points[(i + 1) % points.Length];
      sum += a.X * b.Y - b.X * a.Y;
    }

    return Math.Abs(sum) * 0.5;
  }

  private static string Sha256(byte[] bytes) =>
      Convert.ToHexString(SHA256.HashData(bytes));

  private static void WriteHashIfRequested(string fileName, string hash)
  {
    string? directory = Environment.GetEnvironmentVariable("OPENREBAR_TFM_HASH_DIR");
    if (string.IsNullOrWhiteSpace(directory))
      return;

    Directory.CreateDirectory(directory);
    File.WriteAllText(Path.Combine(directory, fileName), hash + "\n");
  }
}
