using System.Globalization;
using OpenRebar.Domain.Models;
using OpenRebar.Domain.Ports;
using OpenRebar.Domain.Rules;

namespace OpenRebar.Application.UseCases;

/// <summary>
/// Lays the layer mesh and the additional bars selected above it. No mesh leaves the zones unchanged.
/// </summary>
public static class BackgroundLayoutPlanner
{
  public static BackgroundLayoutPlan Apply(
      IReadOnlyList<ReinforcementZone> zones,
      IReadOnlyList<LayerDesignInput> layers,
      SlabGeometry slab,
      IReadOnlyList<int> allowedDiametersMm,
      string spacingMode)
  {
    var backgrounds = layers
        .Where(layer => layer.Background is not null)
        .ToDictionary(layer => layer.Layer, layer => layer.Background!);
    if (backgrounds.Count == 0)
      return new BackgroundLayoutPlan(zones, [], false);

    var selector = new AdditionalBarSelector();
    var planned = new List<ReinforcementZone>(zones);
    var warnings = new List<string>();
    bool needsHuman = false;
    var halfSpacingWarned = new HashSet<LayerKey>();

    foreach (var zone in planned)
    {
      if (zone.DesignLayer is not LayerKey layer || !backgrounds.TryGetValue(layer, out var background))
        continue;

      double backgroundArea = background.Spec.AreaPerMeterMm2;
      double required = zone.AsRequiredMm2PerM > 0
          ? zone.AsRequiredMm2PerM
          : zone.Spec.AreaPerMeterMm2;
      zone.AsRequiredMm2PerM = required;
      zone.AsBackgroundMm2PerM = backgroundArea;
      zone.AsDeltaMm2PerM = Math.Max(0, required - backgroundArea);
      zone.GridOriginMm = background.GridOriginMm;

      if (zone.AsDeltaMm2PerM <= 1e-6)
      {
        zone.Role = ZoneRole.Background;
        zone.SuppressLayout = true;
        continue;
      }

      if (IsHalfSpacing(spacingMode))
      {
        zone.Role = ZoneRole.Additional;
        zone.SuppressLayout = true;
        needsHuman = true;
        if (halfSpacingWarned.Add(layer))
        {
          warnings.Add(
              $"Layer {layer} needs a human decision: spacing mode s/2 is not applied automatically.");
        }

        continue;
      }

      if (IsExplicit(spacingMode))
      {
        zone.Role = ZoneRole.Additional;
        zone.SuppressLayout = true;
        needsHuman = true;
        warnings.Add(
            $"Zone {zone.Id} on {layer} needs a human decision: spacing mode explicit has no spacing value.");
        continue;
      }

      var selection = selector.Select(new AdditionalBarRequest(
          zone.AsDeltaMm2PerM,
          background,
          allowedDiametersMm,
          new AdditionalSpacingPolicy(AdditionalSpacingMode.InterleaveSameAsBackground)));
      if (selection.Status != AdditionalBarSelector.Selected
          || selection.DiameterMm is not int diameter
          || selection.SpacingMm is not int spacing)
      {
        zone.Role = ZoneRole.Additional;
        zone.SuppressLayout = true;
        needsHuman = true;
        warnings.Add(
            $"Zone {zone.Id} on {layer} needs a human decision: no allowed diameter covers {zone.AsDeltaMm2PerM.ToString("0.#", CultureInfo.InvariantCulture)} mm²/m above the background mesh.");
        continue;
      }

      zone.Role = ZoneRole.Additional;
      zone.SuppressLayout = false;
      zone.LayoutSpec = zone.Spec with { DiameterMm = diameter, SpacingMm = spacing };
      zone.BackgroundSpacingMm = background.Spec.SpacingMm;
      zone.GridMode = LayerGridMode.Interleave;
    }

    foreach (var pair in backgrounds.OrderBy(pair => pair.Key.ToString(), StringComparer.Ordinal))
      planned.Add(MeshZone(pair.Key, pair.Value, slab));

    return new BackgroundLayoutPlan(planned, warnings, needsHuman);
  }

  private static bool IsHalfSpacing(string spacingMode) =>
      string.Equals(spacingMode, "s/2", StringComparison.OrdinalIgnoreCase);

  private static bool IsExplicit(string spacingMode) =>
      string.Equals(spacingMode, "explicit", StringComparison.OrdinalIgnoreCase);

  private static ReinforcementZone MeshZone(LayerKey layer, BackgroundMesh background, SlabGeometry slab)
  {
    var zone = new ReinforcementZone
    {
      Id = $"{layer}-background",
      Boundary = slab.OuterBoundary,
      Holes = slab.Openings,
      Spec = background.Spec,
      Direction = RebarDirection.X,
      ZoneType = ZoneType.Simple,
      DesignLayer = layer,
      Role = ZoneRole.Background,
      IsBackgroundMesh = true,
      GridMode = LayerGridMode.Mesh,
      GridOriginMm = background.GridOriginMm,
      AsRequiredMm2PerM = background.Spec.AreaPerMeterMm2,
      AsBackgroundMm2PerM = background.Spec.AreaPerMeterMm2
    };
    LayerKeyParser.Apply(zone, layer);
    return zone;
  }
}

public sealed record BackgroundLayoutPlan(
    IReadOnlyList<ReinforcementZone> Zones,
    IReadOnlyList<string> Warnings,
    bool NeedsHumanDecision);
