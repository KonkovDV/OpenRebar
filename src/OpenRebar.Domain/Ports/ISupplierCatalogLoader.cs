using OpenRebar.Domain.Models;

namespace OpenRebar.Domain.Ports;

/// <summary>
/// Loads supplier catalogs with available rebar sizes and prices.
/// </summary>
public interface ISupplierCatalogLoader
{
  /// <summary>
  /// Load catalog from a file (CSV, JSON).
  /// </summary>
  Task<SupplierCatalog> LoadAsync(string filePath, CancellationToken cancellationToken = default);

  /// <summary>
  /// Get default stock lengths when no supplier catalog is available.
  /// Standard Russian market lengths: 6000, 9000, 11700, 12000 mm.
  /// </summary>
  SupplierCatalog GetDefaultCatalog();
}
