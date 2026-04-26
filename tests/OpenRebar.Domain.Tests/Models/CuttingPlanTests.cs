using FluentAssertions;
using OpenRebar.Domain.Models;

namespace OpenRebar.Domain.Tests.Models;

public class CuttingPlanTests
{
    [Fact]
    public void WasteMm_ShouldSubtractSawKerfLossFromRemainingStock()
    {
        var plan = new CuttingPlan
        {
            StockLengthMm = 11700,
            Cuts = [5000, 5000],
            SawCutWidthMm = 3
        };

        plan.SawKerfLossMm.Should().Be(6);
        plan.ConsumedLengthMm.Should().Be(10006);
        plan.WasteMm.Should().Be(1694);
        plan.WastePercent.Should().BeApproximately(1694.0 / 11700.0 * 100.0, 0.0001);
    }
}