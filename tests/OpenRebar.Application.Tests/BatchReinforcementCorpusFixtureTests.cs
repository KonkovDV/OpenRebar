using System.Text.Json;
using OpenRebar.Application.UseCases;
using OpenRebar.Domain.Models;
using OpenRebar.Domain.Ports;
using OpenRebar.Infrastructure.Catalog;
using OpenRebar.Infrastructure.DxfProcessing;
using OpenRebar.Infrastructure.ImageProcessing;
using OpenRebar.Infrastructure.Logging;
using OpenRebar.Infrastructure.Optimization;
using OpenRebar.Infrastructure.ReinforcementEngine;
using OpenRebar.Infrastructure.Reporting;
using OpenRebar.Infrastructure.Stubs;
using OpenRebar.Infrastructure.ZoneProcessing;
using FluentAssertions;

namespace OpenRebar.Application.Tests;

public class BatchReinforcementCorpusFixtureTests
{
  private const string CorpusRootEnvironmentVariable = "OPENREBAR_BATCH_CORPUS_ROOT";

  private static readonly OptimizationSettings DefaultOptimizationSettings = new()
  {
    SawCutWidthMm = 3,
    MinScrapLengthMm = 300
  };

  [Fact]
  [Trait("Category", "Corpus")]
  public async Task ExecuteAsync_WithFixtureCorpus_ShouldStayWithinConfiguredQualityEnvelope()
  {
    var corpusRoot = TryResolveCorpusRoot();
    if (corpusRoot is null)
    {
      Console.WriteLine(
          "Batch corpus manifest not found. Add tests/OpenRebar.Application.Tests/Fixtures/BatchBenchmarkCorpus/manifest.json or set OPENREBAR_BATCH_CORPUS_ROOT.");
      return;
    }

    var manifest = LoadManifest(corpusRoot);
    manifest.Cases.Should().NotBeEmpty("a corpus manifest should describe at least one slab benchmark case");

    var tempDirectory = Path.Combine(Path.GetTempPath(), $"OpenRebar-batch-corpus-{Guid.NewGuid():N}");
    Directory.CreateDirectory(tempDirectory);

    try
    {
      var optimizationSettings = manifest.OptimizationSettings?.ToDomainSettings() ?? DefaultOptimizationSettings;
      var logger = new ConsoleStructuredLogger();
      var catalogLoader = new FileSupplierCatalogLoader();
      var pipeline = new GenerateReinforcementPipeline(
          dxfParser: new DxfIsolineParser(),
          pngParser: new PngIsolineParser(),
          zoneDetector: new StandardZoneDetector(),
          calculator: new StandardReinforcementCalculator(logger),
          optimizer: new ColumnGenerationOptimizer(),
          catalogLoader: catalogLoader,
          placer: new StubRevitPlacer(),
          reportStore: new JsonFileReportStore(),
          logger: logger);

      var batchPipeline = new BatchReinforcementPipeline(pipeline);
      var inputs = manifest.Cases
          .Select(@case => @case.ToPipelineInput(corpusRoot, tempDirectory, optimizationSettings))
          .ToList();

      var result = await batchPipeline.ExecuteAsync(inputs);
      var defaultCatalog = catalogLoader.GetDefaultCatalog();
      var ffdOptimizer = new FirstFitDecreasingOptimizer();

      result.Failures.Should().BeEmpty();
      result.SlabResults.Should().HaveCount(manifest.Cases.Count);

      foreach (var caseDefinition in manifest.Cases)
      {
        var slabResult = result.SlabResults.Single(slab => slab.SlabId == caseDefinition.SlabId);

        slabResult.Result.StoredReport.Should().NotBeNull();
        File.Exists(slabResult.Result.StoredReport!.OutputPath).Should().BeTrue();

        var optimizerIds = slabResult.Result.OptimizationResults.Values
            .Select(opt => opt.Provenance?.OptimizerId)
            .Where(id => id != null)
            .Cast<string>()
            .ToList();
        optimizerIds.Should().OnlyContain(id =>
            id == "column-generation-relaxation-v1" ||
            id == "first-fit-decreasing-v1" ||
            id == "exact-small-instance-search-v1");

        var requiredLengths = slabResult.Result.ClassifiedZones
            .SelectMany(zone => zone.Rebars)
            .Select(rebar => rebar.TotalLength)
            .ToList();

        requiredLengths.Should().NotBeEmpty($"fixture corpus case '{caseDefinition.Name}' should generate at least one rebar segment");

        var actual = slabResult.Result.OptimizationResults.Values.Single();
        var baseline = ffdOptimizer.Optimize(
            requiredLengths,
            defaultCatalog.AvailableLengths,
            optimizationSettings);

        int allowedBarRegression = caseDefinition.MaxBarRegression ?? 0;
        double allowedWasteRegressionPercent = caseDefinition.MaxWasteRegressionPercent ?? 0.01;

        actual.TotalStockBarsNeeded.Should().BeLessThanOrEqualTo(
            baseline.TotalStockBarsNeeded + allowedBarRegression,
            "fixture corpus case '{0}' should stay within the configured stock-bar envelope versus FFD",
            caseDefinition.Name);

        actual.TotalWastePercent.Should().BeLessThanOrEqualTo(
            baseline.TotalWastePercent + allowedWasteRegressionPercent,
            "fixture corpus case '{0}' should stay within the configured waste envelope versus FFD",
            caseDefinition.Name);

        if (caseDefinition.MaxAbsoluteWastePercent.HasValue)
        {
          actual.TotalWastePercent.Should().BeLessThanOrEqualTo(
              caseDefinition.MaxAbsoluteWastePercent.Value,
              "fixture corpus case '{0}' defines an absolute waste ceiling",
              caseDefinition.Name);
        }
      }
    }
    finally
    {
      if (Directory.Exists(tempDirectory))
        Directory.Delete(tempDirectory, recursive: true);
    }
  }

