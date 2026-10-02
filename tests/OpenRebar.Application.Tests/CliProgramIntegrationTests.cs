using System.Text.Json;
using FluentAssertions;
using IxMilia.Dxf;
using IxMilia.Dxf.Entities;
using OpenRebar.Domain.Models;

namespace OpenRebar.Application.Tests;

public class CliProgramIntegrationTests
{
  [Fact]
  public async Task Main_WithCustomSlabDimensions_ShouldExportArtifactsAndReturnZero()
  {
    var tempDirectory = Path.Combine(Path.GetTempPath(), $"OpenRebar-cli-{Guid.NewGuid():N}");
    Directory.CreateDirectory(tempDirectory);

    var dxfPath = Path.Combine(tempDirectory, "floor-05.dxf");
    CreateSampleDxf(dxfPath);

    try
    {
      var exitCode = await global::OpenRebar.Cli.Program.Main(
      [
          dxfPath,
                "--thickness", "220",
                "--cover", "30",
                "--slab-width", "12000",
                "--slab-height", "9000",
                "--layer", "BottomX"
      ]);

      exitCode.Should().Be(2);

      var reportPath = Path.ChangeExtension(dxfPath, ".result.json");
      var schedulePath = Path.ChangeExtension(dxfPath, ".schedule.csv");
      var aeroBimPath = Path.ChangeExtension(dxfPath, ".aerobim.json");
      var ifcPath = Path.ChangeExtension(dxfPath, ".reinforcement.ifc");

      File.Exists(reportPath).Should().BeTrue();
      File.Exists(schedulePath).Should().BeTrue();
      File.Exists(aeroBimPath).Should().BeTrue();
      File.Exists(ifcPath).Should().BeTrue();

      using var report = JsonDocument.Parse(await File.ReadAllTextAsync(reportPath));
      var slab = report.RootElement.GetProperty("slab");
      slab.GetProperty("thicknessMm").GetDouble().Should().Be(220);
      slab.GetProperty("coverMm").GetDouble().Should().Be(30);

      var boundingBox = slab.GetProperty("boundingBox");
      boundingBox.GetProperty("width").GetDouble().Should().Be(12000);
      boundingBox.GetProperty("height").GetDouble().Should().Be(9000);
    }
    finally
    {
      if (Directory.Exists(tempDirectory))
        Directory.Delete(tempDirectory, recursive: true);
    }
  }

  [Fact]
  public async Task Main_WithAeroBimStorageDir_ShouldWriteHandoffManifestInsideStorageRoot()
  {
    var tempDirectory = Path.Combine(Path.GetTempPath(), $"OpenRebar-cli-handoff-{Guid.NewGuid():N}");
    var aeroBimStorageDir = Path.Combine(tempDirectory, "aerobim-storage");
    Directory.CreateDirectory(tempDirectory);

    var dxfPath = Path.Combine(tempDirectory, "floor-10.dxf");
    CreateSampleDxf(dxfPath);

    try
    {
      var exitCode = await global::OpenRebar.Cli.Program.Main(
      [
          dxfPath,
                "--aerobim-storage-dir", aeroBimStorageDir,
                "--layer", "BottomX"
      ]);

      exitCode.Should().Be(2);

      var copiedReportPath = Path.Combine(aeroBimStorageDir, "integrations", "openrebar", "floor-10.result.json");
      var handoffPath = Path.Combine(aeroBimStorageDir, "integrations", "openrebar", "floor-10.result.handoff.json");

      File.Exists(copiedReportPath).Should().BeTrue();
      File.Exists(handoffPath).Should().BeTrue();

      using var handoff = JsonDocument.Parse(await File.ReadAllTextAsync(handoffPath));
      handoff.RootElement.GetProperty("reinforcement_report_path").GetString()
          .Should().Be("integrations/openrebar/floor-10.result.json");
      handoff.RootElement.GetProperty("contract_id").GetString()
          .Should().Be("OpenRebar.reinforcement.report.v2");
      handoff.RootElement.GetProperty("project_code").GetString()
          .Should().Be("OpenRebar-CLI");
    }
    finally
    {
      if (Directory.Exists(tempDirectory))
        Directory.Delete(tempDirectory, recursive: true);
    }
  }

