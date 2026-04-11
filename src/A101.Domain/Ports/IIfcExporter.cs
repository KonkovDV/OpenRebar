using A101.Domain.Models;

namespace A101.Domain.Ports;

/// <summary>
/// Exports reinforcement layouts as IFC4 models for downstream BIM interoperability.
/// </summary>
public interface IIfcExporter
{
    Task ExportAsync(
        IReadOnlyList<ReinforcementZone> zones,
        SlabGeometry slab,
        string outputPath,
        CancellationToken ct = default);
}