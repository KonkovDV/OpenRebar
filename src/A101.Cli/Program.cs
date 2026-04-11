using System.Diagnostics;
using A101.Domain.Exceptions;
using A101.Application.UseCases;
using A101.Domain.Models;
using A101.Domain.Ports;
using A101.Infrastructure.DependencyInjection;
using A101.Infrastructure.Stubs;
using Microsoft.Extensions.DependencyInjection;

namespace A101.Cli;

/// <summary>
/// Console entry point for running the reinforcement pipeline without Revit.
/// Useful for batch processing, CI testing, and debugging.
///
/// Usage:
///   dotnet run --project src/A101.Cli -- &lt;isoline-file&gt; [--catalog &lt;catalog.json&gt;] [--ml-url &lt;url&gt;]
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
        string steelClass = GetArgValue(args, "--steel") ?? "A500C";
        string concreteClass = GetArgValue(args, "--concrete") ?? "B25";
        double thickness = double.TryParse(GetArgValue(args, "--thickness"), out var t) ? t : 200;
        double cover = double.TryParse(GetArgValue(args, "--cover"), out var c) ? c : 25;

        if (!File.Exists(isolineFile))
        {
            Console.Error.WriteLine($"Error: File not found: {isolineFile}");
            return 1;
        }

        Console.WriteLine($"A101 Reinforcement CLI v1.0.0");
        Console.WriteLine($"  Isoline file: {isolineFile}");
        Console.WriteLine($"  Concrete: {concreteClass}, Steel: {steelClass}");
        Console.WriteLine($"  Slab: {thickness}mm thick, {cover}mm cover");
        if (legendPath is not null) Console.WriteLine($"  Legend config: {legendPath}");
        if (mlUrl is not null) Console.WriteLine($"  ML service: {mlUrl}");
        Console.WriteLine();

        // Build DI container
        var services = new ServiceCollection();
        services.AddA101CoreServices(mlUrl);
        services.AddSingleton<IRevitPlacer, StubRevitPlacer>();

        var sp = services.BuildServiceProvider(validateScopes: true);

        var legendLoader = sp.GetRequiredService<ILegendLoader>();
        var ifcExporter = sp.GetRequiredService<IIfcExporter>();
        var reportExporter = sp.GetRequiredService<IReportExporter>();
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
                new Point2D(30000, 0),
                new Point2D(30000, 20000),
                new Point2D(0, 20000)
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
                ProjectCode = "A101-CLI",
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

    private static void PrintUsage()
    {
        Console.WriteLine(@"
A101 Reinforcement CLI — run the full pipeline without Revit.

Usage:
  dotnet run --project src/A101.Cli -- <isoline-file> [options]

Options:
  --catalog <path>     Supplier catalog (JSON/CSV). Default: Russian market standard.
    --legend <path>      Legend config (JSON). Default: built-in 7-color A500C legend.
  --ml-url <url>       ML segmentation service URL. Default: color quantization fallback.
  --steel <class>      Steel class. Default: A500C.
  --concrete <class>   Concrete class. Default: B25.
  --thickness <mm>     Slab thickness. Default: 200.
  --cover <mm>         Concrete cover. Default: 25.

Examples:
  dotnet run --project src/A101.Cli -- data/floor5.dxf
    dotnet run --project src/A101.Cli -- data/floor5.dxf --legend configs/lira.legend.json
  dotnet run --project src/A101.Cli -- data/isoline.png --ml-url http://localhost:8101
  dotnet run --project src/A101.Cli -- data/floor5.dxf --catalog suppliers/evraz.json
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
}
