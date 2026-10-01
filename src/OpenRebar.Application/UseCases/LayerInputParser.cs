using OpenRebar.Domain.Models;

namespace OpenRebar.Application.UseCases;

/// <summary>
/// Parses one <c>--layer-input</c> value: <c>BottomX=path.dxf;legend=legend.json;bg=10@200</c>.
/// </summary>
public static class LayerInputParser
{
  public static bool TryParse(string text, out LayerInputDraft draft, out string? error)
  {
    draft = new LayerInputDraft(default, "", null, null, null);
    if (string.IsNullOrWhiteSpace(text))
    {
      error = "Error: --layer-input is empty.";
      return false;
    }

    var parts = text.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    var head = parts[0].Split('=', 2);
    if (head.Length != 2 || !LayerKeyParser.TryParse(head[0], out LayerKey layer) || string.IsNullOrWhiteSpace(head[1]))
    {
      error = "Error: --layer-input must start with BottomX|BottomY|TopX|TopY=path.";
      return false;
    }

    string? legendPath = null;
    int? diameter = null;
    int? spacing = null;
    for (int i = 1; i < parts.Length; i++)
    {
      var piece = parts[i].Split('=', 2);
      if (piece.Length != 2)
      {
        error = $"Error: --layer-input segment '{parts[i]}' must be key=value.";
        return false;
      }

      if (piece[0].Equals("legend", StringComparison.OrdinalIgnoreCase))
      {
        legendPath = piece[1];
        continue;
      }

      if (piece[0].Equals("bg", StringComparison.OrdinalIgnoreCase))
      {
        if (!TryParseBackground(piece[1], out diameter, out spacing))
        {
          error = "Error: bg must look like 10@200.";
          return false;
        }

        continue;
      }

      error = $"Error: unknown --layer-input key '{piece[0]}'.";
      return false;
    }

    draft = new LayerInputDraft(layer, head[1], legendPath, diameter, spacing);
    error = null;
    return true;
  }

  private static bool TryParseBackground(string text, out int? diameter, out int? spacing)
  {
    diameter = null;
    spacing = null;
    string body = text.Trim().TrimStart('Ø', 'ø');
    var halves = body.Split('@', 2);
    if (halves.Length != 2
        || !int.TryParse(halves[0], out int parsedDiameter)
        || !int.TryParse(halves[1], out int parsedSpacing)
        || parsedDiameter <= 0
        || parsedSpacing <= 0)
      return false;

    diameter = parsedDiameter;
    spacing = parsedSpacing;
    return true;
  }
}

public sealed record LayerInputDraft(
    LayerKey Layer,
    string FilePath,
    string? LegendPath,
    int? BackgroundDiameterMm,
    int? BackgroundSpacingMm);