  private static string? TryResolveCorpusRoot()
  {
    var envRoot = Environment.GetEnvironmentVariable(CorpusRootEnvironmentVariable);
    if (!string.IsNullOrWhiteSpace(envRoot))
    {
      var fullEnvRoot = Path.GetFullPath(envRoot);
      if (File.Exists(Path.Combine(fullEnvRoot, "manifest.json")))
        return fullEnvRoot;
    }

    var repoRoot = FindRepositoryRoot();
    var defaultRoot = Path.Combine(
        repoRoot,
        "tests",
        "OpenRebar.Application.Tests",
        "Fixtures",
        "BatchBenchmarkCorpus");

    return File.Exists(Path.Combine(defaultRoot, "manifest.json"))
        ? defaultRoot
        : null;
  }

  private static BatchCorpusManifest LoadManifest(string corpusRoot)
  {
    var manifestPath = Path.Combine(corpusRoot, "manifest.json");
    var json = File.ReadAllText(manifestPath);
    var manifest = JsonSerializer.Deserialize<BatchCorpusManifest>(json, new JsonSerializerOptions
    {
      PropertyNameCaseInsensitive = true
    });

    return manifest ?? throw new InvalidOperationException($"Unable to deserialize batch corpus manifest at '{manifestPath}'.");
  }

  private static string FindRepositoryRoot()
  {
    var current = new DirectoryInfo(AppContext.BaseDirectory);
    while (current is not null)
    {
      if (File.Exists(Path.Combine(current.FullName, "OpenRebar.sln")))
        return current.FullName;

      current = current.Parent;
    }

    throw new DirectoryNotFoundException("Could not locate OpenRebar.sln from the test execution directory.");
  }

  private sealed class BatchCorpusManifest
  {
    public BatchCorpusOptimizationSettings? OptimizationSettings { get; init; }
    public List<BatchCorpusCase> Cases { get; init; } = [];
  }

  private sealed class BatchCorpusOptimizationSettings
  {
    public double? SawCutWidthMm { get; init; }
    public double? MinScrapLengthMm { get; init; }

    public OptimizationSettings ToDomainSettings() => new()
    {
      SawCutWidthMm = SawCutWidthMm ?? DefaultOptimizationSettings.SawCutWidthMm,
      MinScrapLengthMm = MinScrapLengthMm ?? DefaultOptimizationSettings.MinScrapLengthMm
    };
  }

  private sealed class BatchCorpusCase
  {
    public required string Name { get; init; }
    public required string SlabId { get; init; }
    public required string DxfPath { get; init; }
    public required double SlabWidthMm { get; init; }
    public required double SlabHeightMm { get; init; }
    public double ThicknessMm { get; init; } = 200;
    public double CoverMm { get; init; } = 25;
    public string ConcreteClass { get; init; } = "B25";
    public string LevelName { get; init; } = "Corpus";
    public List<BatchCorpusLegendEntry>? LegendEntries { get; init; }
    public int? MaxBarRegression { get; init; }
    public double? MaxWasteRegressionPercent { get; init; }
    public double? MaxAbsoluteWastePercent { get; init; }

    public PipelineInput ToPipelineInput(
        string corpusRoot,
        string tempDirectory,
        OptimizationSettings optimizationSettings)
    {
      var dxfPath = Path.GetFullPath(Path.Combine(corpusRoot, DxfPath));
      File.Exists(dxfPath).Should().BeTrue($"fixture corpus case '{Name}' should point to an existing DXF file");

      return new PipelineInput
      {
        IsolineFilePath = dxfPath,
        Legend = BuildLegend(LegendEntries),
        Slab = new SlabGeometry
        {
          OuterBoundary = new Polygon(
              [
                  new Point2D(0, 0),
                        new Point2D(SlabWidthMm, 0),
                        new Point2D(SlabWidthMm, SlabHeightMm),
                        new Point2D(0, SlabHeightMm)
              ]),
          ThicknessMm = ThicknessMm,
          CoverMm = CoverMm,
          ConcreteClass = ConcreteClass
        },
        Metadata = new PipelineExecutionMetadata
        {
          ProjectCode = "OpenRebar-BATCH-CORPUS",
          SlabId = SlabId,
          LevelName = LevelName
        },
        OptimizationSettings = optimizationSettings,
        PlaceInRevit = false,
        PersistReport = true,
        ReportOutputPath = Path.Combine(tempDirectory, $"{SlabId}.result.json")
      };
    }
  }

  private sealed class BatchCorpusLegendEntry
  {
    public required int[] Rgb { get; init; }
    public required int DiameterMm { get; init; }
    public required int SpacingMm { get; init; }
    public required string SteelClass { get; init; }
  }

  private static ColorLegend BuildLegend(IReadOnlyList<BatchCorpusLegendEntry>? legendEntries)
  {
    var entries = legendEntries is { Count: > 0 }
        ? legendEntries
        :
        [
            new BatchCorpusLegendEntry
                {
                    Rgb = [255, 0, 0],
                    DiameterMm = 12,
                    SpacingMm = 200,
                    SteelClass = "A500C"
                }
        ];

    return new ColorLegend(entries.Select(entry =>
    {
      entry.Rgb.Length.Should().Be(3, "legend RGB entries must contain exactly three channels");

      return new LegendEntry(
        new IsolineColor((byte)entry.Rgb[0], (byte)entry.Rgb[1], (byte)entry.Rgb[2]),
        new ReinforcementSpec
        {
          DiameterMm = entry.DiameterMm,
          SpacingMm = entry.SpacingMm,
          SteelClass = entry.SteelClass
        });
    }).ToList());
  }
}
