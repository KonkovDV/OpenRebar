using A101.Domain.Models;
using A101.Domain.Rules;
using FluentAssertions;

namespace A101.Domain.Tests.Rules;

public class PolygonDecompositionTests
{
    [Fact]
    public void DecomposeRectangle_ShouldReturnSingleBbox()
    {
        // A rectangular polygon should decompose to a single bounding box
        var polygon = new Polygon([
            new Point2D(0, 0),
            new Point2D(3000, 0),
            new Point2D(3000, 2000),
            new Point2D(0, 2000)
        ]);

        var result = PolygonDecomposition.DecomposeToRectangles(polygon);

        result.Should().HaveCount(1);
        result[0].Width.Should().BeApproximately(3000, 1);
        result[0].Height.Should().BeApproximately(2000, 1);
    }

    [Fact]
    public void PointInPolygon_InsideRectangle_ShouldReturnTrue()
    {
        var polygon = new Polygon([
            new Point2D(0, 0),
            new Point2D(1000, 0),
            new Point2D(1000, 1000),
            new Point2D(0, 1000)
        ]);

        PolygonDecomposition.IsPointInPolygon(new Point2D(500, 500), polygon)
            .Should().BeTrue();
    }

    [Fact]
    public void PointInPolygon_OutsideRectangle_ShouldReturnFalse()
    {
        var polygon = new Polygon([
            new Point2D(0, 0),
            new Point2D(1000, 0),
            new Point2D(1000, 1000),
            new Point2D(0, 1000)
        ]);

        PolygonDecomposition.IsPointInPolygon(new Point2D(1500, 500), polygon)
            .Should().BeFalse();
    }

    [Fact]
    public void PolygonArea_Rectangle_ShouldBeCorrect()
    {
        var polygon = new Polygon([
            new Point2D(0, 0),
            new Point2D(3000, 0),
            new Point2D(3000, 2000),
            new Point2D(0, 2000)
        ]);

        polygon.CalculateArea().Should().BeApproximately(6_000_000, 1); // 3000 * 2000
    }

    [Fact]
    public void PolygonArea_Triangle_ShouldBeCorrect()
    {
        var polygon = new Polygon([
            new Point2D(0, 0),
            new Point2D(4000, 0),
            new Point2D(0, 3000),
        ]);

        polygon.CalculateArea().Should().BeApproximately(6_000_000, 1); // 0.5 * 4000 * 3000
    }
}
