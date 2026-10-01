using OpenRebar.Domain.Exceptions;
using OpenRebar.Domain.Models;
using OpenRebar.Domain.Rules;

namespace OpenRebar.Application.UseCases;

/// <summary>
/// Builds rectangular reinforcement zones from a numeric As field.
/// A group that is not one rectangle stays split, one element per zone.
/// </summary>
public static class AsFieldZoneBuilder
{
  private static readonly LayerKey[] Layers = [LayerKey.BottomX, LayerKey.BottomY, LayerKey.TopX, LayerKey.TopY];
  private static readonly int[] DefaultDiameters = [10, 12, 14, 16, 18, 20, 22, 25];

  public const string SplitWarning =
      "Field regions that do not form one rectangle stay split into element rectangles.";

  public static AsFieldLayout Build(AsField field, SlabGeometry slab, AsFieldLayoutOptions? options = null)
  {
    options ??= new AsFieldLayoutOptions();
    var diameters = options.DiametersMm is { Count: > 0 } ? options.DiametersMm : DefaultDiameters;
    var spacings = options.SpacingsMm is { Count: > 0 }
        ? options.SpacingsMm
        : NormativeProfiles.Sp63_2018.StandardSpacingsMm;
    var slabBox = slab.OuterBoundary.GetBoundingBox();
    var zones = new List<ReinforcementZone>();
    var warnings = new List<string>();
    bool split = false;

    foreach (var layer in Layers)
    {
      var members = field.Elements
          .Where(element => element.AsMm2PerM.TryGetValue(layer.ToString(), out double asRequired) && asRequired > 1e-6)
          .ToList();
      if (members.Count == 0)
        continue;

      foreach (var element in members)
      {
        if (element.MinX < slabBox.Min.X - 1e-6
            || element.MinY < slabBox.Min.Y - 1e-6
            || element.MaxX > slabBox.Max.X + 1e-6
            || element.MaxY > slabBox.Max.Y + 1e-6)
        {
          throw new AsFieldReadException(
              "FIELD_OUTSIDE",
              $"Element '{element.Id}' is outside the slab.");
        }
      }

      var grouped = members.GroupBy(element => StepKey(element, layer, diameters, spacings));
      foreach (var group in grouped)
      {
        var chosen = group.Key;
        var blocks = Partition(group.ToList(), ref split);
        foreach (var block in blocks)
        {
          double required = block.Max(element => element.AsMm2PerM[layer.ToString()]);
          double half = block.Min(element => Math.Min(element.MaxX - element.MinX, element.MaxY - element.MinY)) / 2.0;
          double minX = Math.Max(slabBox.Min.X, block.Min(element => element.MinX) - half);
          double minY = Math.Max(slabBox.Min.Y, block.Min(element => element.MinY) - half);
          double maxX = Math.Min(slabBox.Max.X, block.Max(element => element.MaxX) + half);
          double maxY = Math.Min(slabBox.Max.Y, block.Max(element => element.MaxY) + half);
          var zone = new ReinforcementZone
          {
            Id = $"{layer}-{zones.Count + 1}",
            Boundary = Rectangle(minX, minY, maxX, maxY),
            Spec = new ReinforcementSpec
            {
              DiameterMm = chosen.DiameterMm,
              SpacingMm = chosen.SpacingMm,
              SteelClass = options.SteelClass
            },
            Direction = RebarDirection.X,
            ZoneType = ZoneType.Simple,
            SourceLayerName = layer.ToString(),
            DesignLayer = layer,
            Role = ZoneRole.Additional,
            AsRequiredMm2PerM = required,
            AsDeltaMm2PerM = required,
            SourceClassId = $"Ø{chosen.DiameterMm}@{chosen.SpacingMm}"
          };
          LayerKeyParser.Apply(zone, layer);
          zones.Add(zone);
        }
      }
    }

    if (split)
      warnings.Add(SplitWarning);

    return new AsFieldLayout(zones, warnings);
  }

  private static Step StepKey(
      AsFieldElement element,
      LayerKey layer,
      IReadOnlyList<int> diameters,
      IReadOnlyList<int> spacings)
  {
    double required = element.AsMm2PerM[layer.ToString()];
    Step? best = null;
    double bestMass = double.PositiveInfinity;
    foreach (int spacing in spacings)
    {
      foreach (int diameter in diameters.Distinct().Order())
      {
        if (diameter <= 0 || spacing <= 0)
          continue;

        double provided = Math.PI * diameter * diameter / 4.0 * (1000.0 / spacing);
        if (provided + 1e-6 < required)
          continue;

        double gap = spacing - diameter / 2.0;
        if (gap + 1e-6 < Math.Max(diameter, 25))
          continue;

        double mass = ReinforcementLimits.GetLinearMass(diameter) * (1000.0 / spacing);
        if (mass < bestMass - 1e-9)
        {
          bestMass = mass;
          best = new Step(diameter, spacing);
        }
      }
    }

    if (best is null)
    {
      throw new AsFieldReadException(
          "FIELD_UNCOVERED",
          $"No allowed bar covers {layer} at {required:0.###} mm²/m.");
    }

    return best;
  }

  private static List<List<AsFieldElement>> Partition(List<AsFieldElement> elements, ref bool split)
  {
    if (elements.Count == 1 || TilesRectangle(elements))
      return [elements];

    split = true;
    return elements.Select(element => new List<AsFieldElement> { element }).ToList();
  }

  private static bool TilesRectangle(IReadOnlyList<AsFieldElement> elements)
  {
    double minX = elements.Min(element => element.MinX);
    double minY = elements.Min(element => element.MinY);
    double maxX = elements.Max(element => element.MaxX);
    double maxY = elements.Max(element => element.MaxY);
    double box = (maxX - minX) * (maxY - minY);
    double sum = elements.Sum(element => (element.MaxX - element.MinX) * (element.MaxY - element.MinY));
    return Math.Abs(box - sum) <= 1.0;
  }

  private static Polygon Rectangle(double minX, double minY, double maxX, double maxY) => new(
  [
      new Point2D(minX, minY),
      new Point2D(maxX, minY),
      new Point2D(maxX, maxY),
      new Point2D(minX, maxY)
  ]);

  private sealed record Step(int DiameterMm, int SpacingMm);
}

public sealed record AsFieldLayoutOptions
{
  public string SteelClass { get; init; } = "A500C";
  public IReadOnlyList<int> DiametersMm { get; init; } = [];
  public IReadOnlyList<int> SpacingsMm { get; init; } = [];
}

public sealed record AsFieldLayout(
    IReadOnlyList<ReinforcementZone> Zones,
    IReadOnlyList<string> Warnings);
