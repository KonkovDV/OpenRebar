using OpenRebar.Application.UseCases;
using OpenRebar.Domain.Ports;
using OpenRebar.Infrastructure.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace OpenRebar.RevitPlugin;

/// <summary>
/// DI container bootstrap — registers all services.
/// Based on MicroPhoenix Clean Architecture patterns:
///   Domain ports → Infrastructure adapters, constructor injection only.
/// </summary>
public static class Bootstrap
{
  /// <summary>
  /// Build the full DI container for the Revit plugin runtime.
  /// </summary>
  /// <param name="revitPlacer">Revit placer instance (real or stub).</param>
  /// <param name="mlServiceUrl">
  /// URL of the Python ML segmentation service.
  /// If null, PNG parsing falls back to color quantization (no ML).
  /// Default: http://localhost:8101
  /// </param>
  public static IServiceProvider BuildServiceProvider(
      IRevitPlacer revitPlacer,
      string? mlServiceUrl = null)
  {
    var services = new ServiceCollection();

    services.AddOpenRebarCoreServices(mlServiceUrl);

    // Revit placer — provided by the host (depends on active Revit context)
    services.AddSingleton<IRevitPlacer>(revitPlacer);

    return services.BuildServiceProvider(validateScopes: true);
  }
}
