using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using OpenRebar.Application.UseCases;
using OpenRebar.Domain.Models;
using OpenRebar.Domain.Ports;
using OpenRebar.Domain.Rules;
using OpenRebar.Infrastructure.DependencyInjection;
using OpenRebar.Infrastructure.DxfProcessing;
using OpenRebar.Infrastructure.ImageProcessing;
using OpenRebar.Infrastructure.Profiles;
using OpenRebar.Infrastructure.Stubs;

namespace OpenRebar.Application.Tests;

public class ExampleArtifactHashTests
{
  private static readonly Regex IfcTimestamp = new(
      @"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?",
      RegexOptions.CultureInvariant | RegexOptions.Compiled);

  private static readonly Regex IfcGlobalId = new(
      "'[0-9A-Za-z_$]{22}'",
      RegexOptions.CultureInvariant | RegexOptions.Compiled);

  [Fact]
  public async Task Examples_WriteNormalizedArtifactHashes()
  {
    string repo = RepoRoot();
    string temp = Path.Combine(Path.GetTempPath(), $"openrebar-tfm-{Guid.NewGuid():N}");
    Directory.CreateDirectory(temp);
    CopyExamples(repo, temp);

    try
    {
      await HashExample(temp, "dxf-simple-slab", Path.Combine(temp, "examples", "dxf", "simple-slab", "input.dxf"), projectPath: null, csv: false, png: false, LayerKey.BottomX);
      await HashExample(temp, "png-simple-slab", Path.Combine(temp, "examples", "png", "simple-slab", "input.png"), projectPath: null, csv: false, png: true, LayerKey.BottomX);
      await HashExample(temp, "fe-uniform-slab", Path.Combine(temp, "examples", "fe-field", "uniform-slab.csv"), projectPath: null, csv: true, png: false, layer: null);
      await HashExample(temp, "fe-supported-slab", Path.Combine(temp, "examples", "fe-field", "supported-slab.csv"), Path.Combine(temp, "examples", "fe-field", "supported-slab.project.json"), csv: true, png: false, layer: null);
      await HashExample(temp, "project-simple-slab", isolinePath: null, Path.Combine(temp, "examples", "project", "simple-slab.project.json"), csv: false, png: false, layer: null);
    }
    finally
    {
      if (Directory.Exists(temp))
        Directory.Delete(temp, recursive: true);
    }
  }

