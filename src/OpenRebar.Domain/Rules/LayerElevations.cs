using OpenRebar.Domain.Models;

namespace OpenRebar.Domain.Rules;

/// <summary>
/// Bar axis elevation above the slab soffit, mm.
/// The clear cover is measured to the bar surface, so the outer bar axis of a face sits at
/// cover + d_outer/2. The inner bar of the same face lies on the outer bar, at
/// cover + d_outer + d_inner/2. The top face mirrors this from the slab thickness.
/// Which direction is outer is a company choice, not a code value. The default is X.
/// </summary>
public static class LayerElevations
{
  public static double AxisElevationMm(
      RebarLayer face,
      RebarDirection direction,
      double thicknessMm,
      double coverBottomMm,
      double coverTopMm,
      double outerDiameterMm,
      double innerDiameterMm,
      RebarDirection outerDirection = RebarDirection.X)
  {
    if (thicknessMm <= 0)
      throw new ArgumentOutOfRangeException(nameof(thicknessMm), thicknessMm, "Slab thickness must be positive.");
    if (coverBottomMm < 0)
      throw new ArgumentOutOfRangeException(nameof(coverBottomMm), coverBottomMm, "Cover cannot be negative.");
    if (coverTopMm < 0)
      throw new ArgumentOutOfRangeException(nameof(coverTopMm), coverTopMm, "Cover cannot be negative.");
    if (outerDiameterMm < 0)
      throw new ArgumentOutOfRangeException(nameof(outerDiameterMm), outerDiameterMm, "Diameter cannot be negative.");
    if (innerDiameterMm < 0)
      throw new ArgumentOutOfRangeException(nameof(innerDiameterMm), innerDiameterMm, "Diameter cannot be negative.");

    bool outer = direction == outerDirection;
    double cover = face == RebarLayer.Bottom ? coverBottomMm : coverTopMm;
    double fromFace = outer
        ? cover + outerDiameterMm / 2.0
        : cover + outerDiameterMm + innerDiameterMm / 2.0;
    double elevation = face == RebarLayer.Bottom ? fromFace : thicknessMm - fromFace;
    if (elevation <= 0 || elevation >= thicknessMm)
      throw new ArgumentException("The bar layers do not fit in the slab thickness.", nameof(thicknessMm));
    return elevation;
  }

  /// <summary>
  /// True when the four layer bands, each one bar diameter tall, fit between the covers
  /// without the bottom and top meshes touching.
  /// </summary>
  public static bool LayersFit(
      double thicknessMm,
      double coverBottomMm,
      double coverTopMm,
      double bottomOuterMm,
      double bottomInnerMm,
      double topOuterMm,
      double topInnerMm)
  {
    double used = coverBottomMm + bottomOuterMm + bottomInnerMm + coverTopMm + topOuterMm + topInnerMm;
    return used < thicknessMm;
  }
}
