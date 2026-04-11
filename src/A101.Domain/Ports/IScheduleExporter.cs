using A101.Domain.Models;

namespace A101.Domain.Ports;

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