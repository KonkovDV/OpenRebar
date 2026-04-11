using System.Text.Json;
using A101.Domain.Models;
using A101.Infrastructure.Reporting;
using FluentAssertions;

namespace A101.Infrastructure.Tests.Reporting;

public class JsonFileReportStoreTests
{
    [Fact]
    public async Task SaveAsync_ShouldWriteCanonicalJsonAndReturnMetadata()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "a101-report-store-tests", Guid.NewGuid().ToString("N"));
        var outputPath = Path.Combine(tempDirectory, "report.json");
        var store = new JsonFileReportStore();

        try
        {
            var report = new ReinforcementExecutionReport
            {
                GeneratedAtUtc = new DateTimeOffset(2026, 4, 11, 12, 0, 0, TimeSpan.Zero),
                Metadata = new PipelineExecutionMetadata
                {
                    ProjectCode = "A101-TEST",
                    SlabId = "SLAB-7",
                    LevelName = "Level 07"
                },
                IsolineFileName = "floor07.dxf",
                IsolineFileFormat = "dxf",
                Slab = new SlabExecutionReport
                {
                    ConcreteClass = "B25",
                    ThicknessMm = 200,
                    CoverMm = 25,
                    EffectiveDepthMm = 175,
                    AreaMm2 = 5000000,
                    OpeningCount = 1,
                    BoundingBox = new BoundingBoxExecutionReport
                    {
                        MinX = 0,
                        MinY = 0,
                        MaxX = 5000,
                        MaxY = 1000,
                        Width = 5000,
                        Height = 1000
                    }
                },
                Zones = [],
                OptimizationByDiameter = [],
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
                    TotalRebarSegments = 0,
                    TotalWastePercent = 0,
                    TotalWasteMm = 0,
                    TotalMassKg = 0
                }
            };

            var reference = await store.SaveAsync(report, outputPath);

            File.Exists(outputPath).Should().BeTrue();
            reference.OutputPath.Should().Be(Path.GetFullPath(outputPath));
            reference.MediaType.Should().Be("application/json");
            reference.ByteCount.Should().BeGreaterThan(0);
            reference.Sha256.Should().NotBeNullOrWhiteSpace();

            var json = await File.ReadAllTextAsync(outputPath);
            using var document = JsonDocument.Parse(json);
            document.RootElement.GetProperty("contractId").GetString().Should().Be("a101.reinforcement.report.v1");
            document.RootElement.GetProperty("metadata").GetProperty("slabId").GetString().Should().Be("SLAB-7");
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
                Directory.Delete(tempDirectory, recursive: true);
        }
    }
}