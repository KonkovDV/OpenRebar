using OpenRebar.Domain.Models;
using OpenRebar.Domain.Ports;
using FluentAssertions;

namespace OpenRebar.Domain.Tests.Models;

public class ColorLegendTests
{
  [Fact]
  public void Constructor_EmptyLegend_ShouldThrow()
  {
    var act = () => new ColorLegend([]);
    act.Should().Throw<ArgumentException>();
  }

  [Fact]
  public void Constructor_DuplicateColors_ShouldThrow()
  {
    var act = () => new ColorLegend([
        new LegendEntry(new IsolineColor(255, 0, 0), new ReinforcementSpec
            {
                DiameterMm = 12, SpacingMm = 200, SteelClass = "A500C"
            }),
            new LegendEntry(new IsolineColor(255, 0, 0), new ReinforcementSpec
            {
                DiameterMm = 16, SpacingMm = 150, SteelClass = "A500C"
            })
    ]);

    act.Should().Throw<ArgumentException>();
  }

  [Fact]
  public void FindClosest_ExactMatch_ShouldReturnEntry()
  {
    var legend = new ColorLegend([
        new LegendEntry(new IsolineColor(255, 0, 0), new ReinforcementSpec
            {
                DiameterMm = 12, SpacingMm = 200, SteelClass = "A500C"
            }),
            new LegendEntry(new IsolineColor(0, 255, 0), new ReinforcementSpec
            {
                DiameterMm = 16, SpacingMm = 150, SteelClass = "A500C"
            }),
        ]);

    var result = legend.FindClosest(new IsolineColor(255, 0, 0));

    result.Should().NotBeNull();
    result!.Spec.DiameterMm.Should().Be(12);
  }

  [Fact]
  public void FindClosest_NearMatch_ShouldReturnClosestEntry()
  {
    var legend = new ColorLegend([
        new LegendEntry(new IsolineColor(255, 0, 0), new ReinforcementSpec
            {
                DiameterMm = 12, SpacingMm = 200, SteelClass = "A500C"
            }),
        ]);

    // Slightly off-red
    var result = legend.FindClosest(new IsolineColor(250, 5, 3));

    result.Should().NotBeNull();
    result!.Spec.DiameterMm.Should().Be(12);
  }

  [Fact]
  public void FindClosest_TooFar_ShouldReturnNull()
  {
    var legend = new ColorLegend([
        new LegendEntry(new IsolineColor(255, 0, 0), new ReinforcementSpec
            {
                DiameterMm = 12, SpacingMm = 200, SteelClass = "A500C"
            }),
        ]);

    // Very different color (red vs pure blue → large ΔE)
    var result = legend.FindClosest(new IsolineColor(0, 0, 255), maxDeltaE: 30);

    result.Should().BeNull("the color is too far from any legend entry");
  }
}

public class IsolineColorTests
{
  [Fact]
  public void DeltaE_IdenticalColors_ShouldBeZero()
  {
    var c1 = new IsolineColor(128, 64, 32);
    var c2 = new IsolineColor(128, 64, 32);

    c1.DeltaE(c2).Should().BeApproximately(0, 0.001);
  }

  [Fact]
  public void DeltaE_BlackVsWhite_ShouldBeLarge()
  {
    var black = new IsolineColor(0, 0, 0);
    var white = new IsolineColor(255, 255, 255);

    // ΔE between black and white ≈ 100 (L* range)
    black.DeltaE(white).Should().BeGreaterThan(90);
  }

  [Fact]
  public void DeltaE_PerceptuallySimilar_ShouldBeSmall()
  {
    // Two slightly different reds
    var red1 = new IsolineColor(255, 0, 0);
    var red2 = new IsolineColor(250, 5, 3);

    red1.DeltaE(red2).Should().BeLessThan(5, "perceptually similar reds");
  }

  [Fact]
  public void DeltaE_ShouldBeSymmetric()
  {
    var c1 = new IsolineColor(200, 100, 50);
    var c2 = new IsolineColor(100, 200, 150);

    c1.DeltaE(c2).Should().BeApproximately(c2.DeltaE(c1), 0.001);
  }

  [Fact]
  public void ToLab_PureRed_ShouldHavePositiveA()
  {
    var red = new IsolineColor(255, 0, 0);
    var (l, a, b) = red.ToLab();

    l.Should().BeGreaterThan(0);
    a.Should().BeGreaterThan(0, "red has positive a* in Lab");
  }

