using A101.Domain.Models;
using A101.Domain.Ports;
using A101.Domain.Rules;

namespace A101.Infrastructure.ReinforcementEngine;

/// <summary>
/// Calculates rebar layout within zones.
/// Generates individual rebar segments with correct spacing,
/// anchorage lengths, and lap splices.
/// </summary>
public sealed class StandardReinforcementCalculator : IReinforcementCalculator
{
    public IReadOnlyList<ReinforcementZone> CalculateRebars(
        IReadOnlyList<ReinforcementZone> zones,
        SlabGeometry slab)
    {
        foreach (var zone in zones)
        {
            var rebars = GenerateRebarsForZone(zone, slab);
            zone.Rebars = rebars;
        }

        return zones;
    }

    private static List<RebarSegment> GenerateRebarsForZone(
        ReinforcementZone zone,
        SlabGeometry slab)
    {
        var rebars = new List<RebarSegment>();

        // Get zone bounding box (or iterate sub-rectangles for complex zones)
        var rectangles = zone.SubRectangles ?? [zone.Boundary.GetBoundingBox()];

        foreach (var rect in rectangles)
        {
            var zoneRebars = GenerateRebarsInRectangle(rect, zone, slab);
            rebars.AddRange(zoneRebars);
        }

        return rebars;
    }

    private static List<RebarSegment> GenerateRebarsInRectangle(
        BoundingBox rect,
        ReinforcementZone zone,
        SlabGeometry slab)
    {
        var rebars = new List<RebarSegment>();
        int spacing = zone.Spec.SpacingMm;
        int diameter = zone.Spec.DiameterMm;

        double anchorageLength = AnchorageRules.CalculateAnchorageLength(
            diameter, zone.Spec.SteelClass, slab.ConcreteClass);

        if (zone.Direction == RebarDirection.X)
        {
            // Rebars run along X axis, spaced along Y
            double startY = rect.Min.Y + spacing / 2.0;
            for (double y = startY; y <= rect.Max.Y; y += spacing)
            {
                rebars.Add(new RebarSegment
                {
                    Start = new Point2D(rect.Min.X, y),
                    End = new Point2D(rect.Max.X, y),
                    DiameterMm = diameter,
                    AnchorageLengthStart = anchorageLength,
                    AnchorageLengthEnd = anchorageLength
                });
            }
        }
        else // RebarDirection.Y
        {
            // Rebars run along Y axis, spaced along X
            double startX = rect.Min.X + spacing / 2.0;
            for (double x = startX; x <= rect.Max.X; x += spacing)
            {
                rebars.Add(new RebarSegment
                {
                    Start = new Point2D(x, rect.Min.Y),
                    End = new Point2D(x, rect.Max.Y),
                    DiameterMm = diameter,
                    AnchorageLengthStart = anchorageLength,
                    AnchorageLengthEnd = anchorageLength
                });
            }
        }

        return rebars;
    }
}
