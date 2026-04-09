using A101.Domain.Models;
using A101.Domain.Ports;
using A101.Domain.Rules;

namespace A101.Infrastructure.ZoneProcessing;

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
                var subRects = PolygonDecomposition.DecomposeToRectangles(classified.Boundary);
                classified = classified with { SubRectangles = subRects };
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
            BoundingBoxesOverlap(bbox, opening.GetBoundingBox()));

        if (isSpecial)
        {
            return zone with { ZoneType = ZoneType.Special };
        }

        // Check rectangularity
        ZoneType type = fillRatio > 0.85 ? ZoneType.Simple : ZoneType.Complex;

        // Determine rebar direction from aspect ratio
        RebarDirection direction = bbox.Width >= bbox.Height
            ? RebarDirection.X
            : RebarDirection.Y;

        return zone with
        {
            ZoneType = type,
            Direction = direction
        };
    }

    private static bool BoundingBoxesOverlap(BoundingBox a, BoundingBox b)
    {
        return a.Min.X < b.Max.X && a.Max.X > b.Min.X &&
               a.Min.Y < b.Max.Y && a.Max.Y > b.Min.Y;
    }
}
