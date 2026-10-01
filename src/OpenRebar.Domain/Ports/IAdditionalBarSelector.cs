using OpenRebar.Domain.Models;

namespace OpenRebar.Domain.Ports;

/// <summary>
/// Picks additional bars that cover a required area above the background mesh.
/// </summary>
public interface IAdditionalBarSelector
{
  AdditionalBarSelection Select(AdditionalBarRequest request);
}

public enum AdditionalSpacingMode
{
  InterleaveSameAsBackground,
  Explicit
}

public sealed record AdditionalSpacingPolicy(AdditionalSpacingMode Mode, int? ExplicitSpacingMm = null);

public sealed record AdditionalBarRequest(
    double DeltaAsMm2PerM,
    BackgroundMesh Background,
    IReadOnlyList<int> AllowedDiametersMm,
    AdditionalSpacingPolicy Policy);

public sealed record AdditionalBarSelection(
    string Status,
    int? DiameterMm,
    int? SpacingMm,
    double AsProvidedMm2PerM);
