using System.Globalization;
using OpenRebar.Domain.Exceptions;
using OpenRebar.Domain.Models;

namespace OpenRebar.Application.UseCases;

/// <summary>
/// Reads a rectangular element table. Point clouds and non-rectangular cells are not inferred here.
/// </summary>
public static class TabularAsFieldReader
{
  private static readonly string[] Geometry = ["id", "minX", "minY", "maxX", "maxY"];

  public static AsField ReadFile(string path)
  {
    string text = File.ReadAllText(path);
    return Read(text, Detect(text));
  }

  public static AsFieldMapping Detect(string text)
  {
    string first = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n')
        .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .FirstOrDefault() ?? "";
    bool semicolon = first.Contains(';', StringComparison.Ordinal);
    string delimiter = semicolon ? ";" : ",";
    var headers = first.Split(delimiter, StringSplitOptions.None).Select(cell => cell.Trim()).ToList();
    if (headers.Any(header => header.Equals("AS1", StringComparison.OrdinalIgnoreCase)))
    {
      return AsFieldMapping.LiraPlates with
      {
        Delimiter = delimiter,
        DecimalSeparator = semicolon ? "," : "."
      };
    }

    var columns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    foreach (string layer in new[] { "BottomX", "BottomY", "TopX", "TopY" })
    {
      if (headers.Any(header => header.Equals(layer, StringComparison.OrdinalIgnoreCase)))
        columns[layer] = layer;
    }

    if (columns.Count == 0)
      throw new AsFieldReadException("FIELD_MISSING", "The table needs AS1–AS4 or BottomX, BottomY, TopX, TopY columns.");

    return new AsFieldMapping
    {
      AdapterId = "tabular-field",
      Delimiter = delimiter,
      DecimalSeparator = semicolon ? "," : ".",
      Units = "mm2_per_m",
      Columns = columns
    };
  }

  public static AsField Read(string text, AsFieldMapping mapping)
  {
    if (!mapping.AxesMatch)
      throw new AsFieldReadException("FIELD_AXES_MISMATCH", "Local plate axes do not match the reinforcement frame.");

    if (mapping.Delimiter == mapping.DecimalSeparator)
      throw new AsFieldReadException("FIELD_DELIMITER", "The column delimiter and the decimal separator must differ.");

    if (mapping.Units is not ("cm2_per_m" or "mm2_per_m"))
      throw new AsFieldReadException("FIELD_UNITS", "Units must be cm2_per_m or mm2_per_m.");

    var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n')
        .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    if (lines.Length < 2)
      throw new AsFieldReadException("FIELD_EMPTY", "The table needs a header and at least one element.");

    var headers = Split(lines[0], mapping.Delimiter);
    var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    for (int i = 0; i < headers.Count; i++)
      index[headers[i]] = i;

    foreach (string name in Geometry)
    {
      if (!index.ContainsKey(name))
        throw new AsFieldReadException("FIELD_MISSING", $"Column '{name}' is required.");
    }

    foreach (string column in mapping.Columns.Keys)
    {
      if (!index.ContainsKey(column))
        throw new AsFieldReadException("FIELD_MISSING", $"Mapped column '{column}' is missing.");
    }

    var elements = new List<AsFieldElement>();
    var cm2Values = new List<double>();
    for (int row = 1; row < lines.Length; row++)
    {
      var cells = Split(lines[row], mapping.Delimiter);
      string id = Cell(cells, index["id"], "id", row);
      double minX = Number(Cell(cells, index["minX"], "minX", row), mapping, "minX", row);
      double minY = Number(Cell(cells, index["minY"], "minY", row), mapping, "minY", row);
      double maxX = Number(Cell(cells, index["maxX"], "maxX", row), mapping, "maxX", row);
      double maxY = Number(Cell(cells, index["maxY"], "maxY", row), mapping, "maxY", row);
      if (maxX <= minX || maxY <= minY)
        throw new AsFieldReadException("FIELD_GEOMETRY", $"Element '{id}' is not a positive rectangle.");

      var values = new Dictionary<string, double>(StringComparer.Ordinal);
      foreach (var column in mapping.Columns)
      {
        double raw = Number(Cell(cells, index[column.Key], column.Key, row), mapping, column.Key, row);
        if (raw < 0)
          throw new AsFieldReadException("FIELD_NEGATIVE", $"Element '{id}' has a negative {column.Key}.");

        double mm2 = mapping.Units == "cm2_per_m" ? raw * 100.0 : raw;
        values[column.Value] = mm2;
        cm2Values.Add(mapping.Units == "cm2_per_m" ? raw : raw / 100.0);
      }

      elements.Add(new AsFieldElement
      {
        Id = id,
        MinX = minX,
        MinY = minY,
        MaxX = maxX,
        MaxY = maxY,
        AsMm2PerM = values
      });
    }

    var warnings = new List<string>();
    if (cm2Values.Count > 0)
    {
      var ordered = cm2Values.OrderBy(value => value).ToList();
      double median = ordered[ordered.Count / 2];
      if (median is < 0.1 or > 100)
        warnings.Add("Median As is outside 0.1–100 cm²/m. Check the units.");
    }

    return new AsField
    {
      AdapterId = mapping.AdapterId,
      Units = "mm2_per_m",
      Elements = elements,
      Warnings = warnings
    };
  }

  private static List<string> Split(string line, string delimiter) =>
      line.Split(delimiter, StringSplitOptions.None).Select(cell => cell.Trim()).ToList();

  private static string Cell(IReadOnlyList<string> cells, int index, string name, int row)
  {
    if (index >= cells.Count || string.IsNullOrWhiteSpace(cells[index]))
      throw new AsFieldReadException("FIELD_MISSING", $"Row {row + 1} has no value for '{name}'.");
    return cells[index];
  }

  private static double Number(string text, AsFieldMapping mapping, string name, int row)
  {
    string normalized = mapping.DecimalSeparator == ","
        ? text.Replace(',', '.')
        : text;
    if (!double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
      throw new AsFieldReadException("FIELD_NUMBER", $"Row {row + 1} column '{name}' is not a number.");
    return value;
  }
}
