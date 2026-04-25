using OpenRebar.Domain.Models;

namespace OpenRebar.Domain.Rules;

/// <summary>
/// Heuristic algorithms for decomposing complex polygons into axis-aligned rectangles.
/// Used for non-rectangular reinforcement zones (around openings, L-shapes, etc.)
/// with auditable coverage metrics rather than silent geometric guesses.
/// </summary>
public static class PolygonDecomposition
{
    public const double RectangularFillRatioThreshold = 0.85;
    public const double DefaultMinRectangleAreaMm2 = 10_000;
    public const int DefaultCellDivisionCount = 20;
    public const int CoverageSamplingResolutionPerAxis = 4;
    public const double CellCoverageInclusionThreshold = 0.35;

    /// <summary>
    /// Decompose a polygon into a set of axis-aligned rectangles.
    /// Returns the rectangle cover only; for auditable metrics use <see cref="DecomposeWithMetrics"/>.
    /// </summary>
    /// <param name="polygon">Input polygon to decompose.</param>
    /// <param name="minAreaMm2">Minimum rectangle area to keep (mm²).</param>
    /// <returns>List of rectangles covering the polygon.</returns>
    public static IReadOnlyList<BoundingBox> DecomposeToRectangles(
        Polygon polygon,
        double minAreaMm2 = DefaultMinRectangleAreaMm2)
    {
        return DecomposeWithMetrics(polygon, minAreaMm2).Rectangles;
    }

    /// <summary>
    /// Decompose a polygon into a set of axis-aligned rectangles and return auditable metrics.
    /// The algorithm is heuristic but exposes coverage/over-coverage so the caller can verify the result.
    /// </summary>
    public static PolygonDecompositionResult DecomposeWithMetrics(
        Polygon polygon,
        double minAreaMm2 = DefaultMinRectangleAreaMm2)
    {
        var bbox = polygon.GetBoundingBox();
        var result = new List<BoundingBox>();

        double polygonArea = polygon.CalculateArea();
        double bboxArea = bbox.Area;
        double fillRatio = bboxArea > 0 ? polygonArea / bboxArea : 0;

        if (fillRatio > RectangularFillRatioThreshold)
        {
            result.Add(bbox);
            return new PolygonDecompositionResult
            {
                Rectangles = result,
                Metrics = BuildMetrics(polygon, result, Math.Max(bbox.Width, bbox.Height), usedRectangularShortcut: true)
            };
        }

        if (IsOrthogonalPolygon(polygon))
        {
            var exactRectangles = BuildOrthogonalStripRectangles(polygon);
            if (exactRectangles.Count > 0)
            {
                return new PolygonDecompositionResult
                {
                    Rectangles = exactRectangles,
                    Metrics = BuildMetrics(
                        polygon,
                        exactRectangles,
                        DetermineRepresentativeStripHeight(polygon),
                        usedRectangularShortcut: false)
                };
            }
        }

        double cellSize = Math.Max(
            Math.Max(bbox.Width, bbox.Height) / DefaultCellDivisionCount,
            Math.Sqrt(minAreaMm2));

        var cells = new List<BoundingBox>();

        for (double x = bbox.Min.X; x < bbox.Max.X; x += cellSize)
        {
            for (double y = bbox.Min.Y; y < bbox.Max.Y; y += cellSize)
            {
                var cell = new BoundingBox(
                    new Point2D(x, y),
                    new Point2D(
                        Math.Min(x + cellSize, bbox.Max.X),
                        Math.Min(y + cellSize, bbox.Max.Y)));

                if (cell.Area < minAreaMm2)
                    continue;

                double coverageEstimate = EstimateCellCoverage(cell, polygon);
                if (ShouldKeepCell(cell, polygon, coverageEstimate))
                {
                    cells.Add(cell);
                }
            }
        }

        result.AddRange(MergeAdjacentCells(cells, cellSize));

        if (result.Count == 0)
            result = [bbox];

        return new PolygonDecompositionResult
        {
            Rectangles = result,
            Metrics = BuildMetrics(polygon, result, cellSize, usedRectangularShortcut: false)
        };
    }

    private static bool IsOrthogonalPolygon(Polygon polygon)
    {
        for (int i = 0; i < polygon.Vertices.Count; i++)
        {
            var start = polygon.Vertices[i];
            var end = polygon.Vertices[(i + 1) % polygon.Vertices.Count];
            if (!IsZero(start.X - end.X) && !IsZero(start.Y - end.Y))
                return false;
        }

        return true;
    }

