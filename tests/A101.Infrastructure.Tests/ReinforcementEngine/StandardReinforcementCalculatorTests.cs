using A101.Domain.Models;
using A101.Infrastructure.ReinforcementEngine;
using FluentAssertions;

namespace A101.Infrastructure.Tests.ReinforcementEngine;

public class StandardReinforcementCalculatorTests
{
    private readonly StandardReinforcementCalculator _calculator = new();

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
    public void CalculateRebars_XDirection_ShouldGenerateHorizontalBars()
    {
        var zone = MakeZone(RebarDirection.X, RebarLayer.Bottom,
            new BoundingBox(new Point2D(0, 0), new Point2D(3000, 2000)));

        var result = _calculator.CalculateRebars([zone], TestSlab);

        result.Should().HaveCount(1);
        var rebars = result[0].Rebars;
        rebars.Should().NotBeEmpty();

        // All rebars should run horizontally (same Y per bar, varying X)
        foreach (var bar in rebars)
        {
            bar.Start.Y.Should().Be(bar.End.Y);
            bar.Start.X.Should().BeLessThan(bar.End.X);
        }
    }

    [Fact]
    public void CalculateRebars_YDirection_ShouldGenerateVerticalBars()
    {
        var zone = MakeZone(RebarDirection.Y, RebarLayer.Bottom,
            new BoundingBox(new Point2D(0, 0), new Point2D(2000, 3000)));

        var result = _calculator.CalculateRebars([zone], TestSlab);

        var rebars = result[0].Rebars;
        rebars.Should().NotBeEmpty();

        // All rebars should run vertically (same X per bar, varying Y)
        foreach (var bar in rebars)
        {
            bar.Start.X.Should().Be(bar.End.X);
            bar.Start.Y.Should().BeLessThan(bar.End.Y);
        }
    }

    [Fact]
    public void CalculateRebars_TopLayer_ShouldUsePoorBondCondition()
    {
        var zoneBottom = MakeZone(RebarDirection.X, RebarLayer.Bottom,
            new BoundingBox(new Point2D(0, 0), new Point2D(3000, 2000)));
        var zoneTop = MakeZone(RebarDirection.X, RebarLayer.Top,
            new BoundingBox(new Point2D(0, 0), new Point2D(3000, 2000)));

        _calculator.CalculateRebars([zoneBottom], TestSlab);
        var bottomAnch = zoneBottom.Rebars[0].AnchorageLengthStart;

        var calc2 = new StandardReinforcementCalculator();
        calc2.CalculateRebars([zoneTop], TestSlab);
        var topAnch = zoneTop.Rebars[0].AnchorageLengthStart;

        // Top bars (poor bond) should have LONGER anchorage than bottom (good bond)
        topAnch.Should().BeGreaterThan(bottomAnch);
    }

    [Fact]
    public void CalculateRebars_ShouldAssignMarks()
    {
        var zone = MakeZone(RebarDirection.X, RebarLayer.Bottom,
            new BoundingBox(new Point2D(0, 0), new Point2D(3000, 2000)));

        _calculator.CalculateRebars([zone], TestSlab);

        zone.Rebars.Should().AllSatisfy(r => r.Mark.Should().NotBeNullOrEmpty());
    }

    [Fact]
    public void CalculateRebars_SpacingShouldMatchSpec()
    {
        var zone = MakeZone(RebarDirection.X, RebarLayer.Bottom,
            new BoundingBox(new Point2D(0, 0), new Point2D(3000, 2000)));

        _calculator.CalculateRebars([zone], TestSlab);

        var rebars = zone.Rebars;
        if (rebars.Count >= 2)
        {
            double spacing = rebars[1].Start.Y - rebars[0].Start.Y;
            spacing.Should().Be(200, "spec spacing is 200mm");
        }
    }

