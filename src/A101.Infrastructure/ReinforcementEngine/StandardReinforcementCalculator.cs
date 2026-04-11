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
    private readonly IStructuredLogger _logger;

    public StandardReinforcementCalculator(IStructuredLogger logger)
    {
        _logger = logger;
    }

    public IReadOnlyList<ReinforcementZone> CalculateRebars(
        IReadOnlyList<ReinforcementZone> zones,
        SlabGeometry slab)
    {
        int markCounter = 0;

        foreach (var zone in zones)
        {
            var rebars = GenerateRebarsForZone(zone, slab, ref markCounter);
            zone.Rebars = rebars;
        }

        return zones;
    }

    private List<RebarSegment> GenerateRebarsForZone(
        ReinforcementZone zone,
        SlabGeometry slab,
        ref int markCounter)
    {
        return GenerateRebarsInPolygon(zone.Boundary, zone, slab, ref markCounter);
    }

    private List<RebarSegment> GenerateRebarsInPolygon(
        Polygon polygon,
        ReinforcementZone zone,
        SlabGeometry slab,
        ref int markCounter)
    {
        var rebars = new List<RebarSegment>();
        int spacing = zone.Spec.SpacingMm;
        int diameter = zone.Spec.DiameterMm;
        var bbox = polygon.GetBoundingBox();

        // Bond condition depends on layer: top bars → Poor, bottom → Good
        var bondCondition = zone.Layer == RebarLayer.Top
            ? AnchorageRules.BondCondition.Poor
            : AnchorageRules.BondCondition.Good;

        double anchorageLength = AnchorageRules.CalculateAnchorageLength(
            diameter, zone.Spec.SteelClass, slab.ConcreteClass, bondCondition);
        int skippedShortSegments = 0;

        if (zone.Direction == RebarDirection.X)
        {
            double startY = bbox.Min.Y + spacing / 2.0;
            for (double y = startY; y < bbox.Max.Y; y += spacing)
            {
                var intervals = GetHorizontalIntervals(polygon, y);
                intervals = SubtractOpeningIntervals(intervals, slab.Openings.Select(o => GetHorizontalIntervals(o, y)));

                foreach (var (start, end) in intervals)
                {
                    double clearSpan = end - start;
                    if (clearSpan < 1e-6)
                        continue;

                    if (clearSpan + 1e-6 < anchorageLength)
                    {
                        skippedShortSegments++;
                        continue;
                    }

                    rebars.Add(new RebarSegment
                    {
                        Start = new Point2D(start, y),
                        End = new Point2D(end, y),
                        DiameterMm = diameter,
                        AnchorageLengthStart = anchorageLength,
                        AnchorageLengthEnd = anchorageLength,
                        Mark = $"{++markCounter}"
                    });
                }
            }
        }
        else // RebarDirection.Y
        {
            double startX = bbox.Min.X + spacing / 2.0;
            for (double x = startX; x < bbox.Max.X; x += spacing)
            {
                var intervals = GetVerticalIntervals(polygon, x);
                intervals = SubtractOpeningIntervals(intervals, slab.Openings.Select(o => GetVerticalIntervals(o, x)));

                foreach (var (start, end) in intervals)
                {
                    double clearSpan = end - start;
                    if (clearSpan < 1e-6)
                        continue;

                    if (clearSpan + 1e-6 < anchorageLength)
                    {
                        skippedShortSegments++;
                        continue;
                    }

                    rebars.Add(new RebarSegment
                    {
                        Start = new Point2D(x, start),
                        End = new Point2D(x, end),
                        DiameterMm = diameter,
                        AnchorageLengthStart = anchorageLength,
                        AnchorageLengthEnd = anchorageLength,
                        Mark = $"{++markCounter}"
                    });
                }
            }
        }

        // Validate max spacing per SP 63 §10.3.8
        bool isPrimary = zone.Direction == RebarDirection.X;
        double maxSpacing = ReinforcementLimits.MaxSpacing(slab.ThicknessMm, isPrimary);
        if (spacing > maxSpacing)
        {
            _logger.Warn(
                "Spacing exceeds normative maximum",
                ("zoneId", zone.Id),
                ("spacingMm", spacing),
                ("maxSpacingMm", Math.Round(maxSpacing, 2)),
                ("slabThicknessMm", slab.ThicknessMm));
        }

            if (skippedShortSegments > 0)
            {
                _logger.Warn(
                "Short rebar segments were skipped",
                ("zoneId", zone.Id),
                ("skippedSegmentCount", skippedShortSegments),
                ("minimumClearSpanMm", Math.Round(anchorageLength, 2)));
            }

        return rebars;
    }

    private static IReadOnlyList<(double Start, double End)> GetHorizontalIntervals(Polygon polygon, double y)
    {
        var intersections = new List<double>();
        var vertices = polygon.Vertices;

        for (int i = 0; i < vertices.Count; i++)
        {
            var a = vertices[i];
            var b = vertices[(i + 1) % vertices.Count];

            if ((a.Y <= y && b.Y > y) || (b.Y <= y && a.Y > y))
            {
                double t = (y - a.Y) / (b.Y - a.Y);
                intersections.Add(a.X + t * (b.X - a.X));
            }
        }

        intersections.Sort();
        var intervals = new List<(double Start, double End)>();
        for (int i = 0; i + 1 < intersections.Count; i += 2)
            intervals.Add((intersections[i], intersections[i + 1]));

        return intervals;
    }

    private static IReadOnlyList<(double Start, double End)> GetVerticalIntervals(Polygon polygon, double x)
    {
        var intersections = new List<double>();
        var vertices = polygon.Vertices;

        for (int i = 0; i < vertices.Count; i++)
        {
            var a = vertices[i];
            var b = vertices[(i + 1) % vertices.Count];

            if ((a.X <= x && b.X > x) || (b.X <= x && a.X > x))
            {
                double t = (x - a.X) / (b.X - a.X);
                intersections.Add(a.Y + t * (b.Y - a.Y));
            }
        }

        intersections.Sort();
        var intervals = new List<(double Start, double End)>();
        for (int i = 0; i + 1 < intersections.Count; i += 2)
            intervals.Add((intersections[i], intersections[i + 1]));

        return intervals;
    }

    private static IReadOnlyList<(double Start, double End)> SubtractOpeningIntervals(
        IReadOnlyList<(double Start, double End)> baseIntervals,
        IEnumerable<IReadOnlyList<(double Start, double End)>> openingIntervalSets)
    {
        var current = baseIntervals.ToList();

        foreach (var openingIntervals in openingIntervalSets)
        {
            foreach (var opening in openingIntervals)
            {
                var next = new List<(double Start, double End)>();

                foreach (var interval in current)
                {
                    if (opening.End <= interval.Start || opening.Start >= interval.End)
                    {
                        next.Add(interval);
                        continue;
                    }

                    if (opening.Start > interval.Start)
                        next.Add((interval.Start, Math.Min(opening.Start, interval.End)));

                    if (opening.End < interval.End)
                        next.Add((Math.Max(opening.End, interval.Start), interval.End));
                }

                current = next;
            }
        }

        return current;
    }
}
