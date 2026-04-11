using A101.Application.UseCases;
using A101.Domain.Ports;
using A101.Infrastructure.Catalog;
using A101.Infrastructure.DxfProcessing;
using A101.Infrastructure.Export;
using A101.Infrastructure.ImageProcessing;
using A101.Infrastructure.Legend;
using A101.Infrastructure.Logging;
using A101.Infrastructure.Optimization;
using A101.Infrastructure.ReinforcementEngine;
using A101.Infrastructure.Reporting;
using A101.Infrastructure.ZoneProcessing;
using Microsoft.Extensions.DependencyInjection;

namespace A101.Infrastructure.DependencyInjection;

/// <summary>
/// Common service registration for CLI and Revit hosts.
/// Keeps adapter and use-case wiring in one place to avoid drift between entrypoints.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddA101CoreServices(
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