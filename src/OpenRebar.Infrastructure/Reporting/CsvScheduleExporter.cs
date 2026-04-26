using System.Text;
using OpenRebar.Domain.Models;
using OpenRebar.Domain.Ports;
using OpenRebar.Domain.Rules;

namespace OpenRebar.Infrastructure.Reporting;

/// <summary>
/// Exports a semicolon-delimited reinforcement schedule suitable for Russian Excel defaults.
/// </summary>
public sealed class CsvScheduleExporter : IScheduleExporter
{
  public async Task ExportAsync(
      IReadOnlyList<ReinforcementZone> zones,
      string outputPath,
      CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(outputPath))
      throw new ArgumentException("Output path is required.", nameof(outputPath));

    var directory = Path.GetDirectoryName(outputPath);
    if (!string.IsNullOrWhiteSpace(directory))
      Directory.CreateDirectory(directory);

    var rows = zones
        .SelectMany(z => z.Rebars.Select(rebar => new { Zone = z, Rebar = rebar }))
        .GroupBy(item => new
        {
          item.Rebar.Mark,
          item.Rebar.DiameterMm,
          LengthMm = Math.Round(item.Rebar.TotalLength, 0),
          item.Zone.Spec.SteelClass
        })
        .OrderBy(group => group.Key.DiameterMm)
        .ThenBy(group => group.Key.LengthMm)
        .ThenBy(group => group.Key.Mark)
        .ToList();

    var builder = new StringBuilder();
    builder.AppendLine("Марка;Диаметр, мм;Длина, мм;Количество;Масса 1 шт, кг;Масса всего, кг;Класс стали");

    foreach (var group in rows)
    {
      var representative = group.First().Rebar;
      double lengthMm = group.Key.LengthMm;
      int quantity = group.Count();
      double massPerPiece = ReinforcementLimits.GetLinearMass(group.Key.DiameterMm) * (lengthMm / 1000.0);
      double totalMass = massPerPiece * quantity;

      builder.AppendLine(string.Join(";",
          group.Key.Mark,
          group.Key.DiameterMm,
          lengthMm,
          quantity,
          massPerPiece.ToString("F2"),
          totalMass.ToString("F2"),
          group.Key.SteelClass));
    }

    await File.WriteAllTextAsync(outputPath, builder.ToString(), Encoding.UTF8, ct);
  }
}
