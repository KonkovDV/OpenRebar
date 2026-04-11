namespace A101.RevitPlugin.Revit;

#if REVIT_SDK
using System.Globalization;
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
        const double MillimetersToFeet = 1.0 / 304.8;

        var doc = _uiDoc.Document;
        int rebarsPlaced = 0;
        int tagsCreated = 0;
        int bendingDetails = 0;
        var warnings = new List<string>();
        var errors = new List<string>();

        if (settings.CreateTags)
            warnings.Add("Rebar tag creation is not implemented yet; tags will be skipped.");

        if (settings.CreateBendingDetails)
            warnings.Add("Bending detail creation is not implemented yet; bending details will be skipped.");

        var hostFloor = ResolveHostFloor(doc, settings, warnings);
        if (hostFloor is null)
        {
            errors.Add("Host floor could not be resolved for reinforcement placement.");
            return Task.FromResult(new PlacementResult
            {
                TotalRebarsPlaced = 0,
                TotalTagsCreated = 0,
                TotalBendingDetails = 0,
                Warnings = warnings,
                Errors = errors
            });
        }

        double coverMm = GetHostCoverMm(hostFloor);
        double thicknessMm = GetHostThicknessMm(hostFloor);
        var barTypeIndex = BuildBarTypeIndex(doc);

        using var txn = new Transaction(doc, "A101: Place Reinforcement");
        txn.Start();

        try
        {
            foreach (var zone in zones)
            {
                foreach (var segment in zone.Rebars)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var barType = FindExistingBarType(barTypeIndex, segment.DiameterMm);
                    if (barType is null)
                    {
                        warnings.Add($"Missing RebarBarType for {segment.DiameterMm}mm in zone {zone.Id}.");
                        continue;
                    }

                    try
                    {
                        var startPoint = new XYZ(
                            segment.Start.X * MillimetersToFeet,
                            segment.Start.Y * MillimetersToFeet,
                            0);
                        var endPoint = new XYZ(
                            segment.End.X * MillimetersToFeet,
                            segment.End.Y * MillimetersToFeet,
                            0);

                        double zFeet = zone.Layer == RebarLayer.Bottom
                            ? settings.ElevationOffsetFeet + coverMm * MillimetersToFeet
                            : settings.ElevationOffsetFeet + (thicknessMm - coverMm) * MillimetersToFeet;

                        startPoint = new XYZ(startPoint.X, startPoint.Y, zFeet);
                        endPoint = new XYZ(endPoint.X, endPoint.Y, zFeet);

                        var curves = new List<Curve>
                        {
                            Line.CreateBound(startPoint, endPoint)
                        };

                        var rebar = Rebar.CreateFromCurves(
                            doc,
                            RebarStyle.Standard,
                            barType,
                            startHook: null,
                            endHook: null,
                            host: hostFloor,
                            norm: XYZ.BasisZ,
                            curves,
                            RebarHookOrientation.Left,
                            RebarHookOrientation.Left,
                            useExistingShapeIfPossible: true,
                            createNewShape: true);

                        if (rebar is null)
                        {
                            errors.Add($"Failed to create rebar for zone {zone.Id}, mark {segment.Mark}.");
                            continue;
                        }

                        if (settings.GroupByZone)
                            rebar.LookupParameter(settings.ZoneParameterName)?.Set(zone.Id);

                        rebarsPlaced++;
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"Zone {zone.Id}, mark {segment.Mark}: {ex.Message}");
                    }
                }
            }

            txn.Commit();
        }
        catch (Exception ex)
        {
            txn.RollBack();
            rebarsPlaced = 0;
            tagsCreated = 0;
            bendingDetails = 0;
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

    private static Floor? ResolveHostFloor(Document doc, PlacementSettings settings, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(settings.HostElementId))
        {
            warnings.Add("HostElementId is not provided. Rebar placement skipped.");
            return null;
        }

        if (!int.TryParse(settings.HostElementId, NumberStyles.Integer, CultureInfo.InvariantCulture, out int hostId))
        {
            warnings.Add($"HostElementId '{settings.HostElementId}' is invalid. Rebar placement skipped.");
            return null;
        }

        var floor = doc.GetElement(new ElementId(hostId)) as Floor;
        if (floor is null)
            warnings.Add($"Host element {settings.HostElementId} is not a valid Floor. Rebar placement skipped.");

        return floor;
    }

    private static double GetHostThicknessMm(Floor floor)
    {
        return floor.FloorType.GetCompoundStructure()?.GetLayers().Sum(layer => layer.Width) * 304.8 ?? 200.0;
    }

    private static double GetHostCoverMm(Floor floor)
    {
        var coverTypeId = floor.get_Parameter(BuiltInParameter.CLEAR_COVER_BOTTOM)?.AsElementId();
        var coverType = coverTypeId is not null && coverTypeId != ElementId.InvalidElementId
            ? floor.Document.GetElement(coverTypeId) as RebarCoverType
            : null;

        return coverType is not null ? coverType.CoverDistance * 304.8 : 25.0;
    }

    private static IReadOnlyDictionary<int, RebarBarType> BuildBarTypeIndex(Document doc)
    {
        var index = new Dictionary<int, RebarBarType>();

        foreach (RebarBarType barType in new FilteredElementCollector(doc)
            .OfClass(typeof(RebarBarType)))
        {
            int nominalDiameterMm = (int)Math.Round(
                barType.BarNominalDiameter * 304.8,
                MidpointRounding.AwayFromZero);

            index.TryAdd(nominalDiameterMm, barType);
        }

        return index;
    }

    private static RebarBarType? FindExistingBarType(
        IReadOnlyDictionary<int, RebarBarType> barTypeIndex,
        int diameterMm)
    {
        if (barTypeIndex.TryGetValue(diameterMm, out var exactMatch))
            return exactMatch;

        return barTypeIndex.Values
            .Select(barType => new
            {
                BarType = barType,
                Delta = Math.Abs(barType.BarNominalDiameter * 304.8 - diameterMm)
            })
            .Where(candidate => candidate.Delta < 0.5)
            .OrderBy(candidate => candidate.Delta)
            .Select(candidate => candidate.BarType)
            .FirstOrDefault();
    }
}
#else
internal static class RevitRebarPlacerPlaceholder
{
    public const string Message = "Define REVIT_SDK and provide Autodesk Revit references to build the real Revit placer.";
}
#endif
