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
