using System.Text.Json;
using FluentAssertions;
using Json.Schema;
using OpenRebar.Domain.Models;
using OpenRebar.Infrastructure.Reporting;

namespace OpenRebar.Infrastructure.Tests.Reporting;

/// <summary>
/// Validates that the canonical report output satisfies the structural
/// constraints defined in contracts/aerobim-reinforcement-report.schema.json.
/// This suite validates contract-critical shape constraints that map directly
/// to the canonical schema, including top-level property strictness.
/// </summary>
public class ReportSchemaComplianceTests
{
    private static readonly HashSet<string> ExpectedTopLevelProperties =
    [
        "contractId",
        "schemaVersion",
        "generatedAtUtc",
        "metadata",
        "normativeProfile",
        "analysisProvenance",
        "isolineFileName",
        "isolineFileFormat",
        "slab",
        "zones",
        "optimizationByDiameter",
        "placement",
        "summary",
        "warnings",
        "errors",
        "partialResult"
    ];

    [Fact]
    public async Task SaveAsync_OutputShouldValidateAgainstCanonicalJsonSchema()
    {
        var store = new JsonFileReportStore();
        var outputPath = Path.Combine(Path.GetTempPath(), $"OpenRebar-schema-runtime-{Guid.NewGuid():N}.json");

        var report = BuildSampleReport();

        try
        {
            await store.SaveAsync(report, outputPath);

            var evaluation = EvaluateAgainstCanonicalSchema(outputPath);

            evaluation.IsValid.Should().BeTrue($"schema validation errors: {DescribeErrors(evaluation)}");
        }
        finally
        {
            if (File.Exists(outputPath))
                File.Delete(outputPath);
        }
    }

    [Fact]
    public async Task SaveAsync_PartialResultShouldValidateAgainstCanonicalJsonSchema()
    {
        var store = new JsonFileReportStore();
        var outputPath = Path.Combine(Path.GetTempPath(), $"OpenRebar-schema-partial-runtime-{Guid.NewGuid():N}.json");

        var report = BuildSampleReport() with
        {
            PartialResult = true,
            Errors =
            [
                new PipelineFailureDiagnostic
                {
                    Stage = "Optimization",
                    ErrorMessage = "solver aborted",
                    ExceptionType = "OptimizationException",
                    OccurredAtUtc = DateTimeOffset.UtcNow,
                    IsCritical = true
                }
            ],
            AnalysisProvenance = new AnalysisProvenanceExecutionReport
            {
                Geometry = new GeometryProcessingExecutionReport
                {
                    DecompositionAlgorithm = "n/a",
                    RectangularShortcutFillRatio = 0,
                    MinRectangleAreaMm2 = 1,
                    SamplingResolutionPerAxis = 1,
                    CellCoverageInclusionThreshold = 0
                },
                Optimization = new OptimizationProcessingExecutionReport
                {
                    OptimizerId = "n/a",
                    MasterProblemStrategy = "n/a",
                    PricingStrategy = "n/a",
                    IntegerizationStrategy = "n/a",
                    DemandAggregationPrecisionMm = 0,
                    QualityFloor = "n/a",
                    AnyFallbackMasterSolverUsed = false
                }
            },
            Zones = [],
            OptimizationByDiameter = [],
            Summary = new ExecutionSummaryReport
            {
                ParsedZoneCount = 0,
                ClassifiedZoneCount = 0,
                TotalRebarSegments = 0,
                TotalWastePercent = 0,
                TotalWasteMm = 0,
                TotalMassKg = 0,
                EstimatedCost = null
            }
        };

        try
        {
            await store.SaveAsync(report, outputPath);

            var evaluation = EvaluateAgainstCanonicalSchema(outputPath);

            evaluation.IsValid.Should().BeTrue($"schema validation errors: {DescribeErrors(evaluation)}");
        }
        finally
        {
            if (File.Exists(outputPath))
                File.Delete(outputPath);
        }
    }

