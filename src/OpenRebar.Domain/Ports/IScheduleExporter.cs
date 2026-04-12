using OpenRebar.Domain.Models;

namespace OpenRebar.Domain.Ports;

/// <summary>
/// Exports reinforcement schedules for downstream documentation workflows.
/// </summary>
public interface IScheduleExporter
{
    Task ExportAsync(
        IReadOnlyList<ReinforcementZone> zones,
        string outputPath,
        CancellationToken ct = default);
}