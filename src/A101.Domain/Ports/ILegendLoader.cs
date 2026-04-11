using A101.Domain.Models;

namespace A101.Domain.Ports;

/// <summary>
/// Loads reinforcement color legends from external configuration.
/// </summary>
public interface ILegendLoader
{
    Task<ColorLegend> LoadAsync(string path, CancellationToken ct = default);
    ColorLegend GetDefaultLegend(string steelClass);
}