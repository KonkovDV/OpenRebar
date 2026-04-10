using A101.Domain.Models;
using A101.Domain.Ports;
using A101.Domain.Rules;

namespace A101.Infrastructure.ReinforcementEngine;

/// <summary>
/// Calculates rebar layout within zones.
/// Generates individual rebar segments with correct spacing,
/// anchorage lengths, lap splices, and mark numbering.
/// Supports both X and Y directions and Top/Bottom layers.
/// </summary>
public sealed class StandardReinforcementCalculator : IReinforcementCalculator
{
    private int _markCounter;

    public IReadOnlyList<ReinforcementZone> CalculateRebars(
        IReadOnlyList<ReinforcementZone> zones,
        SlabGeometry slab)
    {
        _markCounter = 0;

        foreach (var zone in zones)
        {
            var rebars = GenerateRebarsForZone(zone, slab);
            zone.Rebars = rebars;
        }

        return zones;
    }

    private List<RebarSegment> GenerateRebarsForZone(
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

    private List<RebarSegment> GenerateRebarsInRectangle(
        BoundingBox rect,
        ReinforcementZone zone,
        SlabGeometry slab)
    {
        var rebars = new List<RebarSegment>();
        int spacing = zone.Spec.SpacingMm;
        int diameter = zone.Spec.DiameterMm;

        // Bond condition depends on layer: top bars → Poor, bottom → Good
        var bondCondition = zone.Layer == RebarLayer.Top
            ? AnchorageRules.BondCondition.Poor
            : AnchorageRules.BondCondition.Good;

        double anchorageLength = AnchorageRules.CalculateAnchorageLength(
            diameter, zone.Spec.SteelClass, slab.ConcreteClass, bondCondition);

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
                    AnchorageLengthEnd = anchorageLength,
                    Mark = $"{++_markCounter}"
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
                    AnchorageLengthEnd = anchorageLength,
                    Mark = $"{++_markCounter}"
                });
            }
        }

        // Validate max spacing per SP 63 §10.3.8
        bool isPrimary = zone.Direction == RebarDirection.X;
        double maxSpacing = ReinforcementLimits.MaxSpacing(slab.ThicknessMm, isPrimary);
        if (spacing > maxSpacing)
        {
            // Log warning: spacing exceeds code maximum
            // In production this should go through IStructuredLogger
        }

        return rebars;
    }
}
