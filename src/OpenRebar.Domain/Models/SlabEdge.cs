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

  public const string ContinuousSpanNotProvided =
      "A Continuous edge is extended like a support. The adjacent span is not in this input.";

  public const string EdgeDevelopmentShort = "EdgeDevelopmentShort";

  public const string RealDeficit = "RealDeficit";

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
  /// Axis-aligned working outline. Free sides are inset by the edge cover.
  /// Supported and Continuous sides grow outward by the usable support depth.
  /// </summary>
  public static Polygon WorkingRectangle(SlabGeometry slab)
  {
    var box = slab.OuterBoundary.GetBoundingBox();
    double minX = Bound(box.Min.X, slab, "minX", outwardIsNegative: true);
    double maxX = Bound(box.Max.X, slab, "maxX", outwardIsNegative: false);
    double minY = Bound(box.Min.Y, slab, "minY", outwardIsNegative: true);
    double maxY = Bound(box.Max.Y, slab, "maxY", outwardIsNegative: false);
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

  private static double Bound(double side, SlabGeometry slab, string segment, bool outwardIsNegative)
  {
    if (Kind(slab, segment) != SlabEdgeKind.Free)
    {
      double outwardMm = OutwardMm(slab, segment);
      return outwardIsNegative ? side - outwardMm : side + outwardMm;
    }

    return outwardIsNegative ? side + slab.EdgeCoverMm : side - slab.EdgeCoverMm;
  }
}
