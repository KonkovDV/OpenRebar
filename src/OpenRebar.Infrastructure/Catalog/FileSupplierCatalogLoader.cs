using System.Globalization;
using OpenRebar.Domain.Exceptions;
using OpenRebar.Domain.Models;
using OpenRebar.Domain.Ports;
using System.Text.Json;

namespace OpenRebar.Infrastructure.Catalog;

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
      _ => throw new SupplierCatalogLoadException(filePath, $"Unsupported catalog format: {ext}")
    };
  }

  public SupplierCatalog GetDefaultCatalog()
  {
    string? generic = Profiles.CompanyProfileLoader.TryResolveBundledGeneric();
    if (generic is not null)
      return Profiles.CompanyProfileLoader.ToCatalog(Profiles.CompanyProfileLoader.Load(generic).Profile);

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
    try
    {
      var json = await File.ReadAllTextAsync(filePath, ct);
      var data = JsonSerializer.Deserialize<SupplierCatalogDto>(json, new JsonSerializerOptions
      {
        PropertyNameCaseInsensitive = true
      });

      if (data is null)
        throw new SupplierCatalogLoadException(filePath, "Failed to parse supplier catalog JSON.");

      if (data.Lengths.Count == 0)
        throw new SupplierCatalogLoadException(filePath, "Supplier catalog does not contain any stock lengths.");

      var lengths = new List<StockLength>(data.Lengths.Count);
      for (int index = 0; index < data.Lengths.Count; index++)
      {
        StockLengthDto row = data.Lengths[index];
        lengths.Add(CheckedLength(filePath, $"Entry {index + 1}", row.LengthMm, row.PricePerTon, row.InStock, lengths));
      }

      return new SupplierCatalog
      {
        SupplierName = data.SupplierName ?? Path.GetFileNameWithoutExtension(filePath),
        AvailableLengths = lengths
      };
    }
    catch (SupplierCatalogLoadException)
    {
      throw;
    }
    catch (JsonException ex)
    {
      throw new SupplierCatalogLoadException(filePath, ex.Message, ex);
    }
  }

  private static readonly string[] CsvHeader = ["lengthmm", "priceperton", "instock"];

  private static async Task<SupplierCatalog> LoadCsvAsync(
      string filePath, CancellationToken ct)
  {
    var lines = await File.ReadAllLinesAsync(filePath, ct);
    if (lines.Length == 0 || !HeaderMatches(lines[0]))
      throw new SupplierCatalogLoadException(filePath, "CSV header must be LengthMm,PricePerTon,InStock.");

    var lengths = new List<StockLength>();
    for (int index = 1; index < lines.Length; index++)
    {
      string line = lines[index];
      if (string.IsNullOrWhiteSpace(line))
        continue;

      int lineNumber = index + 1;
      var parts = line.Split(',', StringSplitOptions.TrimEntries);
      if (parts.Length != 3)
        throw new SupplierCatalogLoadException(filePath, $"Line {lineNumber}: expected 3 columns.");

      if (!TryFinite(parts[0], out double lengthMm))
        throw new SupplierCatalogLoadException(filePath, $"Line {lineNumber}: LengthMm is not a number.");

      double? price = null;
      if (parts[1].Length > 0)
      {
        if (!TryFinite(parts[1], out double parsed))
          throw new SupplierCatalogLoadException(filePath, $"Line {lineNumber}: PricePerTon is not a number.");
        price = parsed;
      }

      if (!bool.TryParse(parts[2], out bool inStock))
        throw new SupplierCatalogLoadException(filePath, $"Line {lineNumber}: InStock must be true or false.");

      lengths.Add(CheckedLength(filePath, $"Line {lineNumber}", lengthMm, price, inStock, lengths));
    }

    if (lengths.Count == 0)
      throw new SupplierCatalogLoadException(filePath, "CSV catalog does not contain any valid stock lengths.");

    return new SupplierCatalog
    {
      SupplierName = Path.GetFileNameWithoutExtension(filePath),
      AvailableLengths = lengths
    };
  }

  private static bool HeaderMatches(string line)
  {
    var parts = line.Split(',', StringSplitOptions.TrimEntries);
    if (parts.Length != CsvHeader.Length)
      return false;

    for (int i = 0; i < CsvHeader.Length; i++)
    {
      if (!parts[i].Equals(CsvHeader[i], StringComparison.OrdinalIgnoreCase))
        return false;
    }

    return true;
  }

  private static bool TryFinite(string text, out double value)
  {
    return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
        && double.IsFinite(value);
  }

  private static StockLength CheckedLength(
      string filePath,
      string place,
      double lengthMm,
      double? pricePerTon,
      bool inStock,
      List<StockLength> accepted)
  {
    string where = $"{place}: ";
    if (!double.IsFinite(lengthMm) || lengthMm <= 0)
      throw new SupplierCatalogLoadException(filePath, $"{where}LengthMm must be a finite number greater than 0.");

    if (pricePerTon is { } price && (!double.IsFinite(price) || price < 0))
      throw new SupplierCatalogLoadException(filePath, $"{where}PricePerTon must be empty or a finite number of at least 0.");

    if (accepted.Any(stock => Math.Abs(stock.LengthMm - lengthMm) <= 1e-6))
      throw new SupplierCatalogLoadException(filePath, $"{where}duplicate length {lengthMm.ToString(CultureInfo.InvariantCulture)}.");

    return new StockLength
    {
      LengthMm = lengthMm,
      PricePerTon = pricePerTon,
      InStock = inStock
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
