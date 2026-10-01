namespace OpenRebar.Domain.Rules;

/// <summary>
/// Anchorage and lap length per SP 63.13330.2018 §§10.3.24 and 10.3.30.
/// Coefficients come from the versioned profile, not from a bond-condition enum.
/// </summary>
public static class AnchorageRules
{
  /// <summary>
  /// Share of bars lapped in one section. Replaces the old 25/50/100 split:
  /// at most half the bars use α = 1.2, and a full lap in one section uses α = 2.0.
  /// </summary>
  public enum LapSpliceShare
  {
    UpTo50,
    Full100
  }

  /// <summary>
  /// Unrounded basic anchorage l0,an = Rs·d / (4·η1·η2·Rbt), in millimetres.
  /// </summary>
  public static double CalculateBasicAnchorageLength(
      int diameterMm,
      string steelClass,
      string concreteClass)
  {
    double eta1 = NormativeProfiles.GetEta1(steelClass);
    double eta2 = NormativeProfiles.GetEta2(diameterMm);
    double rBond = eta1 * eta2 * GetBondStress(concreteClass);
    double rs = GetDesignStrength(steelClass);
    return rs * diameterMm / (4.0 * rBond);
  }

  /// <summary>
  /// Required anchorage l_an, rounded up to the profile step.
  /// l_an = α·l0,an·As,cal/As,ef, not less than max(0.3·l0,an, 15d, 200 mm).
  /// </summary>
  public static double CalculateAnchorageLength(
      int diameterMm,
      string steelClass,
      string concreteClass,
      bool inCompression = false,
      double? asCalOverAsEf = null,
      double topBarAnchorageFactor = 1.0)
  {
    var profile = NormativeProfiles.Sp63_2018;
    double basic = CalculateBasicAnchorageLength(diameterMm, steelClass, concreteClass);
    double alpha = inCompression ? profile.AnchorageCompressionAlpha : profile.AnchorageTensionAlpha;
    double areaRatio = asCalOverAsEf ?? profile.AsCalOverAsEf;
    double required = alpha * basic * areaRatio * topBarAnchorageFactor;
    double minimum = Math.Max(
        profile.AnchorageMinimumFactorOfBasic * basic,
        Math.Max(profile.AnchorageMinimumDiameters * diameterMm, profile.AnchorageMinimumMm));
    return RoundUp(Math.Max(required, minimum), profile.RoundUpMm);
  }

  /// <summary>
  /// Lap length from unrounded l0,an, rounded up to the profile step.
  /// </summary>
  public static double CalculateLapLength(
      int diameterMm,
      string steelClass,
      string concreteClass,
      LapSpliceShare share = LapSpliceShare.Full100,
      bool inCompression = false,
      double topBarAnchorageFactor = 1.0)
  {
    var profile = NormativeProfiles.Sp63_2018;
    double basic = CalculateBasicAnchorageLength(diameterMm, steelClass, concreteClass);
    double alpha = inCompression
        ? profile.LapCompressionAlpha
        : share == LapSpliceShare.UpTo50
            ? profile.LapUpTo50Alpha
            : profile.LapFull100Alpha;
    double required = alpha * basic * topBarAnchorageFactor;
    double minimum = Math.Max(
        profile.LapMinimumFactorOfAlphaBasic * alpha * basic,
        Math.Max(profile.LapMinimumDiameters * diameterMm, profile.LapMinimumMm));
    return RoundUp(Math.Max(required, minimum), profile.RoundUpMm);
  }

  public static bool IsPeriodicProfile(string steelClass) => NormativeProfiles.IsPeriodicProfile(steelClass);

  public static double GetBondStress(string concreteClass) => NormativeProfiles.GetBondStress(concreteClass);

  public static double GetDesignStrength(string steelClass) => NormativeProfiles.GetDesignStrength(steelClass);

  private static double RoundUp(double lengthMm, double stepMm)
  {
    return Math.Ceiling(lengthMm / stepMm - 1e-9) * stepMm;
  }
}
