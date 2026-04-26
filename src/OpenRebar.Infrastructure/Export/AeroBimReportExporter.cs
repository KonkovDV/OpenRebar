using System.Text.Encodings.Web;
using System.Text.Json;
using OpenRebar.Domain.Models;
using OpenRebar.Domain.Ports;
using OpenRebar.Domain.Rules;

namespace OpenRebar.Infrastructure.Export;

/// <summary>
/// Emits a downstream-friendly JSON report tailored for AeroBIM ingestion.
/// </summary>
public sealed class AeroBimReportExporter : IReportExporter
{
  private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
  {
    WriteIndented = true,
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
  };

  public async Task ExportAsync(
      ReinforcementExecutionReport report,
      IReadOnlyList<ReinforcementZone> zones,
      string outputPath,
      CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(outputPath))
      throw new ArgumentException("Output path is required.", nameof(outputPath));

    var directory = Path.GetDirectoryName(outputPath);
    if (!string.IsNullOrWhiteSpace(directory))
      Directory.CreateDirectory(directory);

    var payload = new Dictionary<string, object?>
    {
      ["$schema"] = "aerobim-OpenRebar-reinforcement-report/v1",
      ["project_id"] = report.Metadata.ProjectCode,
      ["slab_id"] = report.Metadata.SlabId,
      ["normative_profile_id"] = report.NormativeProfile.ProfileId,
      ["concrete_class"] = report.Slab.ConcreteClass,
      ["zones"] = zones.Select(zone => new
      {
        zone_id = zone.Id,
        boundary_mm = zone.Boundary.Vertices.Select(v => new[] { v.X, v.Y }).ToList(),
        diameter_mm = zone.Spec.DiameterMm,
        spacing_mm = zone.Spec.SpacingMm,
        steel_class = zone.Spec.SteelClass,
        direction = zone.Direction.ToString(),
        layer = zone.Layer.ToString(),
        anchorage_mm = zone.Rebars.FirstOrDefault()?.AnchorageLengthStart ?? 0,
        lap_splice_mm = AnchorageRules.CalculateLapLength(
              zone.Spec.DiameterMm,
              zone.Spec.SteelClass,
              report.Slab.ConcreteClass,
              condition: zone.Layer == RebarLayer.Top
                  ? AnchorageRules.BondCondition.Poor
                  : AnchorageRules.BondCondition.Good),
        rebar_count = zone.Rebars.Count,
        total_length_mm = zone.Rebars.Sum(r => r.TotalLength)
      }).ToList(),
      ["optimization"] = new
      {
        optimizer = report.AnalysisProvenance.Optimization.OptimizerId,
        cutting_plans = report.OptimizationByDiameter.SelectMany(group =>
            group.CuttingPlans.Select(plan => new
            {
              stock_mm = plan.StockLengthMm,
              cuts_mm = plan.CutsMm,
              waste_mm = plan.WasteMm,
              diameter_mm = group.DiameterMm
            })).ToList(),
        total_waste_percent = report.Summary.TotalWastePercent,
        total_mass_kg = report.Summary.TotalMassKg,
        total_stock_bars = report.OptimizationByDiameter.Sum(group => group.StockBarsNeeded)
      },
      ["normative_basis"] = report.Metadata.DesignCode,
      ["analysis_provenance"] = new
      {
        decomposition_algorithm = report.AnalysisProvenance.Geometry.DecompositionAlgorithm,
        demand_aggregation_precision_mm = report.AnalysisProvenance.Optimization.DemandAggregationPrecisionMm,
        optimizer_id = report.AnalysisProvenance.Optimization.OptimizerId
      }
    };

    var json = JsonSerializer.Serialize(payload, SerializerOptions);
    await File.WriteAllTextAsync(outputPath, json, ct);
  }
}
