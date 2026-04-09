using A101.Domain.Models;
using A101.Domain.Ports;
using System.Text.Json;

namespace A101.Infrastructure.Catalog;

/// <summary>
/// Loads supplier catalogs from JSON/CSV files.
/// Provides default Russian market stock lengths.
/// </summary>
public sealed class FileSupplierCatalogLoader : ISupplierCatalogLoader
{
    public async Task<SupplierCatalog> LoadAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Supplier catalog not found: {filePath}");

        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext switch
        {
            ".json" => await LoadJsonAsync(filePath, cancellationToken),
            ".csv" => await LoadCsvAsync(filePath, cancellationToken),
            _ => throw new NotSupportedException($"Unsupported catalog format: {ext}")
        };
    }

    public SupplierCatalog GetDefaultCatalog()
    {
        return new SupplierCatalog
        {
            SupplierName = "Default (Russian market)",
            AvailableLengths =
            [
                new StockLength { LengthMm = 6000, InStock = true },
                new StockLength { LengthMm = 9000, InStock = true },
                new StockLength { LengthMm = 11700, InStock = true },
                new StockLength { LengthMm = 12000, InStock = true },
            ]
        };
    }

    private static async Task<SupplierCatalog> LoadJsonAsync(
        string filePath, CancellationToken ct)
    {
        var json = await File.ReadAllTextAsync(filePath, ct);
        var data = JsonSerializer.Deserialize<SupplierCatalogDto>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (data is null)
            throw new InvalidDataException("Failed to parse supplier catalog JSON.");

        return new SupplierCatalog
        {
            SupplierName = data.SupplierName ?? Path.GetFileNameWithoutExtension(filePath),
            AvailableLengths = data.Lengths.Select(l => new StockLength
            {
                LengthMm = l.LengthMm,
                PricePerTon = l.PricePerTon,
                InStock = l.InStock
            }).ToList()
        };
    }

    private static async Task<SupplierCatalog> LoadCsvAsync(
        string filePath, CancellationToken ct)
    {
        var lines = await File.ReadAllLinesAsync(filePath, ct);
        var lengths = new List<StockLength>();

        // Expected CSV: LengthMm,PricePerTon,InStock
        foreach (var line in lines.Skip(1)) // Skip header
        {
            var parts = line.Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length < 1) continue;

            if (double.TryParse(parts[0], out double lengthMm))
            {
                double? price = parts.Length > 1 && double.TryParse(parts[1], out double p) ? p : null;
                bool inStock = parts.Length <= 2 || !bool.TryParse(parts[2], out bool s) || s;

                lengths.Add(new StockLength
                {
                    LengthMm = lengthMm,
                    PricePerTon = price,
                    InStock = inStock
                });
            }
        }

        return new SupplierCatalog
        {
            SupplierName = Path.GetFileNameWithoutExtension(filePath),
            AvailableLengths = lengths
        };
    }

    // DTO for JSON deserialization
    private sealed record SupplierCatalogDto
    {
        public string? SupplierName { get; init; }
        public List<StockLengthDto> Lengths { get; init; } = [];
    }

    private sealed record StockLengthDto
    {
        public double LengthMm { get; init; }
        public double? PricePerTon { get; init; }
        public bool InStock { get; init; } = true;
    }
}
