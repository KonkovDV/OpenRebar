using OpenRebar.Domain.Models;

namespace OpenRebar.Domain.Rules;

/// <summary>
/// Algorithms for decomposing complex polygons into minimal rectangles.
/// Used for non-rectangular reinforcement zones (around openings, L-shapes, etc.).
/// </summary>
public static class PolygonDecomposition
{
    /// <summary>
    /// Decompose a polygon into a set of axis-aligned rectangles.
    /// Uses a greedy sweep-line approach: finds the largest inscribed rectangle,
    /// subtracts it, and repeats.
    /// </summary>
    /// <param name="polygon">Input polygon to decompose.</param>
    /// <param name="minAreaMm2">Minimum rectangle area to keep (mm²).</param>
    /// <returns>List of rectangles covering the polygon.</returns>
    public static IReadOnlyList<BoundingBox> DecomposeToRectangles(
        Polygon polygon,
        double minAreaMm2 = 10_000) // 100mm × 100mm minimum
    {
        var bbox = polygon.GetBoundingBox();
        var result = new List<BoundingBox>();

        // For MVP: if the polygon bounding box is close to the polygon area,
        // treat the whole zone as one rectangle.
        double polygonArea = polygon.CalculateArea();
        double bboxArea = bbox.Area;
        double fillRatio = polygonArea / bboxArea;

        if (fillRatio > 0.85)
        {
            // Nearly rectangular — use bounding box directly
            result.Add(bbox);
            return result;
        }

        // For complex shapes: grid-based decomposition
        // Subdivide bounding box into cells, keep cells that overlap with polygon
        double cellSize = Math.Max(bbox.Width, bbox.Height) / 20.0;
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

                if (cell.Area >= minAreaMm2 && IsCellInsidePolygon(cell, polygon))
                {
                    cells.Add(cell);
                }
            }
        }

        // Merge adjacent cells into larger rectangles (greedy row merge)
        result.AddRange(MergeAdjacentCells(cells, cellSize));

        return result.Count > 0 ? result : [bbox]; // Fallback to bbox
    }

    /// <summary>
    /// Check if a cell center is inside the polygon (ray casting).
    /// </summary>
    private static bool IsCellInsidePolygon(BoundingBox cell, Polygon polygon)
    {
        var center = cell.Center;
        return IsPointInPolygon(center, polygon);
    }

    /// <summary>
    /// Ray casting algorithm for point-in-polygon test.
    /// </summary>
    public static bool IsPointInPolygon(Point2D point, Polygon polygon)
    {
        var vertices = polygon.Vertices;
        bool inside = false;
        int n = vertices.Count;

        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            if ((vertices[i].Y > point.Y) != (vertices[j].Y > point.Y) &&
                point.X < (vertices[j].X - vertices[i].X) * (point.Y - vertices[i].Y)
                    / (vertices[j].Y - vertices[i].Y) + vertices[i].X)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    /// <summary>
    /// Merge adjacent cells into larger rectangles.
    /// </summary>
    private static IReadOnlyList<BoundingBox> MergeAdjacentCells(
        List<BoundingBox> cells, double cellSize)
    {
        if (cells.Count == 0) return [];

        // Sort by Y then X for row-based merge
        var sorted = cells.OrderBy(c => c.Min.Y).ThenBy(c => c.Min.X).ToList();
        var merged = new List<BoundingBox>();
        var used = new HashSet<int>();

        for (int i = 0; i < sorted.Count; i++)
        {
            if (used.Contains(i)) continue;

            var current = sorted[i];
            used.Add(i);

            // Extend rightward
            for (int j = i + 1; j < sorted.Count; j++)
            {
                if (used.Contains(j)) continue;

                var next = sorted[j];
                if (Math.Abs(next.Min.Y - current.Min.Y) < 0.01 &&
                    Math.Abs(next.Min.X - current.Max.X) < cellSize * 0.1)
                {
                    current = new BoundingBox(current.Min, next.Max);
                    used.Add(j);
                }
            }

            merged.Add(current);
        }

        return merged;
    }
}
