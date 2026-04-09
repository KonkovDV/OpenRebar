using A101.Domain.Models;
using A101.Domain.Ports;

namespace A101.RevitPlugin.Revit;

/// <summary>
/// Stub Revit placer for development/testing without Revit runtime.
/// In production, this is replaced by the real RevitRebarPlacer
/// that uses the Revit API (Rebar, RebarInSystem, RebarTag, BendingDetail).
/// </summary>
public sealed class StubRevitPlacer : IRevitPlacer
{
    public Task<PlacementResult> PlaceReinforcementAsync(
        IReadOnlyList<ReinforcementZone> zones,
        PlacementSettings settings,
        CancellationToken cancellationToken = default)
    {
        int totalRebars = zones.Sum(z => z.Rebars.Count);

        return Task.FromResult(new PlacementResult
        {
            TotalRebarsPlaced = totalRebars,
            TotalTagsCreated = settings.CreateTags ? totalRebars : 0,
            TotalBendingDetails = settings.CreateBendingDetails ? zones.Count : 0,
            Warnings = ["StubRevitPlacer: elements logged but not placed in Revit model."]
        });
    }
}