    private static IReadOnlyList<BoundingBox> BuildOrthogonalStripRectangles(Polygon polygon)
    {
        var sortedY = polygon.Vertices
            .Select(vertex => vertex.Y)
            .Distinct()
            .OrderBy(y => y)
            .ToList();

        if (sortedY.Count < 2)
            return [];

        var strips = new List<BoundingBox>();

        for (int bandIndex = 0; bandIndex < sortedY.Count - 1; bandIndex++)
        {
            double minY = sortedY[bandIndex];
            double maxY = sortedY[bandIndex + 1];
            if (maxY - minY <= 1e-6)
                continue;

            double sampleY = (minY + maxY) / 2.0;
            var intersections = GetHorizontalIntersections(polygon, sampleY);
            if (intersections.Count == 0 || intersections.Count % 2 != 0)
                return [];

            for (int i = 0; i < intersections.Count; i += 2)
            {
                double minX = intersections[i];
                double maxX = intersections[i + 1];
                if (maxX - minX <= 1e-6)
                    continue;

                strips.Add(new BoundingBox(
                    new Point2D(minX, minY),
                    new Point2D(maxX, maxY)));
            }
        }

        return MergeExactRectangles(strips);
    }

    private static List<double> GetHorizontalIntersections(Polygon polygon, double sampleY)
    {
        var intersections = new List<double>();

        for (int i = 0; i < polygon.Vertices.Count; i++)
        {
            var start = polygon.Vertices[i];
            var end = polygon.Vertices[(i + 1) % polygon.Vertices.Count];

            if (!IsZero(start.X - end.X))
                continue;

            double minY = Math.Min(start.Y, end.Y);
            double maxY = Math.Max(start.Y, end.Y);
            if (sampleY >= minY && sampleY < maxY)
                intersections.Add(start.X);
        }

        intersections.Sort();
        return intersections;
    }

    private static IReadOnlyList<BoundingBox> MergeExactRectangles(IReadOnlyList<BoundingBox> rectangles)
    {
        if (rectangles.Count == 0)
            return [];

        var merged = rectangles
            .OrderBy(rect => rect.Min.X)
            .ThenBy(rect => rect.Min.Y)
            .ToList();

        bool changed;
        do
        {
            changed = false;
            for (int i = 0; i < merged.Count && !changed; i++)
            {
                for (int j = i + 1; j < merged.Count; j++)
                {
                    if (CanMergeExactly(merged[i], merged[j], out var combined))
                    {
                        merged[i] = combined;
                        merged.RemoveAt(j);
                        changed = true;
                        break;
                    }
                }
            }
        }
        while (changed);

        return merged;
    }

    private static bool CanMergeExactly(BoundingBox first, BoundingBox second, out BoundingBox merged)
    {
        if (IsZero(first.Min.X - second.Min.X) &&
            IsZero(first.Max.X - second.Max.X) &&
            IsZero(first.Max.Y - second.Min.Y))
        {
            merged = new BoundingBox(first.Min, new Point2D(first.Max.X, second.Max.Y));
            return true;
        }

        if (IsZero(first.Min.Y - second.Min.Y) &&
            IsZero(first.Max.Y - second.Max.Y) &&
            IsZero(first.Max.X - second.Min.X))
        {
            merged = new BoundingBox(first.Min, new Point2D(second.Max.X, first.Max.Y));
            return true;
        }

        merged = default;
        return false;
    }

    private static double DetermineRepresentativeStripHeight(Polygon polygon)
    {
        var sortedY = polygon.Vertices
            .Select(vertex => vertex.Y)
            .Distinct()
            .OrderBy(y => y)
            .ToList();

        return sortedY.Zip(sortedY.Skip(1), (first, second) => second - first)
            .Where(delta => delta > 1e-6)
            .DefaultIfEmpty(Math.Max(polygon.GetBoundingBox().Width, polygon.GetBoundingBox().Height))
            .Min();
    }

    private static bool ShouldKeepCell(BoundingBox cell, Polygon polygon, double coverageEstimate)
    {
        if (coverageEstimate >= CellCoverageInclusionThreshold)
            return true;

        if (IsPointInPolygon(cell.Center, polygon))
            return true;

        if (GetCellCorners(cell).Any(corner => IsPointInPolygon(corner, polygon)))
            return true;

        if (polygon.Vertices.Any(vertex => IsPointInBox(vertex, cell)))
            return true;

        return DoesCellIntersectPolygon(cell, polygon);
    }

    private static double EstimateCellCoverage(BoundingBox cell, Polygon polygon)
    {
        int inside = 0;
        int total = CoverageSamplingResolutionPerAxis * CoverageSamplingResolutionPerAxis;

        for (int ix = 0; ix < CoverageSamplingResolutionPerAxis; ix++)
        {
            for (int iy = 0; iy < CoverageSamplingResolutionPerAxis; iy++)
            {
                double sampleX = cell.Min.X + (ix + 0.5) * cell.Width / CoverageSamplingResolutionPerAxis;
                double sampleY = cell.Min.Y + (iy + 0.5) * cell.Height / CoverageSamplingResolutionPerAxis;

                if (IsPointInPolygon(new Point2D(sampleX, sampleY), polygon))
                    inside++;
            }
        }

        return total > 0 ? (double)inside / total : 0;
    }

