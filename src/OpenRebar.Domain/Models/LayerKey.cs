namespace OpenRebar.Domain.Models;

/// <summary>
/// Reinforcement layer requested by the user or read from a source layer name.
/// </summary>
public enum LayerKey
{
  BottomX,
  BottomY,
  TopX,
  TopY
}

public static class LayerKeyParser
{
  public static bool TryParse(string? text, out LayerKey layer)
  {
    if (!string.IsNullOrWhiteSpace(text)
        && !int.TryParse(text, out _)
        && Enum.TryParse(text, ignoreCase: true, out layer)
        && Enum.IsDefined(layer))
      return true;

    layer = default;
    return false;
  }

  public static void Apply(ReinforcementZone zone, LayerKey layer)
  {
    zone.Layer = layer is LayerKey.TopX or LayerKey.TopY
        ? RebarLayer.Top
        : RebarLayer.Bottom;
    zone.Direction = layer is LayerKey.BottomY or LayerKey.TopY
        ? RebarDirection.Y
        : RebarDirection.X;
    zone.DirectionInferredFromGeometry = false;
  }
}
