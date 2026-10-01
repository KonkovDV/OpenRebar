using FluentAssertions;
using FsCheck.Xunit;
using OpenRebar.Domain.Models;

namespace OpenRebar.Domain.Tests.Models;

public sealed class IsolineColorFsCheckTests
{
  [Property(MaxTest = 500)]
  public void DeltaE_ShouldBeSymmetric_ForAnyRgb(
      byte r1,
      byte g1,
      byte b1,
      byte r2,
      byte g2,
      byte b2)
  {
    var left = new IsolineColor(r1, g1, b1);
    var right = new IsolineColor(r2, g2, b2);

    var forward = left.DeltaE(right);
    var backward = right.DeltaE(left);

    forward.Should().BeApproximately(backward, 1e-6);
  }

  [Property(MaxTest = 500)]
  public void DeltaE_ShouldBeNonNegative_ForAnyRgb(
      byte r1,
      byte g1,
      byte b1,
      byte r2,
      byte g2,
      byte b2)
  {
    var left = new IsolineColor(r1, g1, b1);
    var right = new IsolineColor(r2, g2, b2);

    left.DeltaE(right).Should().BeGreaterOrEqualTo(0.0);
  }
}
