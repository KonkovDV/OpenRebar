using System.Globalization;
using System.Text;
using OpenRebar.Domain.Models;
using OpenRebar.Domain.Ports;
using OpenRebar.Domain.Rules;

namespace OpenRebar.Infrastructure.Reporting;

/// <summary>
/// Exports a semicolon-delimited specification and a steel-mass sheet.
/// A position name is Ø20 A500C l = 6565. The designation column stays empty.
/// Rows are positions, ordered by the numeric mark.
/// </summary>
public sealed class CsvScheduleExporter : IScheduleExporter
{
  public Task ExportAsync(
      IReadOnlyList<ReinforcementZone> zones,
      string outputPath,
      CancellationToken ct = default)
  {
    return ExportAsync(zones, outputPath, ScheduleNumberCulture.Ru, ct);
  }

  public async Task ExportAsync(
      IReadOnlyList<ReinforcementZone> zones,
      string outputPath,
      ScheduleNumberCulture numberCulture,
      CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(outputPath))
      throw new ArgumentException("Output path is required.", nameof(outputPath));

    var directory = Path.GetDirectoryName(outputPath);
    if (!string.IsNullOrWhiteSpace(directory))
      Directory.CreateDirectory(directory);

    CultureInfo culture = numberCulture == ScheduleNumberCulture.Invariant
        ? CultureInfo.InvariantCulture
        : CultureInfo.GetCultureInfo("ru-RU");

    var rows = zones
        .SelectMany(zone => zone.Rebars
            .Where(rebar => rebar.Status != BarInstanceStatus.Discarded)
            .Select(rebar => new ScheduleRow(zone, rebar)))
        .ToList();

    var builder = new StringBuilder();
    const string header = "Позиция;Обозначение;Наименование;Кол.;Масса ед., кг;Примечание";
    foreach (string layer in LayerOrder)
    {
      builder.AppendLine($"[{layer}]");
      var layerRows = rows
          .Where(row => LayerLabel(row.Zone) == layer)
          .GroupBy(row => row.Rebar.Mark ?? "")
          .Select(group =>
          {
            var first = group.First();
            return new ScheduleGroup(
                group.Key,
                first.Rebar.DiameterMm,
                (int)Math.Round(first.Rebar.TotalLength, MidpointRounding.AwayFromZero),
                first.Zone.Spec.SteelClass,
                string.IsNullOrWhiteSpace(first.Rebar.ShapeCode) ? "00" : first.Rebar.ShapeCode,
                group.Count());
          })
          .OrderBy(group => int.TryParse(group.Mark, NumberStyles.Integer, CultureInfo.InvariantCulture, out int mark) ? mark : int.MaxValue)
          .ThenBy(group => group.Mark, StringComparer.Ordinal)
          .ToList();

      if (layerRows.Count == 0)
      {
        builder.AppendLine("status;NotProvided");
        continue;
      }

      builder.AppendLine(header);
      foreach (var group in layerRows)
      {
        double massPerPiece = ReinforcementLimits.GetLinearMass(group.DiameterMm) * (group.LengthMm / 1000.0);
        builder.AppendLine(string.Join(";",
            group.Mark,
            "",
            $"Ø{group.DiameterMm} {group.SteelClass} l = {group.LengthMm}",
            group.Quantity.ToString(CultureInfo.InvariantCulture),
            massPerPiece.ToString("F2", culture),
            $"форма {group.ShapeCode}"));
      }
    }

    var steelTotals = rows
        .GroupBy(row => (Steel: row.Zone.Spec.SteelClass, Diameter: row.Rebar.DiameterMm))
        .Select(group =>
        {
          double mass = group.Sum(row =>
          {
            int length = (int)Math.Round(row.Rebar.TotalLength, MidpointRounding.AwayFromZero);
            return ReinforcementLimits.GetLinearMass(row.Rebar.DiameterMm) * (length / 1000.0);
          });
          return (group.Key.Steel, group.Key.Diameter, Mass: mass);
        })
        .OrderBy(item => item.Steel, StringComparer.Ordinal)
        .ThenBy(item => item.Diameter)
        .ToList();
    builder.AppendLine("[Расход стали]");
    builder.AppendLine("Класс стали;Диаметр, мм;Масса, кг");
    foreach (var item in steelTotals)
    {
      builder.AppendLine(string.Join(";",
          item.Steel,
          item.Diameter.ToString(CultureInfo.InvariantCulture),
          item.Mass.ToString("F2", culture)));
    }

    await File.WriteAllTextAsync(outputPath, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), ct);
  }

  private static readonly string[] LayerOrder = ["BottomX", "BottomY", "TopX", "TopY"];

  private static string LayerLabel(ReinforcementZone zone) =>
      zone.DesignLayer?.ToString() ?? $"{zone.Layer}{zone.Direction}";

  private sealed record ScheduleRow(ReinforcementZone Zone, RebarSegment Rebar);

  private sealed record ScheduleGroup(
      string Mark,
      int DiameterMm,
      int LengthMm,
      string SteelClass,
      string ShapeCode,
      int Quantity);
}
