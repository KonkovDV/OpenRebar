using System.Security.Cryptography;
using System.Text.Json;
using FluentAssertions;
using OpenRebar.Domain.Models;
using OpenRebar.Domain.Ports;
using OpenRebar.Infrastructure.Export;

namespace OpenRebar.Infrastructure.Tests.Export;

public class AeroBimHandoffManifestWriterTests
{
    [Fact]
    public async Task WriteAsync_ShouldCopyCanonicalReportAndEmitManifestInsideAeroBimStorage()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "OpenRebar-handoff-tests", Guid.NewGuid().ToString("N"));
        var sourceDir = Path.Combine(tempRoot, "source");
        var storageDir = Path.Combine(tempRoot, "aerobim-storage");
        Directory.CreateDirectory(sourceDir);

        var sourceReportPath = Path.Combine(sourceDir, "floor03.result.json");
        const string reportJson = """
        {
          "contractId": "OpenRebar.reinforcement.report.v1",
          "schemaVersion": "1.0.0",
          "generatedAtUtc": "2026-04-22T12:00:00Z"
        }
        """;
        await File.WriteAllTextAsync(sourceReportPath, reportJson);

        var writer = new AeroBimHandoffManifestWriter();
        var report = BuildReport();
        var storedReport = new StoredReportReference
        {
            OutputPath = sourceReportPath,
            MediaType = "application/json",
            Sha256 = ComputeSha256(reportJson),
            ByteCount = reportJson.Length,
        };

        try
        {
            var artifact = await writer.WriteAsync(report, storedReport, storageDir);

            File.Exists(artifact.ManifestPath).Should().BeTrue();
            File.Exists(artifact.ReinforcementReportPath).Should().BeTrue();
            artifact.RelativeManifestPath.Should().Be("integrations/openrebar/floor03.result.handoff.json");
            artifact.RelativeReinforcementReportPath.Should().Be("integrations/openrebar/floor03.result.json");

            var copiedReportJson = await File.ReadAllTextAsync(artifact.ReinforcementReportPath);
            copiedReportJson.Should().Be(reportJson);

            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(artifact.ManifestPath));
            document.RootElement.GetProperty("handoff_type").GetString().Should().Be("openrebar-aerobim-report-handoff.v1");
            document.RootElement.GetProperty("reinforcement_report_path").GetString().Should().Be(artifact.RelativeReinforcementReportPath);
            document.RootElement.GetProperty("report_sha256").GetString().Should().Be(storedReport.Sha256);
            document.RootElement.GetProperty("contract_id").GetString().Should().Be(report.ContractId);
            document.RootElement.GetProperty("project_code").GetString().Should().Be(report.Metadata.ProjectCode);
            document.RootElement.GetProperty("slab_id").GetString().Should().Be(report.Metadata.SlabId);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static ReinforcementExecutionReport BuildReport()
    {
        return new ReinforcementExecutionReport
        {
            GeneratedAtUtc = new DateTimeOffset(2026, 4, 22, 12, 0, 0, TimeSpan.Zero),
            Metadata = new PipelineExecutionMetadata
            {
                ProjectCode = "Residential Tower Alpha",
                SlabId = "SLAB-03",
                LevelName = "03"
            },
            NormativeProfile = new NormativeProfileExecutionReport
            {
                ProfileId = "ru.sp63.2018",
                Jurisdiction = "RU",
                DesignCode = "SP 63.13330.2018",
                TablesVersion = "ru.sp63.2018.tables.v1"
            },
            AnalysisProvenance = new AnalysisProvenanceExecutionReport
            {
                Geometry = new GeometryProcessingExecutionReport
                {
                    DecompositionAlgorithm = "adaptive-orthogonal-strip-or-grid/v3",
                    RectangularShortcutFillRatio = 0.85,
                    MinRectangleAreaMm2 = 10_000,
                    SamplingResolutionPerAxis = 4,
                    CellCoverageInclusionThreshold = 0.35,
                    MinCoverageRatioAcrossComplexZones = null,
                    MaxOverCoverageRatioAcrossComplexZones = null
                },
                Optimization = new OptimizationProcessingExecutionReport
                {
                    OptimizerId = "column-generation-relaxation-v1",
                    MasterProblemStrategy = "restricted-master-lp-highs",
                    PricingStrategy = "bounded-knapsack-dp",
                    IntegerizationStrategy = "largest-remainder-plus-repair",
                    DemandAggregationPrecisionMm = 0.1,
                    QualityFloor = "ffd-non-regression-floor",
                    AnyFallbackMasterSolverUsed = false
                }
            },
            IsolineFileName = "floor03.dxf",
            IsolineFileFormat = "dxf",
            Slab = new SlabExecutionReport
            {
                ConcreteClass = "B25",
                ThicknessMm = 200,
                CoverMm = 25,
                EffectiveDepthMm = 175,
                AreaMm2 = 5_000_000,
                OpeningCount = 0,
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
    }

    private static string ComputeSha256(string payload)
    {
        return Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(payload)))
            .ToLowerInvariant();
    }
}