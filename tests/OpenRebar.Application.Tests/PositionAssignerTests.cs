using OpenRebar.Application.UseCases;
using OpenRebar.Domain.Models;
using OpenRebar.Domain.Rules;
using FluentAssertions;

namespace OpenRebar.Application.Tests;

public class PositionAssignerTests
{
  [Fact]
  public void Assign_SameType_SharesOneMark_AndKeepsStableBarIds()
  {
    var zone = new ReinforcementZone
    {
      Id = "Z-1",
      Boundary = MakeRect(0, 0, 6000, 4000),
      Spec = new ReinforcementSpec { DiameterMm = 20, SpacingMm = 150, SteelClass = "A500C" },
      Direction = RebarDirection.X,
      Layer = RebarLayer.Bottom,
      ZoneType = ZoneType.Simple,
      Rebars =
      [
          MakeRebar(20, 7660, 0),
          MakeRebar(20, 7660, 150),
          new RebarSegment
          {
            Start = new Point2D(0, 300),
            End = new Point2D(2000, 300),
            DiameterMm = 12,
            AnchorageLengthStart = 0,
            AnchorageLengthEnd = 0,
            Status = BarInstanceStatus.Discarded
          }
      ]
    };

    var positions = PositionAssigner.Assign([zone]);

    positions.Should().ContainSingle();
    positions[0].Mark.Should().Be("1");
    positions[0].Quantity.Should().Be(2);
    positions[0].LengthMm.Should().Be(7660);
    positions[0].Layer.Should().Be("BottomX");
    zone.Rebars.Take(2).Should().OnlyContain(rebar => rebar.Mark == "1" && rebar.BarId.Length == 16);
    zone.Rebars[0].BarId.Should().NotBe(zone.Rebars[1].BarId);
    zone.Rebars[2].Status.Should().Be(BarInstanceStatus.Discarded);
    zone.Rebars[2].Mark.Should().BeNull();

    string firstId = zone.Rebars[0].BarId;
    var again = PositionAssigner.Assign([zone]);
    again.Should().ContainSingle();
    again[0].Mark.Should().Be("1");
    zone.Rebars[0].BarId.Should().Be(firstId);
  }

  [Fact]
  public void Assign_OrdersByLayerThenDiameterThenDescendingLength()
  {
    var bottom = Zone("bottom", RebarLayer.Bottom, RebarDirection.X, 20, 5000, 1000);
    var top = Zone("top", RebarLayer.Top, RebarDirection.Y, 12, 4000, 2000);

    var positions = PositionAssigner.Assign([top, bottom]);

    positions.Select(position => position.Mark).Should().Equal("1", "2", "3", "4");
    positions[0].Layer.Should().Be("BottomX");
    positions[0].DiameterMm.Should().Be(20);
    positions[0].LengthMm.Should().Be(5000);
    positions[1].LengthMm.Should().Be(1000);
    positions[2].Layer.Should().Be("TopY");
    positions[2].LengthMm.Should().Be(4000);
    positions[3].LengthMm.Should().Be(2000);
  }

  /// <summary>
  /// PR-6: TotalMassKg must be computed from the exact TotalLength of each bar,
  /// not from the rounded position key length.  The 0.49 mm arc tail is enough
  /// to produce a 0.032 kg drift on 27 Ø20 bars that each round to the same mm.
  /// </summary>
  [Fact]
  public void Assign_TotalMassUsesExactLength()
  {
    // Ø20 A500C — bars 6565.49 mm long (fractional arc tail; rounds to 6565)
    const int diameter = 20;
    const double exactLengthMm = 6565.49;
    const int n = 27;
    var rebars = Enumerable.Range(0, n)
        .Select(i => MakeRebarExact(diameter, exactLengthMm, i * 200.0))
        .ToList();

    var zone = new ReinforcementZone
    {
      Id = "simple-slab",
      Boundary = MakeRect(0, 0, 6000, 6000),
      Spec = new ReinforcementSpec { DiameterMm = diameter, SpacingMm = 200, SteelClass = "A500C" },
      Direction = RebarDirection.X,
      Layer = RebarLayer.Bottom,
      ZoneType = ZoneType.Simple,
      Rebars = rebars
    };

    var positions = PositionAssigner.Assign([zone]);

    positions.Should().ContainSingle("all bars share one position");
    var pos = positions[0];
    pos.LengthMm.Should().Be(6565, "rounded position length is the catalogue length");
    pos.Quantity.Should().Be(n);

    double linearMass = ReinforcementLimits.GetLinearMass(diameter);
    double expectedTotal = linearMass * n * exactLengthMm / 1000.0;
    double expectedPerPiece = expectedTotal / n;

    pos.TotalMassKg.Should().BeApproximately(expectedTotal, 1e-6,
        "TotalMassKg must use exact bar lengths, not the rounded position key");
    pos.MassPerPieceKg.Should().BeApproximately(expectedPerPiece, 1e-6,
        "MassPerPieceKg is TotalMassKg / count");

    // Verify the old (wrong) value is different: 27 * 6.565 * linearMass != expectedTotal
    double oldTotal = linearMass * n * 6565.0 / 1000.0;
    oldTotal.Should().NotBeApproximately(expectedTotal, 1e-4,
        "rounded length produces a different total mass, confirming the regression is meaningful");
  }

  private static ReinforcementZone Zone(
      string id,
      RebarLayer layer,
      RebarDirection direction,
      int diameter,
      double longLength,
      double shortLength) => new()
      {
        Id = id,
        Boundary = MakeRect(0, 0, 6000, 4000),
        Spec = new ReinforcementSpec { DiameterMm = diameter, SpacingMm = 200, SteelClass = "A500C" },
        Direction = direction,
        Layer = layer,
        ZoneType = ZoneType.Simple,
        Rebars = [MakeRebar(diameter, longLength, 0), MakeRebar(diameter, shortLength, 200)]
      };

  private static Polygon MakeRect(double x, double y, double width, double height) => new([
      new Point2D(x, y),
      new Point2D(x + width, y),
      new Point2D(x + width, y + height),
      new Point2D(x, y + height)
  ]);

  private static RebarSegment MakeRebar(int diameterMm, double totalLengthMm, double y) => new()
  {
    Start = new Point2D(0, y),
    End = new Point2D(totalLengthMm, y),
    DiameterMm = diameterMm,
    AnchorageLengthStart = totalLengthMm >= 400 ? 200 : 0,
    AnchorageLengthEnd = totalLengthMm >= 400 ? 200 : 0
  };

  /// <summary>
  /// Creates a bar with a fractional TotalLength that does not equal End.X - Start.X.
  /// We simulate this by placing the bar's End such that the straight distance is
  /// (exactLengthMm - 0.01) and then storing extra arc mm in BendArcMm, so that
  /// TotalLength = |End - Start| + BendArcMm == exactLengthMm.
  /// </summary>
  private static RebarSegment MakeRebarExact(int diameterMm, double exactLengthMm, double y)
  {
    const double arcMm = 0.49; // simulated bend arc tail
    double straightMm = exactLengthMm - arcMm;
    return new RebarSegment
    {
      Start = new Point2D(0, y),
      End = new Point2D(straightMm, y),
      DiameterMm = diameterMm,
      AnchorageLengthStart = 200,
      AnchorageLengthEnd = 200,
      BendArcMm = arcMm
    };
  }
}
