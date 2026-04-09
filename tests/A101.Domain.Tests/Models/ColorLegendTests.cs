using A101.Domain.Models;
using FluentAssertions;

namespace A101.Domain.Tests.Models;

public class ColorLegendTests
{
    [Fact]
    public void FindClosest_ExactMatch_ShouldReturnEntry()
    {
        var legend = new ColorLegend([
            new LegendEntry(new IsolineColor(255, 0, 0), new ReinforcementSpec
            {
                DiameterMm = 12, SpacingMm = 200, SteelClass = "A500C"
            }),
            new LegendEntry(new IsolineColor(0, 255, 0), new ReinforcementSpec
            {
                DiameterMm = 16, SpacingMm = 150, SteelClass = "A500C"
            }),
        ]);

        var result = legend.FindClosest(new IsolineColor(255, 0, 0));

        result.Should().NotBeNull();
        result!.Spec.DiameterMm.Should().Be(12);
    }

    [Fact]
    public void FindClosest_NearMatch_ShouldReturnClosestEntry()
    {
        var legend = new ColorLegend([
            new LegendEntry(new IsolineColor(255, 0, 0), new ReinforcementSpec
            {
                DiameterMm = 12, SpacingMm = 200, SteelClass = "A500C"
            }),
        ]);

        // Slightly off-red
        var result = legend.FindClosest(new IsolineColor(250, 5, 3));

        result.Should().NotBeNull();
        result!.Spec.DiameterMm.Should().Be(12);
    }

    [Fact]
    public void FindClosest_TooFar_ShouldReturnNull()
    {
        var legend = new ColorLegend([
            new LegendEntry(new IsolineColor(255, 0, 0), new ReinforcementSpec
            {
                DiameterMm = 12, SpacingMm = 200, SteelClass = "A500C"
            }),
        ]);

        // Very different color
        var result = legend.FindClosest(new IsolineColor(0, 0, 255), maxDistance: 30);

        result.Should().BeNull("the color is too far from any legend entry");
    }
}
