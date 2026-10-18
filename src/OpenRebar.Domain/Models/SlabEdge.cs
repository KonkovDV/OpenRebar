namespace OpenRebar.Domain.Models;

/// <summary>
/// One side of the slab outline. A missing side is Free.
/// </summary>
public enum SlabEdgeKind
{
  Free,
  Supported,
  Continuous
}

/// <summary>
/// A side of the slab bounding box. Anchorage may enter a support; a hook is not credited.
/// </summary>
public sealed record SlabEdge
{
  public required string Segment { get; init; }

  public required SlabEdgeKind Kind { get; init; }

  /// <summary>Wall or support thickness, mm. The usable anchorage room is this minus the edge cover.</summary>
  public double? SupportDepthMm { get; init; }
}

/// <summary>
/// Resolves slab sides. Unlisted sides are Free.
/// </summary>
public static class SlabEdges
{
  public static readonly string[] Segments = ["minX", "maxX", "minY", "maxY"];

  public const string EdgeKindDefaulted =
      "EdgeKindDefaulted: edges that were not set are Free. Anchorage is not carried into a support.";

  public const string EdgeCoverBelowCover =
      "EdgeCoverBelowCover: coverEdgeMm is below coverMm. A free edge is inset by max(coverEdgeMm, coverMm + d/2).";

  public const string ContinuousSpanNotProvided =
      "A Continuous edge is extended like a support. The adjacent span is not in this input.";

  public const string EdgeDevelopmentShort = "EdgeDevelopmentShort";

  public const string RealDeficit = "RealDeficit";

  public const string EdgesRequireRectangularSlab = "edgesRequireRectangularSlab";

  public const string EdgesRequireRectangularSlabMessage =
      "edgesRequireRectangularSlab: Supported and Continuous edges are named on the bounding box. This outline is not an axis-aligned rectangle, so it is not replaced by that box.";

  public static readonly string[] Remedy =
  [
      "anchorIntoSupport",
      "uBarAtFreeEdge",
      "reduceAsReqNearEdge(input)"
  ];

  public static SlabEdgeKind Kind(SlabGeometry slab, string segment)
  {
    var edge = Find(slab, segment);
    return edge?.Kind ?? SlabEdgeKind.Free;
  }

  public static double OutwardMm(SlabGeometry slab, string segment)
  {
    var edge = Find(slab, segment);
    if (edge is null || edge.Kind == SlabEdgeKind.Free)
      return 0;

    double depth = edge.SupportDepthMm ?? 0;
    return Math.Max(0, depth - slab.EdgeCoverMm);
  }

  /// <summary>A declared support keeps its face. It is not inset like a free edge.</summary>
  public static bool HasDeclaredSupport(SlabGeometry slab) =>
      slab.Edges.Any(edge => edge.Kind != SlabEdgeKind.Free);

  public static bool AnyDefaulted(SlabGeometry slab) =>
      Segments.Any(segment => Find(slab, segment) is null);

  public static IReadOnlyList<string> Warnings(SlabGeometry slab)
  {
    var notes = new List<string>();
    if (AnyDefaulted(slab))
      notes.Add(EdgeKindDefaulted);

    if (slab.EdgeCoverMm + 1e-9 < slab.CoverMm
        && Segments.Any(segment => Kind(slab, segment) == SlabEdgeKind.Free))
      notes.Add(EdgeCoverBelowCover);

    foreach (string segment in Segments)
    {
      var edge = Find(slab, segment);
      if (edge is not null && edge.Kind != SlabEdgeKind.Free && edge.SupportDepthMm is null)
        notes.Add($"SupportDepthMissing: {segment} is {edge.Kind} without supportDepthMm, so anchorage is not extended.");
    }

    if (slab.Edges.Any(edge => edge.Kind == SlabEdgeKind.Continuous))
      notes.Add(ContinuousSpanNotProvided);

    return notes;
  }

  /// <summary>
  /// How far a free edge pulls the bar axis in, mm.
  /// The axis sits at cover + d/2 unless the explicit edge cover is larger.
  /// </summary>
  public static double FreeInsetMm(SlabGeometry slab, double diameterMm)
  {
    if (diameterMm < 0)
      throw new ArgumentOutOfRangeException(nameof(diameterMm), diameterMm, "Diameter cannot be negative.");

    return Math.Max(slab.EdgeCoverMm, slab.CoverMm + diameterMm / 2.0);
  }

