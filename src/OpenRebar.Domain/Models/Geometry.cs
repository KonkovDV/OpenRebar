namespace OpenRebar.Domain.Models;

/// <summary>
/// Tolerance parameters for geometric operations and predicates.
/// Allows robust handling of floating-point coordinates and epsilon-based comparisons.
/// </summary>
public sealed record GeometryTolerance
{
    /// <summary>
    /// Shared computational epsilon (mm) for low-level geometric predicates.
    /// Keep this value stable to avoid test and runtime drift across adapters.
    /// </summary>
    public const double ComputationalEpsilonMm = 1e-6;

    /// <summary>
    /// Linear tolerance in millimeters for point-in-polygon and line-intersection tests.
    /// Default: 0.01 mm (10 micrometers) for typical CAD precision.
    /// </summary>
    public double LinearToleranceMm { get; init; } = 0.01;

    /// <summary>
    /// Area ratio tolerance for coverage verification (0-1 scale).
    /// Default: 0.05 (5% variation acceptable for polygon decomposition coverage/overcoverage).
    /// </summary>
    public double AreaRatioTolerance { get; init; } = 0.05;

    /// <summary>
    /// Validates that tolerance parameters are in valid range.
    /// </summary>
    public void Validate()
    {
        if (LinearToleranceMm < 0 || LinearToleranceMm > 1)
            throw new ArgumentOutOfRangeException(nameof(LinearToleranceMm), 
                "Linear tolerance must be between 0 and 1 mm");
        if (AreaRatioTolerance < 0 || AreaRatioTolerance > 1)
            throw new ArgumentOutOfRangeException(nameof(AreaRatioTolerance),
                "Area ratio tolerance must be between 0 and 1");
    }

    /// <summary>
    /// Returns default tolerance (0.01 mm linear, 5% area).
    /// </summary>
    public static GeometryTolerance Default => new();

    /// <summary>
    /// Returns strict tolerance (0.001 mm linear, 1% area) for high-precision work.
    /// </summary>
    public static GeometryTolerance Strict => new()
    {
        LinearToleranceMm = 0.001,
        AreaRatioTolerance = 0.01
    };

    /// <summary>
    /// Returns computational tolerance profile used by low-level predicates.
    /// This preserves legacy epsilon-based behavior while centralizing policy.
    /// </summary>
    public static GeometryTolerance Computational => new()
    {
        LinearToleranceMm = ComputationalEpsilonMm,
        AreaRatioTolerance = 0.05
    };

    /// <summary>
    /// Returns relaxed tolerance (0.1 mm linear, 10% area) for coarse geometries.
    /// </summary>
    public static GeometryTolerance Relaxed => new()
    {
        LinearToleranceMm = 0.1,
        AreaRatioTolerance = 0.1
    };
}

/// <summary>
/// 2D point in slab coordinate system (millimeters).
/// </summary>
public readonly record struct Point2D(double X, double Y)
{
    public double DistanceTo(Point2D other) =>
        Math.Sqrt(Math.Pow(X - other.X, 2) + Math.Pow(Y - other.Y, 2));

    public static Point2D operator +(Point2D a, Point2D b) => new(a.X + b.X, a.Y + b.Y);
    public static Point2D operator -(Point2D a, Point2D b) => new(a.X - b.X, a.Y - b.Y);
}

/// <summary>
/// Axis-aligned bounding box.
/// </summary>
public readonly record struct BoundingBox(Point2D Min, Point2D Max)
{
    public double Width => Max.X - Min.X;
    public double Height => Max.Y - Min.Y;
    public double Area => Width * Height;
    public Point2D Center => new((Min.X + Max.X) / 2, (Min.Y + Max.Y) / 2);
}

/// <summary>
/// Closed polygon defined by an ordered list of vertices.
/// </summary>
public sealed class Polygon
{
    public IReadOnlyList<Point2D> Vertices { get; }

    public Polygon(IReadOnlyList<Point2D> vertices)
    {
        if (vertices.Count < 3)
            throw new ArgumentException("Polygon requires at least 3 vertices.", nameof(vertices));

        Vertices = vertices;
    }

    public BoundingBox GetBoundingBox()
    {
        var minX = Vertices.Min(v => v.X);
        var minY = Vertices.Min(v => v.Y);
        var maxX = Vertices.Max(v => v.X);
        var maxY = Vertices.Max(v => v.Y);
        return new BoundingBox(new Point2D(minX, minY), new Point2D(maxX, maxY));
    }

    public double CalculateArea()
    {
        // Shoelace formula
        double area = 0;
        int n = Vertices.Count;
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            area += Vertices[i].X * Vertices[j].Y;
            area -= Vertices[j].X * Vertices[i].Y;
        }
        return Math.Abs(area) / 2.0;
    }
}
