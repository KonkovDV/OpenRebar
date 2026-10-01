using OpenRebar.Domain.Models;

namespace OpenRebar.Domain.Ports;

/// <summary>
/// Exports reinforcement schedules for downstream documentation workflows.
/// </summary>
public enum ScheduleNumberCulture
{
  Ru,
  Invariant
}

public interface IScheduleExporter
{
  Task ExportAsync(
      IReadOnlyList<ReinforcementZone> zones,
      string outputPath,
      CancellationToken ct = default);

  Task ExportAsync(
      IReadOnlyList<ReinforcementZone> zones,
      string outputPath,
      ScheduleNumberCulture numberCulture,
      CancellationToken ct = default);
}
