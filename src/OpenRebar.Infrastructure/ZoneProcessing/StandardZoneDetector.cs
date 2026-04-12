using OpenRebar.Domain.Models;
using OpenRebar.Domain.Ports;
using OpenRebar.Domain.Rules;

namespace OpenRebar.Infrastructure.ZoneProcessing;

/// <summary>
/// Classifies reinforcement zones by type and decomposes complex polygons
/// into axis-aligned rectangles using PolygonDecomposition.
/// </summary>
public sealed class StandardZoneDetector : IZoneDetector
{
    /// <summary>
    /// Minimum area for a zone to be considered valid (mm²). ~0.01 m².
    /// </summary>
    private const double MinZoneAreaMm2 = 100_000;

    public IReadOnlyList<ReinforcementZone> ClassifyAndDecompose(
        IReadOnlyList<ReinforcementZone> zones,
        SlabGeometry slab)
    {
        var result = new List<ReinforcementZone>();

        foreach (var zone in zones)
        {
            // Skip tiny zones
            if (zone.Boundary.CalculateArea() < MinZoneAreaMm2)
                continue;

            var classified = ClassifyZone(zone, slab);

            // Decompose complex zones
            if (classified.ZoneType == ZoneType.Complex)
            {
                var decomposition = PolygonDecomposition.DecomposeWithMetrics(classified.Boundary);
                classified = CloneZone(
                    classified,
                    decomposition.Rectangles,
                    classified.ZoneType,
                    classified.Direction,
                    decomposition.Metrics);
            }

            result.Add(classified);
        }

        return result;
    }

    private static ReinforcementZone ClassifyZone(ReinforcementZone zone, SlabGeometry slab)
    {
        var bbox = zone.Boundary.GetBoundingBox();
        double area = zone.Boundary.CalculateArea();
        double bboxArea = bbox.Area;
        double fillRatio = bboxArea > 0 ? area / bboxArea : 0;

        // Check if zone overlaps with an opening → Special
        bool isSpecial = slab.Openings.Any(opening =>
            PolygonsOverlap(zone.Boundary, opening));

        if (isSpecial)
        {
            return CloneZone(zone, zone.SubRectangles, ZoneType.Special, zone.Direction);
        }

        // Check rectangularity
        ZoneType type = fillRatio > 0.85 ? ZoneType.Simple : ZoneType.Complex;

        // Determine rebar direction from aspect ratio
        RebarDirection direction = bbox.Width >= bbox.Height
            ? RebarDirection.X
            : RebarDirection.Y;

        return CloneZone(zone, zone.SubRectangles, type, direction);
    }

    private static ReinforcementZone CloneZone(
        ReinforcementZone source,
        IReadOnlyList<BoundingBox>? subRectangles,
        ZoneType zoneType,
        RebarDirection direction,
        PolygonDecompositionMetrics? decompositionMetrics = null)
    {
        return new ReinforcementZone
        {
            Id = source.Id,
            Boundary = source.Boundary,
            Spec = source.Spec,
            Direction = direction,
            ZoneType = zoneType,
            Layer = source.Layer,
            SubRectangles = subRectangles,
            DecompositionMetrics = decompositionMetrics,
            Rebars = source.Rebars
        };
    }

    private static bool BoundingBoxesOverlap(BoundingBox a, BoundingBox b)
    {
        return a.Min.X < b.Max.X && a.Max.X > b.Min.X &&
               a.Min.Y < b.Max.Y && a.Max.Y > b.Min.Y;
    }

    private static bool PolygonsOverlap(Polygon first, Polygon second)
    {
        if (!BoundingBoxesOverlap(first.GetBoundingBox(), second.GetBoundingBox()))
            return false;

        if (HasIntersectingEdges(first, second))
            return true;

        return first.Vertices.Any(vertex => PolygonDecomposition.IsPointInPolygon(vertex, second)) ||
               second.Vertices.Any(vertex => PolygonDecomposition.IsPointInPolygon(vertex, first));
    }

    private static bool HasIntersectingEdges(Polygon first, Polygon second)
    {
        for (int i = 0; i < first.Vertices.Count; i++)
        {
            var a1 = first.Vertices[i];
            var a2 = first.Vertices[(i + 1) % first.Vertices.Count];

            for (int j = 0; j < second.Vertices.Count; j++)
            {
                var b1 = second.Vertices[j];
                var b2 = second.Vertices[(j + 1) % second.Vertices.Count];

                if (SegmentsIntersect(a1, a2, b1, b2))
                    return true;
            }
        }

        return false;
    }

    private static bool SegmentsIntersect(Point2D a1, Point2D a2, Point2D b1, Point2D b2)
    {
        double o1 = Orientation(a1, a2, b1);
        double o2 = Orientation(a1, a2, b2);
        double o3 = Orientation(b1, b2, a1);
        double o4 = Orientation(b1, b2, a2);

        if ((o1 > 0 && o2 < 0 || o1 < 0 && o2 > 0) &&
            (o3 > 0 && o4 < 0 || o3 < 0 && o4 > 0))
        {
            return true;
        }

        return IsZero(o1) && IsPointOnSegment(b1, a1, a2) ||
               IsZero(o2) && IsPointOnSegment(b2, a1, a2) ||
               IsZero(o3) && IsPointOnSegment(a1, b1, b2) ||
               IsZero(o4) && IsPointOnSegment(a2, b1, b2);
    }

    private static double Orientation(Point2D a, Point2D b, Point2D c)
    {
        return (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
    }

    private static bool IsPointOnSegment(Point2D point, Point2D start, Point2D end, double tolerance = 1e-6)
    {
        return point.X >= Math.Min(start.X, end.X) - tolerance &&
               point.X <= Math.Max(start.X, end.X) + tolerance &&
               point.Y >= Math.Min(start.Y, end.Y) - tolerance &&
               point.Y <= Math.Max(start.Y, end.Y) + tolerance;
    }

    private static bool IsZero(double value, double tolerance = 1e-6)
    {
        return Math.Abs(value) <= tolerance;
    }
}
