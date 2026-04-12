using System.Text.Json;
using OpenRebar.Domain.Models;
using OpenRebar.Infrastructure.Export;
using FluentAssertions;

namespace OpenRebar.Infrastructure.Tests.Export;

public class AeroBimReportExporterTests
{
    [Fact]
    public async Task ExportAsync_ShouldWriteAeroBimContractJson()
    {
        var exporter = new AeroBimReportExporter();
        var outputPath = Path.Combine(Path.GetTempPath(), $"OpenRebar-aerobim-{Guid.NewGuid():N}.json");

        var zone = new ReinforcementZone
        {
            Id = "Z-001",
            Boundary = new Polygon([
                new Point2D(0, 0),
                new Point2D(5000, 0),
                new Point2D(5000, 3000),
                new Point2D(0, 3000)
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
                    End = new Point2D(5000, 0),
                    DiameterMm = 12,
                    AnchorageLengthStart = 500,
                    AnchorageLengthEnd = 500,
                    Mark = "1"
                }
            ]
        };

        var report = new ReinforcementExecutionReport
        {
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            Metadata = new PipelineExecutionMetadata
            {
                ProjectCode = "рк-25-0042",
                SlabId = "Плита_Этаж_03",
                LevelName = "03"
            },
            IsolineFileName = "floor_03.dxf",
            IsolineFileFormat = "dxf",
            Slab = new SlabExecutionReport
            {
                ConcreteClass = "B25",
                ThicknessMm = 200,
                CoverMm = 25,
                EffectiveDepthMm = 175,
                AreaMm2 = 15_000_000,
                OpeningCount = 0,
                BoundingBox = new BoundingBoxExecutionReport
                {
                    MinX = 0,
                    MinY = 0,
                    MaxX = 5000,
                    MaxY = 3000,
                    Width = 5000,
                    Height = 3000
                }
            },
            Zones = [new ZoneExecutionReport
            {
                ZoneId = "Z-001",
                ZoneType = "Simple",
                Direction = "X",
                Layer = "Bottom",
                DiameterMm = 12,
                SpacingMm = 200,
                RebarCount = 1,
                TotalClearSpanMm = 5000,
                TotalLengthMm = 6000,
                BoundingBox = new BoundingBoxExecutionReport
                {
                    MinX = 0,
                    MinY = 0,
                    MaxX = 5000,
                    MaxY = 3000,
                    Width = 5000,
                    Height = 3000
                }
            }],
            OptimizationByDiameter = [new DiameterOptimizationExecutionReport
            {
                DiameterMm = 12,
                SupplierName = "Default",
                RebarCount = 1,
                StockBarsNeeded = 1,
                TotalWasteMm = 1700,
                TotalWastePercent = 14.53,
                TotalRebarLengthMm = 10000,
                TotalMassKg = 10,
                CuttingPlans = [new CuttingPlanExecutionReport
                {
                    StockLengthMm = 11700,
                    CutsMm = [5000, 5000],
                    WasteMm = 1700,
                    WastePercent = 14.53
                }]
            }],
            Placement = new PlacementExecutionReport
            {
                Requested = false,
                Executed = false,
                Success = true,
                TotalRebarsPlaced = 0,
                TotalTagsCreated = 0,
                TotalBendingDetails = 0
            },
            Summary = new ExecutionSummaryReport
            {
                ParsedZoneCount = 1,
                ClassifiedZoneCount = 1,
                TotalRebarSegments = 1,
                TotalWastePercent = 14.53,
                TotalWasteMm = 1700,
                TotalMassKg = 10
            }
        };

        try
        {
            await exporter.ExportAsync(report, [zone], outputPath);

            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(outputPath));
            document.RootElement.GetProperty("$schema").GetString().Should().Be("aerobim-OpenRebar-reinforcement-report/v1");
            document.RootElement.GetProperty("project_id").GetString().Should().Be("рк-25-0042");
            document.RootElement.GetProperty("slab_id").GetString().Should().Be("Плита_Этаж_03");
            document.RootElement.GetProperty("zones").GetArrayLength().Should().Be(1);
            document.RootElement.GetProperty("zones")[0].GetProperty("steel_class").GetString().Should().Be("A500C");
            document.RootElement.GetProperty("optimization").GetProperty("total_stock_bars").GetInt32().Should().Be(1);
        }
        finally
        {
            if (File.Exists(outputPath))
                File.Delete(outputPath);
        }
    }
}