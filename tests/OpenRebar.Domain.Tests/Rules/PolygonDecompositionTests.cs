using OpenRebar.Domain.Models;
using OpenRebar.Domain.Rules;
using FluentAssertions;

namespace OpenRebar.Domain.Tests.Rules;

public class PolygonDecompositionTests
{
    [Fact(DisplayName = "SP 63 §6.1 — Geometric Decomposition: Rectangle Shortcut")]
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

    [Fact(DisplayName = "SP 63 §6.1 — Point-in-Polygon Test (Inside)")]
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

    [Fact(DisplayName = "SP 63 §6.1 — Point-in-Polygon Test (Outside)")]
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

    [Fact(DisplayName = "SP 63 §6.1 — Area Calculation (Rectangle)")]
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

    [Fact(DisplayName = "SP 63 §6.1 — Area Calculation (Triangle)")]
    public void PolygonArea_Triangle_ShouldBeCorrect()
    {
        var polygon = new Polygon([
            new Point2D(0, 0),
            new Point2D(4000, 0),
            new Point2D(0, 3000),
        ]);

        polygon.CalculateArea().Should().BeApproximately(6_000_000, 1); // 0.5 * 4000 * 3000
    }

    [Fact(DisplayName = "SP 63 §6.1 — Rectangular Decomposition with Metrics")]
    public void DecomposeWithMetrics_Rectangle_ShouldUseRectangularShortcut()
    {
        var polygon = new Polygon([
            new Point2D(0, 0),
            new Point2D(4000, 0),
            new Point2D(4000, 2000),
            new Point2D(0, 2000)
        ]);

        var result = PolygonDecomposition.DecomposeWithMetrics(polygon);

        result.Rectangles.Should().HaveCount(1);
        result.Metrics.UsedRectangularShortcut.Should().BeTrue();
        result.Metrics.CoverageRatio.Should().BeApproximately(1.0, 0.001);
        result.Metrics.OverCoverageRatio.Should().BeApproximately(0.0, 0.001);
    }

    [Fact(DisplayName = "SP 63 §6.1 — Decomposition with Coverage Metrics (L-Shape)")]
    public void DecomposeWithMetrics_LShape_ShouldProvideCoverageEvidence()
    {
        var polygon = new Polygon([
            new Point2D(0, 0),
            new Point2D(4000, 0),
            new Point2D(4000, 1000),
            new Point2D(1000, 1000),
            new Point2D(1000, 4000),
            new Point2D(0, 4000)
        ]);

        var result = PolygonDecomposition.DecomposeWithMetrics(polygon);

        result.Rectangles.Should().NotBeEmpty();
        result.Metrics.UsedRectangularShortcut.Should().BeFalse();
        result.Metrics.RectangleCount.Should().Be(result.Rectangles.Count);
        result.Metrics.CoverageRatio.Should().BeGreaterThan(0.94,
            "the decomposition should cover almost all of the polygon area");
        result.Metrics.OverCoverageRatio.Should().BeLessThan(0.25,
            "the decomposition should not explode over-coverage for a simple L-shape");
    }

    [Fact(DisplayName = "SP 63 §6.1 — Exact Coverage for Orthogonal Corridor")]
    public void DecomposeWithMetrics_ThinOrthogonalCorridor_ShouldUseExactCoveragePath()
    {
        var polygon = new Polygon([
            new Point2D(0, 0),
            new Point2D(5000, 0),
            new Point2D(5000, 200),
            new Point2D(200, 200),
            new Point2D(200, 5000),
            new Point2D(0, 5000)
        ]);

        var result = PolygonDecomposition.DecomposeWithMetrics(polygon);

        result.Rectangles.Should().NotBeEmpty();
        result.Metrics.UsedRectangularShortcut.Should().BeFalse();
        result.Metrics.CoverageRatio.Should().BeApproximately(1.0, 0.001);
        result.Metrics.OverCoverageRatio.Should().BeApproximately(0.0, 0.001);
    }

    [Fact(DisplayName = "SP 63 §6.1 — Complex Concave Geometry")]
    public void DecomposeWithMetrics_StronglyConcaveOrthogonalShape_ShouldRetainExactCoverage()
    {
        var polygon = new Polygon([
            new Point2D(0, 0),
            new Point2D(7000, 0),
            new Point2D(7000, 1000),
            new Point2D(1000, 1000),
            new Point2D(1000, 3000),
            new Point2D(6000, 3000),
            new Point2D(6000, 4000),
            new Point2D(0, 4000)
        ]);

        var result = PolygonDecomposition.DecomposeWithMetrics(polygon);

        result.Rectangles.Should().HaveCountGreaterThan(1);
        result.Metrics.CoverageRatio.Should().BeApproximately(1.0, 0.001);
        result.Metrics.OverCoverageRatio.Should().BeApproximately(0.0, 0.001);
    }
}
