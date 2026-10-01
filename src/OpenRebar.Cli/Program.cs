using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using OpenRebar.Domain;
using OpenRebar.Domain.Rules;
using OpenRebar.Domain.Exceptions;
using OpenRebar.Application.UseCases;
using OpenRebar.Domain.Models;
using OpenRebar.Domain.Ports;
using OpenRebar.Infrastructure.DependencyInjection;
using OpenRebar.Infrastructure.Profiles;
using OpenRebar.Infrastructure.Export;
using OpenRebar.Infrastructure.Stubs;
using Microsoft.Extensions.DependencyInjection;

namespace OpenRebar.Cli;

/// <summary>
/// Console entry point for running the reinforcement pipeline without Revit.
/// Useful for batch processing, CI testing, and debugging.
///
/// Usage:
///   dotnet run --project src/OpenRebar.Cli -- &lt;isoline-file&gt; [--catalog &lt;catalog.json&gt;] [--ml-url &lt;url&gt;]
/// </summary>
public static class Program
{
  public static async Task<int> Main(string[] args)
  {
    if (args.Length == 0 || args[0] is "-h" or "--help")
    {
      PrintUsage();
      return 0;
    }

    if (args[0].Equals("profile", StringComparison.OrdinalIgnoreCase))
      return ProfileCommands.Run(args);

    string? projectPath = GetArgValue(args, "--project") ?? GetArgValue(args, "--design");
    string? profilePath = GetArgValue(args, "--profile");
    string? normArg = GetArgValue(args, "--norm");
    var layerInputArgs = GetArgValues(args, "--layer-input");
    bool hasPositionalFile = !args[0].StartsWith('-');
    string? catalogPath = GetArgValue(args, "--catalog");
    string? legendPath = GetArgValue(args, "--legend");
    string? mlUrl = GetArgValue(args, "--ml-url");
    string? aeroBimStorageDir = GetArgValue(args, "--aerobim-storage-dir");
    string? layerName = GetArgValue(args, "--layer");
    bool legacyDirection = args.Any(arg => arg.Equals("--legacy-direction-heuristic", StringComparison.OrdinalIgnoreCase));
    LayerKey? layer = null;
    if (layerName is not null)
    {
      if (!LayerKeyParser.TryParse(layerName, out LayerKey parsedLayer))
      {
        Console.Error.WriteLine("Error: --layer must be BottomX, BottomY, TopX, or TopY.");
        return 1;
      }

      layer = parsedLayer;
    }

    string? pixelsPerMmText = GetArgValue(args, "--px-per-mm");
    string? originText = GetArgValue(args, "--origin-px");
    RasterCalibration? rasterCalibration = null;
    if (pixelsPerMmText is not null || originText is not null)
    {
      if (!double.TryParse(pixelsPerMmText, NumberStyles.Float, CultureInfo.InvariantCulture, out double pixelsPerMm)
          || pixelsPerMm <= 0
          || !TryParsePixelPair(originText, out double originX, out double originY))
      {
        Console.Error.WriteLine("Error: --px-per-mm and --origin-px x,y are both required, and the scale must be positive.");
        return 1;
      }

      rasterCalibration = new RasterCalibration(pixelsPerMm, originX, originY);
      if (GetArgValue(args, "--roi-px") is string roiText)
      {
        var parts = roiText.Split(',');
        if (parts.Length != 4
            || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int roiX)
            || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int roiY)
            || !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int roiWidth)
            || !int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int roiHeight)
            || roiWidth <= 0
            || roiHeight <= 0)
        {
          Console.Error.WriteLine("Error: --roi-px must be x,y,width,height.");
          return 1;
        }

        rasterCalibration = rasterCalibration with
        {
          RoiX = roiX,
          RoiY = roiY,
          RoiWidth = roiWidth,
          RoiHeight = roiHeight
        };
      }
    }

    string dxfUnitsName = GetArgValue(args, "--dxf-units") ?? "mm";
    if (dxfUnitsName.Trim().ToLowerInvariant() is not ("mm" or "cm" or "m" or "in" or "inch"))
    {
      Console.Error.WriteLine("Error: --dxf-units must be mm, cm, m, or in.");
      return 1;
    }

    string csvCultureName = GetArgValue(args, "--csv-culture") ?? "ru";
    if (!TryParseScheduleCulture(csvCultureName, out ScheduleNumberCulture csvCulture))
    {
      Console.Error.WriteLine("Error: --csv-culture must be ru or invariant.");
      return 1;
    }

    string steelClass = GetArgValue(args, "--steel") ?? "A500C";
    string concreteClass = GetArgValue(args, "--concrete") ?? "B25";

    if (!TryGetOptionalDouble(args, "--thickness", 200, minimumValue: 0, inclusiveMinimum: false, out double thickness, out string? thicknessError))
    {
      Console.Error.WriteLine(thicknessError);
      return 1;
    }

    if (!TryGetOptionalDouble(args, "--cover", 25, minimumValue: 0, inclusiveMinimum: true, out double cover, out string? coverError))
    {
      Console.Error.WriteLine(coverError);
      return 1;
    }

    if (!TryGetOptionalDouble(args, "--edge-cover", 0, minimumValue: 0, inclusiveMinimum: true, out double edgeCover, out string? edgeCoverError))
    {
      Console.Error.WriteLine(edgeCoverError);
      return 1;
    }

    if (!TryGetOptionalDouble(args, "--opening-clearance", 0, minimumValue: 0, inclusiveMinimum: true, out double openingClearance, out string? openingClearanceError))
    {
      Console.Error.WriteLine(openingClearanceError);
      return 1;
    }

    if (!TryGetOptionalDouble(args, "--slab-width", 30000, minimumValue: 0, inclusiveMinimum: false, out double slabWidth, out string? slabWidthError))
    {
      Console.Error.WriteLine(slabWidthError);
      return 1;
    }

    if (!TryGetOptionalDouble(args, "--min-zone-area-mm2", 100_000, minimumValue: 0, inclusiveMinimum: false, out double minZoneAreaMm2, out string? minZoneAreaError))
    {
      Console.Error.WriteLine(minZoneAreaError);
      return 1;
    }

    if (!TryGetOptionalDouble(args, "--slab-height", 20000, minimumValue: 0, inclusiveMinimum: false, out double slabHeight, out string? slabHeightError))
    {
      Console.Error.WriteLine(slabHeightError);
      return 1;
    }

    if (projectPath is not null && hasPositionalFile)
    {
      Console.Error.WriteLine("Error: pass either a drawing or --project, not both.");
      return 1;
    }

    if (projectPath is not null && layerInputArgs.Count > 0)
    {
      Console.Error.WriteLine("Error: --layer-input cannot be combined with --project.");
      return 1;
    }

    if (!hasPositionalFile && projectPath is null && layerInputArgs.Count == 0)
    {
      Console.Error.WriteLine("Error: pass a drawing, --project, or --layer-input.");
      return 1;
    }

    if (profilePath is not null && !File.Exists(profilePath))
    {
      Console.Error.WriteLine($"Error: Company profile was not found: {profilePath}");
      return 1;
    }

    if (profilePath is null)
      profilePath = CompanyProfileLoader.TryResolveBundledGeneric();

    LoadedCompanyProfile? loadedProfile = null;
    if (profilePath is not null)
    {
      try
      {
        loadedProfile = CompanyProfileLoader.Load(profilePath);
      }
      catch (CompanyProfileLoadException ex)
      {
        Console.Error.WriteLine($"Error: {ex.Message}");
        return 1;
      }
    }

    bool steelFromCaller = GetArgValue(args, "--steel") is not null;
    bool concreteFromCaller = GetArgValue(args, "--concrete") is not null;
    if (loadedProfile is not null && !steelFromCaller)
      steelClass = loadedProfile.Profile.SteelClass;
    if (loadedProfile is not null && !concreteFromCaller)
      concreteClass = loadedProfile.Profile.ConcreteClass;

    if (normArg is not null && !string.Equals(normArg, NormativeProfiles.DefaultProfileId, StringComparison.Ordinal))
    {
      Console.Error.WriteLine($"Error: --norm must be {NormativeProfiles.DefaultProfileId}.");
      return 1;
    }

    ProjectDesignDocument? project = null;
    if (projectPath is not null)
    {
      if (!File.Exists(projectPath))
      {
        Console.Error.WriteLine($"Error: Project file was not found: {projectPath}");
        return 1;
      }

      try
      {
        project = await ProjectDesignLoader.LoadAsync(projectPath);
      }
      catch (Exception ex) when (ex is JsonException or InvalidDataException)
      {
        Console.Error.WriteLine($"Error: {ex.Message}");
        return 1;
      }
    }

    if (project is not null)
    {
      if (GetArgValue(args, "--thickness") is null)
        thickness = project.Slab.ThicknessMm;
      if (GetArgValue(args, "--cover") is null)
        cover = project.Slab.CoverMm;
      if (GetArgValue(args, "--edge-cover") is null)
        edgeCover = project.Slab.CoverEdgeMm;
      if (GetArgValue(args, "--opening-clearance") is null)
        openingClearance = project.Slab.OpeningClearanceMm;
      if (GetArgValue(args, "--slab-width") is null)
        slabWidth = project.Slab.WidthMm;
      if (GetArgValue(args, "--slab-height") is null)
        slabHeight = project.Slab.HeightMm;
      if (GetArgValue(args, "--concrete") is null && !string.IsNullOrWhiteSpace(project.Slab.ConcreteClass))
        concreteClass = project.Slab.ConcreteClass;
      if (GetArgValue(args, "--steel") is null && !string.IsNullOrWhiteSpace(project.SteelClass))
        steelClass = project.SteelClass;
      if (normArg is null && project.Norm is not null
          && !string.Equals(project.Norm, NormativeProfiles.DefaultProfileId, StringComparison.Ordinal))
      {
        Console.Error.WriteLine($"Error: project norm must be {NormativeProfiles.DefaultProfileId}.");
        return 1;
      }

      if (slabWidth <= 0 || slabHeight <= 0 || thickness <= 0)
      {
        Console.Error.WriteLine("Error: project slab width, height, and thickness must be positive.");
        return 1;
      }
    }

    var layerDrafts = new List<LayerInputDraft>();
    foreach (string rawLayer in layerInputArgs)
    {
      if (!LayerInputParser.TryParse(rawLayer, out LayerInputDraft draft, out string? layerInputError))
      {
        Console.Error.WriteLine(layerInputError);
        return 1;
      }

      layerDrafts.Add(draft);
    }

    string isolineFile = hasPositionalFile
        ? args[0]
        : project?.SourcePath ?? layerDrafts[0].FilePath;
    if (hasPositionalFile && !File.Exists(isolineFile))
    {
      Console.Error.WriteLine($"Error: File not found: {isolineFile}");
      return 1;
    }

    AsField? asField = null;
    if (hasPositionalFile)
    {
      string extension = Path.GetExtension(isolineFile);
      if (extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
      {
        Console.Error.WriteLine("Error: XLSX is not read yet. Export the same table as CSV.");
        return 1;
      }

      if (extension.Equals(".csv", StringComparison.OrdinalIgnoreCase))
      {
        if (layer is not null)
        {
          Console.Error.WriteLine("Error: a field table already names the layers. Do not pass --layer.");
          return 1;
        }

        try
        {
          asField = TabularAsFieldReader.ReadFile(isolineFile);
        }
        catch (AsFieldReadException ex)
        {
          Console.Error.WriteLine($"Error: {ex.Message}");
          return 1;
        }
      }
    }

    if (cover >= thickness)
    {
      Console.Error.WriteLine("Error: --cover must be smaller than --thickness.");
      return 1;
    }

    try
    {
      Console.WriteLine($"OpenRebar Reinforcement CLI v{ProductVersion.Current}");
      Console.WriteLine($"  Isoline file: {isolineFile}");
      Console.WriteLine($"  Concrete: {concreteClass}, Steel: {steelClass}");
      Console.WriteLine($"  Slab: {thickness.ToString(CultureInfo.InvariantCulture)}mm thick, {cover.ToString(CultureInfo.InvariantCulture)}mm cover, {slabWidth.ToString(CultureInfo.InvariantCulture)}x{slabHeight.ToString(CultureInfo.InvariantCulture)}mm footprint");
      if (legendPath is not null) Console.WriteLine($"  Legend config: {legendPath}");
      if (mlUrl is not null) Console.WriteLine($"  ML service: {mlUrl}");
      Console.WriteLine();

      // Build DI container
      var services = new ServiceCollection();
      services.AddOpenRebarCoreServices(mlUrl);
      services.AddSingleton<IRevitPlacer, StubRevitPlacer>();

      var sp = services.BuildServiceProvider(validateScopes: true);

      var legendLoader = sp.GetRequiredService<ILegendLoader>();
      var ifcExporter = sp.GetRequiredService<IIfcExporter>();
      var reportExporter = sp.GetRequiredService<IReportExporter>();
      var handoffWriter = sp.GetRequiredService<AeroBimHandoffManifestWriter>();
      var scheduleExporter = sp.GetRequiredService<IScheduleExporter>();

      var legend = legendPath is not null
          ? await legendLoader.LoadAsync(legendPath)
          : loadedProfile is not null
              ? CompanyProfileLoader.ToLegend(loadedProfile.Profile, steelClass)
              : legendLoader.GetDefaultLegend(steelClass);

      var designLayers = new List<LayerDesignInput>();
      if (project is not null)
      {
        foreach (var layerDocument in project.Layers)
        {
          if (!LayerKeyParser.TryParse(layerDocument.Layer, out LayerKey projectLayer))
          {
            Console.Error.WriteLine($"Error: project layer '{layerDocument.Layer}' must be BottomX, BottomY, TopX, or TopY.");
            return 1;
          }

          if (!File.Exists(layerDocument.File))
          {
            Console.Error.WriteLine($"Error: File not found: {layerDocument.File}");
            return 1;
          }

          designLayers.Add(new LayerDesignInput
          {
            Layer = projectLayer,
            IsolineFilePath = layerDocument.File,
            Legend = await LoadLayerLegendAsync(legendLoader, layerDocument.LegendFile, legend),
            Background = CreateBackground(projectLayer, layerDocument.Background, steelClass)
          });
        }
      }
      else
      {
        foreach (var draft in layerDrafts)
        {
          if (!File.Exists(draft.FilePath))
          {
            Console.Error.WriteLine($"Error: File not found: {draft.FilePath}");
            return 1;
          }

          designLayers.Add(new LayerDesignInput
          {
            Layer = draft.Layer,
            IsolineFilePath = Path.GetFullPath(draft.FilePath),
            Legend = await LoadLayerLegendAsync(legendLoader, draft.LegendPath, legend),
            Background = draft.BackgroundDiameterMm is int diameter && draft.BackgroundSpacingMm is int spacing
                ? new BackgroundMesh(
                    draft.Layer,
                    new ReinforcementSpec { DiameterMm = diameter, SpacingMm = spacing, SteelClass = steelClass },
                    GridOriginMm: 0)
                : null
          });
        }
      }

      if (designLayers.Count > 0)
      {
        string? designError = LayerDesignRules.Validate(designLayers);
        if (designError is not null)
        {
          Console.Error.WriteLine($"Error: {designError}");
          return 1;
        }
      }

      // Build slab geometry
      var slab = new SlabGeometry
      {
        OuterBoundary = new Polygon(
          [
              new Point2D(0, 0),
                    new Point2D(slabWidth, 0),
                    new Point2D(slabWidth, slabHeight),
                    new Point2D(0, slabHeight)
          ]),
        ThicknessMm = thickness,
        CoverMm = cover,
        EdgeCoverMm = edgeCover,
        OpeningClearanceMm = openingClearance,
        ConcreteClass = concreteClass
      };

      var input = new PipelineInput
      {
        IsolineFilePath = isolineFile,
        Legend = legend,
        Slab = slab,
        SupplierCatalogPath = catalogPath,
        Metadata = new PipelineExecutionMetadata
        {
          ProjectCode = project?.ProjectCode ?? "OpenRebar-CLI",
          SlabId = project?.SlabId ?? Path.GetFileNameWithoutExtension(isolineFile),
          LevelName = "Standalone",
          NormativeProfileId = normArg ?? project?.Norm ?? NormativeProfiles.DefaultProfileId
        },
        PlaceInRevit = false,
        PersistReport = true,
        ReportOutputPath = Path.ChangeExtension(isolineFile, ".result.json"),
        Layer = designLayers.Count > 0 ? null : layer,
        Layers = designLayers,
        CompanyProfilePath = profilePath,
        CompanyProfileId = loadedProfile?.Profile.Id,
        CompanyProfileVersion = loadedProfile?.Profile.Version ?? "0",
        JointRatioMax = loadedProfile?.Profile.Laps.JointRatioMax ?? 0.5,
        EndCondition = loadedProfile?.Profile.Ends.Condition ?? "NeedsHook",
        SupplierCatalog = catalogPath is null && loadedProfile is not null
            ? CompanyProfileLoader.ToCatalog(loadedProfile.Profile)
            : null,
        ParameterSources = loadedProfile is null
            ? []
            : ParameterSourceRecorder.Build(
                loadedProfile.Profile,
                steelClass,
                steelFromCaller || (project is not null && !steelFromCaller && !string.IsNullOrWhiteSpace(project.SteelClass)) ? "project" : "profile",
                concreteClass,
                concreteFromCaller || (project is not null && !concreteFromCaller && !string.IsNullOrWhiteSpace(project.Slab.ConcreteClass)) ? "project" : "profile",
                csvCultureName,
                GetArgValue(args, "--csv-culture") is not null ? "project" : "profile",
                catalogPath ?? loadedProfile.Profile.Supply.SupplierName,
                catalogPath ?? string.Join(",", loadedProfile.Profile.Supply.StockLengthsMm),
                catalogPath is not null ? "project" : "profile",
                legend.Entries.Count,
                legendPath is not null ? "project" : "profile"),
        UseLegacyDirectionHeuristic = designLayers.Count == 0 && legacyDirection && layer is null && asField is null,
        RequireExplicitLayer = true,
        AsField = asField,
        FieldDiametersMm = loadedProfile?.Profile.Additional.DiametersMm ?? [],
        AdditionalSpacingMode = loadedProfile?.Profile.Additional.SpacingMode ?? "interleave",
        FieldSpacingsMm = NormativeProfiles.Sp63_2018.StandardSpacingsMm,
        IncludeDiagnostics = args.Any(arg => arg.Equals("--include-diagnostics", StringComparison.OrdinalIgnoreCase))
      };

      var pngParser = sp.GetRequiredService<OpenRebar.Infrastructure.ImageProcessing.PngIsolineParser>();
      pngParser.Calibration = rasterCalibration;
      pngParser.MinZoneAreaMm2 = minZoneAreaMm2;

      var dxfParser = sp.GetRequiredService<OpenRebar.Infrastructure.DxfProcessing.DxfIsolineParser>();
      if (!dxfParser.TrySetUnitsWhenUnset(dxfUnitsName))
      {
        Console.Error.WriteLine("Error: --dxf-units must be mm, cm, m, or in.");
        return 1;
      }

      var pipeline = sp.GetRequiredService<GenerateReinforcementPipeline>();

      Console.WriteLine("Running pipeline...");
      var sw = Stopwatch.StartNew();

      var result = await pipeline.ExecuteAsync(input);

      sw.Stop();
      int exitCode = ResolveExitCode(result.Report);
      if (exitCode != 0)
      {
        var critical = result.Report?.Errors.FirstOrDefault(error => error.IsCritical);
        if (critical is not null)
          Console.Error.WriteLine(critical.ErrorMessage);
      }

      Console.WriteLine();
      PrintResult(result, sw.Elapsed);

      if (result.StoredReport is not null)
      {
        if (result.Verification?.Cells.Count > 0)
        {
          string reportPath = result.StoredReport.OutputPath;
          string? directory = Path.GetDirectoryName(reportPath);
          string name = Path.GetFileName(reportPath);
          const string suffix = ".result.json";
          string stem = name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
              ? name[..^suffix.Length]
              : Path.GetFileNameWithoutExtension(name);
          string folder = string.IsNullOrEmpty(directory) ? Directory.GetCurrentDirectory() : directory;
          await OpenRebar.Infrastructure.Reporting.VerificationHeatmapWriter.WriteAsync(
              result.Verification,
              Path.Combine(folder, stem + ".verification.png"),
              Path.Combine(folder, stem + ".verification.json"));
        }

        Console.WriteLine($"\nResult exported to: {result.StoredReport.OutputPath}");
        Console.WriteLine("Schema contract: contracts/aerobim-reinforcement-report.schema.json");

        if (!string.IsNullOrWhiteSpace(aeroBimStorageDir) && result.Report is not null)
        {
          var handoff = await handoffWriter.WriteAsync(
              result.Report,
              result.StoredReport,
              aeroBimStorageDir);
          Console.WriteLine($"AeroBIM handoff manifest written to: {handoff.ManifestPath}");
          Console.WriteLine(
              $"AeroBIM request field reinforcement_handoff_path: {handoff.RelativeManifestPath}");
        }
      }

      string schedulePath = Path.ChangeExtension(isolineFile, ".schedule.csv");
      await scheduleExporter.ExportAsync(result.ClassifiedZones, schedulePath, csvCulture);
      Console.WriteLine($"Schedule exported to: {schedulePath}");

      if (result.Report is not null)
      {
        string aeroBimPath = Path.ChangeExtension(isolineFile, ".aerobim.json");
        await reportExporter.ExportAsync(result.Report, result.ClassifiedZones, aeroBimPath);
        Console.WriteLine($"AeroBIM report exported to: {aeroBimPath}");
      }

      string ifcPath = Path.ChangeExtension(isolineFile, ".reinforcement.ifc");
      await ifcExporter.ExportAsync(result.ClassifiedZones, input.Slab, ifcPath);
      Console.WriteLine($"IFC export written to: {ifcPath}");

      return exitCode;
    }
    catch (OpenRebarDomainException ex)
    {
      Console.Error.WriteLine($"Error [{ex.ErrorCode}]: {ex.Message}");
      return 1;
    }
    catch (Exception ex)
    {
      Console.Error.WriteLine($"Unhandled error: {ex.Message}");
      return 1;
    }
  }

  /// <summary>
  /// 0 passed, 1 input or IO, 2 verification failed, 3 partial result.
  /// </summary>
  public static int ResolveExitCode(ReinforcementExecutionReport? report)
  {
    if (report is null)
      return 1;

    var critical = report.Errors.Where(error => error.IsCritical).ToList();
    if (critical.Count == 0)
      return 0;

    if (critical.Any(error => error.ExceptionType is "VerificationFailed"))
      return 2;

    if (critical.Any(error => error.ExceptionType is
            "LayerNotSpecified"
            or "RasterNotCalibratedException"
            or "InvalidIsolineFileException"
            or "LegendLoadException"))
      return 1;

    return report.PartialResult ? 3 : 1;
  }

  private static bool TryParsePixelPair(string? text, out double x, out double y)
  {
    x = 0;
    y = 0;
    if (string.IsNullOrWhiteSpace(text))
      return false;

    var parts = text.Split(',');
    return parts.Length == 2
        && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x)
        && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out y);
  }

  private static void PrintUsage()
  {
    Console.WriteLine(@"
OpenRebar Reinforcement CLI — run the full pipeline without Revit.

Usage:
  dotnet run --project src/OpenRebar.Cli -- <isoline-file> [options]

Options:
  --catalog <path>     Supplier catalog (JSON/CSV). Default: Russian market standard.
    --legend <path>      Legend config (JSON). Default: built-in 7-color A500C legend.
  --ml-url <url>       ML segmentation service URL. Default: color quantization fallback.
    --aerobim-storage-dir <path>
                                                Copy the canonical report into an AeroBIM storage root and emit a handoff manifest.
  --project <path>     Project file (slab, materials, layers). --design is the same flag.
  --profile <path>     Company profile. Default: profiles/generic.json.
                       profile init | validate | diff  — see profile --help.
  --norm <id>          Normative profile id. Currently ru.sp63.2018.
  --layer-input <BottomX=path.dxf;legend=legend.json;bg=10@200>
                       One layer. Repeat for up to four layers. Do not combine with --project.
  --layer <BottomX|BottomY|TopX|TopY>
                       A .csv field table already names its layers. Do not combine it with --layer.
                       Reinforcement layer for a single drawing. Required unless the DXF layer name maps to one,
                       or --legacy-direction-heuristic is set.
  --legacy-direction-heuristic
                       Infer bar direction from the zone bounding box and warn.
  --steel <class>      Steel class. Default: A500C.
  --csv-culture <ru|invariant>
                       Number format in the schedule CSV. Default: ru.
  --dxf-units <mm|cm|m|in>
                       Unit assumed when the DXF header has no $INSUNITS. Default: mm.
  --px-per-mm <value>  PNG scale. Required for raster input, together with --origin-px.
  --origin-px <x,y>    Pixel that maps to slab (0, 0). Y in the image points down.
  --roi-px <x,y,w,h>   Slab rectangle on the image. Default: the whole image, with a warning.
  --min-zone-area-mm2 <mm2>
                       Drop raster components smaller than this. Default: 100000 (0.1 m²).
  --include-diagnostics
                       Write exception stack traces into the report.

Exit codes:
  0  passed
  1  input or IO error
  2  verification failed
  3  partial result
  --concrete <class>   Concrete class. Default: B25.
  --thickness <mm>     Slab thickness. Default: 200.
  --cover <mm>         Concrete cover. Default: 25.
  --edge-cover <mm>    Working-area inset from the slab edge. Default: 0.
  --opening-clearance <mm>
                       Extra gap around each opening. Default: 0.
    --slab-width <mm>    Slab outer boundary width. Default: 30000.
    --slab-height <mm>   Slab outer boundary height. Default: 20000.

Examples:
  dotnet run --project src/OpenRebar.Cli -- data/floor5.dxf
    dotnet run --project src/OpenRebar.Cli -- data/floor5.dxf --legend configs/lira.legend.json
  dotnet run --project src/OpenRebar.Cli -- data/isoline.png --ml-url http://localhost:8101
  dotnet run --project src/OpenRebar.Cli -- data/floor5.dxf --catalog suppliers/evraz.json
    dotnet run --project src/OpenRebar.Cli -- data/floor5.dxf --aerobim-storage-dir ../AeroBIM/var/reports
    dotnet run --project src/OpenRebar.Cli -- data/floor5.dxf --slab-width 18000 --slab-height 9000
");
  }

  private static void PrintResult(PipelineResult result, TimeSpan elapsed)
  {
    Console.WriteLine("═══════════════════════════════════════════════════════");
    Console.WriteLine("  PIPELINE RESULT");
    Console.WriteLine("═══════════════════════════════════════════════════════");
    Console.WriteLine($"  Zones parsed:         {result.ParsedZoneCount}");
    Console.WriteLine($"  Zones classified:     {result.ClassifiedZones.Count}");
    Console.WriteLine($"    Simple:             {result.ClassifiedZones.Count(z => z.ZoneType == ZoneType.Simple)}");
    Console.WriteLine($"    Complex:            {result.ClassifiedZones.Count(z => z.ZoneType == ZoneType.Complex)}");
    Console.WriteLine($"    Special:            {result.ClassifiedZones.Count(z => z.ZoneType == ZoneType.Special)}");
    Console.WriteLine($"  Total rebar segments: {result.TotalRebarSegments}");
    Console.WriteLine();

    foreach (var (diameter, optResult) in result.OptimizationResults.OrderBy(kv => kv.Key))
    {
      Console.WriteLine($"  ── ⌀{diameter} mm ──────────────────────────────────");
      Console.WriteLine($"    Stock bars needed:  {optResult.TotalStockBarsNeeded}");
      Console.WriteLine($"    Total waste:        {optResult.TotalWastePercent:F1}%");
      Console.WriteLine($"    Total rebar length: {optResult.TotalRebarLengthMm / 1000.0:F1} m");
      if (optResult.TotalMassKg.HasValue)
        Console.WriteLine($"    Total mass:         {optResult.TotalMassKg.Value:F1} kg");
      if (optResult.EstimatedCost.HasValue)
        Console.WriteLine($"    Estimated cost:     {optResult.EstimatedCost.Value:F2}");
    }

    Console.WriteLine();
    Console.WriteLine($"  Average waste:        {result.TotalWastePercent:F1}%");
    Console.WriteLine($"  Total mass:           {result.TotalMassKg:F1} kg");
    Console.WriteLine($"  Time:                 {elapsed.TotalSeconds:F2}s");
    Console.WriteLine("═══════════════════════════════════════════════════════");
  }

  private static bool TryParseScheduleCulture(string value, out ScheduleNumberCulture culture)
  {
    if (value.Equals("ru", StringComparison.OrdinalIgnoreCase))
    {
      culture = ScheduleNumberCulture.Ru;
      return true;
    }

    if (value.Equals("invariant", StringComparison.OrdinalIgnoreCase))
    {
      culture = ScheduleNumberCulture.Invariant;
      return true;
    }

    culture = ScheduleNumberCulture.Ru;
    return false;
  }

  private static async Task<ColorLegend> LoadLayerLegendAsync(
      ILegendLoader legendLoader,
      string? legendFile,
      ColorLegend fallback)
  {
    if (string.IsNullOrWhiteSpace(legendFile))
      return fallback;

    return await legendLoader.LoadAsync(legendFile);
  }

  private static BackgroundMesh? CreateBackground(LayerKey layer, ProjectBackgroundDocument? background, string steelClass)
  {
    if (background is null)
      return null;

    return new BackgroundMesh(
        layer,
        new ReinforcementSpec
        {
          DiameterMm = background.DiameterMm,
          SpacingMm = background.SpacingMm,
          SteelClass = string.IsNullOrWhiteSpace(background.SteelClass) ? steelClass : background.SteelClass
        },
        background.GridOriginMm);
  }

  private static List<string> GetArgValues(string[] args, string flag)
  {
    var values = new List<string>();
    for (int i = 0; i < args.Length - 1; i++)
    {
      if (args[i].Equals(flag, StringComparison.OrdinalIgnoreCase))
        values.Add(args[i + 1]);
    }

    return values;
  }

  private static string? GetArgValue(string[] args, string flag)
  {
    for (int i = 0; i < args.Length - 1; i++)
    {
      if (args[i].Equals(flag, StringComparison.OrdinalIgnoreCase))
        return args[i + 1];
    }
    return null;
  }

  private static bool TryGetOptionalDouble(
      string[] args,
      string flag,
      double defaultValue,
      double minimumValue,
      bool inclusiveMinimum,
      out double value,
      out string? error)
  {
    var raw = GetArgValue(args, flag);
    if (raw is null)
    {
      value = defaultValue;
      error = null;
      return true;
    }

    if (!double.TryParse(raw, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out value))
    {
      error = $"Error: Invalid numeric value for {flag}: '{raw}'. Use invariant numeric format, for example 200 or 200.5.";
      return false;
    }

    bool isValid = inclusiveMinimum ? value >= minimumValue : value > minimumValue;
    if (!isValid)
    {
      error = inclusiveMinimum
          ? $"Error: {flag} must be greater than or equal to {minimumValue.ToString(CultureInfo.InvariantCulture)}."
          : $"Error: {flag} must be greater than {minimumValue.ToString(CultureInfo.InvariantCulture)}.";
      return false;
    }

    error = null;
    return true;
  }
}
