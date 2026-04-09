namespace A101.Domain.Models;

/// <summary>
/// Slab geometry extracted from the Revit model.
/// </summary>
public sealed class SlabGeometry
{
    /// <summary>Outer boundary of the slab.</summary>
    public required Polygon OuterBoundary { get; init; }

    /// <summary>Openings (elevator shafts, columns, etc.).</summary>
    public IReadOnlyList<Polygon> Openings { get; init; } = [];

    /// <summary>Slab thickness in mm.</summary>
    public required double ThicknessMm { get; init; }

    /// <summary>Concrete cover to reinforcement in mm.</summary>
    public required double CoverMm { get; init; }

    /// <summary>Concrete class designation (e.g. "C25/30", "B25").</summary>
    public required string ConcreteClass { get; init; }
}
