using FluentAssertions;
using OpenRebar.Application.UseCases;
using OpenRebar.Domain.Models;

namespace OpenRebar.Application.Tests;

public class GridReinforcementVerifierTests
{
  private readonly GridReinforcementVerifier _verifier = new();

  [Fact]
  public void Verify_FullSpacingGrid_PassesWithNoDeficit()
  {
    var result = _verifier.Verify([Zone(BarsAt(100, 300, 500, 700, 900))]);

    result.Status.Should().Be(VerificationStatuses.Passed);
    result.UnderReinforcedAreaM2.Should().Be(0);
    result.DeficitRegions.Should().BeEmpty();
    result.MinProvisionRatio.Should().BeGreaterThanOrEqualTo(1);
    result.CellSizeMm.Should().Be(50);
  }

  [Fact]
  public void Verify_RemovedBar_FailsAndReportsTheGap()
  {
    var result = _verifier.Verify([Zone(BarsAt(100, 300, 700, 900))]);

    result.Status.Should().Be(VerificationStatuses.Failed);
    result.UnderReinforcedAreaM2.Should().BeGreaterThan(0);
    result.MinProvisionRatio.Should().BeLessThan(1);
    result.DeficitRegions.Should().ContainSingle();
    var gap = result.DeficitRegions[0];
    gap.ZoneId.Should().Be("Z1");
    gap.MinY.Should().BeGreaterThan(350);
    gap.MaxY.Should().BeLessThan(650);
    gap.MinX.Should().Be(0);
    gap.MaxX.Should().Be(1000);
  }

  [Fact]
  public void Verify_ExplicitUnderCoverageAllowance_IsNotASilentPass()
  {
    var zone = Zone(BarsAt(100, 300, 700, 900));

    var failed = _verifier.Verify([zone], new VerificationSettings { UnderCoverageRatio = 0 });
    var allowed = _verifier.Verify([zone], new VerificationSettings { UnderCoverageRatio = 1 });

    failed.Status.Should().Be(VerificationStatuses.Failed);
    allowed.Status.Should().Be(VerificationStatuses.PassedWithUnderCoverage);
    allowed.DeficitRegions.Should().NotBeEmpty();
    allowed.UnderReinforcedAreaM2.Should().Be(failed.UnderReinforcedAreaM2);
  }

  [Fact]
  public void Verifier_UsesFieldDirectly()
  {
    var zone = Zone(BarsAt(100, 300, 500, 700, 900));
    zone.AsRequiredMm2PerM = 10_000;

    var result = _verifier.Verify([zone]);

    result.Status.Should().Be(VerificationStatuses.Failed);
    result.MinProvisionRatio.Should().BeLessThan(1);
  }

  [Fact]
  public void Verify_PeakSmoothing_WaivesTheStatusAndKeepsTheRawGap()
  {
    var result = _verifier.Verify(
        [Zone(BarsAt(100, 300, 700, 900))],
        new VerificationSettings { PeakSmoothingWindowMm = 250 });

    result.Status.Should().Be(VerificationStatuses.PassedWithSmoothing);
    result.PeakSmoothingWindowMm.Should().Be(250);
    result.DeficitRegions.Should().NotBeEmpty();
    result.UnderReinforcedAreaM2.Should().BeGreaterThan(0);
  }

  [Fact]
  public void Verify_StatedBackgroundAreaWithoutMeshBars_DoesNotPass()
  {
    var zone = Zone(BarsAt(100, 300, 500, 700, 900).Select(bar => bar with { DiameterMm = 10 }).ToList());
    zone.DesignLayer = LayerKey.BottomX;
    zone.AsRequiredMm2PerM = zone.Spec.AreaPerMeterMm2;
    zone.AsBackgroundMm2PerM = 10_000;

    _verifier.Verify([zone]).Status.Should().Be(VerificationStatuses.Failed);
  }

