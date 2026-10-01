namespace OpenRebar.Domain.Exceptions;

/// <summary>
/// A raster isoline was read without a scale and origin, so pixel coordinates
/// must not be treated as millimetres.
/// </summary>
public sealed class RasterNotCalibratedException : OpenRebarDomainException
{
  public RasterNotCalibratedException(string filePath)
      : base(
          "raster_not_calibrated",
          $"Raster isoline '{filePath}' requires --px-per-mm and --origin-px before it can be read in millimetres.")
  {
  }
}
