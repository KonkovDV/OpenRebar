using FluentAssertions;
using OpenRebar.Domain.Models;
using Xunit;

namespace OpenRebar.Domain.Tests.Models;

public sealed class GeometryToleranceTests
{
    [Fact]
    public void ComputationalProfile_ShouldUseSharedComputationalEpsilon()
    {
        var tolerance = GeometryTolerance.Computational;

        tolerance.LinearToleranceMm.Should().Be(GeometryTolerance.ComputationalEpsilonMm);
        tolerance.AreaRatioTolerance.Should().Be(0.05);
    }

    [Fact]
    public void DefaultProfile_ShouldRemainMorePermissiveThanComputationalProfile()
    {
        GeometryTolerance.Default.LinearToleranceMm
            .Should()
            .BeGreaterThan(GeometryTolerance.Computational.LinearToleranceMm);
    }
}
