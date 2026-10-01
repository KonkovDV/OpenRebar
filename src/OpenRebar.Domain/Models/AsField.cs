namespace OpenRebar.Domain.Models;

/// <summary>
/// Required reinforcement taken from a calculation table, in mm²/m.
/// </summary>
public sealed record AsField
{
  public required string AdapterId { get; init; }
  public required string Units { get; init; }
  public required IReadOnlyList<AsFieldElement> Elements { get; init; }
  public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed record AsFieldElement
{
  public required string Id { get; init; }
  public required double MinX { get; init; }
  public required double MinY { get; init; }
  public required double MaxX { get; init; }
  public required double MaxY { get; init; }
  public required IReadOnlyDictionary<string, double> AsMm2PerM { get; init; }
}

/// <summary>
/// How to read one tabular export. Column values are layer names such as BottomX.
/// </summary>
public sealed record AsFieldMapping
{
  public required string AdapterId { get; init; }
  public required string Delimiter { get; init; }
  public required string DecimalSeparator { get; init; }
  public required string Units { get; init; }
  public required IReadOnlyDictionary<string, string> Columns { get; init; }
  public bool AxesMatch { get; init; } = true;

  public static AsFieldMapping LiraPlates { get; } = new()
  {
    AdapterId = "lira-sapr.plates",
    Delimiter = ";",
    DecimalSeparator = ",",
    Units = "cm2_per_m",
    AxesMatch = true,
    Columns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
      ["AS1"] = "BottomX",
      ["AS2"] = "TopX",
      ["AS3"] = "BottomY",
      ["AS4"] = "TopY"
    }
  };
}
