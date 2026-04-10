namespace A101.Domain.Rules;

/// <summary>
/// Anchorage and lap-splice length calculation per SP 63.13330.2018 §10.3.24–10.3.31.
/// Formula: l₀,an = Rs·d / (4·R_bond), where R_bond = η₁·η₂·Rbt.
/// </summary>
public static class AnchorageRules
{
    /// <summary>
    /// Conditions of concrete placement affecting bond quality (η₂ coefficient).
    /// SP 63.13330.2018 §10.3.24.
    /// </summary>
    public enum BondCondition
    {
        /// <summary>Good conditions: η₂ = 1.0 (horizontal bars in lower zone, vertical bars).</summary>
        Good,
        /// <summary>Poor conditions: η₂ = 0.7 (horizontal bars in upper zone, bars cast with slipform).</summary>
        Poor
    }

    /// <summary>
    /// Percentage of lapped bars in one cross-section, determines α_lap coefficient.
    /// SP 63.13330.2018 §10.3.31 Table 10.4.
    /// </summary>
    public enum LapPercentage
    {
        /// <summary>≤25% of bars lapped → α = 1.2.</summary>
        UpTo25,
        /// <summary>26–50% of bars lapped → α = 1.4.</summary>
        UpTo50,
        /// <summary>51–100% of bars lapped → α = 2.0 (typical for slabs).</summary>
        UpTo100
    }

    /// <summary>
    /// Calculate the required anchorage length (mm) per SP 63.13330.2018 §10.3.24–10.3.27.
    /// l₀,an = Rs·d / (4·R_bond);  l_an = max(l₀,an, 15d, 200mm) for tension.
    /// </summary>
    /// <param name="diameterMm">Rebar diameter in mm.</param>
    /// <param name="steelClass">Steel class (e.g. "A500C").</param>
    /// <param name="concreteClass">Concrete class (e.g. "B25").</param>
    /// <param name="condition">Bond condition (Good/Poor). Default: Good.</param>
    /// <param name="inCompression">If true, minimum = max(10d, 150mm) instead of max(15d, 200mm).</param>
    /// <returns>Required anchorage length in mm, rounded up to nearest 10mm.</returns>
    public static double CalculateAnchorageLength(
        int diameterMm,
        string steelClass,
        string concreteClass,
        BondCondition condition = BondCondition.Good,
        bool inCompression = false)
    {
        double rbt = GetBondStress(concreteClass);
        double rs = GetDesignStrength(steelClass);

        // Bond coefficient η₁: depends on rebar surface profile
        double eta1 = IsPeriodicProfile(steelClass) ? 2.5 : 1.5;

        // Bond coefficient η₂: depends on concrete placement quality
        double eta2 = condition == BondCondition.Good ? 1.0 : 0.7;

        // Bond strength: R_bond = η₁ · η₂ · Rbt
        double rBond = eta1 * eta2 * rbt;

        // Basic anchorage: l₀,an = Rs · d / (4 · R_bond)
        double basicLength = (rs * diameterMm) / (4.0 * rBond);

        // Minimum anchorage (SP 63 §10.3.27)
        double minAnch = inCompression
            ? Math.Max(10.0 * diameterMm, 150.0)
            : Math.Max(15.0 * diameterMm, 200.0);

        double result = Math.Max(basicLength, minAnch);

        // Round up to nearest 10mm (construction practice)
        return Math.Ceiling(result / 10.0) * 10.0;
    }

    /// <summary>
    /// Calculate lap splice length (mm) per SP 63.13330.2018 §10.3.31.
    /// l_lap = α · l₀,an;  minimum = max(20d, 250mm) for tension.
    /// </summary>
    /// <param name="diameterMm">Rebar diameter in mm.</param>
    /// <param name="steelClass">Steel class (e.g. "A500C").</param>
    /// <param name="concreteClass">Concrete class (e.g. "B25").</param>
    /// <param name="lapPercent">Percentage of bars lapped in one section. Default: UpTo100 (typical for slabs).</param>
    /// <param name="condition">Bond condition. Default: Good.</param>
    /// <param name="inCompression">If true, minimum = max(15d, 200mm).</param>
    /// <returns>Required lap splice length in mm, rounded up to nearest 10mm.</returns>
    public static double CalculateLapLength(
        int diameterMm,
        string steelClass,
        string concreteClass,
        LapPercentage lapPercent = LapPercentage.UpTo100,
        BondCondition condition = BondCondition.Good,
        bool inCompression = false)
    {
        double anchorage = CalculateAnchorageLength(diameterMm, steelClass, concreteClass, condition, inCompression);

        double alphaLap = lapPercent switch
        {
            LapPercentage.UpTo25 => 1.2,
            LapPercentage.UpTo50 => 1.4,
            LapPercentage.UpTo100 => 2.0,
            _ => 2.0
        };

        double lapLength = alphaLap * anchorage;

        // Minimum lap (SP 63 §10.3.31)
        double minLap = inCompression
            ? Math.Max(15.0 * diameterMm, 200.0)
            : Math.Max(20.0 * diameterMm, 250.0);

        double result = Math.Max(lapLength, minLap);

        return Math.Ceiling(result / 10.0) * 10.0;
    }

    /// <summary>
    /// Determines if rebar has periodic (ribbed) surface profile.
    /// Periodic profile → η₁ = 2.5; smooth → η₁ = 1.5.
    /// </summary>
    public static bool IsPeriodicProfile(string steelClass) => steelClass switch
    {
        "A240" or "A-I" => false,       // Smooth (гладкая)
        "A400" or "A-III" => true,      // Periodic (периодический профиль)
        "A500" or "A500C" or "A500SP" => true,
        "A600" => true,
        "B500" or "B500C" => true,
        _ => true, // Default: periodic
    };

    /// <summary>
    /// Get design bond stress Rbt (MPa) for concrete class per SP 63 Table 6.8.
    /// </summary>
    public static double GetBondStress(string concreteClass) => concreteClass switch
    {
        "B15" or "C12/15" => 0.75,
        "B20" or "C16/20" => 0.90,
        "B25" or "C20/25" => 1.05,
        "B30" or "C25/30" => 1.15,
        "B35" or "C28/35" => 1.30,
        "B40" or "C32/40" => 1.40,
        "B45" or "C35/45" => 1.50,
        "B50" or "C40/50" => 1.60,
        "B55" or "C45/55" => 1.65,
        "B60" or "C50/60" => 1.70,
        _ => 1.05, // Default B25
    };

    /// <summary>
    /// Get design tensile strength Rs (MPa) for steel class per SP 63 Table 6.14.
    /// </summary>
    public static double GetDesignStrength(string steelClass) => steelClass switch
    {
        "A240" or "A-I" => 210,
        "A400" or "A-III" => 355,
        "A500" or "A500C" or "A500SP" => 435,
        "A600" => 520,
        "B500" or "B500C" => 435,
        _ => 435, // Default A500C
    };
}
