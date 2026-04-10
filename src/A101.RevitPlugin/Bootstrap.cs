using A101.Application.UseCases;
using A101.Domain.Ports;
using A101.Infrastructure.Catalog;
using A101.Infrastructure.DxfProcessing;
using A101.Infrastructure.ImageProcessing;
using A101.Infrastructure.Optimization;
using A101.Infrastructure.ReinforcementEngine;
using A101.Infrastructure.ZoneProcessing;
using Microsoft.Extensions.DependencyInjection;

namespace A101.RevitPlugin;

/// <summary>
/// DI container bootstrap — registers all services.
/// Based on MicroPhoenix Clean Architecture patterns:
///   Domain ports → Infrastructure adapters, constructor injection only.
/// </summary>
public static class Bootstrap
{
    public static IServiceProvider BuildServiceProvider(IRevitPlacer revitPlacer)
    {
        var services = new ServiceCollection();

        // Domain ports → Infrastructure adapters
        services.AddSingleton<IIsolineParser, DxfIsolineParser>();
        services.AddSingleton<IZoneDetector, StandardZoneDetector>();
        services.AddSingleton<IReinforcementCalculator, StandardReinforcementCalculator>();
        services.AddSingleton<IRebarOptimizer, ColumnGenerationOptimizer>();
        services.AddSingleton<ISupplierCatalogLoader, FileSupplierCatalogLoader>();

        // PNG parser (with optional ML service)
        services.AddSingleton<PngIsolineParser>();

        // Revit placer — provided by the host (depends on active Revit context)
        services.AddSingleton(revitPlacer);

        // Application use cases
        services.AddTransient<GenerateReinforcementPipeline>(sp =>
            new GenerateReinforcementPipeline(
                dxfParser: sp.GetRequiredService<IIsolineParser>(),
                pngParser: sp.GetRequiredService<PngIsolineParser>(),
                zoneDetector: sp.GetRequiredService<IZoneDetector>(),
                calculator: sp.GetRequiredService<IReinforcementCalculator>(),
                optimizer: sp.GetRequiredService<IRebarOptimizer>(),
                catalogLoader: sp.GetRequiredService<ISupplierCatalogLoader>(),
                placer: sp.GetRequiredService<IRevitPlacer>()));

        services.AddTransient<OptimizeRebarCuttingUseCase>();

        return services.BuildServiceProvider();
    }
}