  /// <summary>
  /// True when every edge is horizontal or vertical and the polygon fills its bounding box.
  /// An L-shape and a slanted edge are not rectangles.
  /// </summary>
  public static bool IsAxisAlignedRectangle(Polygon polygon)
  {
    const double axisToleranceMm = 1e-6;
    var box = polygon.GetBoundingBox();
    double width = box.Max.X - box.Min.X;
    double height = box.Max.Y - box.Min.Y;
    if (width <= axisToleranceMm || height <= axisToleranceMm)
      return false;

    var vertices = polygon.Vertices;
    for (int i = 0; i < vertices.Count; i++)
    {
      var start = vertices[i];
      var end = vertices[(i + 1) % vertices.Count];
      double dx = Math.Abs(start.X - end.X);
      double dy = Math.Abs(start.Y - end.Y);
      if (dx <= axisToleranceMm && dy <= axisToleranceMm)
        continue;
      if (dx > axisToleranceMm && dy > axisToleranceMm)
        return false;
    }

    double boxArea = width * height;
    return Math.Abs(polygon.CalculateArea() - boxArea) <= Math.Max(1e-4, boxArea * 1e-12);
  }

  /// <summary>
  /// Support edges are named on the bounding box. They are not defined on any other outline.
  /// </summary>
  public static bool SupportEdgesNeedARectangle(SlabGeometry slab) =>
      HasDeclaredSupport(slab) && !IsAxisAlignedRectangle(slab.OuterBoundary);

  /// <summary>
  /// Axis-aligned working outline. Free sides are inset by <see cref="FreeInsetMm"/>.
  /// Supported and Continuous sides grow outward by the usable support depth.
  /// A non-rectangular outline with those edges throws instead of returning the box.
  /// </summary>
  public static Polygon WorkingRectangle(SlabGeometry slab, double diameterMm)
  {
    if (SupportEdgesNeedARectangle(slab))
      throw new InvalidDataException(EdgesRequireRectangularSlabMessage);

    var box = slab.OuterBoundary.GetBoundingBox();
    double minX = Bound(box.Min.X, slab, "minX", outwardIsNegative: true, diameterMm);
    double maxX = Bound(box.Max.X, slab, "maxX", outwardIsNegative: false, diameterMm);
    double minY = Bound(box.Min.Y, slab, "minY", outwardIsNegative: true, diameterMm);
    double maxY = Bound(box.Max.Y, slab, "maxY", outwardIsNegative: false, diameterMm);
    return new Polygon(
    [
        new Point2D(minX, minY),
        new Point2D(maxX, minY),
        new Point2D(maxX, maxY),
        new Point2D(minX, maxY)
    ]);
  }

  public static SlabEdge Parse(string? segment, string? kind, double? supportDepthMm)
  {
    string canonical = Segments.FirstOrDefault(name => string.Equals(name, segment?.Trim(), StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidDataException($"Edge segment '{segment}' must be minX, maxX, minY, or maxY.");

    if (!Enum.TryParse<SlabEdgeKind>(kind?.Trim(), ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
      throw new InvalidDataException($"Edge kind '{kind}' must be Free, Supported, or Continuous.");

    if (supportDepthMm is < 0)
      throw new InvalidDataException("supportDepthMm cannot be negative.");

    return new SlabEdge
    {
      Segment = canonical,
      Kind = parsed,
      SupportDepthMm = supportDepthMm
    };
  }

  private static SlabEdge? Find(SlabGeometry slab, string segment) =>
      slab.Edges.FirstOrDefault(edge => string.Equals(edge.Segment, segment, StringComparison.OrdinalIgnoreCase));

  private static double Bound(double side, SlabGeometry slab, string segment, bool outwardIsNegative, double diameterMm)
  {
    if (Kind(slab, segment) != SlabEdgeKind.Free)
    {
      double outwardMm = OutwardMm(slab, segment);
      return outwardIsNegative ? side - outwardMm : side + outwardMm;
    }

    double inset = FreeInsetMm(slab, diameterMm);
    return outwardIsNegative ? side + inset : side - inset;
  }
}