    [Fact]
    public void CalculateRebars_AnchorageLengthShouldBePositive()
    {
        var zone = MakeZone(RebarDirection.X, RebarLayer.Bottom,
            new BoundingBox(new Point2D(0, 0), new Point2D(5000, 3000)));

        _calculator.CalculateRebars([zone], TestSlab);

        zone.Rebars.Should().AllSatisfy(r =>
        {
            r.AnchorageLengthStart.Should().BeGreaterThan(0);
            r.AnchorageLengthEnd.Should().BeGreaterThan(0);
            r.TotalLength.Should().BeGreaterThan(r.ClearSpan);
        });
    }

    [Fact]
    public void CalculateRebars_LShapedZone_ShouldClipBarsToPolygon()
    {
        var zone = new ReinforcementZone
        {
            Id = "L-ZONE",
            Boundary = new Polygon([
                new Point2D(0, 0),
                new Point2D(4000, 0),
                new Point2D(4000, 1000),
                new Point2D(1000, 1000),
                new Point2D(1000, 4000),
                new Point2D(0, 4000)
            ]),
            Spec = new ReinforcementSpec { DiameterMm = 12, SpacingMm = 200, SteelClass = "A500C" },
            Direction = RebarDirection.X,
            ZoneType = ZoneType.Complex,
            Layer = RebarLayer.Bottom
        };

        _calculator.CalculateRebars([zone], TestSlab);

        zone.Rebars.Should().NotBeEmpty();

        var upperBars = zone.Rebars.Where(r => r.Start.Y > 1000).ToList();
        upperBars.Should().NotBeEmpty();
        upperBars.Should().OnlyContain(r => r.End.X <= 1000 + 0.001,
            "bars above the notch must be clipped to the narrow leg of the L-shape");

        var lowerBars = zone.Rebars.Where(r => r.Start.Y < 1000).ToList();
        lowerBars.Should().Contain(r => r.End.X > 3000,
            "bars in the lower strip should still span the wide part of the polygon");
    }

    [Fact]
    public void CalculateRebars_ShouldSplitBarsAroundOpening()
    {
        var slabWithOpening = new SlabGeometry
        {
            OuterBoundary = TestSlab.OuterBoundary,
            Openings =
            [
                new Polygon([
                    new Point2D(1500, 1500),
                    new Point2D(2500, 1500),
                    new Point2D(2500, 2500),
                    new Point2D(1500, 2500)
                ])
            ],
            ThicknessMm = 200,
            CoverMm = 25,
            ConcreteClass = "B25"
        };

        var zone = MakeZone(RebarDirection.X, RebarLayer.Bottom,
            new BoundingBox(new Point2D(0, 0), new Point2D(4000, 4000)));

        _calculator.CalculateRebars([zone], slabWithOpening);

        var openingBandBars = zone.Rebars
            .Where(r => r.Start.Y > 1500 && r.Start.Y < 2500)
            .OrderBy(r => r.Start.X)
            .ToList();

        openingBandBars.Should().NotBeEmpty();
        openingBandBars.Should().Contain(r => r.End.X <= 1500 + 0.001,
            "bars before the opening should stop at the opening edge");
        openingBandBars.Should().Contain(r => r.Start.X >= 2500 - 0.001,
            "bars after the opening should restart after the opening edge");
        openingBandBars.Should().NotContain(r => r.Start.X < 1500 && r.End.X > 2500,
            "no bar may pass through the opening span");
    }

    [Fact]
    public void CalculateRebars_EmptyZones_ShouldReturnEmptyList()
    {
        var result = _calculator.CalculateRebars([], TestSlab);
        result.Should().BeEmpty();
    }

    private static ReinforcementZone MakeZone(
        RebarDirection direction, RebarLayer layer, BoundingBox bbox)
    {
        var polygon = new Polygon([
            bbox.Min,
            new Point2D(bbox.Max.X, bbox.Min.Y),
            bbox.Max,
            new Point2D(bbox.Min.X, bbox.Max.Y)
        ]);

        return new ReinforcementZone
        {
            Id = "TEST-001",
            Boundary = polygon,
            Spec = new ReinforcementSpec { DiameterMm = 12, SpacingMm = 200, SteelClass = "A500C" },
            Direction = direction,
            ZoneType = ZoneType.Simple,
            Layer = layer
        };
    }
}
