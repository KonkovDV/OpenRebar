using OpenRebar.Domain.Models;
using OpenRebar.Infrastructure.ZoneProcessing;
using FluentAssertions;

namespace OpenRebar.Infrastructure.Tests.ZoneProcessing;

public class StandardZoneDetectorTests
{
    private readonly StandardZoneDetector _detector = new();

    private static readonly SlabGeometry TestSlab = new()
    {
        OuterBoundary = new Polygon([
            new Point2D(0, 0), new Point2D(10000, 0),
            new Point2D(10000, 8000), new Point2D(0, 8000)
        ]),
        ThicknessMm = 200,
        CoverMm = 25,
        ConcreteClass = "B25"
    };

    [Fact]
    public void RectangularZone_ShouldClassifyAsSimple()
    {
        var zone = MakeZone([
            new Point2D(1000, 1000), new Point2D(4000, 1000),
            new Point2D(4000, 3000), new Point2D(1000, 3000)
        ]);

        var result = _detector.ClassifyAndDecompose([zone], TestSlab);

        result.Should().HaveCount(1);
        result[0].ZoneType.Should().Be(ZoneType.Simple);
    }

    [Fact]
    public void WideZone_ShouldHaveXDirection()
    {
        var zone = MakeZone([
            new Point2D(0, 0), new Point2D(5000, 0),
            new Point2D(5000, 2000), new Point2D(0, 2000)
        ]);

        var result = _detector.ClassifyAndDecompose([zone], TestSlab);

        result[0].Direction.Should().Be(RebarDirection.X, "wider than tall");
    }

    [Fact]
    public void TallZone_ShouldHaveYDirection()
    {
        var zone = MakeZone([
            new Point2D(0, 0), new Point2D(2000, 0),
            new Point2D(2000, 5000), new Point2D(0, 5000)
        ]);

        var result = _detector.ClassifyAndDecompose([zone], TestSlab);

        result[0].Direction.Should().Be(RebarDirection.Y, "taller than wide");
    }

    [Fact]
    public void TinyZone_ShouldBeFilteredOut()
    {
        // Zone area < 100_000 mm² (10cm × 10cm)
        var zone = MakeZone([
            new Point2D(0, 0), new Point2D(100, 0),
            new Point2D(100, 100), new Point2D(0, 100)
        ]);

        var result = _detector.ClassifyAndDecompose([zone], TestSlab);

        result.Should().BeEmpty("zone area is below threshold");
    }

    [Fact]
    public void ZoneOverlappingOpening_ShouldBeSpecial()
    {
        var opening = new Polygon([
            new Point2D(2000, 2000), new Point2D(3000, 2000),
            new Point2D(3000, 3000), new Point2D(2000, 3000)
        ]);

        var slabWithOpening = new SlabGeometry
        {
            OuterBoundary = TestSlab.OuterBoundary,
            Openings = [opening],
            ThicknessMm = 200,
            CoverMm = 25,
            ConcreteClass = "B25"
        };

        var zone = MakeZone([
            new Point2D(1500, 1500), new Point2D(3500, 1500),
            new Point2D(3500, 3500), new Point2D(1500, 3500)
        ]);

        var result = _detector.ClassifyAndDecompose([zone], slabWithOpening);

        result.Should().HaveCount(1);
        result[0].ZoneType.Should().Be(ZoneType.Special);
    }

    [Fact]
    public void ZoneWhoseBoundingBoxOverlapsOpeningButPolygonDoesNot_ShouldRemainComplex()
    {
        var opening = new Polygon([
            new Point2D(2000, 2000), new Point2D(3000, 2000),
            new Point2D(3000, 3000), new Point2D(2000, 3000)
        ]);

        var slabWithOpening = new SlabGeometry
        {
            OuterBoundary = TestSlab.OuterBoundary,
            Openings = [opening],
            ThicknessMm = 200,
            CoverMm = 25,
            ConcreteClass = "B25"
        };

        var zone = MakeZone([
            new Point2D(1000, 1000),
            new Point2D(4000, 1000),
            new Point2D(4000, 1500),
            new Point2D(1500, 1500),
            new Point2D(1500, 4000),
            new Point2D(1000, 4000)
        ]);

        var result = _detector.ClassifyAndDecompose([zone], slabWithOpening);

        result.Should().HaveCount(1);
        result[0].ZoneType.Should().Be(ZoneType.Complex,
            "the opening sits inside the L-shape notch, so bbox overlap alone must not trigger special classification");
    }

    [Fact]
    public void MultipleZones_ShouldAllBeClassified()
    {
        var zones = new[]
        {
            MakeZone([
                new Point2D(0, 0), new Point2D(4000, 0),
                new Point2D(4000, 2000), new Point2D(0, 2000)
            ]),
            MakeZone([
                new Point2D(5000, 0), new Point2D(8000, 0),
                new Point2D(8000, 3000), new Point2D(5000, 3000)
            ])
        };

        var result = _detector.ClassifyAndDecompose(zones, TestSlab);

        result.Should().HaveCount(2);
    }

    private static ReinforcementZone MakeZone(Point2D[] vertices)
    {
        return new ReinforcementZone
        {
            Id = "TEST",
            Boundary = new Polygon(vertices),
            Spec = new ReinforcementSpec { DiameterMm = 12, SpacingMm = 200, SteelClass = "A500C" },
            Direction = RebarDirection.X,
            ZoneType = ZoneType.Simple
        };
    }
}
