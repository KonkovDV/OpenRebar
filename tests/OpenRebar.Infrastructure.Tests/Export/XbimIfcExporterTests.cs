using OpenRebar.Domain.Models;
using OpenRebar.Infrastructure.Export;
using FluentAssertions;
using Xbim.Ifc;
using Xbim.Ifc4.Interfaces;

namespace OpenRebar.Infrastructure.Tests.Export;

public class XbimIfcExporterTests
{
  [Fact]
  public async Task ExportAsync_ShouldCreateIfcFileThatCanBeOpenedWithXbim()
  {
    var exporter = new XbimIfcExporter();
    var path = Path.Combine(Path.GetTempPath(), $"OpenRebar-{Guid.NewGuid():N}.ifc");

    var slab = new SlabGeometry
    {
      OuterBoundary = new Polygon([
            new Point2D(0, 0),
                new Point2D(6000, 0),
                new Point2D(6000, 4000),
                new Point2D(0, 4000)
        ]),
      ThicknessMm = 200,
      CoverMm = 25,
      ConcreteClass = "B25"
    };

    IReadOnlyList<ReinforcementZone> zones =
    [
        new ReinforcementZone
            {
                Id = "Z-001",
                Boundary = new Polygon([
                    new Point2D(0, 0),
                    new Point2D(3000, 0),
                    new Point2D(3000, 2000),
                    new Point2D(0, 2000)
                ]),
                Spec = new ReinforcementSpec { DiameterMm = 12, SpacingMm = 200, SteelClass = "A500C" },
                Direction = RebarDirection.X,
                ZoneType = ZoneType.Simple,
                Layer = RebarLayer.Bottom,
                Rebars =
                [
                    new RebarSegment
                    {
                        Start = new Point2D(0, 0),
                        End = new Point2D(2500, 0),
                        DiameterMm = 12,
                        AnchorageLengthStart = 200,
                        AnchorageLengthEnd = 200,
                        Mark = "1"
                    },
                    new RebarSegment
                    {
                        Start = new Point2D(0, 200),
                        End = new Point2D(2500, 200),
                        DiameterMm = 12,
                        AnchorageLengthStart = 200,
                        AnchorageLengthEnd = 200,
                        Mark = "2"
                    }
                ]
            }
    ];

    try
    {
      await exporter.ExportAsync(zones, slab, path);

      File.Exists(path).Should().BeTrue();
      using var model = IfcStore.Open(path);
      model.Instances.OfType<IIfcProject>().Should().ContainSingle();
      model.Instances.OfType<IIfcSlab>().Should().ContainSingle();
      model.Instances.OfType<IIfcReinforcingBar>().Should().HaveCount(2);
      model.Instances.OfType<IIfcMaterial>().Should().ContainSingle(
          "bars that share the same steel class should reuse one IFC material definition instead of duplicating it per bar");
    }
    finally
    {
      if (File.Exists(path))
        File.Delete(path);
    }
  }

  [Fact]
  public async Task ExportAsync_RepeatsTheBarIdentity()
  {
    var exporter = new XbimIfcExporter();
    string firstPath = Path.Combine(Path.GetTempPath(), $"OpenRebar-{Guid.NewGuid():N}.ifc");
    string secondPath = Path.Combine(Path.GetTempPath(), $"OpenRebar-{Guid.NewGuid():N}.ifc");
    (SlabGeometry slab, IReadOnlyList<ReinforcementZone> zones) = Sample();

    try
    {
      await exporter.ExportAsync(zones, slab, firstPath);
      await exporter.ExportAsync(zones, slab, secondPath);

      IReadOnlyList<(string Name, string GlobalId)> first = BarIdentity(firstPath);
      IReadOnlyList<(string Name, string GlobalId)> second = BarIdentity(secondPath);
      second.Should().Equal(first);
      first.Select(bar => bar.Name).Should().Equal("bar-a", "bar-b");
    }
    finally
    {
      if (File.Exists(firstPath))
        File.Delete(firstPath);
      if (File.Exists(secondPath))
        File.Delete(secondPath);
    }
  }

  private static (SlabGeometry Slab, IReadOnlyList<ReinforcementZone> Zones) Sample()
  {
    var slab = new SlabGeometry
    {
      OuterBoundary = new Polygon([
          new Point2D(0, 0),
          new Point2D(6000, 0),
          new Point2D(6000, 4000),
          new Point2D(0, 4000)
      ]),
      ThicknessMm = 200,
      CoverMm = 25,
      ConcreteClass = "B25"
    };

    IReadOnlyList<ReinforcementZone> zones =
    [
        new ReinforcementZone
        {
          Id = "Z-001",
          Boundary = slab.OuterBoundary,
          Spec = new ReinforcementSpec { DiameterMm = 12, SpacingMm = 200, SteelClass = "A500C" },
          Direction = RebarDirection.X,
          ZoneType = ZoneType.Simple,
          Layer = RebarLayer.Bottom,
          Rebars =
          [
              new RebarSegment
              {
                Start = new Point2D(0, 0),
                End = new Point2D(2500, 0),
                DiameterMm = 12,
                AnchorageLengthStart = 200,
                AnchorageLengthEnd = 200,
                BarId = "bar-a",
                Mark = "1"
              },
              new RebarSegment
              {
                Start = new Point2D(0, 200),
                End = new Point2D(2500, 200),
                DiameterMm = 12,
                AnchorageLengthStart = 200,
                AnchorageLengthEnd = 200,
                BarId = "bar-b",
                Mark = "1"
              }
          ]
        }
    ];

    return (slab, zones);
  }

  private static IReadOnlyList<(string Name, string GlobalId)> BarIdentity(string path)
  {
    using var model = IfcStore.Open(path);
    return model.Instances.OfType<IIfcReinforcingBar>()
        .Select(bar => (Name: bar.Name.ToString() ?? "", GlobalId: bar.GlobalId.ToString() ?? ""))
        .OrderBy(bar => bar.Name, StringComparer.Ordinal)
        .ToList();
  }
}
