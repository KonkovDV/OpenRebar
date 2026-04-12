namespace OpenRebar.RevitPlugin.Revit;

#if REVIT_SDK
using System.Globalization;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using OpenRebar.Domain.Models;
using OpenRebar.Domain.Ports;

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
        var createdRebarIds = new List<ElementId>();

        if (settings.CreateBendingDetails)
            warnings.Add("Bending detail element creation is not implemented yet; OpenRebar will track unique bar shapes for downstream detailing.");

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

        // P1: Validate host element is a structural floor
        if (!ValidateHostElement(hostFloor, warnings))
        {
            errors.Add("Host floor validation failed. Check warnings for details.");
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
        int batchIndex = 0;
        int rebarsInCurrentTransaction = 0;

        using var transactionGroup = new TransactionGroup(doc, "OpenRebar: Place Reinforcement");
        transactionGroup.Start();

        Transaction? currentTransaction = null;

        try
        {
            currentTransaction = StartPlacementTransaction(doc, ++batchIndex);

            foreach (var zone in zones)
            {
                foreach (var segment in zone.Rebars)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (rebarsInCurrentTransaction >= settings.MaxRebarsPerTransaction)
                    {
                        CommitPlacementTransaction(currentTransaction);
                        currentTransaction.Dispose();
                        currentTransaction = StartPlacementTransaction(doc, ++batchIndex);
                        rebarsInCurrentTransaction = 0;
                    }

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
                        rebarsInCurrentTransaction++;
                        createdRebarIds.Add(rebar.Id);
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"Zone {zone.Id}, mark {segment.Mark}: {ex.Message}");
                    }
                }
            }

            CommitPlacementTransaction(currentTransaction);
            currentTransaction.Dispose();
            currentTransaction = null;

            // P1: Tag creation pass
            if (settings.CreateTags && createdRebarIds.Count > 0)
            {
                var tagTransaction = StartPlacementTransaction(doc, ++batchIndex);
                try
                {
                    var activeView = _uiDoc.ActiveView;
                    foreach (var rebarId in createdRebarIds)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var rebar = doc.GetElement(rebarId) as Rebar;
                        if (rebar is null)
                            continue;

                        try
                        {
                            var bbox = rebar.get_BoundingBox(activeView);
                            if (bbox is null) continue;

                            var midpoint = new XYZ(
                                (bbox.Min.X + bbox.Max.X) / 2.0,
                                (bbox.Min.Y + bbox.Max.Y) / 2.0,
                                (bbox.Min.Z + bbox.Max.Z) / 2.0);

                            var tag = IndependentTag.Create(
                                doc,
                                activeView.Id,
                                new Reference(rebar),
                                leaderEnd: false,
                                TagMode.TM_ADDBY_CATEGORY,
                                TagOrientation.Horizontal,
                                midpoint);

                            if (tag is not null) tagsCreated++;
                        }
                        catch (Exception ex)
                        {
                            warnings.Add($"Tag creation warning: {ex.Message}");
                        }
                    }

                    CommitPlacementTransaction(tagTransaction);
                    tagTransaction.Dispose();
                }
                catch (Exception ex)
                {
                    TryRollback(tagTransaction);
                    tagTransaction.Dispose();
                    warnings.Add($"Tag creation batch failed: {ex.Message}");
                }
            }

            // P1: Bending detail creation pass
            if (settings.CreateBendingDetails && createdRebarIds.Count > 0)
            {
                var processedShapes = new HashSet<ElementId>();

                foreach (var rebarId in createdRebarIds)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var rebar = doc.GetElement(rebarId) as Rebar;
                    if (rebar is null)
                        continue;

                    var shapeId = rebar.GetShapeId();
                    if (shapeId == ElementId.InvalidElementId)
                        continue;

                    processedShapes.Add(shapeId);
                }

                if (processedShapes.Count > 0)
                {
                    warnings.Add($"Tracked {processedShapes.Count} unique rebar shapes for downstream bending details; element creation still requires shape-specific detailing implementation.");
                }
            }

            transactionGroup.Assimilate();
        }
        catch (Exception ex)
        {
            if (currentTransaction is not null)
            {
                TryRollback(currentTransaction);
                currentTransaction.Dispose();
                currentTransaction = null;
            }

            transactionGroup.RollBack();
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

    private static Transaction StartPlacementTransaction(Document doc, int batchIndex)
    {
        var transaction = new Transaction(doc,
            batchIndex == 1
                ? "OpenRebar: Place Reinforcement"
                : $"OpenRebar: Place Reinforcement (batch {batchIndex})");

        transaction.Start();
        return transaction;
    }

    private static void CommitPlacementTransaction(Transaction transaction)
    {
        transaction.Commit();
    }

    private static void TryRollback(Transaction transaction)
    {
        try
        {
            transaction.RollBack();
        }
        catch
        {
            // Prefer preserving the original placement error over rollback noise.
        }
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

    /// <summary>
    /// Validates that the host floor is suitable for reinforcement placement:
    /// structural category, has compound structure, and minimum thickness.
    /// </summary>
    private static bool ValidateHostElement(Floor floor, List<string> warnings)
    {
        var category = floor.Category;
        if (category?.Id.IntegerValue != (int)BuiltInCategory.OST_Floors)
        {
            warnings.Add($"Host element category is {category?.Name ?? "null"}, expected Floors.");
            return false;
        }

        var compoundStructure = floor.FloorType.GetCompoundStructure();
        if (compoundStructure is null)
        {
            warnings.Add("Floor type has no compound structure — cannot determine layers or cover.");
            return false;
        }

        double thicknessMm = compoundStructure.GetLayers().Sum(l => l.Width) * 304.8;
        if (thicknessMm < 50.0)
        {
            warnings.Add($"Floor thickness {thicknessMm:F0}mm is below minimum 50mm for structural reinforcement.");
            return false;
        }

        return true;
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
