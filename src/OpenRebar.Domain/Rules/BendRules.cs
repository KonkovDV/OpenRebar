using OpenRebar.Domain.Models;

namespace OpenRebar.Domain.Rules;

/// <summary>
/// Bend geometry from SP 63.13330.2018 clause 10.3.33. The cut length adds the centerline
/// arc of each bend. The clause does not give a straight tail, so none is added.
/// </summary>
public static class BendRules
{
  public static double MandrelDiameterMm(int diameterMm, string steelClass)
  {
    var profile = NormativeProfiles.Sp63_2018;
    bool periodic = IsPeriodic(steelClass);
    bool below = diameterMm < profile.MandrelSplitDiameterMm;
    double factor = periodic
        ? below ? profile.MandrelPeriodicBelowSplit : profile.MandrelPeriodicFromSplit
        : below ? profile.MandrelSmoothBelowSplit : profile.MandrelSmoothFromSplit;
    return factor * diameterMm;
  }

  public static double InnerRadiusMm(int diameterMm, string steelClass) =>
      MandrelDiameterMm(diameterMm, steelClass) / 2.0;

  public static double CenterlineRadiusMm(int diameterMm, string steelClass) =>
      InnerRadiusMm(diameterMm, steelClass) + diameterMm / 2.0;

  public static BarShapeDescription Describe(
      int diameterMm,
      string steelClass,
      BarEndCondition start,
      BarEndCondition end)
  {
    double angle = BendAngle(start) + BendAngle(end);
    if (angle <= 0)
    {
      return new BarShapeDescription(BarShape.Straight, "00", 0, 0);
    }

    double inner = InnerRadiusMm(diameterMm, steelClass);
    double arc = angle * (inner + diameterMm / 2.0);
    return new BarShapeDescription(ShapeOf(start, end), CodeOf(start, end), inner, arc);
  }

  private static bool IsPeriodic(string steelClass)
  {
    if (string.IsNullOrWhiteSpace(steelClass))
      return NormativeProfiles.IsPeriodicProfile(steelClass);

    string key = steelClass.Trim();
    if (!NormativeProfiles.Sp63_2018.BarTypeByClass.ContainsKey(key))
      return true;

    return NormativeProfiles.IsPeriodicProfile(key);
  }

  private static double BendAngle(BarEndCondition condition) => condition switch
  {
    BarEndCondition.NeedsHook => Math.PI,
    BarEndCondition.NeedsLBar => Math.PI / 2.0,
    BarEndCondition.NeedsUBar => Math.PI,
    _ => 0
  };

  private static BarShape ShapeOf(BarEndCondition start, BarEndCondition end)
  {
    if (start == BarEndCondition.NeedsUBar || end == BarEndCondition.NeedsUBar)
      return BarShape.U;
    if (start == BarEndCondition.NeedsLBar || end == BarEndCondition.NeedsLBar)
      return BarShape.L;
    if (start == BarEndCondition.NeedsHook || end == BarEndCondition.NeedsHook)
      return BarShape.Hooked;
    return BarShape.Straight;
  }

  private static string CodeOf(BarEndCondition start, BarEndCondition end) => ShapeOf(start, end) switch
  {
    BarShape.U => "U",
    BarShape.L => "L",
    BarShape.Hooked => "H",
    _ => "00"
  };
}

public readonly record struct BarShapeDescription(
    BarShape Shape,
    string Code,
    double InnerRadiusMm,
    double ArcMm);
