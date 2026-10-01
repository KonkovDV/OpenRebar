namespace OpenRebar.Domain.Rules;

/// <summary>
/// Minimum/maximum reinforcement constraints per SP 63.13330.2018.
/// </summary>
public static class ReinforcementLimits
{
  /// <summary>Standard diameters available on Russian market (mm).</summary>
  public static IReadOnlyList<int> StandardDiameters => NormativeProfiles.Sp63_2018.StandardDiametersMm;

  /// <summary>Standard spacing values (mm).</summary>
  public static IReadOnlyList<int> StandardSpacings => NormativeProfiles.Sp63_2018.StandardSpacingsMm;

  /// <summary>
  /// Linear mass of rebar (kg/m) by diameter.
  /// GOST 5781-82, class A500C.
  /// </summary>
  public static double GetLinearMass(int diameterMm) => NormativeProfiles.GetLinearMass(diameterMm);

  /// <summary>
  /// Which bars of a layer the spacing limit applies to.
  /// The role comes from the layer, not from the plan direction.
  /// Working and distribution currently share one quoted limit.
  /// </summary>
  public enum SlabReinforcementRole
  {
    Working,
    Distribution
  }

  /// <summary>
  /// Maximum bar spacing per SP 63 §10.3.8.
  /// 200 mm when h ≤ 150 mm; otherwise the lesser of 1.5h and 400 mm.
  /// </summary>
  public static double MaxSpacing(double slabThicknessMm, SlabReinforcementRole role = SlabReinforcementRole.Working)
  {
    _ = role;
    var profile = NormativeProfiles.Sp63_2018;
    if (slabThicknessMm <= profile.MaxSpacingThinSlabThicknessMm)
      return profile.MaxSpacingThinSlabLimitMm;

    return Math.Min(profile.MaxSpacingThickSlabFactor * slabThicknessMm, profile.MaxSpacingThickSlabCapMm);
  }

  /// <summary>
  /// Minimum reinforcement area per SP 63 §10.3.5, μ_min = 0.1% of b·h0.
  /// </summary>
  public static double MinReinforcementArea(double effectiveDepthMm, double widthMm)
  {
    return NormativeProfiles.Sp63_2018.MinReinforcementRatio * effectiveDepthMm * widthMm;
  }
}
