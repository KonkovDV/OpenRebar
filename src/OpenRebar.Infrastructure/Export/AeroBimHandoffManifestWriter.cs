using System.Text.Encodings.Web;
using System.Text.Json;
using OpenRebar.Domain.Models;
using OpenRebar.Domain.Ports;

namespace OpenRebar.Infrastructure.Export;

/// <summary>
/// Copies the canonical reinforcement report into an AeroBIM storage root and writes
/// a small handoff manifest that AeroBIM can consume without manual path rewriting.
/// </summary>
public sealed class AeroBimHandoffManifestWriter
{
  private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
  {
    WriteIndented = true,
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
  };

  public async Task<AeroBimHandoffArtifact> WriteAsync(
      ReinforcementExecutionReport report,
      StoredReportReference storedReport,
      string storageDir,
      CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(storageDir))
      throw new ArgumentException("AeroBIM storage directory is required.", nameof(storageDir));

    if (string.IsNullOrWhiteSpace(storedReport.OutputPath))
      throw new ArgumentException("Stored report output path is required.", nameof(storedReport));

    string sourceReportPath = Path.GetFullPath(storedReport.OutputPath);
    if (!File.Exists(sourceReportPath))
      throw new FileNotFoundException("Stored reinforcement report was not found.", sourceReportPath);

    string storageRoot = Path.GetFullPath(storageDir);
    Directory.CreateDirectory(storageRoot);

    string integrationsDir = Path.Combine(storageRoot, "integrations", "openrebar");
    Directory.CreateDirectory(integrationsDir);

    string targetReportPath = Path.Combine(integrationsDir, Path.GetFileName(sourceReportPath));
    if (!string.Equals(sourceReportPath, Path.GetFullPath(targetReportPath), StringComparison.OrdinalIgnoreCase))
      File.Copy(sourceReportPath, targetReportPath, overwrite: true);

    string manifestFileName = $"{Path.GetFileNameWithoutExtension(targetReportPath)}.handoff.json";
    string manifestPath = Path.Combine(integrationsDir, manifestFileName);

    string relativeReportPath = NormalizeRelativePath(storageRoot, targetReportPath);
    string relativeManifestPath = NormalizeRelativePath(storageRoot, manifestPath);

    var payload = new Dictionary<string, object?>
    {
      ["handoff_type"] = "openrebar-aerobim-report-handoff.v1",
      ["schema_version"] = "1.0.0",
      ["generated_at_utc"] = DateTimeOffset.UtcNow.ToString("O"),
      ["reinforcement_report_path"] = relativeReportPath,
      ["report_sha256"] = storedReport.Sha256,
      ["contract_id"] = report.ContractId,
      ["report_schema_version"] = report.SchemaVersion,
      ["project_code"] = report.Metadata.ProjectCode,
      ["slab_id"] = report.Metadata.SlabId,
    };

    string json = JsonSerializer.Serialize(payload, SerializerOptions);
    await File.WriteAllTextAsync(manifestPath, json, ct);

    return new AeroBimHandoffArtifact
    {
      ManifestPath = Path.GetFullPath(manifestPath),
      RelativeManifestPath = relativeManifestPath,
      ReinforcementReportPath = Path.GetFullPath(targetReportPath),
      RelativeReinforcementReportPath = relativeReportPath,
    };
  }

  private static string NormalizeRelativePath(string storageRoot, string targetPath)
  {
    return Path.GetRelativePath(storageRoot, targetPath).Replace('\\', '/');
  }
}

public sealed record AeroBimHandoffArtifact
{
  public required string ManifestPath { get; init; }
  public required string RelativeManifestPath { get; init; }
  public required string ReinforcementReportPath { get; init; }
  public required string RelativeReinforcementReportPath { get; init; }
}
