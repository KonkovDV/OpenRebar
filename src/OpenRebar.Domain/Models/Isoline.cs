namespace OpenRebar.Domain.Models;

/// <summary>
/// Color from the isoline legend, associated with reinforcement parameters.
/// Supports both RGB Euclidean and perceptually uniform CIE L*a*b* ΔE distance.
/// </summary>
public readonly record struct IsolineColor(byte R, byte G, byte B)
{
    /// <summary>
    /// Euclidean distance in RGB space (legacy, non-perceptually-uniform).
    /// </summary>
    public double DistanceTo(IsolineColor other) =>
        Math.Sqrt(Math.Pow(R - other.R, 2) + Math.Pow(G - other.G, 2) + Math.Pow(B - other.B, 2));

    /// <summary>
    /// Perceptually uniform CIE L*a*b* ΔE*76 distance (ISO/CIE 11664-4).
    /// Better for isoline color matching than RGB Euclidean.
    /// </summary>
    public double DeltaE(IsolineColor other)
    {
        var (l1, a1, b1) = ToLab();
        var (l2, a2, b2) = other.ToLab();
        return Math.Sqrt(
            Math.Pow(l1 - l2, 2) +
            Math.Pow(a1 - a2, 2) +
            Math.Pow(b1 - b2, 2));
    }

    /// <summary>
    /// Convert sRGB to CIE L*a*b* via D65 illuminant.
    /// </summary>
    public (double L, double A, double B) ToLab()
    {
        // sRGB → linear RGB
        double rl = SrgbToLinear(R / 255.0);
        double gl = SrgbToLinear(G / 255.0);
        double bl = SrgbToLinear(B / 255.0);

        // Linear RGB → XYZ (sRGB D65 matrix)
        double x = 0.4124564 * rl + 0.3575761 * gl + 0.1804375 * bl;
        double y = 0.2126729 * rl + 0.7151522 * gl + 0.0721750 * bl;
        double z = 0.0193339 * rl + 0.1191920 * gl + 0.9503041 * bl;

        // D65 white point
        const double xn = 0.95047;
        const double yn = 1.00000;
        const double zn = 1.08883;

        double fx = LabF(x / xn);
        double fy = LabF(y / yn);
        double fz = LabF(z / zn);

        double l = 116.0 * fy - 16.0;
        double a = 500.0 * (fx - fy);
        double b = 200.0 * (fy - fz);

        return (l, a, b);
    }

    private static double SrgbToLinear(double c) =>
        c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);

    private static double LabF(double t) =>
        t > 0.008856 ? Math.Cbrt(t) : (903.3 * t + 16.0) / 116.0;
}

/// <summary>
/// Reinforcement parameters decoded from the isoline color legend.
/// </summary>
public sealed record ReinforcementSpec
{
    /// <summary>Rebar diameter in mm (e.g. 10, 12, 16, 20, 25).</summary>
    public required int DiameterMm
    {
        get => _diameterMm;
        init
        {
            if (value is <= 0 or > 50)
                throw new ArgumentOutOfRangeException(nameof(DiameterMm), value, "Diameter must be 1–50mm.");
            _diameterMm = value;
        }
    }
    private readonly int _diameterMm;

    /// <summary>Spacing between rebars in mm (e.g. 150, 200).</summary>
    public required int SpacingMm
    {
        get => _spacingMm;
        init
        {
            if (value is <= 0 or > 1000)
                throw new ArgumentOutOfRangeException(nameof(SpacingMm), value, "Spacing must be 1–1000mm.");
            _spacingMm = value;
        }
    }
    private readonly int _spacingMm;

    /// <summary>Steel class designation (e.g. "A500C", "A400").</summary>
    public required string SteelClass
    {
        get => _steelClass;
        init
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Steel class is required.", nameof(SteelClass));
            _steelClass = value.Trim();
        }
    }
    private readonly string _steelClass = string.Empty;

    /// <summary>Cross-sectional area of one bar (mm²).</summary>
    public double BarAreaMm2 => Math.PI * DiameterMm * DiameterMm / 4.0;

    /// <summary>Reinforcement area per meter width (mm²/m).</summary>
    public double AreaPerMeterMm2 => BarAreaMm2 * (1000.0 / SpacingMm);
}

/// <summary>
/// Mapping from a color to reinforcement parameters — one entry of the legend.
/// </summary>
public sealed record LegendEntry(IsolineColor Color, ReinforcementSpec Spec);

/// <summary>
/// Full decoded legend: a set of color-to-spec mappings.
/// </summary>
public sealed class ColorLegend
{
    public IReadOnlyList<LegendEntry> Entries { get; }

    public ColorLegend(IReadOnlyList<LegendEntry> entries)
    {
        if (entries.Count == 0)
            throw new ArgumentException("Color legend must contain at least one entry.", nameof(entries));

        if (entries.GroupBy(e => e.Color).Any(g => g.Count() > 1))
            throw new ArgumentException("Color legend cannot contain duplicate colors.", nameof(entries));

        Entries = entries;
    }

    /// <summary>
    /// Find the closest matching legend entry for a given pixel color.
    /// Uses CIE L*a*b* ΔE distance (perceptually uniform) by default.
    /// </summary>
    /// <param name="color">Pixel color to match.</param>
    /// <param name="maxDeltaE">Maximum ΔE*76 threshold. ΔE&lt;3 ~ imperceptible; ΔE&lt;10 ~ same hue.</param>
    public LegendEntry? FindClosest(IsolineColor color, double maxDeltaE = 15.0)
    {
        LegendEntry? best = null;
        double bestDist = double.MaxValue;

        foreach (var entry in Entries)
        {
            var dist = color.DeltaE(entry.Color);
            if (dist < bestDist)
            {
                bestDist = dist;
                best = entry;
            }
        }

        return bestDist <= maxDeltaE ? best : null;
    }
}
