using OpenRebar.Domain.Models;

namespace OpenRebar.Domain.Rules;

/// <summary>
/// Distance from the far face of the slab to the axis of each layer that has bars, mm.
/// Bottom layers use <c>h − z</c>. Top layers use <c>z</c>. <c>z</c> is the axis
/// elevation from <see cref="LayerElevations"/>. The report has one cover, so the
/// top and the bottom use it. A layer with no bar is left out.
/// </summary>
public static class EffectiveDepths
{
  private static readonly LayerKey[] Order =
  [
      LayerKey.BottomX,
      LayerKey.BottomY,
      LayerKey.TopX,
      LayerKey.TopY
  ];

  public static IReadOnlyDictionary<string, double> ByLayer(
      SlabGeometry slab,
      IEnumerable<ReinforcementZone> zones)
  {
    var diameters = new Dictionary<LayerKey, int>();
    foreach (var zone in zones)
    {
      if (zone.Rebars.Count == 0)
        continue;

      var key = zone.DesignLayer ?? Key(zone.Layer, zone.Direction);
      int diameter = zone.Rebars.Max(bar => bar.DiameterMm);
      diameters[key] = diameters.TryGetValue(key, out int placed) ? Math.Max(placed, diameter) : diameter;
    }

    var depths = new Dictionary<string, double>();
    foreach (var key in Order)
    {
      if (!diameters.ContainsKey(key))
        continue;

      depths[key.ToString()] = Depth(slab, key, diameters);
    }

    return depths;
  }

  private static double Depth(SlabGeometry slab, LayerKey key, IReadOnlyDictionary<LayerKey, int> diameters)
  {
    var (face, direction) = Face(key);
    double elevation = LayerElevations.AxisElevationMm(
        face,
        direction,
        slab.ThicknessMm,
        slab.CoverMm,
        slab.CoverMm,
        Diameter(diameters, face, RebarDirection.X),
        Diameter(diameters, face, RebarDirection.Y));
    return face == RebarLayer.Bottom ? slab.ThicknessMm - elevation : elevation;
  }

  private static double Diameter(IReadOnlyDictionary<LayerKey, int> diameters, RebarLayer face, RebarDirection direction) =>
      diameters.TryGetValue(Key(face, direction), out int diameter) ? diameter : 0;

  private static (RebarLayer Face, RebarDirection Direction) Face(LayerKey key) => key switch
  {
    LayerKey.BottomX => (RebarLayer.Bottom, RebarDirection.X),
    LayerKey.BottomY => (RebarLayer.Bottom, RebarDirection.Y),
    LayerKey.TopX => (RebarLayer.Top, RebarDirection.X),
    _ => (RebarLayer.Top, RebarDirection.Y)
  };

  private static LayerKey Key(RebarLayer face, RebarDirection direction) => (face, direction) switch
  {
    (RebarLayer.Bottom, RebarDirection.X) => LayerKey.BottomX,
    (RebarLayer.Bottom, RebarDirection.Y) => LayerKey.BottomY,
    (RebarLayer.Top, RebarDirection.X) => LayerKey.TopX,
    _ => LayerKey.TopY
  };
}
