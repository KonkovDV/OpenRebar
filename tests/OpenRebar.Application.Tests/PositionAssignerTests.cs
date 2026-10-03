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

  [Fact]
  public void Assign_TotalMassUsesExactLength()
  {
    const double totalLengthMm = 175108.1402964464;
    const int count = 27;
    double lengthMm = totalLengthMm / count;
    var zone = new ReinforcementZone
    {
      Id = "Z-1",
      Boundary = MakeRect(0, 0, 6000, 4000),
      Spec = new ReinforcementSpec { DiameterMm = 20, SpacingMm = 150, SteelClass = "A500C" },
      Direction = RebarDirection.X,
      Layer = RebarLayer.Bottom,
      ZoneType = ZoneType.Simple,
      Rebars = Enumerable.Range(0, count)
          .Select(index => MakeRebar(20, lengthMm, index * 150.0))
          .ToList()
    };

    var position = PositionAssigner.Assign([zone]).Single();
    double linearMass = ReinforcementLimits.GetLinearMass(20);
    double exact = linearMass * totalLengthMm / 1000.0;
    double rounded = linearMass * position.LengthMm * count / 1000.0;

    position.LengthMm.Should().Be(6485);
    position.Quantity.Should().Be(count);
    position.TotalMassKg.Should().BeApproximately(exact, 1e-9);
    position.MassPerPieceKg.Should().BeApproximately(exact / count, 1e-9);
    Math.Abs(exact - rounded).Should().BeGreaterThan(1e-4);
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
}