  [Fact]
  public void Verify_MeshBarsPlusAdditionalBars_MeetTheFullRequirement()
  {
    var zone = Zone(BarsAt(100, 300, 500, 700, 900).Select(bar => bar with { DiameterMm = 10 }).ToList());
    zone.DesignLayer = LayerKey.BottomX;
    zone.AsRequiredMm2PerM = zone.Spec.AreaPerMeterMm2;

    var mesh = Zone(BarsAt(0.1, 200, 400, 600, 800, 999.9).Select(bar => bar with { DiameterMm = 8 }).ToList());
    mesh.Id = "BottomX-background";
    mesh.DesignLayer = LayerKey.BottomX;
    mesh.Role = ZoneRole.Background;
    mesh.IsBackgroundMesh = true;
    mesh.AsRequiredMm2PerM = Math.PI * 8 * 8 / 4.0 * (1000.0 / 200.0);

    _verifier.Verify([zone, mesh]).Status.Should().Be(VerificationStatuses.Passed);
  }

  [Fact]
  public void Verify_Diameter12At200_Provides565Point5()
  {
    double expected = Math.PI * 12 * 12 / 4.0 * (1000.0 / 200.0);
    expected.Should().BeApproximately(565.5, 0.05);

    var zone = Zone(BarsAt(100, 300, 500, 700, 900));
    zone.AsRequiredMm2PerM = expected;

    var result = _verifier.Verify([zone]);

    result.Status.Should().Be(VerificationStatuses.Passed);
    result.MinMarginMm2PerM.Should().BeApproximately(0, 1e-6);
    result.UnderReinforcedCells.Should().Be(0);
    result.Layers.Should().ContainSingle(layer => layer.Layer == "Z1" && layer.Status == VerificationStatuses.Passed);
  }

  [Fact]
  public void Verify_DevelopmentIsZeroAtThePhysicalEnd()
  {
    var zone = Zone(
    [
        new RebarSegment
        {
          Start = new Point2D(-400, 100),
          End = new Point2D(400, 100),
          DiameterMm = 12,
          AnchorageLengthStart = 400,
          AnchorageLengthEnd = 0
        }
    ], new Polygon(
    [
        new Point2D(-400, 50),
        new Point2D(400, 50),
        new Point2D(400, 150),
        new Point2D(-400, 150)
    ]));
    zone.AsRequiredMm2PerM = Math.PI * 12 * 12 / 4.0 * (1000.0 / 200.0);

    var result = _verifier.Verify([zone]);

    result.Status.Should().Be(VerificationStatuses.Failed);
    result.UnderReinforcedCells.Should().BeGreaterThan(0);
    result.MinProvisionRatio.Should().BeLessThan(0.1);
    result.DeficitRegions.Should().OnlyContain(region => region.MaxX <= 0.1);
  }

  [Fact]
  public void Verify_CellOnTheJointOfTwoRuns_StaysCovered()
  {
    var strip = new Polygon(
    [
        new Point2D(0, 400),
        new Point2D(1000, 400),
        new Point2D(1000, 600),
        new Point2D(0, 600)
    ]);
    var leftOnly = Zone(BarsBetween(0, 500), strip);
    var both = Zone(BarsBetween(0, 500).Concat(BarsBetween(500, 1000)).ToList(), strip);

    _verifier.Verify([leftOnly]).Status.Should().Be(VerificationStatuses.Failed);
    _verifier.Verify([both]).Status.Should().Be(VerificationStatuses.Passed);
  }

  private static ReinforcementZone Zone(IReadOnlyList<RebarSegment> bars, Polygon? boundary = null) => new()
  {
    Id = "Z1",
    Boundary = boundary ?? new Polygon(
    [
        new Point2D(0, 0),
        new Point2D(1000, 0),
        new Point2D(1000, 1000),
        new Point2D(0, 1000)
    ]),
    Spec = new ReinforcementSpec { DiameterMm = 12, SpacingMm = 200, SteelClass = "A500C" },
    Direction = RebarDirection.X,
    ZoneType = ZoneType.Simple,
    Rebars = bars
  };

  private static IReadOnlyList<RebarSegment> BarsAt(params double[] ordinates) =>
      ordinates.Select(y => new RebarSegment
      {
        Start = new Point2D(0, y),
        End = new Point2D(1000, y),
        DiameterMm = 12,
        AnchorageLengthStart = 0,
        AnchorageLengthEnd = 0
      }).ToList();

  private static IReadOnlyList<RebarSegment> BarsBetween(double startX, double endX) =>
  [
      new RebarSegment
      {
        Start = new Point2D(startX, 500),
        End = new Point2D(endX, 500),
        DiameterMm = 12,
        AnchorageLengthStart = 0,
        AnchorageLengthEnd = 0
      }
  ];
}
