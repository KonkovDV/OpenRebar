using OpenRebar.Domain.Models;
using OpenRebar.Domain.Ports;

namespace OpenRebar.Application.UseCases;

/// <summary>
/// Splits overlapping class regions of one layer into faces.
/// Each face keeps the class with the greater required area, so the overlap is reinforced once.
/// </summary>
public static class LayerClassOverlay
{
  public const string OverlapWarning = "ClassOverlap";
  private const double AreaEpsilonMm2 = 1;

  public static OverlayResult Apply(IReadOnlyList<ReinforcementZone> zones, IPlanarGeometry geometry)
  {
    var output = new List<ReinforcementZone>();
    var warnings = new List<string>();

    foreach (var group in zones.GroupBy(LayerGroup))
    {
      var members = group.ToList();
      if (!members.Any(left => members.Any(right => !ReferenceEquals(left, right) && BoxesOverlap(left, right))))
      {
        output.AddRange(members);
        continue;
      }

      var faces = Arrange(members, geometry);
      var overlapped = faces.Where(face => face.Sources.Count > 1).SelectMany(face => face.Sources).Distinct().ToList();
      if (overlapped.Count > 0)
        warnings.Add($"{OverlapWarning}: {string.Join(", ", overlapped)}");

      output.AddRange(faces.SelectMany(ToZones));
    }

    return new OverlayResult(output, warnings);
  }

  private static List<Face> Arrange(IReadOnlyList<ReinforcementZone> zones, IPlanarGeometry geometry)
  {
    var faces = new List<Face>();
    foreach (var zone in zones)
    {
      var incoming = new PlanarRegion([new PlanarPolygon(zone.Boundary, zone.Holes)]);
      var next = new List<Face>();
      foreach (var face in faces)
      {
        var overlap = geometry.Intersection(face.Region, incoming);
        var rest = geometry.Difference(face.Region, incoming);
        if (rest.Area > AreaEpsilonMm2)
          next.Add(face with { Region = rest });
        if (overlap.Area > AreaEpsilonMm2)
        {
          var winner = ClassAs(zone) > ClassAs(face.Winner) ? zone : face.Winner;
          next.Add(new Face(overlap, winner, [.. face.Sources, zone.Id]));
        }
      }

      var remainder = incoming;
      foreach (var face in faces)
        remainder = geometry.Difference(remainder, face.Region);
      if (remainder.Area > AreaEpsilonMm2)
        next.Add(new Face(remainder, zone, [zone.Id]));

      faces = next;
    }

    return faces;
  }

  private static IEnumerable<ReinforcementZone> ToZones(Face face)
  {
    int part = 0;
    string joined = string.Join("+", face.Sources);
    foreach (var polygon in face.Region.Polygons)
    {
      part++;
      bool many = face.Sources.Count > 1;
      yield return new ReinforcementZone
      {
        Id = face.Region.Polygons.Count == 1 ? joined : $"{joined}:{part}",
        Boundary = polygon.Shell,
        Holes = polygon.Holes,
        Spec = face.Winner.Spec,
        Direction = face.Winner.Direction,
        ZoneType = face.Winner.ZoneType,
        Layer = face.Winner.Layer,
        SourceLayerName = face.Winner.SourceLayerName,
        DirectionInferredFromGeometry = face.Winner.DirectionInferredFromGeometry,
        DesignLayer = face.Winner.DesignLayer,
        Role = face.Winner.Role,
        AsRequiredMm2PerM = many ? ClassAs(face.Winner) : face.Winner.AsRequiredMm2PerM,
        AsBackgroundMm2PerM = face.Winner.AsBackgroundMm2PerM,
        AsDeltaMm2PerM = many
            ? Math.Max(0, ClassAs(face.Winner) - face.Winner.AsBackgroundMm2PerM)
            : face.Winner.AsDeltaMm2PerM,
        SourceClassId = face.Winner.SourceClassId,
        SourceIds = face.Sources,
        BackgroundSpacingMm = face.Winner.BackgroundSpacingMm,
        GridOriginMm = face.Winner.GridOriginMm
      };
    }
  }

  private static double ClassAs(ReinforcementZone zone) =>
      zone.AsRequiredMm2PerM > 0 ? zone.AsRequiredMm2PerM : zone.Spec.AreaPerMeterMm2;

  private static string LayerGroup(ReinforcementZone zone) =>
      $"{zone.DesignLayer}|{zone.Role}|{zone.Layer}|{zone.Direction}";

  private static bool BoxesOverlap(ReinforcementZone left, ReinforcementZone right)
  {
    var a = left.Boundary.GetBoundingBox();
    var b = right.Boundary.GetBoundingBox();
    return a.Min.X < b.Max.X - 1e-6 && a.Max.X > b.Min.X + 1e-6
        && a.Min.Y < b.Max.Y - 1e-6 && a.Max.Y > b.Min.Y + 1e-6;
  }

  private sealed record Face(PlanarRegion Region, ReinforcementZone Winner, List<string> Sources);
}

public sealed record OverlayResult(IReadOnlyList<ReinforcementZone> Zones, IReadOnlyList<string> Warnings);
