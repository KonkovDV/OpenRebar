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

    /// <summary>Slab thickness in mm (typical range 150–500mm).</summary>
    public required double ThicknessMm
    {
        get => _thicknessMm;
        init
        {
            if (value is <= 0 or > 2000)
                throw new ArgumentOutOfRangeException(nameof(ThicknessMm), value, "Slab thickness must be between 0 and 2000mm.");
            _thicknessMm = value;
        }
    }
    private readonly double _thicknessMm;

    /// <summary>Concrete cover to reinforcement in mm (typical 15–75mm).</summary>
    public required double CoverMm
    {
        get => _coverMm;
        init
        {
            if (value is < 0 or > 200)
                throw new ArgumentOutOfRangeException(nameof(CoverMm), value, "Concrete cover must be between 0 and 200mm.");
            _coverMm = value;
        }
    }
    private readonly double _coverMm;

    /// <summary>Concrete class designation (e.g. "C25/30", "B25").</summary>
    public required string ConcreteClass { get; init; }

    /// <summary>Effective depth d₀ = h - a (mm).</summary>
    public double EffectiveDepthMm => ThicknessMm - CoverMm;
}
