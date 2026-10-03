using System.Security.Cryptography;
using OpenRebar.Domain;
using OpenRebar.Domain.Models;

namespace OpenRebar.Application.UseCases;

/// <summary>
/// Fills the four layer slots and the profile / input stamps on a 2.0 report.
/// </summary>
public static class LayerReportBuilder
{
  private static readonly string[] LayerOrder = ["BottomX", "BottomY", "TopX", "TopY"];

  public static IReadOnlyList<LayerExecutionReport> Build(
      PipelineInput input,
      IReadOnlyList<ReinforcementZone> zones)
  {
    return LayerOrder
        .Select(name => BuildLayer(name, input, zones))
        .ToList();
  }

  public static ProfileStamp ProfileFrom(PipelineInput input)
  {
    if (string.IsNullOrWhiteSpace(input.CompanyProfilePath) || !File.Exists(input.CompanyProfilePath))
      return ProfileStamp.Unspecified;

    string id = string.IsNullOrWhiteSpace(input.CompanyProfileId)
        ? Path.GetFileNameWithoutExtension(input.CompanyProfilePath)
        : input.CompanyProfileId;
    string version = string.IsNullOrWhiteSpace(input.CompanyProfileVersion)
        ? "0"
        : input.CompanyProfileVersion;
    return new ProfileStamp(id, version, Sha256(input.CompanyProfilePath));
  }

  public static InputSourceStamp InputFrom(PipelineInput input)
  {
    string adapter = input.Layers.Count > 1
        ? "multi-layer"
        : Path.GetExtension(input.IsolineFilePath).TrimStart('.').ToLowerInvariant();
    if (string.IsNullOrWhiteSpace(adapter))
      adapter = "unknown";

    string? hash = File.Exists(input.IsolineFilePath) ? Sha256(input.IsolineFilePath) : null;
    return new InputSourceStamp(adapter, ProductVersion.Current, hash);
  }

  private static LayerExecutionReport BuildLayer(
      string name,
      PipelineInput input,
      IReadOnlyList<ReinforcementZone> zones)
  {
    var members = zones.Where(zone => LayerName(zone) == name).ToList();
    var background = input.Layers.FirstOrDefault(layer => layer.Layer.ToString() == name)?.Background;
    if (members.Count == 0 && background is null)
    {
      return new LayerExecutionReport
      {
        Layer = name,
        Status = "NotProvided"
      };
    }

    var positions = members
        .SelectMany(zone => zone.Rebars
            .Where(rebar => rebar.Status != BarInstanceStatus.Discarded)
            .Select(rebar => (Zone: zone, Rebar: rebar)))
        .GroupBy(item => item.Rebar.Mark ?? "")
        .Select(group =>
        {
          var bars = group.ToList();
          var first = bars[0];
          int length = (int)Math.Round(first.Rebar.TotalLength, MidpointRounding.AwayFromZero);
          var (piece, total) = PositionAssigner.Masses(
              first.Rebar.DiameterMm,
              bars.Select(item => item.Rebar.TotalLength).ToList());
          return new PositionExecutionReport
          {
            Mark = group.Key,
            DiameterMm = first.Rebar.DiameterMm,
            SteelClass = first.Zone.Spec.SteelClass,
            ShapeCode = string.IsNullOrWhiteSpace(first.Rebar.ShapeCode) ? "00" : first.Rebar.ShapeCode,
            LengthMm = length,
            Quantity = bars.Count,
            MassPerPieceKg = piece,
            TotalMassKg = total,
            Layer = name
          };
        })
        .OrderBy(position => int.TryParse(position.Mark, out int mark) ? mark : int.MaxValue)
        .ToList();

    return new LayerExecutionReport
    {
      Layer = name,
      Status = "Provided",
      Background = background is null
          ? null
          : new LayerBackgroundReport
          {
            DiameterMm = background.Spec.DiameterMm,
            SpacingMm = background.Spec.SpacingMm,
            SteelClass = background.Spec.SteelClass,
            GridOriginMm = background.GridOriginMm
          },
      Classes = members
          .Select(zone => zone.SourceClassId ?? $"{zone.Spec.SteelClass} Ø{zone.Spec.DiameterMm}@{zone.Spec.SpacingMm}")
          .Distinct(StringComparer.Ordinal)
          .OrderBy(value => value, StringComparer.Ordinal)
          .ToList(),
      ZoneIds = members.Select(zone => zone.Id).ToList(),
      Positions = positions,
      MassKg = positions.Sum(position => position.TotalMassKg)
    };
  }

  private static string LayerName(ReinforcementZone zone) =>
      zone.DesignLayer?.ToString() ?? $"{zone.Layer}{zone.Direction}";

  private static string Sha256(string path)
  {
    using var stream = File.OpenRead(path);
    return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
  }
}
