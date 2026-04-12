namespace OpenRebar.RevitPlugin.Revit;

#if REVIT_SDK
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using OpenRebar.Domain.Models;

/// <summary>
/// Extracts slab geometry and metadata from a selected Revit floor.
/// </summary>
public static class RevitSlabExtractor
{
    private const double FeetToMm = 304.8;

    public static SlabGeometry Extract(Floor floor)
    {
        var options = new Options { DetailLevel = ViewDetailLevel.Fine };
        var geometry = floor.get_Geometry(options);

        var outerBoundary = ExtractOuterBoundary(geometry);
        var openings = ExtractOpenings(floor);
        double thicknessMm = floor.FloorType.GetCompoundStructure()?.GetLayers().Sum(layer => layer.Width) * FeetToMm ?? 200.0;
        double coverMm = GetRebarCover(floor);
        string concreteClass = GetConcreteClass(floor) ?? "B25";

        return new SlabGeometry
        {
            OuterBoundary = new Polygon(outerBoundary),
            Openings = openings,
            ThicknessMm = thicknessMm,
            CoverMm = coverMm,
            ConcreteClass = concreteClass
        };
    }

    private static List<Point2D> ExtractOuterBoundary(GeometryElement geometry)
    {
        var bottomFace = geometry
            .OfType<Solid>()
            .Where(solid => solid.Volume > 1e-9)
            .SelectMany(solid => solid.Faces.Cast<Face>())
            .OfType<PlanarFace>()
            .Where(face => face.FaceNormal.Z < -0.99)
            .OrderBy(face => face.Origin.Z)
            .FirstOrDefault();

        var curveLoop = bottomFace?
            .GetEdgesAsCurveLoops()
            .OrderByDescending(loop => loop.Sum(curve => curve.Length))
            .FirstOrDefault();

        if (curveLoop is null)
        {
            var fallback = geometry
                .OfType<Solid>()
                .Where(solid => solid.Volume > 1e-9)
                .Select(solid => solid.GetBoundingBox())
                .FirstOrDefault(bbox => bbox is not null);

            if (fallback is not null)
            {
                return
                [
                    new Point2D(fallback.Min.X * FeetToMm, fallback.Min.Y * FeetToMm),
                    new Point2D(fallback.Max.X * FeetToMm, fallback.Min.Y * FeetToMm),
                    new Point2D(fallback.Max.X * FeetToMm, fallback.Max.Y * FeetToMm),
                    new Point2D(fallback.Min.X * FeetToMm, fallback.Max.Y * FeetToMm)
                ];
            }

            throw new InvalidOperationException("Unable to extract floor outer boundary from Revit geometry.");
        }

        return curveLoop
            .Select(curve => curve.GetEndPoint(0))
            .Append(curveLoop.Last().GetEndPoint(1))
            .Select(point => new Point2D(point.X * FeetToMm, point.Y * FeetToMm))
            .DistinctBy(point => (Math.Round(point.X, 3), Math.Round(point.Y, 3)))
            .ToList();
    }

    private static List<Polygon> ExtractOpenings(Floor floor)
    {
        var openings = new List<Polygon>();

        foreach (var insertId in floor.FindInserts(addRectOpenings: true, includeShadows: false, includeEmbeddedWalls: false, includeSharedEmbeddedInserts: true))
        {
            var element = floor.Document.GetElement(insertId);
            var bbox = element?.get_BoundingBox(null);
            if (bbox is null)
                continue;

            openings.Add(new Polygon(
            [
                new Point2D(bbox.Min.X * FeetToMm, bbox.Min.Y * FeetToMm),
                new Point2D(bbox.Max.X * FeetToMm, bbox.Min.Y * FeetToMm),
                new Point2D(bbox.Max.X * FeetToMm, bbox.Max.Y * FeetToMm),
                new Point2D(bbox.Min.X * FeetToMm, bbox.Max.Y * FeetToMm)
            ]));
        }

        return openings;
    }

    private static double GetRebarCover(Floor floor)
    {
        var coverTypeId = floor.get_Parameter(BuiltInParameter.CLEAR_COVER_BOTTOM)?.AsElementId();
        var coverType = coverTypeId is not null && coverTypeId != ElementId.InvalidElementId
            ? floor.Document.GetElement(coverTypeId) as RebarCoverType
            : null;

        return coverType?.CoverDistance * FeetToMm ?? 25.0;
    }

    private static string? GetConcreteClass(Floor floor)
    {
        var layers = floor.FloorType.GetCompoundStructure()?.GetLayers();
        if (layers is null)
            return null;

        foreach (var layer in layers)
        {
            var material = floor.Document.GetElement(layer.MaterialId) as Material;
            if (material?.Name is null)
                continue;

            var match = Regex.Match(material.Name, @"\b(B\d{2}|C\d{2}/\d{2})\b", RegexOptions.IgnoreCase);
            if (match.Success)
                return match.Value.ToUpperInvariant();
        }

        return null;
    }
}
#else
internal static class RevitSlabExtractorPlaceholder
{
    public const string Message = "Define REVIT_SDK and provide Autodesk Revit references to build the real slab extractor.";
}
#endif