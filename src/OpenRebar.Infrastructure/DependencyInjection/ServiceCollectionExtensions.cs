using OpenRebar.Application.UseCases;
using OpenRebar.Domain.Ports;
using OpenRebar.Infrastructure.Catalog;
using OpenRebar.Infrastructure.DxfProcessing;
using OpenRebar.Infrastructure.Export;
using OpenRebar.Infrastructure.ImageProcessing;
using OpenRebar.Infrastructure.Legend;
using OpenRebar.Infrastructure.Logging;
using OpenRebar.Infrastructure.Optimization;
using OpenRebar.Infrastructure.ReinforcementEngine;
using OpenRebar.Infrastructure.Reporting;
using OpenRebar.Infrastructure.ZoneProcessing;
using Microsoft.Extensions.DependencyInjection;

namespace OpenRebar.Infrastructure.DependencyInjection;

/// <summary>
/// Common service registration for CLI and Revit hosts.
/// Keeps adapter and use-case wiring in one place to avoid drift between entrypoints.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddOpenRebarCoreServices(
        this IServiceCollection services,
        string? mlServiceUrl = null)
    {
        services.AddSingleton<IStructuredLogger, ConsoleStructuredLogger>();
        services.AddSingleton<DxfIsolineParser>();
        services.AddSingleton<IZoneDetector, StandardZoneDetector>();
        services.AddSingleton<IReinforcementCalculator, StandardReinforcementCalculator>();
        services.AddSingleton<IRebarOptimizer, ColumnGenerationOptimizer>();
        services.AddSingleton<ISupplierCatalogLoader, FileSupplierCatalogLoader>();
        services.AddSingleton<ILegendLoader, JsonLegendLoader>();
        services.AddSingleton<IIfcExporter, XbimIfcExporter>();
        services.AddSingleton<IReportStore, JsonFileReportStore>();
        services.AddSingleton<IReportExporter, AeroBimReportExporter>();
        services.AddSingleton<AeroBimHandoffManifestWriter>();
        services.AddSingleton<IScheduleExporter, CsvScheduleExporter>();

        if (!string.IsNullOrWhiteSpace(mlServiceUrl))
        {
            services.AddSingleton<IImageSegmentationService>(
                _ => new HttpImageSegmentationService(mlServiceUrl));
        }

        services.AddSingleton<PngIsolineParser>(sp =>
            new PngIsolineParser(sp.GetService<IImageSegmentationService>()));

        services.AddTransient<GenerateReinforcementPipeline>(sp =>
            new GenerateReinforcementPipeline(
                dxfParser: sp.GetRequiredService<DxfIsolineParser>(),
                pngParser: sp.GetRequiredService<PngIsolineParser>(),
                zoneDetector: sp.GetRequiredService<IZoneDetector>(),
                calculator: sp.GetRequiredService<IReinforcementCalculator>(),
                optimizer: sp.GetRequiredService<IRebarOptimizer>(),
                catalogLoader: sp.GetRequiredService<ISupplierCatalogLoader>(),
                placer: sp.GetRequiredService<IRevitPlacer>(),
                reportStore: sp.GetRequiredService<IReportStore>(),
                logger: sp.GetRequiredService<IStructuredLogger>()));

        services.AddTransient<OptimizeRebarCuttingUseCase>(sp =>
            new OptimizeRebarCuttingUseCase(
                sp.GetRequiredService<IRebarOptimizer>(),
                sp.GetRequiredService<ISupplierCatalogLoader>(),
                sp.GetRequiredService<IStructuredLogger>()));

        services.AddTransient<BatchReinforcementPipeline>();

        return services;
    }
}