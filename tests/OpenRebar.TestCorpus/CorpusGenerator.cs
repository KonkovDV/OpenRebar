using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenRebar.TestCorpus;

public sealed class CorpusFiles
{
  public required string Id { get; init; }
  public required byte[] Dxf { get; init; }
  public required byte[] Png { get; init; }
  public required byte[] GroundTruthJson { get; init; }
}

public static class CorpusGenerator
{
  private static readonly JsonSerializerOptions JsonOptions = new()
  {
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true,
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
  };

  private static readonly Dictionary<string, ScenarioDefinition> ById =
      ScenarioCatalog.All.ToDictionary(scenario => scenario.Id);

  public static IReadOnlyList<string> ScenarioIds { get; } =
      ScenarioCatalog.All.Select(scenario => scenario.Id).ToArray();

  public static CorpusFiles Generate(string scenarioId)
  {
    if (!ById.TryGetValue(scenarioId, out var scenario))
    {
      throw new ArgumentException(
          $"Unknown scenario '{scenarioId}'. Known: {string.Join(", ", ScenarioIds)}.",
          nameof(scenarioId));
    }

    var layout = CorpusLayout.Create(scenario);
    return new CorpusFiles
    {
      Id = scenario.Id,
      Dxf = DxfCorpusWriter.Write(scenario),
      Png = PngCorpusWriter.Write(scenario, layout),
      GroundTruthJson = GroundTruth(scenario, layout)
    };
  }

  public static void WriteAll(string directory)
  {
    foreach (string id in ScenarioIds)
      Write(id, directory);
  }

  public static void Write(string scenarioId, string directory)
  {
    var files = Generate(scenarioId);
    string folder = Path.Combine(directory, files.Id);
    Directory.CreateDirectory(folder);
    File.WriteAllBytes(Path.Combine(folder, "input.dxf"), files.Dxf);
    File.WriteAllBytes(Path.Combine(folder, "input.png"), files.Png);
    File.WriteAllBytes(Path.Combine(folder, "ground_truth.json"), files.GroundTruthJson);
  }

  private static byte[] GroundTruth(ScenarioDefinition scenario, CorpusLayout layout)
  {
    var document = new GroundTruthDocument
    {
      Id = scenario.Id,
      Seed = scenario.Seed,
      Description = scenario.Description,
      PxPerMm = CorpusLayout.PxPerMm,
      OriginPx = [layout.OriginX, layout.OriginY],
      NotableEdgeAngleDeg = scenario.NotableEdgeAngleDeg,
      Slab = new SlabDocument
      {
        Outer = Ring(scenario.SlabOuter),
        Openings = scenario.Openings.Select(Ring).ToArray(),
        ThicknessMm = scenario.ThicknessMm,
        CoverMm = scenario.CoverMm,
        MinX = layout.MinX,
        MinY = layout.MinY,
        MaxX = layout.MaxX,
        MaxY = layout.MaxY
      },
      Classes = layout.UsedClassIndexes.Select(ClassDocument.From).ToArray(),
      Zones = scenario.Zones.Select(zone => new ZoneDocument
      {
        ClassIndex = zone.ClassIndex,
        Outer = Ring(zone.Outer),
        Holes = zone.Holes.Select(Ring).ToArray(),
        AreaMm2 = Area(zone.Outer) - zone.Holes.Sum(Area)
      }).ToArray(),
      LegendSwatches = layout.Swatches.Select(swatch => new SwatchDocument
      {
        ClassIndex = swatch.ClassIndex,
        X = swatch.X,
        Y = swatch.Y,
        Width = swatch.Width,
        Height = swatch.Height
      }).ToArray(),
      Cli = new CliDocument
      {
        SlabWidthMm = layout.MaxX,
        SlabHeightMm = layout.MaxY,
        ThicknessMm = scenario.ThicknessMm,
        CoverMm = scenario.CoverMm
      }
    };

    byte[] json = JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);
    var withNewline = new byte[json.Length + 1];
    json.CopyTo(withNewline, 0);
    withNewline[^1] = (byte)'\n';
    return withNewline;
  }

  private static double[][] Ring(IReadOnlyList<Mm> ring) =>
      ring.Select(point => new[] { point.X, point.Y }).ToArray();

  private static double Area(IReadOnlyList<Mm> ring)
  {
    double sum = 0;
    for (int i = 0; i < ring.Count; i++)
    {
      var a = ring[i];
      var b = ring[(i + 1) % ring.Count];
      sum += a.X * b.Y - b.X * a.Y;
    }

    return Math.Abs(sum) * 0.5;
  }

  private sealed class GroundTruthDocument
  {
    public string Generator { get; init; } = "OpenRebar.TestCorpus/v0";
    public string Id { get; init; } = "";
    public int Seed { get; init; }
    public string Description { get; init; } = "";
    public string Units { get; init; } = "mm";
    public string DxfUnits { get; init; } = "mm";
    public double PxPerMm { get; init; }
    public double[] OriginPx { get; init; } = [];
    public string PixelMapping { get; init; } =
        "x_mm=(px-originPx[0])/pxPerMm; y_mm=(originPx[1]-py)/pxPerMm; image Y grows downward";
    public string IntendedLayer { get; init; } = "BottomX";
    public double? NotableEdgeAngleDeg { get; init; }
    public SlabDocument Slab { get; init; } = new();
    public ClassDocument[] Classes { get; init; } = [];
    public ZoneDocument[] Zones { get; init; } = [];
    public SwatchDocument[] LegendSwatches { get; init; } = [];
    public CliDocument Cli { get; init; } = new();
  }

  private sealed class SlabDocument
  {
    public double[][] Outer { get; init; } = [];
    public double[][][] Openings { get; init; } = [];
    public double ThicknessMm { get; init; }
    public double CoverMm { get; init; }
    public double MinX { get; init; }
    public double MinY { get; init; }
    public double MaxX { get; init; }
    public double MaxY { get; init; }
  }

  private sealed class ClassDocument
  {
    public int Index { get; init; }
    public int[] Rgb { get; init; } = [];
    public short? Aci { get; init; }
    public int DiameterMm { get; init; }
    public int SpacingMm { get; init; }
    public string SteelClass { get; init; } = LegendPalette.SteelClass;
    public double AreaPerMeterMm2 { get; init; }

    public static ClassDocument From(int index)
    {
      var legendClass = LegendPalette.Get(index);
      return new ClassDocument
      {
        Index = legendClass.Index,
        Rgb = [legendClass.R, legendClass.G, legendClass.B],
        Aci = legendClass.Aci,
        DiameterMm = legendClass.DiameterMm,
        SpacingMm = legendClass.SpacingMm,
        AreaPerMeterMm2 = legendClass.AreaPerMeterMm2
      };
    }
  }

  private sealed class ZoneDocument
  {
    public int ClassIndex { get; init; }
    public double[][] Outer { get; init; } = [];
    public double[][][] Holes { get; init; } = [];
    public double AreaMm2 { get; init; }
  }

  private sealed class SwatchDocument
  {
    public int ClassIndex { get; init; }
    public int X { get; init; }
    public int Y { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
  }

  private sealed class CliDocument
  {
    public double[] OriginMm { get; init; } = [0, 0];
    public double SlabWidthMm { get; init; }
    public double SlabHeightMm { get; init; }
    public double ThicknessMm { get; init; }
    public double CoverMm { get; init; }
  }
}
