using System.Globalization;
using OpenRebar.Domain.Exceptions;
using OpenRebar.Domain.Models;
using OpenRebar.Infrastructure.Catalog;
using FluentAssertions;

namespace OpenRebar.Infrastructure.Tests.Catalog;

public class FileSupplierCatalogLoaderTests
{
  private readonly FileSupplierCatalogLoader _loader = new();

  [Fact]
  public void GetDefaultCatalog_ShouldReturnStandardRussianLengths()
  {
    var catalog = _loader.GetDefaultCatalog();

    catalog.SupplierName.Should().NotBeNullOrEmpty();
    catalog.AvailableLengths.Should().HaveCountGreaterThanOrEqualTo(3);

    // Standard Russian market lengths
    catalog.AvailableLengths.Select(s => s.LengthMm)
        .Should().Contain(new[] { 6000.0, 11700.0, 12000.0 });
  }

  [Fact]
  public void GetDefaultCatalog_AllShouldBeInStock()
  {
    var catalog = _loader.GetDefaultCatalog();
    catalog.AvailableLengths.Should().AllSatisfy(s => s.InStock.Should().BeTrue());
  }

  [Fact]
  public async Task LoadAsync_NonExistentFile_ShouldThrowFileNotFound()
  {
    var act = async () => await _loader.LoadAsync("nonexistent.json");
    await act.Should().ThrowAsync<FileNotFoundException>();
  }

  [Fact]
  public async Task LoadAsync_UnsupportedFormat_ShouldThrow()
  {
    // Create a temp file with unsupported extension
    var tmpFile = Path.GetTempFileName() + ".xml";
    await File.WriteAllTextAsync(tmpFile, "<root/>");

    try
    {
      var act = async () => await _loader.LoadAsync(tmpFile);
      await act.Should().ThrowAsync<SupplierCatalogLoadException>();
    }
    finally
    {
      File.Delete(tmpFile);
    }
  }

  [Fact]
  public async Task LoadAsync_InvalidJson_ShouldThrowSupplierCatalogLoadException()
  {
    var tmpFile = Path.GetTempFileName() + ".json";
    await File.WriteAllTextAsync(tmpFile, "{not-json");

    try
    {
      var act = async () => await _loader.LoadAsync(tmpFile);
      await act.Should().ThrowAsync<SupplierCatalogLoadException>();
    }
    finally
    {
      File.Delete(tmpFile);
    }
  }

  [Fact]
  public async Task LoadAsync_ValidCsv_ShouldParse()
  {
    var tmpFile = Path.GetTempFileName() + ".csv";
    await File.WriteAllTextAsync(tmpFile,
        "LengthMm,PricePerTon,InStock\n" +
        "6000,85000,true\n" +
        "11700,83000,true\n" +
        "12000,,false\n");

    try
    {
      var catalog = await _loader.LoadAsync(tmpFile);

      catalog.AvailableLengths.Should().HaveCount(3);
      catalog.AvailableLengths[0].LengthMm.Should().Be(6000);
      catalog.AvailableLengths[0].PricePerTon.Should().Be(85000);
      catalog.AvailableLengths[2].InStock.Should().BeFalse();
    }
    finally
    {
      File.Delete(tmpFile);
    }
  }

  [Theory]
  [InlineData("0")]
  [InlineData("-6000")]
  [InlineData("NaN")]
  [InlineData("Infinity")]
  [InlineData("6000,5")]
  public async Task LoadCsv_RejectsABadLength(string length)
  {
    var act = async () => await _loader.LoadAsync(await WriteCsv($"{length},1,true"));
    await act.Should().ThrowAsync<SupplierCatalogLoadException>();
  }

  [Fact]
  public async Task LoadCsv_RejectsADuplicateLength()
  {
    var act = async () => await _loader.LoadAsync(await WriteCsv("6000,1,true\n6000,2,false"));
    await act.Should().ThrowAsync<SupplierCatalogLoadException>().WithMessage("*duplicate length*");
  }

