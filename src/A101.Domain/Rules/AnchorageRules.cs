namespace A101.Domain.Rules;

/// <summary>
/// Anchorage length calculation per SP 63.13330.2018 / Eurocode 2.
/// </summary>
public static class AnchorageRules
{
    /// <summary>
    /// Calculate the required anchorage length (mm).
    /// Simplified formula per SP 63.13330.2018 §10.3.
    /// </summary>
    /// <param name="diameterMm">Rebar diameter in mm.</param>
    /// <param name="steelClass">Steel class (e.g. "A500C").</param>
    /// <param name="concreteClass">Concrete class (e.g. "B25").</param>
    /// <returns>Required anchorage length in mm.</returns>
    public static double CalculateAnchorageLength(int diameterMm, string steelClass, string concreteClass)
    {
        // Bond stress Rbt for concrete class
        double rbt = GetBondStress(concreteClass);
        // Design strength of rebar
        double rs = GetDesignStrength(steelClass);

        // Basic anchorage: l_an = (Rs * d) / (4 * Rbt)
        // With safety factor 1.0-1.5 depending on conditions
        double basicLength = (rs * diameterMm) / (4 * rbt);

        // Minimum anchorage = max(15d, 200mm, basicLength)
        double minAnch = Math.Max(15.0 * diameterMm, 200.0);
        return Math.Max(basicLength, minAnch);
    }

    /// <summary>
    /// Calculate lap splice length (mm).
    /// Typically 1.2 * anchorage length per SP 63.
    /// </summary>
    public static double CalculateLapLength(int diameterMm, string steelClass, string concreteClass)
    {
        double anchorage = CalculateAnchorageLength(diameterMm, steelClass, concreteClass);
        double minLap = Math.Max(20.0 * diameterMm, 250.0);
        return Math.Max(anchorage * 1.2, minLap);
    }

    /// <summary>
    /// Get bond stress Rbt (MPa) for concrete class per SP 63.
    /// </summary>
    private static double GetBondStress(string concreteClass) => concreteClass switch
    {
        "B15" or "C12/15" => 0.75,
        "B20" or "C16/20" => 0.90,
        "B25" or "C20/25" => 1.05,
        "B30" or "C25/30" => 1.15,
        "B35" or "C28/35" => 1.30,
        "B40" or "C32/40" => 1.40,
        _ => 1.05, // Default B25
    };

    /// <summary>
    /// Get design tensile strength Rs (MPa) for steel class.
    /// </summary>
    private static double GetDesignStrength(string steelClass) => steelClass switch
    {
        "A240" or "A-I" => 210,
        "A400" or "A-III" => 355,
        "A500" or "A500C" or "A500SP" => 435,
        "A600" => 520,
        "B500" or "B500C" => 435,
        _ => 435, // Default A500C
    };
}