  private static async Task HashExample(
      string tempRoot,
      string id,
      string? isolinePath,
      string? projectPath,
      bool csv,
      bool png,
      LayerKey? layer)
  {
    var services = new ServiceCollection();
    services.AddOpenRebarCoreServices();
    services.AddSingleton<IRevitPlacer, StubRevitPlacer>();
    using var provider = services.BuildServiceProvider(validateScopes: true);

    string? profilePath = CompanyProfileLoader.TryResolveBundledGeneric();
    LoadedCompanyProfile? profile = profilePath is null ? null : CompanyProfileLoader.Load(profilePath);
    string steel = profile?.Profile.SteelClass ?? "A500C";
    string concrete = profile?.Profile.ConcreteClass ?? "B25";
    var legendLoader = provider.GetRequiredService<ILegendLoader>();
    ColorLegend legend = profile is null
        ? legendLoader.GetDefaultLegend(steel)
        : CompanyProfileLoader.ToLegend(profile.Profile, steel);

    ProjectDesignDocument? project = projectPath is null
        ? null
        : await ProjectDesignLoader.LoadAsync(projectPath);

    string isoline = isolinePath ?? project?.SourcePath
        ?? throw new InvalidOperationException($"{id} has no input.");
    AsField? field = csv ? TabularAsFieldReader.ReadFile(isoline) : null;
    if (project is not null)
    {
      steel = string.IsNullOrWhiteSpace(project.SteelClass) ? steel : project.SteelClass;
      concrete = string.IsNullOrWhiteSpace(project.Slab.ConcreteClass) ? concrete : project.Slab.ConcreteClass;
    }

    var layers = new List<LayerDesignInput>();
    if (project is not null)
    {
      foreach (var document in project.Layers)
      {
        if (!LayerKeyParser.TryParse(document.Layer, out LayerKey projectLayer))
          throw new InvalidOperationException($"{id} layer '{document.Layer}' is not a design layer.");

        layers.Add(new LayerDesignInput
        {
          Layer = projectLayer,
          IsolineFilePath = document.File,
          Legend = legend,
          Background = document.Background is null
              ? null
              : new BackgroundMesh(
                  projectLayer,
                  new ReinforcementSpec
                  {
                    DiameterMm = document.Background.DiameterMm,
                    SpacingMm = document.Background.SpacingMm,
                    SteelClass = string.IsNullOrWhiteSpace(document.Background.SteelClass)
                        ? steel
                        : document.Background.SteelClass
                  },
                  document.Background.GridOriginMm)
        });
      }
    }

    double width = project?.Slab.WidthMm ?? 6000;
    double height = project?.Slab.HeightMm ?? 4000;
    var slab = new SlabGeometry
    {
      OuterBoundary = new Polygon(
      [
          new Point2D(0, 0),
          new Point2D(width, 0),
          new Point2D(width, height),
          new Point2D(0, height)
      ]),
      ThicknessMm = project?.Slab.ThicknessMm ?? 220,
      CoverMm = project?.Slab.CoverMm ?? 30,
      EdgeCoverMm = project?.Slab.CoverEdgeMm ?? 0,
      OpeningClearanceMm = project?.Slab.OpeningClearanceMm ?? 0,
      ConcreteClass = concrete,
      Edges = project?.Slab.ParsedEdges ?? []
    };

    provider.GetRequiredService<DxfIsolineParser>().TrySetUnitsWhenUnset("mm");
    if (png)
    {
      var parser = provider.GetRequiredService<PngIsolineParser>();
      parser.Calibration = new RasterCalibration(0.1, 24, 424, 24, 24, 600, 400);
      parser.MinZoneAreaMm2 = 100_000;
    }

    var input = new PipelineInput
    {
      IsolineFilePath = isoline,
      Legend = legend,
      Slab = slab,
      Metadata = new PipelineExecutionMetadata
      {
        ProjectCode = project?.ProjectCode ?? "OpenRebar-CLI",
        SlabId = project?.SlabId ?? Path.GetFileNameWithoutExtension(isoline),
        LevelName = "Standalone",
        NormativeProfileId = project?.Norm ?? NormativeProfiles.DefaultProfileId
      },
      PlaceInRevit = false,
      PersistReport = true,
      ReportOutputPath = Path.ChangeExtension(isoline, ".result.json"),
      Layer = layers.Count > 0 ? null : layer,
      Layers = layers,
      CompanyProfilePath = profile?.SourcePath,
      CompanyProfileId = profile?.Profile.Id,
      CompanyProfileVersion = profile?.Profile.Version ?? "0",
      JointRatioMax = profile?.Profile.Laps.JointRatioMax ?? 0.5,
      EndCondition = profile?.Profile.Ends.Condition ?? "NeedsHook",
      Couplers = profile?.Profile.Laps.Couplers ?? false,
      SupplierCatalog = profile is null ? null : CompanyProfileLoader.ToCatalog(profile.Profile),
      RequireExplicitLayer = true,
      AsField = field,
      FieldDiametersMm = profile?.Profile.Additional.DiametersMm ?? [],
      AdditionalSpacingMode = profile?.Profile.Additional.SpacingMode ?? "interleave",
      FieldSpacingsMm = NormativeProfiles.Sp63_2018.StandardSpacingsMm
    };

    var result = await provider.GetRequiredService<GenerateReinforcementPipeline>().ExecuteAsync(input);
    result.Report.Should().NotBeNull($"{id} writes a report");

    string schedulePath = Path.ChangeExtension(isoline, ".schedule.csv");
    string ifcPath = Path.ChangeExtension(isoline, ".reinforcement.ifc");
    await provider.GetRequiredService<IScheduleExporter>().ExportAsync(result.ClassifiedZones, schedulePath, ScheduleNumberCulture.Invariant);
    await provider.GetRequiredService<IIfcExporter>().ExportAsync(result.ClassifiedZones, slab, ifcPath);

    string resultText = NormalizeResult(await File.ReadAllTextAsync(input.ReportOutputPath!), tempRoot);
    string scheduleText = (await File.ReadAllTextAsync(schedulePath)).Replace("\r\n", "\n", StringComparison.Ordinal);
    string ifcText = NormalizeIfc(await File.ReadAllTextAsync(ifcPath), tempRoot);

    WriteHash(id + ".result.sha256", resultText);
    WriteHash(id + ".schedule.sha256", scheduleText);
    WriteHash(id + ".ifc.sha256", ifcText);
  }

