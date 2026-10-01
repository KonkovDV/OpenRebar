using System.Text.RegularExpressions;
using OpenRebar.Domain.Models;

namespace OpenRebar.Application.UseCases;

/// <summary>
/// Maps a CAD layer name onto a reinforcement layer. Explicit --layer wins over this map.
/// </summary>
public static partial class ReinforcementLayerMapper
{
  public static bool TryMap(string? sourceLayerName, out LayerKey layer)
  {
    if (LayerKeyParser.TryParse(sourceLayerName, out layer))
      return true;

    if (string.IsNullOrWhiteSpace(sourceLayerName))
    {
      layer = default;
      return false;
    }

    foreach (var rule in Rules)
    {
      if (rule.Pattern.IsMatch(sourceLayerName))
      {
        layer = rule.Layer;
        return true;
      }
    }

    layer = default;
    return false;
  }

  private static readonly (Regex Pattern, LayerKey Layer)[] Rules =
  [
      (BottomXPattern(), LayerKey.BottomX),
      (BottomYPattern(), LayerKey.BottomY),
      (TopXPattern(), LayerKey.TopX),
      (TopYPattern(), LayerKey.TopY)
  ];

  [GeneratedRegex(@"(?i)(bottom|bot|низ).{0,16}x", RegexOptions.CultureInvariant)]
  private static partial Regex BottomXPattern();

  [GeneratedRegex(@"(?i)(bottom|bot|низ).{0,16}y", RegexOptions.CultureInvariant)]
  private static partial Regex BottomYPattern();

  [GeneratedRegex(@"(?i)(top|верх).{0,16}x", RegexOptions.CultureInvariant)]
  private static partial Regex TopXPattern();

  [GeneratedRegex(@"(?i)(top|верх).{0,16}y", RegexOptions.CultureInvariant)]
  private static partial Regex TopYPattern();
}
