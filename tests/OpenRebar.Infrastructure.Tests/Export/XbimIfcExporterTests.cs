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
}