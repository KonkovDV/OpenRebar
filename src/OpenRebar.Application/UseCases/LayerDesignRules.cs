using OpenRebar.Domain.Models;

namespace OpenRebar.Application.UseCases;

/// <summary>
/// Checks a multi-layer design before any drawing is read.
/// </summary>
public static class LayerDesignRules
{
  public static string? Validate(IReadOnlyList<LayerDesignInput> layers)
  {
    if (layers.Count is < 1 or > LayerDesignInput.MaxLayers)
      return $"A design has 1 to {LayerDesignInput.MaxLayers} layers. This one has {layers.Count}.";

    var seen = new HashSet<LayerKey>();
    foreach (var layer in layers)
    {
      if (string.IsNullOrWhiteSpace(layer.IsolineFilePath))
        return $"Layer {layer.Layer} has no drawing path.";
      if (!seen.Add(layer.Layer))
        return $"Layer {layer.Layer} is listed more than once.";
    }

    return null;
  }
}
