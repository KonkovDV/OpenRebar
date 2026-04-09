namespace A101.Domain.Models;

/// <summary>
/// Color from the isoline legend, associated with reinforcement parameters.
/// </summary>
public readonly record struct IsolineColor(byte R, byte G, byte B)
{
    public double DistanceTo(IsolineColor other) =>
        Math.Sqrt(Math.Pow(R - other.R, 2) + Math.Pow(G - other.G, 2) + Math.Pow(B - other.B, 2));
}

/// <summary>
/// Reinforcement parameters decoded from the isoline color legend.
/// </summary>
public sealed record ReinforcementSpec
{
    /// <summary>Rebar diameter in mm (e.g. 10, 12, 16, 20, 25).</summary>
    public required int DiameterMm { get; init; }

    /// <summary>Spacing between rebars in mm (e.g. 150, 200).</summary>
    public required int SpacingMm { get; init; }

    /// <summary>Steel class designation (e.g. "A500C", "A400").</summary>
    public required string SteelClass { get; init; }
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
        Entries = entries;
    }

    /// <summary>
    /// Find the closest matching legend entry for a given pixel color.
    /// Uses Euclidean distance in RGB space.
    /// </summary>
    public LegendEntry? FindClosest(IsolineColor color, double maxDistance = 30.0)
    {
        LegendEntry? best = null;
        double bestDist = double.MaxValue;

        foreach (var entry in Entries)
        {
            var dist = color.DistanceTo(entry.Color);
            if (dist < bestDist)
            {
                bestDist = dist;
                best = entry;
            }
        }

        return bestDist <= maxDistance ? best : null;
    }
}
