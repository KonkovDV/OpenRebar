using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using OpenRebar.Domain.Models;
using OpenRebar.Domain.Rules;

namespace OpenRebar.Application.UseCases;

/// <summary>
/// Assigns schedule positions. The default key is steel class, diameter, shape, and
/// length rounded to 1 mm. Layer is used only to order the numbers.
/// </summary>
public static class PositionAssigner
{
  public static IReadOnlyList<PositionExecutionReport> Assign(IReadOnlyList<ReinforcementZone> zones)
  {
    var listed = new List<BarRef>();
    foreach (var zone in zones)
    {
      foreach (var rebar in zone.Rebars)
      {
        if (rebar.Status == BarInstanceStatus.Discarded)
          continue;

        listed.Add(new BarRef(zone, rebar, PositionKey.From(zone, rebar)));
      }
    }

    var groups = listed
        .GroupBy(item => item.Key)
        .OrderBy(group => group.Min(item => LayerSortKey(item.Zone)))
        .ThenBy(group => group.Key.DiameterMm)
        .ThenByDescending(group => group.Key.RoundedLengthMm)
        .ThenBy(group => group.Key.SteelClass, StringComparer.Ordinal)
        .ThenBy(group => group.Key.ShapeCode, StringComparer.Ordinal)
        .ToList();

    var markByKey = new Dictionary<PositionKey, string>();
    for (int i = 0; i < groups.Count; i++)
      markByKey[groups[i].Key] = (i + 1).ToString(CultureInfo.InvariantCulture);

    var usedIds = new HashSet<string>(StringComparer.Ordinal);
    foreach (var zone in zones)
    {
      zone.Rebars = zone.Rebars
          .Select(rebar =>
          {
            string barId = AllocateBarId(zone, rebar, usedIds);
            if (rebar.Status == BarInstanceStatus.Discarded)
              return rebar with { BarId = barId };

            var key = PositionKey.From(zone, rebar);
            return rebar with { BarId = barId, Mark = markByKey[key] };
          })
          .ToList();
    }

    return groups
        .Select(group =>
        {
          var key = group.Key;
          var items = group.ToList();
          var (massPerPiece, totalMassKg) = Masses(
              key.DiameterMm,
              items.Select(item => item.Rebar.TotalLength).ToList());
          string layer = string.Join(
              "+",
              group.Select(item => LayerLabel(item.Zone)).Distinct(StringComparer.Ordinal).OrderBy(label => label, StringComparer.Ordinal));
          return new PositionExecutionReport
          {
            Mark = markByKey[key],
            DiameterMm = key.DiameterMm,
            SteelClass = key.SteelClass,
            ShapeCode = key.ShapeCode,
            LengthMm = key.RoundedLengthMm,
            Quantity = items.Count,
            MassPerPieceKg = massPerPiece,
            TotalMassKg = totalMassKg,
            Layer = layer
          };
        })
        .ToList();
  }

  /// <summary>
  /// Mass from the exact cut lengths. Callers round only when they print a schedule.
  /// </summary>
  public static (double MassPerPieceKg, double TotalMassKg) Masses(
      int diameterMm,
      IReadOnlyCollection<double> totalLengthsMm)
  {
    double exactTotalLengthMm = 0;
    foreach (double lengthMm in totalLengthsMm)
      exactTotalLengthMm += lengthMm;

    double totalMassKg = ReinforcementLimits.GetLinearMass(diameterMm) * exactTotalLengthMm / 1000.0;
    double massPerPieceKg = totalLengthsMm.Count == 0 ? 0 : totalMassKg / totalLengthsMm.Count;
    return (massPerPieceKg, totalMassKg);
  }

  private static string AllocateBarId(ReinforcementZone zone, RebarSegment rebar, HashSet<string> usedIds)
  {
    string payload = string.Create(
        CultureInfo.InvariantCulture,
        $"{zone.Layer}|{zone.Direction}|{rebar.DiameterMm}|{rebar.Start.X:F3}|{rebar.Start.Y:F3}|{rebar.End.X:F3}|{rebar.End.Y:F3}");
    string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)))[..16].ToLowerInvariant();
    string barId = hash;
    int suffix = 2;
    while (!usedIds.Add(barId))
    {
      barId = $"{hash}-{suffix}";
      suffix++;
    }

    return barId;
  }

  private static int LayerSortKey(ReinforcementZone zone)
  {
    int face = zone.Layer == RebarLayer.Top ? 2 : 0;
    int axis = zone.Direction == RebarDirection.Y ? 1 : 0;
    return face + axis;
  }

  private static string LayerLabel(ReinforcementZone zone) => $"{zone.Layer}{zone.Direction}";

  private readonly record struct PositionKey(string SteelClass, int DiameterMm, string ShapeCode, int RoundedLengthMm)
  {
    public static PositionKey From(ReinforcementZone zone, RebarSegment rebar)
    {
      string shape = string.IsNullOrWhiteSpace(rebar.ShapeCode) ? "00" : rebar.ShapeCode;
      int length = (int)Math.Round(rebar.TotalLength, MidpointRounding.AwayFromZero);
      return new PositionKey(zone.Spec.SteelClass, rebar.DiameterMm, shape, length);
    }
  }

  private readonly record struct BarRef(ReinforcementZone Zone, RebarSegment Rebar, PositionKey Key);
}
