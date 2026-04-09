namespace A101.Domain.Models;

/// <summary>
/// Direction of reinforcement within a zone.
/// </summary>
public enum RebarDirection
{
    /// <summary>Primary direction (along X axis).</summary>
    X,
    /// <summary>Secondary direction (along Y axis).</summary>
    Y
}

/// <summary>
/// Classification of a reinforcement zone by complexity.
/// </summary>
public enum ZoneType
{
    /// <summary>Simple rectangular zone.</summary>
    Simple,
    /// <summary>Complex non-rectangular zone requiring decomposition.</summary>
    Complex,
    /// <summary>Special zone (elevator shafts, openings).</summary>
    Special
}

/// <summary>
/// A zone of additional reinforcement identified on the slab.
/// </summary>
public sealed class ReinforcementZone
{
    public required string Id { get; init; }
    public required Polygon Boundary { get; init; }
    public required ReinforcementSpec Spec { get; init; }
    public required RebarDirection Direction { get; init; }
    public required ZoneType ZoneType { get; init; }

    /// <summary>
    /// If complex zone was decomposed, the resulting sub-rectangles.
    /// </summary>
    public IReadOnlyList<BoundingBox>? SubRectangles { get; init; }

    /// <summary>
    /// Computed individual rebar segments for this zone.
    /// </summary>
    public IReadOnlyList<RebarSegment> Rebars { get; set; } = [];
}

/// <summary>
/// A single rebar segment to be placed within a zone.
/// </summary>
public sealed record RebarSegment
{
    /// <summary>Start point of the rebar (mm).</summary>
    public required Point2D Start { get; init; }

    /// <summary>End point of the rebar (mm).</summary>
    public required Point2D End { get; init; }

    /// <summary>Rebar diameter (mm).</summary>
    public required int DiameterMm { get; init; }

    /// <summary>Required anchorage length at start (mm).</summary>
    public required double AnchorageLengthStart { get; init; }

    /// <summary>Required anchorage length at end (mm).</summary>
    public required double AnchorageLengthEnd { get; init; }

    /// <summary>Total length including anchorage (mm).</summary>
    public double TotalLength => Start.DistanceTo(End) + AnchorageLengthStart + AnchorageLengthEnd;
}
