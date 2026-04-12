using OpenRebar.Domain.Models;

namespace OpenRebar.Domain.Ports;

/// <summary>
/// Performs image segmentation on PNG isoline rasters.
/// Calls the Python ML service or runs a local ONNX model.
/// </summary>
public interface IImageSegmentationService
{
    /// <summary>
    /// Segment a PNG isoline image into zone polygons.
    /// </summary>
    /// <param name="imagePath">Path to the PNG file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of polygons with associated colors.</returns>
    Task<IReadOnlyList<(Polygon Boundary, IsolineColor DominantColor)>> SegmentAsync(
        string imagePath,
        CancellationToken cancellationToken = default);
}
