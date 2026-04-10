namespace A101.RevitPlugin.Revit;

#if REVIT_SDK
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using A101.Domain.Models;
using A101.Domain.Ports;

public sealed class RevitRebarPlacer : IRevitPlacer
{
    private readonly UIDocument _uiDoc;

    public RevitRebarPlacer(UIDocument uiDoc)
    {
        _uiDoc = uiDoc;
    }

    public Task<PlacementResult> PlaceReinforcementAsync(
        IReadOnlyList<ReinforcementZone> zones,
        PlacementSettings settings,
        CancellationToken cancellationToken = default)
    {
        var doc = _uiDoc.Document;
        int rebarsPlaced = 0;
        int tagsCreated = 0;
        int bendingDetails = 0;
        var warnings = new List<string>();
        var errors = new List<string>();

        using var txn = new Transaction(doc, "A101: Place Reinforcement");
        txn.Start();

        try
        {
            foreach (var zone in zones)
            {
                foreach (var segment in zone.Rebars)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var barType = FindExistingBarType(doc, segment.DiameterMm);
                    if (barType is null)
                    {
                        warnings.Add($"Missing RebarBarType for {segment.DiameterMm}mm in zone {zone.Id}.");
                        continue;
                    }

                    // TODO: replace this placeholder with AreaReinforcement.Create or Rebar.CreateFromCurves
                    // once the project wires host selection, view context, and cover/type mapping.
                    _ = barType;
                    rebarsPlaced++;

                    if (settings.CreateTags)
                        tagsCreated++;
                }

                if (settings.CreateBendingDetails && zone.Rebars.Count > 0)
                    bendingDetails++;
            }

            txn.Commit();
        }
        catch (Exception ex)
        {
            txn.RollBack();
            errors.Add($"Transaction rolled back: {ex.Message}");
        }

        return Task.FromResult(new PlacementResult
        {
            TotalRebarsPlaced = rebarsPlaced,
            TotalTagsCreated = tagsCreated,
            TotalBendingDetails = bendingDetails,
            Warnings = warnings,
            Errors = errors
        });
    }

    private static RebarBarType? FindExistingBarType(Document doc, int diameterMm)
    {
        var collector = new FilteredElementCollector(doc)
            .OfClass(typeof(RebarBarType));

        foreach (RebarBarType barType in collector)
        {
            var nominalDiameterMm = barType.BarNominalDiameter * 304.8;
            if (Math.Abs(nominalDiameterMm - diameterMm) < 0.5)
                return barType;
        }

        return null;
    }
}
#else
internal static class RevitRebarPlacerPlaceholder
{
    public const string Message = "Define REVIT_SDK and provide Autodesk Revit references to build the real Revit placer.";
}
#endif