  [Fact]
  public void ToLab_PureBlue_ShouldHaveNegativeB()
  {
    var blue = new IsolineColor(0, 0, 255);
    var (l, a, b) = blue.ToLab();

    b.Should().BeLessThan(0, "blue has negative b* in Lab");
  }
}

public class ReinforcementSpecTests
{
  [Fact]
  public void DiameterOutOfRange_ShouldThrow()
  {
    var act = () => new ReinforcementSpec { DiameterMm = 0, SpacingMm = 200, SteelClass = "A500C" };
    act.Should().Throw<ArgumentOutOfRangeException>();
  }

  [Fact]
  public void DiameterTooLarge_ShouldThrow()
  {
    var act = () => new ReinforcementSpec { DiameterMm = 100, SpacingMm = 200, SteelClass = "A500C" };
    act.Should().Throw<ArgumentOutOfRangeException>();
  }

  [Fact]
  public void SpacingZero_ShouldThrow()
  {
    var act = () => new ReinforcementSpec { DiameterMm = 12, SpacingMm = 0, SteelClass = "A500C" };
    act.Should().Throw<ArgumentOutOfRangeException>();
  }

  [Fact]
  public void ValidSpec_ShouldCalculateArea()
  {
    var spec = new ReinforcementSpec { DiameterMm = 12, SpacingMm = 200, SteelClass = "A500C" };

    spec.BarAreaMm2.Should().BeApproximately(Math.PI * 36, 0.1); // π×6²
    spec.AreaPerMeterMm2.Should().BeApproximately(Math.PI * 36 * 5, 1); // 1000/200 = 5 bars/m
  }

  [Fact]
  public void BlankSteelClass_ShouldThrow()
  {
    var act = () => new ReinforcementSpec { DiameterMm = 12, SpacingMm = 200, SteelClass = "  " };
    act.Should().Throw<ArgumentException>();
  }
}

public class SlabGeometryTests
{
  [Fact]
  public void ThicknessOutOfRange_ShouldThrow()
  {
    var act = () => new SlabGeometry
    {
      OuterBoundary = MakeRect(),
      ThicknessMm = 0,
      CoverMm = 25,
      ConcreteClass = "B25"
    };
    act.Should().Throw<ArgumentOutOfRangeException>();
  }

  [Fact]
  public void CoverOutOfRange_ShouldThrow()
  {
    var act = () => new SlabGeometry
    {
      OuterBoundary = MakeRect(),
      ThicknessMm = 200,
      CoverMm = -5,
      ConcreteClass = "B25"
    };
    act.Should().Throw<ArgumentOutOfRangeException>();
  }

  [Fact]
  public void EffectiveDepth_ShouldBeThicknessMinusCover()
  {
    var slab = new SlabGeometry
    {
      OuterBoundary = MakeRect(),
      ThicknessMm = 200,
      CoverMm = 25,
      ConcreteClass = "B25"
    };

    slab.EffectiveDepthMm.Should().Be(175);
  }

  [Fact]
  public void CoverGreaterThanThickness_ShouldThrow()
  {
    var act = () => new SlabGeometry
    {
      OuterBoundary = MakeRect(),
      ThicknessMm = 150,
      CoverMm = 160,
      ConcreteClass = "B25"
    };

    act.Should().Throw<ArgumentException>();
  }

  private static Polygon MakeRect() => new([
      new Point2D(0, 0), new Point2D(1000, 0),
        new Point2D(1000, 1000), new Point2D(0, 1000)
  ]);
}

public class StockLengthTests
{
  [Fact]
  public void NegativePrice_ShouldThrow()
  {
    var act = () => new StockLength { LengthMm = 6000, PricePerTon = -1 };
    act.Should().Throw<ArgumentOutOfRangeException>();
  }

  [Fact]
  public void NonPositiveLength_ShouldThrow()
  {
    var act = () => new StockLength { LengthMm = 0 };
    act.Should().Throw<ArgumentOutOfRangeException>();
  }
}

public class OptimizationSettingsTests
{
  [Fact]
  public void NegativeSawCut_ShouldThrow()
  {
    var act = () => new OptimizationSettings { SawCutWidthMm = -1 };
    act.Should().Throw<ArgumentOutOfRangeException>();
  }

  [Fact]
  public void WeightOutsideUnitInterval_ShouldThrow()
  {
    var act = () => new OptimizationSettings { WasteWeight = 1.5 };
    act.Should().Throw<ArgumentOutOfRangeException>();
  }
}
