namespace A101.Domain.Models;

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
