namespace A101.RevitPlugin.Revit;

// ─────────────────────────────────────────────────────────────
// This file contains the Revit API integration scaffold.
// Uncomment and implement when Revit SDK NuGet is available.
// Target: Revit 2025 (.NET 8) — Autodesk.Revit.DB namespace.
// ─────────────────────────────────────────────────────────────

/*
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using A101.Domain.Models;
using A101.Domain.Ports;

/// <summary>
/// Real Revit placer: creates RebarInSystem elements, tags, and bending details
/// in the active Revit document.
/// </summary>
public sealed class RevitRebarPlacer : IRevitPlacer
{
    private readonly UIDocument _uiDoc;

    public RevitRebarPlacer(UIDocument uiDoc)
    {
        _uiDoc = uiDoc;
    }

    public async Task<PlacementResult> PlaceReinforcementAsync(
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

        using (var txn = new Transaction(doc, "A101: Place Reinforcement"))
        {
            txn.Start();

            try
            {
                foreach (var zone in zones)
                {
                    foreach (var segment in zone.Rebars)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        // 1. Find or create RebarBarType for this diameter
                        var barType = FindOrCreateBarType(doc, segment.DiameterMm, zone.Spec.SteelClass);

                        // 2. Create rebar curve
                        var startPt = new XYZ(
                            segment.Start.X / 304.8, // mm → feet
                            segment.Start.Y / 304.8,
                            0);
                        var endPt = new XYZ(
                            segment.End.X / 304.8,
                            segment.End.Y / 304.8,
                            0);
                        var curve = Line.CreateBound(startPt, endPt);

                        // 3. Create Rebar element
                        // var rebar = Rebar.CreateFromCurves(
                        //     doc, RebarStyle.Standard,
                        //     barType, null, null,
                        //     new XYZ(0, 0, 1),
                        //     new List<Curve> { curve },
                        //     RebarHookOrientation.Left,
                        //     RebarHookOrientation.Left,
                        //     true, true);

                        rebarsPlaced++;

                        // 4. Tag if requested
                        if (settings.CreateTags)
                        {
                            // IndependentTag.Create(doc, ...);
                            tagsCreated++;
                        }
                    }

                    if (settings.CreateBendingDetails)
                    {
                        // Create BendingDetail for this zone group
                        bendingDetails++;
                    }
                }

                txn.Commit();
            }
            catch (Exception ex)
            {
                txn.RollBack();
                errors.Add($"Transaction rolled back: {ex.Message}");
            }
        }

        return new PlacementResult
        {
            TotalRebarsPlaced = rebarsPlaced,
            TotalTagsCreated = tagsCreated,
            TotalBendingDetails = bendingDetails,
            Warnings = warnings,
            Errors = errors
        };
    }

    private static RebarBarType FindOrCreateBarType(Document doc, int diameterMm, string steelClass)
    {
        // Search existing bar types
        var collector = new FilteredElementCollector(doc)
            .OfClass(typeof(RebarBarType));

        foreach (RebarBarType barType in collector)
        {
            var nomDia = barType.BarNominalDiameter * 304.8; // feet → mm
            if (Math.Abs(nomDia - diameterMm) < 0.5)
                return barType;
        }

        throw new InvalidOperationException(
            $"RebarBarType for diameter {diameterMm}mm not found in project. " +
            "Please load the appropriate rebar family.");
    }
}
*/
