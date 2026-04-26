using System.Diagnostics;
using System.Globalization;
using OpenRebar.Domain.Exceptions;
using OpenRebar.Application.UseCases;
using OpenRebar.Domain.Models;
using OpenRebar.Domain.Ports;
using OpenRebar.Infrastructure.DependencyInjection;
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

    string isolineFile = args[0];
    string? catalogPath = GetArgValue(args, "--catalog");
    string? legendPath = GetArgValue(args, "--legend");
    string? mlUrl = GetArgValue(args, "--ml-url");
    string? aeroBimStorageDir = GetArgValue(args, "--aerobim-storage-dir");
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

    if (!TryGetOptionalDouble(args, "--slab-width", 30000, minimumValue: 0, inclusiveMinimum: false, out double slabWidth, out string? slabWidthError))
    {
      Console.Error.WriteLine(slabWidthError);
      return 1;
    }

    if (!TryGetOptionalDouble(args, "--slab-height", 20000, minimumValue: 0, inclusiveMinimum: false, out double slabHeight, out string? slabHeightError))
    {
      Console.Error.WriteLine(slabHeightError);
      return 1;
    }

    if (!File.Exists(isolineFile))
    {
      Console.Error.WriteLine($"Error: File not found: {isolineFile}");
      return 1;
    }

    if (cover >= thickness)
    {
      Console.Error.WriteLine("Error: --cover must be smaller than --thickness.");
      return 1;
    }

    try
    {
      Console.WriteLine($"OpenRebar Reinforcement CLI v1.0.0");
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
          : legendLoader.GetDefaultLegend(steelClass);

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
          ProjectCode = "OpenRebar-CLI",
          SlabId = Path.GetFileNameWithoutExtension(isolineFile),
          LevelName = "Standalone"
        },
        PlaceInRevit = false,
        PersistReport = true,
        ReportOutputPath = Path.ChangeExtension(isolineFile, ".result.json")
      };

      var pipeline = sp.GetRequiredService<GenerateReinforcementPipeline>();

      Console.WriteLine("Running pipeline...");
      var sw = Stopwatch.StartNew();

      var result = await pipeline.ExecuteAsync(input);

      sw.Stop();
      Console.WriteLine();
      PrintResult(result, sw.Elapsed);

      if (result.StoredReport is not null)
      {
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
      await scheduleExporter.ExportAsync(result.ClassifiedZones, schedulePath);
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

      return 0;
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
  --steel <class>      Steel class. Default: A500C.
  --concrete <class>   Concrete class. Default: B25.
  --thickness <mm>     Slab thickness. Default: 200.
  --cover <mm>         Concrete cover. Default: 25.
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