    [Fact]
    public async Task SaveAsync_OutputShouldContainAllRequiredTopLevelFields()
    {
        var store = new JsonFileReportStore();
        var outputPath = Path.Combine(Path.GetTempPath(), $"OpenRebar-schema-{Guid.NewGuid():N}.json");

        var report = BuildSampleReport();

        try
        {
            await store.SaveAsync(report, outputPath);

            using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(outputPath));
            var root = doc.RootElement;

            // All required top-level fields per contract
            root.TryGetProperty("contractId", out _).Should().BeTrue();
            root.TryGetProperty("schemaVersion", out _).Should().BeTrue();
            root.TryGetProperty("generatedAtUtc", out _).Should().BeTrue();
            root.TryGetProperty("metadata", out _).Should().BeTrue();
            root.TryGetProperty("normativeProfile", out _).Should().BeTrue();
            root.TryGetProperty("analysisProvenance", out _).Should().BeTrue();
            root.TryGetProperty("isolineFileName", out _).Should().BeTrue();
            root.TryGetProperty("isolineFileFormat", out _).Should().BeTrue();
            root.TryGetProperty("slab", out _).Should().BeTrue();
            root.TryGetProperty("zones", out _).Should().BeTrue();
            root.TryGetProperty("optimizationByDiameter", out _).Should().BeTrue();
            root.TryGetProperty("placement", out _).Should().BeTrue();
            root.TryGetProperty("summary", out _).Should().BeTrue();
            root.TryGetProperty("errors", out _).Should().BeTrue();
            root.TryGetProperty("partialResult", out _).Should().BeTrue();
        }
        finally
        {
            if (File.Exists(outputPath))
                File.Delete(outputPath);
        }
    }

    [Fact]
    public async Task SaveAsync_TopLevelShouldNotContainUnexpectedProperties()
    {
        var store = new JsonFileReportStore();
        var outputPath = Path.Combine(Path.GetTempPath(), $"OpenRebar-schema-top-level-{Guid.NewGuid():N}.json");

        var report = BuildSampleReport();

        try
        {
            await store.SaveAsync(report, outputPath);

            using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(outputPath));
            var actualProperties = doc.RootElement.EnumerateObject().Select(property => property.Name).ToHashSet();

            actualProperties.Should().BeSubsetOf(ExpectedTopLevelProperties,
                "canonical payload must not emit undeclared top-level fields when schema disallows additional properties");
        }
        finally
        {
            if (File.Exists(outputPath))
                File.Delete(outputPath);
        }
    }

    [Fact]
    public async Task SaveAsync_DiagnosticsEnvelopeShouldMatchContractShape()
    {
        var store = new JsonFileReportStore();
        var outputPath = Path.Combine(Path.GetTempPath(), $"OpenRebar-schema-diagnostics-{Guid.NewGuid():N}.json");

        var report = BuildSampleReport() with
        {
            PartialResult = true,
            Errors =
            [
                new PipelineFailureDiagnostic
                {
                    Stage = "Parse",
                    ErrorMessage = "Unsupported input format",
                    ExceptionType = "ValidationException",
                    OccurredAtUtc = DateTimeOffset.UtcNow,
                    IsCritical = true
                }
            ]
        };

        try
        {
            await store.SaveAsync(report, outputPath);

            using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(outputPath));
            var root = doc.RootElement;

            root.GetProperty("partialResult").GetBoolean().Should().BeTrue();
            var errors = root.GetProperty("errors");
            errors.GetArrayLength().Should().Be(1);

            var error = errors[0];
            error.TryGetProperty("stage", out _).Should().BeTrue();
            error.TryGetProperty("errorMessage", out _).Should().BeTrue();
            error.TryGetProperty("exceptionType", out _).Should().BeTrue();
            error.TryGetProperty("occurredAtUtc", out _).Should().BeTrue();
            error.TryGetProperty("isCritical", out _).Should().BeTrue();
        }
        finally
        {
            if (File.Exists(outputPath))
                File.Delete(outputPath);
        }
    }

    [Fact]
    public async Task SaveAsync_MetadataShouldContainAllRequiredFields()
    {
        var store = new JsonFileReportStore();
        var outputPath = Path.Combine(Path.GetTempPath(), $"OpenRebar-schema-meta-{Guid.NewGuid():N}.json");

        var report = BuildSampleReport();

        try
        {
            await store.SaveAsync(report, outputPath);

            using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(outputPath));
            var metadata = doc.RootElement.GetProperty("metadata");

            metadata.TryGetProperty("projectCode", out _).Should().BeTrue();
            metadata.TryGetProperty("slabId", out _).Should().BeTrue();
            metadata.TryGetProperty("sourceSystem", out _).Should().BeTrue();
            metadata.TryGetProperty("targetSystem", out _).Should().BeTrue();
            metadata.TryGetProperty("countryCode", out _).Should().BeTrue();
            metadata.TryGetProperty("designCode", out _).Should().BeTrue();
            metadata.TryGetProperty("normativeProfileId", out _).Should().BeTrue();
            metadata.TryGetProperty("normativeTablesVersion", out _).Should().BeTrue();
        }
        finally
        {
            if (File.Exists(outputPath))
                File.Delete(outputPath);
        }
    }

    [Fact]
    public async Task SaveAsync_SlabShouldContainAllRequiredFields()
    {
        var store = new JsonFileReportStore();
        var outputPath = Path.Combine(Path.GetTempPath(), $"OpenRebar-schema-slab-{Guid.NewGuid():N}.json");

        var report = BuildSampleReport();

        try
        {
            await store.SaveAsync(report, outputPath);

            using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(outputPath));
            var slab = doc.RootElement.GetProperty("slab");

            slab.TryGetProperty("concreteClass", out _).Should().BeTrue();
            slab.TryGetProperty("thicknessMm", out _).Should().BeTrue();
            slab.TryGetProperty("coverMm", out _).Should().BeTrue();
            slab.TryGetProperty("effectiveDepthMm", out _).Should().BeTrue();
            slab.TryGetProperty("areaMm2", out _).Should().BeTrue();
            slab.TryGetProperty("openingCount", out _).Should().BeTrue();
            slab.TryGetProperty("boundingBox", out _).Should().BeTrue();

            var bbox = slab.GetProperty("boundingBox");
            bbox.TryGetProperty("minX", out _).Should().BeTrue();
            bbox.TryGetProperty("minY", out _).Should().BeTrue();
            bbox.TryGetProperty("maxX", out _).Should().BeTrue();
            bbox.TryGetProperty("maxY", out _).Should().BeTrue();
            bbox.TryGetProperty("width", out _).Should().BeTrue();
            bbox.TryGetProperty("height", out _).Should().BeTrue();
        }
        finally
        {
            if (File.Exists(outputPath))
                File.Delete(outputPath);
        }
    }

    [Fact]
    public async Task SaveAsync_SummaryShouldContainAllRequiredFields()
    {
        var store = new JsonFileReportStore();
        var outputPath = Path.Combine(Path.GetTempPath(), $"OpenRebar-schema-summary-{Guid.NewGuid():N}.json");

        var report = BuildSampleReport();

        try
        {
            await store.SaveAsync(report, outputPath);

            using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(outputPath));
            var summary = doc.RootElement.GetProperty("summary");

            summary.TryGetProperty("parsedZoneCount", out _).Should().BeTrue();
            summary.TryGetProperty("classifiedZoneCount", out _).Should().BeTrue();
            summary.TryGetProperty("totalRebarSegments", out _).Should().BeTrue();
            summary.TryGetProperty("totalWastePercent", out _).Should().BeTrue();
            summary.TryGetProperty("totalWasteMm", out _).Should().BeTrue();
            summary.TryGetProperty("totalMassKg", out _).Should().BeTrue();
        }
        finally
        {
            if (File.Exists(outputPath))
                File.Delete(outputPath);
        }
    }

    [Fact]
    public async Task SaveAsync_OptimizationByDiameterShouldContainQualityBoundsFields()
    {
        var store = new JsonFileReportStore();
        var outputPath = Path.Combine(Path.GetTempPath(), $"OpenRebar-schema-opt-{Guid.NewGuid():N}.json");

        var report = BuildSampleReport();

        try
        {
            await store.SaveAsync(report, outputPath);

            using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(outputPath));
            var optimization = doc.RootElement.GetProperty("optimizationByDiameter")[0];

            optimization.TryGetProperty("dualBound", out _).Should().BeTrue();
            optimization.TryGetProperty("gap", out _).Should().BeTrue();
        }
        finally
        {
            if (File.Exists(outputPath))
                File.Delete(outputPath);
        }
    }

    [Fact]
    public async Task SaveAsync_ContractIdShouldMatchSchemaConstant()
    {
        var store = new JsonFileReportStore();
        var outputPath = Path.Combine(Path.GetTempPath(), $"OpenRebar-schema-cid-{Guid.NewGuid():N}.json");

        var report = BuildSampleReport();

        try
        {
            await store.SaveAsync(report, outputPath);

            using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(outputPath));
            doc.RootElement.GetProperty("contractId").GetString()
                .Should().Be("OpenRebar.reinforcement.report.v1");
        }
        finally
        {
            if (File.Exists(outputPath))
                File.Delete(outputPath);
        }
    }

    [Fact]
    public async Task SaveAsync_NormativeProfileShouldContainAllRequiredFields()
    {
        var store = new JsonFileReportStore();
        var outputPath = Path.Combine(Path.GetTempPath(), $"OpenRebar-schema-normative-{Guid.NewGuid():N}.json");

        var report = BuildSampleReport();

        try
        {
            await store.SaveAsync(report, outputPath);

            using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(outputPath));
            var normative = doc.RootElement.GetProperty("normativeProfile");

            normative.TryGetProperty("profileId", out _).Should().BeTrue();
            normative.TryGetProperty("jurisdiction", out _).Should().BeTrue();
            normative.TryGetProperty("designCode", out _).Should().BeTrue();
            normative.TryGetProperty("tablesVersion", out _).Should().BeTrue();
        }
        finally
        {
            if (File.Exists(outputPath))
                File.Delete(outputPath);
        }
    }

    [Fact]
    public async Task SaveAsync_AnalysisProvenanceShouldContainAllRequiredFields()
    {
        var store = new JsonFileReportStore();
        var outputPath = Path.Combine(Path.GetTempPath(), $"OpenRebar-schema-provenance-{Guid.NewGuid():N}.json");

        var report = BuildSampleReport();

        try
        {
            await store.SaveAsync(report, outputPath);

            using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(outputPath));
            var provenance = doc.RootElement.GetProperty("analysisProvenance");
            var geometry = provenance.GetProperty("geometry");
            var optimization = provenance.GetProperty("optimization");

            geometry.TryGetProperty("decompositionAlgorithm", out _).Should().BeTrue();
            geometry.TryGetProperty("rectangularShortcutFillRatio", out _).Should().BeTrue();
            geometry.TryGetProperty("minRectangleAreaMm2", out _).Should().BeTrue();
            geometry.TryGetProperty("samplingResolutionPerAxis", out _).Should().BeTrue();
            geometry.TryGetProperty("cellCoverageInclusionThreshold", out _).Should().BeTrue();

            optimization.TryGetProperty("optimizerId", out _).Should().BeTrue();
            optimization.TryGetProperty("masterProblemStrategy", out _).Should().BeTrue();
            optimization.TryGetProperty("pricingStrategy", out _).Should().BeTrue();
            optimization.TryGetProperty("integerizationStrategy", out _).Should().BeTrue();
            optimization.TryGetProperty("demandAggregationPrecisionMm", out _).Should().BeTrue();
            optimization.TryGetProperty("qualityFloor", out _).Should().BeTrue();
            optimization.TryGetProperty("anyFallbackMasterSolverUsed", out _).Should().BeTrue();
        }
        finally
        {
            if (File.Exists(outputPath))
                File.Delete(outputPath);
        }
    }

    private static ReinforcementExecutionReport BuildSampleReport() => new()
    {
        GeneratedAtUtc = DateTimeOffset.UtcNow,
        Metadata = new PipelineExecutionMetadata
        {
            ProjectCode = "test-schema",
            SlabId = "SLAB-01",
            LevelName = "01"
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
                MinCoverageRatioAcrossComplexZones = 0.97,
                MaxOverCoverageRatioAcrossComplexZones = 0.08
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
        IsolineFileName = "test.dxf",
        IsolineFileFormat = "dxf",
        Slab = new SlabExecutionReport
        {
            ConcreteClass = "B25",
            ThicknessMm = 200,
            CoverMm = 25,
            EffectiveDepthMm = 175,
            AreaMm2 = 600_000_000,
            OpeningCount = 0,
            BoundingBox = new BoundingBoxExecutionReport
            {
                MinX = 0, MinY = 0, MaxX = 30000, MaxY = 20000,
                Width = 30000, Height = 20000
            }
        },
        Zones =
        [
            new ZoneExecutionReport
            {
                ZoneId = "Z-001",
                ZoneType = "Simple",
                Direction = "X",
                Layer = "Bottom",
                DiameterMm = 12,
                SpacingMm = 200,
                RebarCount = 5,
                TotalClearSpanMm = 5000,
                TotalLengthMm = 30000,
                SubRectangleCount = 2,
                DecompositionCoverageRatio = 0.98,
                DecompositionOverCoverageRatio = 0.05,
                BoundingBox = new BoundingBoxExecutionReport
                {
                    MinX = 0, MinY = 0, MaxX = 5000, MaxY = 3000,
                    Width = 5000, Height = 3000
                }
            }
        ],
        OptimizationByDiameter =
        [
            new DiameterOptimizationExecutionReport
            {
                DiameterMm = 12,
                SupplierName = "Default",
                RebarCount = 5,
                StockBarsNeeded = 2,
                TotalWasteMm = 3400,
                TotalWastePercent = 14.5,
                TotalRebarLengthMm = 30000,
                TotalMassKg = 26.6,
                DualBound = 1.9,
                Gap = 5.26,
                CuttingPlans =
                [
                    new CuttingPlanExecutionReport
                    {
                        StockLengthMm = 11700,
                        CutsMm = [5000, 5000],
                        SawCutWidthMm = 3,
                        WasteMm = 1694,
                        WastePercent = 14.48
                    }
                ]
            }
        ],
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
            TotalRebarSegments = 5,
            TotalWastePercent = 14.48,
            TotalWasteMm = 3388,
            TotalMassKg = 26.6
        }
    };

    private static EvaluationResults EvaluateAgainstCanonicalSchema(string payloadPath)
    {
        var schemaPath = Path.Combine(GetRepositoryRoot(), "contracts", "aerobim-reinforcement-report.schema.json");
        var schema = JsonSchema.FromFile(
            schemaPath,
            new BuildOptions
            {
                Dialect = Dialect.Draft202012,
                SchemaRegistry = new SchemaRegistry(),
                DialectRegistry = new DialectRegistry(),
                VocabularyRegistry = new VocabularyRegistry()
            },
            new Uri(schemaPath));

        using var payloadDocument = JsonDocument.Parse(File.ReadAllText(payloadPath));

        return schema.Evaluate(payloadDocument.RootElement, new EvaluationOptions
        {
            RequireFormatValidation = true
        });
    }

    private static string GetRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "contracts", "aerobim-reinforcement-report.schema.json")))
                return current.FullName;

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate OpenRebar repository root containing contracts/aerobim-reinforcement-report.schema.json.");
    }

    private static string DescribeErrors(EvaluationResults results)
    {
        var messages = new List<string>();
        CollectErrors(results, messages);
        return messages.Count > 0 ? string.Join(" | ", messages) : "none";
    }

    private static void CollectErrors(EvaluationResults results, ICollection<string> messages)
    {
        foreach (var error in results.Errors ?? [])
            messages.Add($"{results.InstanceLocation}: {error.Key} => {error.Value}");

        foreach (var detail in results.Details ?? [])
            CollectErrors(detail, messages);
    }
}
