using System.Text.Json;
using System.Text.Json.Serialization;
using OpenRebar.Domain.Models;

namespace OpenRebar.Application.UseCases;

/// <summary>
/// Reads a project file (<c>--project</c>, or <c>--design</c> as the same file).
/// Paths inside the file are resolved relative to the file's directory.
/// </summary>
public static class ProjectDesignLoader
{
  private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
  {
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true
  };

  public static async Task<ProjectDesignDocument> LoadAsync(string path, CancellationToken cancellationToken = default)
  {
    if (!File.Exists(path))
      throw new FileNotFoundException("Project file was not found.", path);

    await using var stream = File.OpenRead(path);
    var document = await JsonSerializer.DeserializeAsync<ProjectDesignDocument>(stream, Options, cancellationToken)
        ?? throw new InvalidDataException($"Project file '{path}' is empty.");

    string directory = Path.GetDirectoryName(Path.GetFullPath(path)) ?? Directory.GetCurrentDirectory();
    var edges = new List<SlabEdge>();
    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var edge in document.Slab.Edges)
    {
      var parsed = SlabEdges.Parse(edge.Segment, edge.Kind, edge.SupportDepthMm);
      if (!seen.Add(parsed.Segment))
        throw new InvalidDataException($"Edge segment '{parsed.Segment}' is listed more than once.");

      edges.Add(parsed);
    }

    return document with
    {
      SourcePath = Path.GetFullPath(path),
      Slab = document.Slab with { ParsedEdges = edges },
      Layers = document.Layers.Select(layer => layer with
      {
        File = Resolve(directory, layer.File),
        LegendFile = layer.LegendFile is null ? null : Resolve(directory, layer.LegendFile)
      }).ToList()
    };
  }

  private static string Resolve(string directory, string relativeOrAbsolute) =>
      Path.GetFullPath(Path.IsPathRooted(relativeOrAbsolute)
          ? relativeOrAbsolute
          : Path.Combine(directory, relativeOrAbsolute));
}

public sealed record ProjectDesignDocument
{
  public string? ProjectCode { get; init; }
  public string? SlabId { get; init; }
  public string? SteelClass { get; init; }
  public string? Norm { get; init; }
  public ProjectSlabDocument Slab { get; init; } = new();
  public List<ProjectLayerDocument> Layers { get; init; } = [];

  [JsonIgnore]
  public string? SourcePath { get; init; }
}

public sealed record ProjectSlabDocument
{
  public double WidthMm { get; init; }
  public double HeightMm { get; init; }
  public double ThicknessMm { get; init; } = 200;
  public double CoverMm { get; init; } = 25;
  public double CoverEdgeMm { get; init; }
  public double OpeningClearanceMm { get; init; }
  public string ConcreteClass { get; init; } = "B25";
  public List<ProjectEdgeDocument> Edges { get; init; } = [];

  [JsonIgnore]
  public IReadOnlyList<SlabEdge> ParsedEdges { get; init; } = [];
}

public sealed record ProjectEdgeDocument
{
  public string Segment { get; init; } = "";
  public string Kind { get; init; } = "";
  public double? SupportDepthMm { get; init; }
}

public sealed record ProjectLayerDocument
{
  public string Layer { get; init; } = "";
  public string File { get; init; } = "";
  public string? LegendFile { get; init; }
  public ProjectBackgroundDocument? Background { get; init; }
}

public sealed record ProjectBackgroundDocument
{
  public int DiameterMm { get; init; }
  public int SpacingMm { get; init; }
  public string SteelClass { get; init; } = "A500C";
  public double GridOriginMm { get; init; }
}
