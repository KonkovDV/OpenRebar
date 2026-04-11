using A101.Domain.Models;

namespace A101.Domain.Ports;

/// <summary>
/// Exports integration-facing reports derived from canonical reinforcement execution data.
/// </summary>
public interface IReportExporter
{
    Task ExportAsync(
        ReinforcementExecutionReport report,
        IReadOnlyList<ReinforcementZone> zones,
        string outputPath,
        CancellationToken ct = default);
}