using FluentAssertions;
using OpenRebar.Domain.Models;
using OpenRebar.Domain.Ports;
using OpenRebar.Domain.Rules;

namespace OpenRebar.Domain.Tests.Rules;

public class AdditionalBarSelectorTests
{
  private readonly AdditionalBarSelector _selector = new();
  private static readonly int[] Diameters = [10, 12, 14, 16, 20, 25];

  [Fact]
  public void Select_WhenRequiredAreaDoesNotExceedBackground_AddsNothing()
  {
    var result = _selector.Select(Request(deltaAs: 0));

    result.Status.Should().Be(AdditionalBarSelector.NotRequired);
    result.DiameterMm.Should().BeNull();
    result.SpacingMm.Should().BeNull();
  }

  [Fact]
  public void Select_PicksTheLightestDiameterThatCoversTheDelta()
  {
    var result = _selector.Select(Request(deltaAs: 400));

    result.Status.Should().Be(AdditionalBarSelector.Selected);
    result.DiameterMm.Should().Be(12);
    result.SpacingMm.Should().Be(200);
    result.AsProvidedMm2PerM.Should().BeGreaterThan(400);
  }

  [Fact]
  public void Select_WhenNoDiameterCoversTheDelta_NeedsAPerson()
  {
    var result = _selector.Select(Request(deltaAs: 20000));

    result.Status.Should().Be(AdditionalBarSelector.NeedsHumanDecision);
    result.DiameterMm.Should().BeNull();
  }

  [Fact]
  public void Select_WhenInterleaveLeavesNoClearGap_NeedsAPerson()
  {
    var result = _selector.Select(Request(deltaAs: 100, spacingMm: 40));

    result.Status.Should().Be(AdditionalBarSelector.NeedsHumanDecision);
  }

  private static AdditionalBarRequest Request(double deltaAs, int spacingMm = 200) => new(
      deltaAs,
      new BackgroundMesh(
          LayerKey.BottomX,
          new ReinforcementSpec { DiameterMm = 12, SpacingMm = spacingMm, SteelClass = "A500C" },
          0),
      Diameters,
      new AdditionalSpacingPolicy(AdditionalSpacingMode.InterleaveSameAsBackground));
}
