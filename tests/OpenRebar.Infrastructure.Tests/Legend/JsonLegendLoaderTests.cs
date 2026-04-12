using System.Text.Json;
using OpenRebar.Domain.Exceptions;
using OpenRebar.Infrastructure.Legend;
using FluentAssertions;

namespace OpenRebar.Infrastructure.Tests.Legend;

public class JsonLegendLoaderTests
{
    [Fact]
    public async Task LoadAsync_RoundTripJson_ShouldLoadLegend()
    {
        var loader = new JsonLegendLoader();
        var tempFile = Path.Combine(Path.GetTempPath(), $"OpenRebar-legend-{Guid.NewGuid():N}.json");

        var payload = JsonSerializer.Serialize(new
        {
            legends = new[]
            {
                new { color = new[] { 0, 0, 255 }, diameter_mm = 8, spacing_mm = 200, steel_class = "A500C" },
                new { color = new[] { 255, 0, 0 }, diameter_mm = 20, spacing_mm = 150, steel_class = "A500C" }
            }
        });

        try
        {
            await File.WriteAllTextAsync(tempFile, payload);
            var legend = await loader.LoadAsync(tempFile);

            legend.Entries.Should().HaveCount(2);
            legend.Entries[0].Spec.DiameterMm.Should().Be(8);
            legend.Entries[1].Spec.SpacingMm.Should().Be(150);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task LoadAsync_InvalidJson_ShouldThrowLegendLoadException()
    {
        var loader = new JsonLegendLoader();
        var tempFile = Path.Combine(Path.GetTempPath(), $"OpenRebar-legend-invalid-{Guid.NewGuid():N}.json");

        try
        {
            await File.WriteAllTextAsync(tempFile, "{not-json");

            var act = async () => await loader.LoadAsync(tempFile);
            await act.Should().ThrowAsync<LegendLoadException>();
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }
}