    /// <summary>
    /// Ray casting algorithm for point-in-polygon test with tolerance.
    /// </summary>
    public static bool IsPointInPolygon(Point2D point, Polygon polygon, GeometryTolerance? tolerance = null)
    {
        tolerance ??= GeometryTolerance.Default;
        var vertices = polygon.Vertices;
        bool inside = false;
        int n = vertices.Count;

        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            // Use tolerance-aware comparison for Y-axis
            if (!IsZero(vertices[i].Y - vertices[j].Y, tolerance.LinearToleranceMm) &&
                (vertices[i].Y > point.Y + tolerance.LinearToleranceMm) != 
                (vertices[j].Y > point.Y - tolerance.LinearToleranceMm) &&
                point.X < (vertices[j].X - vertices[i].X) * (point.Y - vertices[i].Y)
                    / (vertices[j].Y - vertices[i].Y) + vertices[i].X - tolerance.LinearToleranceMm)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    /// <summary>
    /// Merge adjacent cells into larger rectangles.
    /// First merges horizontally within rows, then vertically across rows with matching spans.
    /// </summary>
    private static IReadOnlyList<BoundingBox> MergeAdjacentCells(
        List<BoundingBox> cells, double cellSize)
    {
        if (cells.Count == 0) return [];

        var horizontal = new List<BoundingBox>();
        var rows = cells
            .OrderBy(c => c.Min.Y)
            .ThenBy(c => c.Min.X)
            .GroupBy(c => Math.Round(c.Min.Y / Math.Max(cellSize, 1), 3));

        foreach (var row in rows)
        {
            BoundingBox? current = null;
            foreach (var cell in row.OrderBy(c => c.Min.X))
            {
                if (current is null)
                {
                    current = cell;
                    continue;
                }

                if (NearlyEqual(cell.Min.Y, current.Value.Min.Y, cellSize) &&
                    NearlyEqual(cell.Min.X, current.Value.Max.X, cellSize))
                {
                    current = new BoundingBox(current.Value.Min, new Point2D(cell.Max.X, current.Value.Max.Y));
                }
                else
                {
                    horizontal.Add(current.Value);
                    current = cell;
                }
            }

            if (current is not null)
                horizontal.Add(current.Value);
        }

        var vertical = new List<BoundingBox>();
        var columns = horizontal
            .OrderBy(c => c.Min.X)
            .ThenBy(c => c.Min.Y)
            .GroupBy(c => (Math.Round(c.Min.X, 3), Math.Round(c.Width, 3)));

        foreach (var column in columns)
        {
            BoundingBox? current = null;
            foreach (var rect in column.OrderBy(c => c.Min.Y))
            {
                if (current is null)
                {
                    current = rect;
                    continue;
                }

                if (NearlyEqual(rect.Min.X, current.Value.Min.X, cellSize) &&
                    NearlyEqual(rect.Width, current.Value.Width, cellSize) &&
                    NearlyEqual(rect.Min.Y, current.Value.Max.Y, cellSize))
                {
                    current = new BoundingBox(current.Value.Min, new Point2D(current.Value.Max.X, rect.Max.Y));
                }
                else
                {
                    vertical.Add(current.Value);
                    current = rect;
                }
            }

            if (current is not null)
                vertical.Add(current.Value);
        }

        return vertical;
    }

    private static PolygonDecompositionMetrics BuildMetrics(
        Polygon polygon,
        IReadOnlyList<BoundingBox> rectangles,
        double cellSize,
        bool usedRectangularShortcut)
    {
        double polygonArea = polygon.CalculateArea();
        double rectangleArea = rectangles.Sum(rect => rect.Area);
        double coverageRatio = EstimatePolygonCoverageRatio(polygon, rectangles);
        double overCoverageRatio = polygonArea > 0
            ? Math.Max(0, rectangleArea - polygonArea) / polygonArea
            : 0;

        return new PolygonDecompositionMetrics
        {
            PolygonAreaMm2 = polygonArea,
            RectangleCoverAreaMm2 = rectangleArea,
            CoverageRatio = coverageRatio,
            OverCoverageRatio = overCoverageRatio,
            CellSizeMm = cellSize,
            RectangleCount = rectangles.Count,
            UsedRectangularShortcut = usedRectangularShortcut
        };
    }

    private static double EstimatePolygonCoverageRatio(Polygon polygon, IReadOnlyList<BoundingBox> rectangles)
    {
        var bbox = polygon.GetBoundingBox();
        int insidePolygon = 0;
        int covered = 0;
        const int samplesPerAxis = 40;

        for (int ix = 0; ix < samplesPerAxis; ix++)
        {
            for (int iy = 0; iy < samplesPerAxis; iy++)
            {
                double sampleX = bbox.Min.X + (ix + 0.5) * bbox.Width / samplesPerAxis;
                double sampleY = bbox.Min.Y + (iy + 0.5) * bbox.Height / samplesPerAxis;
                var point = new Point2D(sampleX, sampleY);

                if (!IsPointInPolygon(point, polygon))
                    continue;

                insidePolygon++;
                if (rectangles.Any(rect => IsPointInBox(point, rect)))
                    covered++;
            }
        }

        return insidePolygon > 0 ? (double)covered / insidePolygon : 1.0;
    }

    private static bool DoesCellIntersectPolygon(BoundingBox cell, Polygon polygon)
    {
        var cellCorners = GetCellCorners(cell);
        for (int i = 0; i < cellCorners.Count; i++)
        {
            var a1 = cellCorners[i];
            var a2 = cellCorners[(i + 1) % cellCorners.Count];

            for (int j = 0; j < polygon.Vertices.Count; j++)
            {
                var b1 = polygon.Vertices[j];
                var b2 = polygon.Vertices[(j + 1) % polygon.Vertices.Count];

                if (SegmentsIntersect(a1, a2, b1, b2))
                    return true;
            }
        }

        return false;
    }

    private static IReadOnlyList<Point2D> GetCellCorners(BoundingBox cell)
    {
        return
        [
            new Point2D(cell.Min.X, cell.Min.Y),
            new Point2D(cell.Max.X, cell.Min.Y),
            new Point2D(cell.Max.X, cell.Max.Y),
            new Point2D(cell.Min.X, cell.Max.Y)
        ];
    }

    private static bool IsPointInBox(Point2D point, BoundingBox box, GeometryTolerance? tolerance = null)
    {
        tolerance ??= GeometryTolerance.Default;
        double tol = tolerance.LinearToleranceMm;
        return point.X >= box.Min.X - tol &&
               point.X <= box.Max.X + tol &&
               point.Y >= box.Min.Y - tol &&
               point.Y <= box.Max.Y + tol;
    }

    private static bool SegmentsIntersect(Point2D a1, Point2D a2, Point2D b1, Point2D b2, GeometryTolerance? tolerance = null)
    {
        tolerance ??= GeometryTolerance.Default;
        double o1 = Orientation(a1, a2, b1);
        double o2 = Orientation(a1, a2, b2);
        double o3 = Orientation(b1, b2, a1);
        double o4 = Orientation(b1, b2, a2);

        if ((o1 > tolerance.LinearToleranceMm && o2 < -tolerance.LinearToleranceMm || 
             o1 < -tolerance.LinearToleranceMm && o2 > tolerance.LinearToleranceMm) &&
            (o3 > tolerance.LinearToleranceMm && o4 < -tolerance.LinearToleranceMm || 
             o3 < -tolerance.LinearToleranceMm && o4 > tolerance.LinearToleranceMm))
        {
            return true;
        }

        return IsZero(o1, tolerance.LinearToleranceMm) && IsPointOnSegment(b1, a1, a2, tolerance) ||
               IsZero(o2, tolerance.LinearToleranceMm) && IsPointOnSegment(b2, a1, a2, tolerance) ||
               IsZero(o3, tolerance.LinearToleranceMm) && IsPointOnSegment(a1, b1, b2, tolerance) ||
               IsZero(o4, tolerance.LinearToleranceMm) && IsPointOnSegment(a2, b1, b2, tolerance);
    }

    private static double Orientation(Point2D a, Point2D b, Point2D c)
    {
        return (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
    }

    private static bool IsPointOnSegment(Point2D point, Point2D start, Point2D end, GeometryTolerance? tolerance = null)
    {
        tolerance ??= GeometryTolerance.Default;
        double tol = tolerance.LinearToleranceMm;
        return point.X >= Math.Min(start.X, end.X) - tol &&
               point.X <= Math.Max(start.X, end.X) + tol &&
               point.Y >= Math.Min(start.Y, end.Y) - tol &&
               point.Y <= Math.Max(start.Y, end.Y) + tol;
    }

    private static bool IsZero(double value, double tolerance = 1e-6)
    {
        return Math.Abs(value) <= tolerance;
    }

    private static bool NearlyEqual(double left, double right, double cellSize)
    {
        return Math.Abs(left - right) <= Math.Max(0.01, cellSize * 0.1);
    }
}

public sealed record PolygonDecompositionResult
{
    public required IReadOnlyList<BoundingBox> Rectangles { get; init; }
    public required PolygonDecompositionMetrics Metrics { get; init; }
}