  [Fact]
  public async Task Main_WhenCoverIsNotLessThanThickness_ShouldReturnOne()
  {
    var tempDirectory = Path.Combine(Path.GetTempPath(), $"OpenRebar-cli-invalid-{Guid.NewGuid():N}");
    Directory.CreateDirectory(tempDirectory);

    var dxfPath = Path.Combine(tempDirectory, "floor-06.dxf");
    CreateSampleDxf(dxfPath);

    try
    {
      var exitCode = await global::OpenRebar.Cli.Program.Main(
      [
          dxfPath,
                "--thickness", "200",
                "--cover", "200"
      ]);

      exitCode.Should().Be(1);
      File.Exists(Path.ChangeExtension(dxfPath, ".result.json")).Should().BeFalse();
    }
    finally
    {
      if (Directory.Exists(tempDirectory))
        Directory.Delete(tempDirectory, recursive: true);
    }
  }

  [Fact]
  public async Task Main_WithHelpFlag_ShouldReturnZero()
  {
    var exitCode = await global::OpenRebar.Cli.Program.Main(["--help"]);
    exitCode.Should().Be(0);
  }

  [Fact]
  public async Task Main_WithNoArguments_ShouldReturnZero()
  {
    var exitCode = await global::OpenRebar.Cli.Program.Main([]);
    exitCode.Should().Be(0);
  }

  [Fact]
  public async Task Main_WithMissingFile_ShouldReturnOne()
  {
    var exitCode = await global::OpenRebar.Cli.Program.Main(
    [
        Path.Combine(Path.GetTempPath(), "nonexistent-OpenRebar-floor.dxf")
    ]);
    exitCode.Should().Be(1);
  }

  [Fact]
  public async Task Main_WithInvalidThickness_ShouldReturnOne()
  {
    var tempDirectory = Path.Combine(Path.GetTempPath(), $"OpenRebar-cli-badnum-{Guid.NewGuid():N}");
    Directory.CreateDirectory(tempDirectory);
    var dxfPath = Path.Combine(tempDirectory, "floor-07.dxf");
    CreateSampleDxf(dxfPath);

    try
    {
      var exitCode = await global::OpenRebar.Cli.Program.Main(
      [
          dxfPath,
                "--thickness", "abc"
      ]);
      exitCode.Should().Be(1);
    }
    finally
    {
      if (Directory.Exists(tempDirectory))
        Directory.Delete(tempDirectory, recursive: true);
    }
  }

  [Fact]
  public async Task Main_WithZeroSlabWidth_ShouldReturnOne()
  {
    var tempDirectory = Path.Combine(Path.GetTempPath(), $"OpenRebar-cli-zero-{Guid.NewGuid():N}");
    Directory.CreateDirectory(tempDirectory);
    var dxfPath = Path.Combine(tempDirectory, "floor-08.dxf");
    CreateSampleDxf(dxfPath);

    try
    {
      var exitCode = await global::OpenRebar.Cli.Program.Main(
      [
          dxfPath,
                "--slab-width", "0"
      ]);
      exitCode.Should().Be(1);
    }
    finally
    {
      if (Directory.Exists(tempDirectory))
        Directory.Delete(tempDirectory, recursive: true);
    }
  }

  [Fact]
  public async Task Main_WithNegativeCover_ShouldReturnOne()
  {
    var tempDirectory = Path.Combine(Path.GetTempPath(), $"OpenRebar-cli-neg-{Guid.NewGuid():N}");
    Directory.CreateDirectory(tempDirectory);
    var dxfPath = Path.Combine(tempDirectory, "floor-09.dxf");
    CreateSampleDxf(dxfPath);

    try
    {
      var exitCode = await global::OpenRebar.Cli.Program.Main(
      [
          dxfPath,
                "--cover", "-5"
      ]);
      exitCode.Should().Be(1);
    }
    finally
    {
      if (Directory.Exists(tempDirectory))
        Directory.Delete(tempDirectory, recursive: true);
    }
  }

  [Fact]
  public async Task Main_UncalibratedPng_ReturnsInputError()
  {
    var tempDirectory = Path.Combine(Path.GetTempPath(), $"OpenRebar-cli-png-{Guid.NewGuid():N}");
    Directory.CreateDirectory(tempDirectory);
    var pngPath = Path.Combine(tempDirectory, "input.png");
    var source = Path.Combine(ResolveRepositoryRoot(), "examples", "png", "simple-slab", "input.png");
    File.Copy(source, pngPath);

    try
    {
      var exitCode = await global::OpenRebar.Cli.Program.Main(
      [
          pngPath,
          "--layer", "BottomX",
          "--slab-width", "6000",
          "--slab-height", "4000"
      ]);
      exitCode.Should().Be(1);
    }
    finally
    {
      if (Directory.Exists(tempDirectory))
        Directory.Delete(tempDirectory, recursive: true);
    }
  }

