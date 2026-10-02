using OpenRebar.Domain.Models;
using OpenRebar.Domain.Ports;

namespace OpenRebar.Domain.Rules;

/// <summary>
/// Chooses the lightest additional bar that meets the area delta at the policy spacing.
/// A shortfall that no allowed diameter can cover is left for a person.
/// </summary>
public sealed class AdditionalBarSelector : IAdditionalBarSelector
{
  public const string NotRequired = "NotRequired";
  public const string Selected = "Selected";
  public const string NeedsHumanDecision = "NeedsHumanDecision";

  public AdditionalBarSelection Select(AdditionalBarRequest request)
  {
    if (!double.IsFinite(request.DeltaAsMm2PerM) || request.DeltaAsMm2PerM < 0)
      return new AdditionalBarSelection(NeedsHumanDecision, null, null, 0);

    if (request.DeltaAsMm2PerM <= 1e-6)
    {
      return new AdditionalBarSelection(NotRequired, null, null, 0);
    }

    if (!TryResolveSpacing(request, out int spacingMm))
      return new AdditionalBarSelection(NeedsHumanDecision, null, null, 0);

    int backgroundDiameter = request.Background.Spec.DiameterMm;
    AdditionalBarSelection? best = null;
    double bestMass = double.PositiveInfinity;

    foreach (int diameter in request.AllowedDiametersMm.Distinct().Order())
    {
      if (diameter <= 0)
        continue;

      double provided = BarArea(diameter) * (1000.0 / spacingMm);
      if (provided + 1e-6 < request.DeltaAsMm2PerM)
        continue;
      if (!Clears(request.Policy.Mode, spacingMm, backgroundDiameter, diameter))
        continue;

      double mass = ReinforcementLimits.GetLinearMass(diameter) * (1000.0 / spacingMm);
      if (mass < bestMass - 1e-9)
      {
        bestMass = mass;
        best = new AdditionalBarSelection(Selected, diameter, spacingMm, provided);
      }
    }

    return best ?? new AdditionalBarSelection(NeedsHumanDecision, null, null, 0);
  }

  private static bool TryResolveSpacing(AdditionalBarRequest request, out int spacingMm)
  {
    if (request.Policy.Mode == AdditionalSpacingMode.Explicit)
    {
      spacingMm = request.Policy.ExplicitSpacingMm ?? 0;
      return spacingMm > 0;
    }

    spacingMm = request.Background.Spec.SpacingMm;
    return spacingMm > 0;
  }

  /// <summary>
  /// Center distance minus the two radii must leave at least the larger bar, and at least 25 mm.
  /// Interleaved bars sit halfway between background bars.
  /// </summary>
  private static bool Clears(AdditionalSpacingMode mode, int spacingMm, int backgroundDiameter, int additionalDiameter)
  {
    double centerDistance = mode == AdditionalSpacingMode.InterleaveSameAsBackground
        ? spacingMm / 2.0
        : spacingMm;
    double gap = centerDistance - (backgroundDiameter + additionalDiameter) / 2.0;
    double required = Math.Max(Math.Max(backgroundDiameter, additionalDiameter), 25);
    return gap + 1e-6 >= required;
  }

  private static double BarArea(int diameterMm) => Math.PI * diameterMm * diameterMm / 4.0;
}
