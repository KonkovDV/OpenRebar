namespace OpenRebar.Domain.Models;

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
      if (!double.IsFinite(value) || value is <= 0 or > 2000)
        throw new ArgumentOutOfRangeException(nameof(ThicknessMm), value, "Slab thickness must be between 0 and 2000mm.");
      if (_coverMm > 0 && value <= _coverMm)
        throw new ArgumentException("Slab thickness must be greater than cover.", nameof(ThicknessMm));
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
      if (!double.IsFinite(value) || value is < 0 or > 200)
        throw new ArgumentOutOfRangeException(nameof(CoverMm), value, "Concrete cover must be between 0 and 200mm.");
      if (_thicknessMm > 0 && value >= _thicknessMm)
        throw new ArgumentException("Concrete cover must be smaller than slab thickness.", nameof(CoverMm));
      _coverMm = value;
    }
  }
  private readonly double _coverMm;

  /// <summary>Concrete class designation (e.g. "C25/30", "B25").</summary>
  public required string ConcreteClass { get; init; }

  /// <summary>Extra inset from the slab edge, mm. A free edge still keeps cover + d/2 when this is smaller.</summary>
  public double EdgeCoverMm { get; init; }

  /// <summary>Extra gap added around each slab opening, mm.</summary>
  public double OpeningClearanceMm { get; init; }

  /// <summary>Sides of the outline. An omitted side is Free.</summary>
  public IReadOnlyList<SlabEdge> Edges { get; init; } = [];

  /// <summary>
  /// Slab thickness minus cover, mm. A partial report uses this because no bar
  /// has been placed yet, so d/2 is not subtracted.
  /// </summary>
  public double EffectiveDepthMm => ThicknessMm - CoverMm;

  /// <summary>
  /// Distance from the opposite face to the axis of an outer bar, mm.
  /// The axis sits at cover + d/2 from the near face.
  /// </summary>
  public double EffectiveDepthFor(int diameterMm)
  {
    if (diameterMm < 0)
      throw new ArgumentOutOfRangeException(nameof(diameterMm), diameterMm, "Diameter cannot be negative.");

    double depth = ThicknessMm - CoverMm - diameterMm / 2.0;
    if (depth <= 0)
      throw new ArgumentOutOfRangeException(nameof(diameterMm), diameterMm, "The bar does not fit in the slab.");

    return depth;
  }
}
