using A101.Domain.Models;
using A101.Infrastructure.Catalog;
using FluentAssertions;

namespace A101.Infrastructure.Tests.Catalog;

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
            await act.Should().ThrowAsync<NotSupportedException>();
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
}
