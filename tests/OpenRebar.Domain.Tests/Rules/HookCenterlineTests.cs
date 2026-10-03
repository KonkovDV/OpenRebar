using FluentAssertions;
using OpenRebar.Domain.Models;
using OpenRebar.Domain.Rules;

namespace OpenRebar.Domain.Tests.Rules;

public class HookCenterlineTests
{
  [Fact]
  public void DefaultProfile_KeepsAZeroTail()
  {
    var hooks = new HooksProfile();

    hooks.Source.Should().Be("default");
    hooks.TailLengthMm(20).Should().Be(0);
    new CompanyProfile
    {
      Id = "plain",
      Version = "1",
      SteelClass = "A500C",
      ConcreteClass = "B25",
      Additional = new AdditionalReinforcementProfile { DiametersMm = [12], SpacingMode = "interleave" },
      Ends = new EndConditionProfile { Condition = "NeedsHook" },
      Laps = new LapProfile { JointRatioMax = 0.5, Couplers = false },
      Positions = new PositionProfile { IncludeLayerInKey = false },
      Schedule = new ScheduleProfile { Culture = "invariant" },
      Supply = new SupplyProfile
      {
        SupplierName = "Test",
        StockLengthsMm = [6000],
        SpecialLengths = false,
        Offcuts = false
      },
      Smoothing = new SmoothingProfile { Allowed = false },
      Verification = new VerificationProfile { UnderCoverageRatio = 0 },
      Safety = new SafetyProfile { Factor = 1, AnchorageLengthFactor = 1, LapLengthFactor = 1 },
      Legend = [new LegendSwatch { Color = [0, 0, 0], DiameterMm = 12, SpacingMm = 200 }]
    }.Hooks.TailDiameters.Should().Be(0);
  }

  [Fact]
  public void Profile_UsesTheSuppliedMultiple()
  {
    var hooks = new HooksProfile { TailDiameters = 6, Source = "owner" };

    hooks.TailLengthMm(20).Should().Be(120);
    hooks.Source.Should().Be("owner");
  }

  [Fact]
  public void StraightBar_IsOneLine()
  {
    bool drawn = HookCenterline.TryBuild(Bar(BarShape.Straight, BarEndCondition.Straight, BarEndCondition.Straight, 0), 40, 0, true, out var spans, out var skip);

    drawn.Should().BeTrue();
    skip.Should().BeNull();
    spans.Should().ContainSingle();
    spans[0].Through.Should().BeNull();
    spans[0].Start.Z.Should().Be(40);
    spans[0].End.X.Should().Be(1000);
  }

  [Fact]
  public void Hook_IsASemicircleWithoutATail()
  {
    double radius = BendRules.CenterlineRadiusMm(20, "A500C");
    var segment = Bar(BarShape.Hooked, BarEndCondition.NeedsHook, BarEndCondition.NeedsHook, BendRules.InnerRadiusMm(20, "A500C"));

    bool drawn = HookCenterline.TryBuild(segment, 40, 0, bendUp: true, out var spans, out _);

    drawn.Should().BeTrue();
    spans.Should().HaveCount(3);
    spans[1].Through.Should().BeNull();
    spans[0].Through.Should().NotBeNull();
    spans[2].Through.Should().NotBeNull();
    OnSemicircle(spans[2], new HookCenterline.Point(1000, 0, 40), radius, 1).Should().BeTrue();
    OnSemicircle(spans[0], new HookCenterline.Point(0, 0, 40), radius, 1).Should().BeTrue();
    spans[2].End.Z.Should().BeApproximately(40 + 2 * radius, 1e-6);
  }

  [Fact]
  public void LBar_IsNotDrawn()
  {
    bool drawn = HookCenterline.TryBuild(
        Bar(BarShape.L, BarEndCondition.NeedsLBar, BarEndCondition.Straight, BendRules.InnerRadiusMm(20, "A500C")),
        40,
        0,
        true,
        out _,
        out var skip);

    drawn.Should().BeFalse();
    skip.Should().Contain("L and U");
  }

  private static bool OnSemicircle(HookCenterline.Span arc, HookCenterline.Point joint, double radius, double hz)
  {
    var center = new HookCenterline.Point(joint.X, joint.Y, joint.Z + hz * radius);
    var through = arc.Through!.Value;
    return Math.Abs(Distance(arc.Start, center) - radius) < 1e-6
        && Math.Abs(Distance(arc.End, center) - radius) < 1e-6
        && Math.Abs(Distance(through, center) - radius) < 1e-6
        && Math.Abs(Distance(arc.Start, arc.End) - 2 * radius) < 1e-6;
  }

  private static double Distance(HookCenterline.Point left, HookCenterline.Point right)
  {
    double dx = left.X - right.X;
    double dy = left.Y - right.Y;
    double dz = left.Z - right.Z;
    return Math.Sqrt(dx * dx + dy * dy + dz * dz);
  }

  private static RebarSegment Bar(BarShape shape, BarEndCondition start, BarEndCondition end, double innerRadius) => new()
  {
    Start = new Point2D(0, 0),
    End = new Point2D(1000, 0),
    DiameterMm = 20,
    AnchorageLengthStart = 0,
    AnchorageLengthEnd = 0,
    Shape = shape,
    ShapeCode = shape == BarShape.Straight ? "00" : "H",
    BendRadiusMm = innerRadius,
    BendArcMm = shape == BarShape.Straight ? 0 : 1,
    EndConditionStart = start,
    EndConditionEnd = end
  };
}
