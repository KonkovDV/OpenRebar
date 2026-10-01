namespace OpenRebar.Domain.Models;

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
/// Vertical position of reinforcement in slab cross-section.
/// </summary>
public enum RebarLayer
{
  /// <summary>Bottom reinforcement (lower face, tension in span).</summary>
  Bottom,
  /// <summary>Top reinforcement (upper face, tension over supports).</summary>
  Top
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
  public required string Id { get; set; }
  public required Polygon Boundary { get; init; }
  public required ReinforcementSpec Spec { get; init; }
  public required RebarDirection Direction { get; set; }
  public required ZoneType ZoneType { get; init; }
  public RebarLayer Layer { get; set; } = RebarLayer.Bottom;

  /// <summary>CAD or raster layer name the zone was read from, when the parser has one.</summary>
  public string? SourceLayerName { get; set; }

  /// <summary>True when direction was inferred from the bounding box instead of an explicit layer.</summary>
  public bool DirectionInferredFromGeometry { get; set; }

  /// <summary>
  /// If complex zone was decomposed, the resulting sub-rectangles.
  /// </summary>
  public IReadOnlyList<BoundingBox>? SubRectangles { get; init; }

  /// <summary>Merged bar lines. Empty until the layout step.</summary>
  public IReadOnlyList<BarRun> Runs { get; set; } = [];

  /// <summary>
  /// Approximate coverage and over-coverage metrics for complex zone decomposition.
  /// Present only when the boundary was decomposed into sub-rectangles.
  /// </summary>
  public PolygonDecompositionMetrics? DecompositionMetrics { get; init; }

  /// <summary>
  /// Computed individual rebar segments for this zone.
  /// </summary>
  public IReadOnlyList<RebarSegment> Rebars { get; set; } = [];

  /// <summary>Interior rings. Empty when the zone has no hole.</summary>
  public IReadOnlyList<Polygon> Holes { get; init; } = [];

  /// <summary>Bars whose run is shorter than the required anchorage.</summary>
  public int ExtendedBeyondZoneCount { get; set; }

  /// <summary>Profile end rule used when the anchorage does not fit in the working area.</summary>
  public string RequestedEndCondition { get; set; } = "NeedsHook";

  /// <summary>Design layer when the zone came from a multi-layer input. Null on the legacy single-layer path.</summary>
  public LayerKey? DesignLayer { get; set; }

  public ZoneRole Role { get; set; } = ZoneRole.Additional;

  /// <summary>Required area from the source field or legend, mm²/m. Zero until an area legend is applied.</summary>
  public double AsRequiredMm2PerM { get; set; }

  public double AsBackgroundMm2PerM { get; set; }

  public double AsDeltaMm2PerM { get; set; }

  public string? SourceClassId { get; set; }

  /// <summary>Class ids that cover this face after the layer overlay. Empty when the zone was not split.</summary>
  public IReadOnlyList<string> SourceIds { get; set; } = [];

  /// <summary>Background spacing used to place additional bars on the layer grid. Null keeps the per-zone spacing.</summary>
  public int? BackgroundSpacingMm { get; set; }

  /// <summary>Origin of the layer grid, in millimetres along the axis perpendicular to the bars.</summary>
  public double? GridOriginMm { get; set; }

  /// <summary>Bars actually placed when the background selector replaces the legend class.</summary>
  public ReinforcementSpec? LayoutSpec { get; set; }

  /// <summary>Specification used to place and check bars.</summary>
  public ReinforcementSpec EffectiveSpec => LayoutSpec ?? Spec;

  /// <summary>True when this zone is covered by the layer mesh and must not emit its own bars.</summary>
  public bool SuppressLayout { get; set; }

  /// <summary>True for the single mesh zone of a layer. Its bars are the background.</summary>
  public bool IsBackgroundMesh { get; set; }

  /// <summary>How stations are chosen along the layer grid.</summary>
  public LayerGridMode GridMode { get; set; }
}

/// <summary>
/// Station layout relative to a layer background grid.
/// </summary>
public enum LayerGridMode
{
  None,
  Interleave,
  Mesh
}

/// <summary>
/// Coverage metrics for a polygon-to-rectangle decomposition.
/// Used to make heuristic geometry processing auditable in execution reports.
/// </summary>
public sealed record PolygonDecompositionMetrics
{
  public required double PolygonAreaMm2 { get; init; }
  public required double RectangleCoverAreaMm2 { get; init; }
  public required double CoverageRatio { get; init; }
  public required double OverCoverageRatio { get; init; }
  public required double CellSizeMm { get; init; }
  public required int RectangleCount { get; init; }
  public required bool UsedRectangularShortcut { get; init; }
}

/// <summary>
/// Whether a bar instance is listed in the schedule, drawing, and IFC export.
/// </summary>
public enum BarInstanceStatus
{
  Active,
  Trimmed,
  Discarded
}

/// <summary>
/// How the required anchorage relates to the run.
/// </summary>
public enum AnchorageStatus
{
  WithinZone,
  ExtendedBeyondZone
}

/// <summary>
/// What happens at one end after the anchorage is clipped to the working area.
/// </summary>
public enum BarEndCondition
{
  Straight,
  ShortenedAtEdge,
  NeedsHook,
  NeedsLBar,
  NeedsUBar
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

  /// <summary>Stable instance id from layer, coordinates, and diameter.</summary>
  public string BarId { get; init; } = "";

  /// <summary>Position number shared by bars of the same type. Assigned by <c>PositionAssigner</c>.</summary>
  public string? Mark { get; init; }

  /// <summary>Internal shape code. Straight bars use 00 until a shape catalog is selected.</summary>
  public string ShapeCode { get; init; } = "00";

  /// <summary>Instance status. Discarded bars stay in the model and leave the schedule.</summary>
  public BarInstanceStatus Status { get; init; } = BarInstanceStatus.Active;

  /// <summary>
  /// ExtendedBeyondZone means the run is shorter than the required anchorage.
  /// The end conditions say whether that anchorage fit inside the working area.
  /// </summary>
  public AnchorageStatus AnchorageStatus { get; init; } = AnchorageStatus.WithinZone;

  /// <summary>End at <see cref="Start"/> after clipping the anchorage to the working area.</summary>
  public BarEndCondition EndConditionStart { get; init; } = BarEndCondition.Straight;

  /// <summary>End at <see cref="End"/> after clipping the anchorage to the working area.</summary>
  public BarEndCondition EndConditionEnd { get; init; } = BarEndCondition.Straight;

  /// <summary>Geometric length from <see cref="Start"/> to <see cref="End"/> (mm).</summary>
  public double TotalLength => Start.DistanceTo(End);

  /// <summary>Geometric length from <see cref="Start"/> to <see cref="End"/> (mm).</summary>
  public double ClearSpan => Start.DistanceTo(End);
}