  [Fact]
  public async Task LoadCsv_RejectsABadInStockValue()
  {
    var act = async () => await _loader.LoadAsync(await WriteCsv("6000,1,yes"));
    await act.Should().ThrowAsync<SupplierCatalogLoadException>().WithMessage("*InStock*");
  }

  [Fact]
  public async Task LoadCsv_RejectsAGarbageRowAndNamesTheLine()
  {
    var act = async () => await _loader.LoadAsync(await WriteCsv("abc,1,true"));
    await act.Should().ThrowAsync<SupplierCatalogLoadException>().WithMessage("*Line 2*");
  }

  [Fact]
  public async Task LoadCsv_RejectsAMissingHeader()
  {
    string path = Path.GetTempFileName() + ".csv";
    await File.WriteAllTextAsync(path, "6000,1,true\n");
    try
    {
      var act = async () => await _loader.LoadAsync(path);
      await act.Should().ThrowAsync<SupplierCatalogLoadException>().WithMessage("*header*");
    }
    finally
    {
      File.Delete(path);
    }
  }

  [Fact]
  public async Task LoadCsv_ParsesADotDecimalUnderRuCulture()
  {
    CultureInfo previous = CultureInfo.CurrentCulture;
    CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");
    try
    {
      var catalog = await _loader.LoadAsync(await WriteCsv("6000.5,85000.25,true"));
      catalog.AvailableLengths.Should().ContainSingle();
      catalog.AvailableLengths[0].LengthMm.Should().Be(6000.5);
      catalog.AvailableLengths[0].PricePerTon.Should().Be(85000.25);
    }
    finally
    {
      CultureInfo.CurrentCulture = previous;
    }
  }

  [Fact]
  public async Task LoadCsv_RejectsANegativePrice()
  {
    var act = async () => await _loader.LoadAsync(await WriteCsv("6000,-1,true"));
    await act.Should().ThrowAsync<SupplierCatalogLoadException>().WithMessage("*PricePerTon*");
  }

  [Fact]
  public async Task LoadCsv_SkipsABlankLine()
  {
    var catalog = await _loader.LoadAsync(await WriteCsv("\n6000,1,true\n"));
    catalog.AvailableLengths.Should().ContainSingle();
    catalog.AvailableLengths[0].LengthMm.Should().Be(6000);
  }

  [Theory]
  [InlineData("0")]
  [InlineData("-6000")]
  public async Task LoadJson_RejectsABadLength(string length)
  {
    var act = async () => await _loader.LoadAsync(await WriteJson(length, "1", "true"));
    await act.Should().ThrowAsync<SupplierCatalogLoadException>();
  }

  [Fact]
  public async Task LoadJson_RejectsADuplicateLength()
  {
    string path = Path.GetTempFileName() + ".json";
    await File.WriteAllTextAsync(path, """{"lengths":[{"lengthMm":6000},{"lengthMm":6000}]}""");
    try
    {
      var act = async () => await _loader.LoadAsync(path);
      await act.Should().ThrowAsync<SupplierCatalogLoadException>().WithMessage("*duplicate length*");
    }
    finally
    {
      File.Delete(path);
    }
  }

  [Fact]
  public async Task LoadJson_RejectsANegativePrice()
  {
    var act = async () => await _loader.LoadAsync(await WriteJson("6000", "-5", "true"));
    await act.Should().ThrowAsync<SupplierCatalogLoadException>().WithMessage("*PricePerTon*");
  }

  private static async Task<string> WriteCsv(string rows)
  {
    string path = Path.GetTempFileName() + ".csv";
    await File.WriteAllTextAsync(path, "LengthMm,PricePerTon,InStock\n" + rows + "\n");
    return path;
  }

  private static async Task<string> WriteJson(string length, string price, string inStock)
  {
    string path = Path.GetTempFileName() + ".json";
    await File.WriteAllTextAsync(
        path,
        $$"""{"lengths":[{"lengthMm":{{length}},"pricePerTon":{{price}},"inStock":{{inStock}}}]}""");
    return path;
  }
}