  [Theory]
  [InlineData("LayerNotSpecified", 1)]
  [InlineData("RasterNotCalibratedException", 1)]
  [InlineData("InvalidIsolineFileException", 1)]
  [InlineData("VerificationFailed", 2)]
  [InlineData("DecompositionQualityViolation", 3)]
  public void ResolveExitCode_MapsCriticalFailures(string exceptionType, int expected)
  {
    var report = new ReinforcementExecutionReport
    {
      GeneratedAtUtc = DateTimeOffset.UtcNow,
      Metadata = new PipelineExecutionMetadata(),
      NormativeProfile = new NormativeProfileExecutionReport
      {
        ProfileId = "ru.sp63.2018",
        Jurisdiction = "RU",
        DesignCode = "SP 63.13330.2018",
        TablesVersion = "ru.sp63.2018.tables.v3"
      },
      AnalysisProvenance = new AnalysisProvenanceExecutionReport
      {
        Geometry = new GeometryProcessingExecutionReport
        {
          DecompositionAlgorithm = "test",
          RectangularShortcutFillRatio = 0.85,
          MinRectangleAreaMm2 = 10000,
          SamplingResolutionPerAxis = 4,
          CellCoverageInclusionThreshold = 0.35
        },
        Optimization = new OptimizationProcessingExecutionReport
        {
          OptimizerId = "none",
          MasterProblemStrategy = "none",
          PricingStrategy = "none",
          IntegerizationStrategy = "none",
          DemandAggregationPrecisionMm = 0,
          QualityFloor = "none",
          AnyFallbackMasterSolverUsed = false
        }
      },
      IsolineFileName = "input",
      IsolineFileFormat = "dxf",
      Slab = new SlabExecutionReport
      {
        ConcreteClass = "B25",
        ThicknessMm = 200,
        CoverMm = 25,
        EffectiveDepthMm = 175,
        AreaMm2 = 1,
        OpeningCount = 0,
        BoundingBox = new BoundingBoxExecutionReport
        {
          MinX = 0,
          MinY = 0,
          MaxX = 1,
          MaxY = 1,
          Width = 1,
          Height = 1
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
        ParsedZoneCount = 0,
        ClassifiedZoneCount = 0,
        TotalRebarSegments = 0,
        TotalWastePercent = 0,
        TotalWasteMm = 0,
        TotalMassKg = 0
      },
      Errors =
      [
          new PipelineFailureDiagnostic
          {
            Stage = "Test",
            ErrorMessage = "failed",
            ExceptionType = exceptionType,
            OccurredAtUtc = DateTimeOffset.UtcNow,
            IsCritical = true
          }
      ],
      PartialResult = true
    };

    global::OpenRebar.Cli.Program.ResolveExitCode(report).Should().Be(expected);
  }

  [Fact]
  public void ResolveExitCode_WithoutCriticalErrors_IsPassed()
  {
    global::OpenRebar.Cli.Program.ResolveExitCode(null).Should().Be(1);
  }

  private static string ResolveRepositoryRoot()
  {
    var current = AppContext.BaseDirectory;
    while (!string.IsNullOrWhiteSpace(current))
    {
      if (File.Exists(Path.Combine(current, "OpenRebar.sln")))
        return current;

      var parent = Directory.GetParent(current);
      if (parent is null)
        break;

      current = parent.FullName;
    }

    throw new InvalidOperationException("Could not resolve repository root.");
  }

  private static void CreateSampleDxf(string outputPath)
  {
    var dxfFile = new DxfFile();
    dxfFile.Header.Version = DxfAcadVersion.R2000;
    dxfFile.Entities.Add(new DxfLwPolyline([
        new DxfLwPolylineVertex { X = 0.0, Y = 0.0 },
            new DxfLwPolylineVertex { X = 2500.0, Y = 0.0 },
            new DxfLwPolylineVertex { X = 2500.0, Y = 2500.0 },
            new DxfLwPolylineVertex { X = 0.0, Y = 2500.0 }
    ])
    {
      IsClosed = true,
      Color = DxfColor.FromIndex(1)
    });

    dxfFile.Save(outputPath);
  }
}