  private static string NormalizeResult(string json, string tempRoot)
  {
    var root = JsonNode.Parse(BlankPaths(json, tempRoot))
        ?? throw new InvalidOperationException("Result JSON is empty.");
    Walk(root);
    return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
  }

  private static void Walk(JsonNode node)
  {
    if (node is JsonObject obj)
    {
      foreach (var property in obj.ToList())
      {
        if (property.Key.EndsWith("Utc", StringComparison.OrdinalIgnoreCase)
            || property.Key.Equals("stackTrace", StringComparison.OrdinalIgnoreCase))
        {
          obj[property.Key] = "__DYNAMIC__";
          continue;
        }

        if (property.Key.Equals("durationMs", StringComparison.OrdinalIgnoreCase))
        {
          obj[property.Key] = 0;
          continue;
        }

        if (property.Value is not null)
          Walk(property.Value);
      }

      return;
    }

    if (node is JsonArray array)
    {
      foreach (var item in array)
      {
        if (item is not null)
          Walk(item);
      }
    }
  }

  private static string NormalizeIfc(string text, string tempRoot)
  {
    string normalized = BlankPaths(text, tempRoot).Replace("\r\n", "\n", StringComparison.Ordinal);
    var kept = normalized
        .Split('\n')
        .Where(line => line.Contains("IFCOWNERHISTORY", StringComparison.OrdinalIgnoreCase) is false);
    return IfcGlobalId.Replace(IfcTimestamp.Replace(string.Join('\n', kept), "TIMESTAMP"), "'GLOBALID'");
  }

  private static string BlankPaths(string text, string tempRoot)
  {
    string forward = tempRoot.Replace('\\', '/');
    string escaped = tempRoot.Replace("\\", "\\\\", StringComparison.Ordinal);
    return text
        .Replace(tempRoot, "/example", StringComparison.OrdinalIgnoreCase)
        .Replace(escaped, "/example", StringComparison.OrdinalIgnoreCase)
        .Replace(forward, "/example", StringComparison.OrdinalIgnoreCase);
  }

  private static void WriteHash(string name, string text)
  {
    string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    hash.Should().HaveLength(64);
    string? directory = Environment.GetEnvironmentVariable("OPENREBAR_TFM_HASH_DIR");
    if (string.IsNullOrWhiteSpace(directory))
      return;

    Directory.CreateDirectory(directory);
    File.WriteAllText(Path.Combine(directory, name), hash + "\n");
  }

  private static void CopyExamples(string repo, string temp)
  {
    string source = Path.Combine(repo, "examples");
    foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
    {
      string relative = Path.GetRelativePath(repo, file);
      string dest = Path.Combine(temp, relative);
      Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
      File.Copy(file, dest);
    }
  }

  private static string RepoRoot()
  {
    var current = new DirectoryInfo(AppContext.BaseDirectory);
    while (current is not null)
    {
      if (File.Exists(Path.Combine(current.FullName, "OpenRebar.sln")))
        return current.FullName;
      current = current.Parent;
    }

    throw new InvalidOperationException("Repository root was not found.");
  }
}
