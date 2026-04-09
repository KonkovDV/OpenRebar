namespace A101.Domain.Rules;

/// <summary>
/// Minimum/maximum reinforcement constraints per SP 63.13330.2018.
/// </summary>
public static class ReinforcementLimits
{
    /// <summary>Standard diameters available on Russian market (mm).</summary>
    public static readonly IReadOnlyList<int> StandardDiameters = [6, 8, 10, 12, 14, 16, 18, 20, 22, 25, 28, 32, 36, 40];

    /// <summary>Standard spacing values (mm).</summary>
    public static readonly IReadOnlyList<int> StandardSpacings = [100, 150, 200, 250, 300];

    /// <summary>
    /// Linear mass of rebar (kg/m) by diameter.
    /// GOST 5781-82, class A500C.
    /// </summary>
    public static double GetLinearMass(int diameterMm) => diameterMm switch
    {
        6 => 0.222,
        8 => 0.395,
        10 => 0.617,
        12 => 0.888,
        14 => 1.210,
        16 => 1.580,
        18 => 2.000,
        20 => 2.470,
        22 => 2.980,
        25 => 3.850,
        28 => 4.830,
        32 => 6.310,
        36 => 7.990,
        40 => 9.870,
        _ => Math.PI * Math.Pow(diameterMm / 2.0 / 1000.0, 2) * 7850.0, // γ_steel = 7850 kg/m³
    };

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
