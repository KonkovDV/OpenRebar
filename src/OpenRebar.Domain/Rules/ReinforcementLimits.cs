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
    /// Maximum spacing of reinforcement per SP 63 §10.3.8.
    /// For slabs: min(1.5h, 400mm) for primary direction, min(3.5h, 500mm) for secondary.
    /// </summary>
    public static double MaxSpacing(double slabThicknessMm, bool isPrimaryDirection)
    {
        if (isPrimaryDirection)
            return Math.Min(1.5 * slabThicknessMm, 400);
        return Math.Min(3.5 * slabThicknessMm, 500);
    }

    /// <summary>
    /// Minimum reinforcement ratio per SP 63 §10.3.5 (μ_min = 0.1%).
    /// </summary>
    public static double MinReinforcementArea(double slabThicknessMm, double widthMm)
    {
        return 0.001 * slabThicknessMm * widthMm;
    }
}
