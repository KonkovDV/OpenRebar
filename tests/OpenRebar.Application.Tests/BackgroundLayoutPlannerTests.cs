using FluentAssertions;
using OpenRebar.Application.UseCases;
using OpenRebar.Domain.Models;

namespace OpenRebar.Application.Tests;

public class BackgroundLayoutPlannerTests
{
  private static readonly int[] Diameters = [10, 12, 16, 20, 25];

  [Fact]
  public void Apply_WithoutBackground_LeavesTheZoneAndAddsNoMesh()
  {
    var zone = Zone(required: 565);

    var plan = BackgroundLayoutPlanner.Apply([zone], [Layer(background: null)], Slab(), Diameters, "interleave");

    plan.NeedsHumanDecision.Should().BeFalse();
    plan.Zones.Should().ContainSingle().Which.Should().BeSameAs(zone);
    zone.SuppressLayout.Should().BeFalse();
    zone.LayoutSpec.Should().BeNull();
  }

  [Fact]
  public void Apply_WhenRequiredDoesNotExceedBackground_SuppressesClassBars()
  {
    var zone = Zone(required: 200);

    var plan = BackgroundLayoutPlanner.Apply([zone], [Layer(Background())], Slab(), Diameters, "interleave");

    plan.NeedsHumanDecision.Should().BeFalse();
    zone.SuppressLayout.Should().BeTrue();
    zone.Role.Should().Be(ZoneRole.Background);
    zone.LayoutSpec.Should().BeNull();
    var mesh = plan.Zones.Single(item => item.IsBackgroundMesh);
    mesh.Id.Should().Be("BottomX-background");
    mesh.Spec.DiameterMm.Should().Be(10);
    mesh.GridMode.Should().Be(LayerGridMode.Mesh);
    mesh.Direction.Should().Be(RebarDirection.X);
  }

  [Fact]
  public void Apply_SelectsTheLightestAdditionalDiameterOnTheBackgroundGrid()
  {
    var zone = Zone(required: 565.5);

    var plan = BackgroundLayoutPlanner.Apply([zone], [Layer(Background())], Slab(), Diameters, "interleave");

    plan.NeedsHumanDecision.Should().BeFalse();
    zone.SuppressLayout.Should().BeFalse();
    zone.Role.Should().Be(ZoneRole.Additional);
    zone.EffectiveSpec.DiameterMm.Should().Be(10);
    zone.EffectiveSpec.SpacingMm.Should().Be(200);
    zone.GridMode.Should().Be(LayerGridMode.Interleave);
    zone.Spec.DiameterMm.Should().Be(12);
  }

  [Fact]
  public void Apply_WhenNoDiameterCoversTheDelta_AsksForADecision()
  {
    var zone = Zone(required: 5000);

    var plan = BackgroundLayoutPlanner.Apply([zone], [Layer(Background())], Slab(), Diameters, "interleave");

    plan.NeedsHumanDecision.Should().BeTrue();
    zone.SuppressLayout.Should().BeTrue();
    zone.LayoutSpec.Should().BeNull();
    plan.Warnings.Should().ContainSingle().Which.Should().Contain("needs a human decision");
  }

  [Fact]
  public void Apply_HalfSpacingMode_DoesNotInventASpacing()
  {
    var zone = Zone(required: 565.5);

    var plan = BackgroundLayoutPlanner.Apply([zone], [Layer(Background())], Slab(), Diameters, "s/2");

    plan.NeedsHumanDecision.Should().BeTrue();
    zone.SuppressLayout.Should().BeTrue();
    zone.LayoutSpec.Should().BeNull();
    plan.Warnings.Should().ContainSingle().Which.Should().Contain("s/2");
  }

  private static ReinforcementZone Zone(double required) => new()
  {
    Id = "Z1",
    Boundary = Rectangle(0, 0, 2000, 2000),
    Spec = new ReinforcementSpec { DiameterMm = 12, SpacingMm = 200, SteelClass = "A500C" },
    Direction = RebarDirection.X,
    ZoneType = ZoneType.Simple,
    DesignLayer = LayerKey.BottomX,
    AsRequiredMm2PerM = required
  };

  private static LayerDesignInput Layer(BackgroundMesh? background) => new()
  {
    Layer = LayerKey.BottomX,
    IsolineFilePath = "floor.dxf",
    Legend = new ColorLegend(
    [
        new LegendEntry(
            new IsolineColor(255, 0, 0),
            new ReinforcementSpec { DiameterMm = 12, SpacingMm = 200, SteelClass = "A500C" })
    ]),
    Background = background
  };

  private static BackgroundMesh Background() => new(
      LayerKey.BottomX,
      new ReinforcementSpec { DiameterMm = 10, SpacingMm = 200, SteelClass = "A500C" },
      0);

  private static SlabGeometry Slab() => new()
  {
    OuterBoundary = Rectangle(0, 0, 6000, 4000),
    ThicknessMm = 200,
    CoverMm = 25,
    ConcreteClass = "B25"
  };

  private static Polygon Rectangle(double minX, double minY, double maxX, double maxY) => new(
  [
      new Point2D(minX, minY),
      new Point2D(maxX, minY),
      new Point2D(maxX, maxY),
      new Point2D(minX, maxY)
  ]);
}
