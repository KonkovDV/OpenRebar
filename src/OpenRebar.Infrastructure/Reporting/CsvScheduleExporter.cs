using System.Globalization;
using System.Text;
using OpenRebar.Domain.Models;
using OpenRebar.Domain.Ports;
using OpenRebar.Domain.Rules;

namespace OpenRebar.Infrastructure.Reporting;

/// <summary>
/// Exports a semicolon-delimited reinforcement schedule suitable for Russian Excel defaults.
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
    const string header = "Марка;Диаметр, мм;Длина, мм;Количество;Масса 1 шт, кг;Масса всего, кг;Класс стали;Слой;Форма;Масса всего по диаметру";
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
                layer,
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

      var massByDiameter = layerRows
          .GroupBy(group => group.DiameterMm)
          .ToDictionary(
              group => group.Key,
              group => group.Sum(item =>
                  ReinforcementLimits.GetLinearMass(item.DiameterMm) * (item.LengthMm / 1000.0) * item.Quantity));

      builder.AppendLine(header);
      foreach (var group in layerRows)
      {
        double massPerPiece = ReinforcementLimits.GetLinearMass(group.DiameterMm) * (group.LengthMm / 1000.0);
        double totalMass = massPerPiece * group.Quantity;
        builder.AppendLine(string.Join(";",
            group.Mark,
            group.DiameterMm.ToString(CultureInfo.InvariantCulture),
            group.LengthMm.ToString(CultureInfo.InvariantCulture),
            group.Quantity.ToString(CultureInfo.InvariantCulture),
            massPerPiece.ToString("F2", culture),
            totalMass.ToString("F2", culture),
            group.SteelClass,
            group.Layer,
            group.ShapeCode,
            massByDiameter[group.DiameterMm].ToString("F2", culture)));
      }
    }

    var diameterTotals = rows
        .GroupBy(row => row.Rebar.DiameterMm)
        .Select(group =>
        {
          double mass = group.Sum(row =>
          {
            int length = (int)Math.Round(row.Rebar.TotalLength, MidpointRounding.AwayFromZero);
            return ReinforcementLimits.GetLinearMass(row.Rebar.DiameterMm) * (length / 1000.0);
          });
          return (Diameter: group.Key, Mass: mass);
        })
        .OrderBy(item => item.Diameter)
        .ToList();
    builder.AppendLine("[Диаметры]");
    builder.AppendLine("Диаметр, мм;Масса, кг");
    foreach (var item in diameterTotals)
    {
      builder.AppendLine(string.Join(";",
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
      string Layer,
      int Quantity);
}
