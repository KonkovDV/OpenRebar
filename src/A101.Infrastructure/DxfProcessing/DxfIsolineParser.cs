using A101.Domain.Models;
using A101.Domain.Ports;

namespace A101.Infrastructure.DxfProcessing;

/// <summary>
/// Parses DXF isoline files using IxMilia.Dxf library.
/// Extracts polygons from hatches/polylines and maps colors to reinforcement specs.
/// </summary>
public sealed class DxfIsolineParser : IIsolineParser
{
    public IReadOnlyList<string> SupportedExtensions => [".dxf"];

    public async Task<IReadOnlyList<ReinforcementZone>> ParseAsync(
        string filePath,
        ColorLegend legend,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"DXF file not found: {filePath}");

        // Read DXF file
        using var stream = File.OpenRead(filePath);
        var dxfFile = IxMilia.Dxf.DxfFile.Load(stream);

        var zones = new List<ReinforcementZone>();
        int zoneIndex = 0;

        foreach (var entity in dxfFile.Entities)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Extract polylines and hatches as zone boundaries
            var (polygon, color) = ExtractPolygonFromEntity(entity, dxfFile);
            if (polygon is null || color is null)
                continue;

            // Match color to legend
            var legendEntry = legend.FindClosest(color.Value);
            if (legendEntry is null)
                continue;

            zones.Add(new ReinforcementZone
            {
                Id = $"DXF-{++zoneIndex:D4}",
                Boundary = polygon,
                Spec = legendEntry.Spec,
                Direction = RebarDirection.X, // Will be refined by ZoneDetector
                ZoneType = ZoneType.Simple    // Will be classified by ZoneDetector
            });
        }

        return zones;
    }

    private static (Polygon? Polygon, IsolineColor? Color) ExtractPolygonFromEntity(
        IxMilia.Dxf.Entities.DxfEntity entity,
        IxMilia.Dxf.DxfFile dxfFile)
    {
        switch (entity)
        {
            case IxMilia.Dxf.Entities.DxfLwPolyline polyline:
            {
                if (polyline.Vertices.Count < 3) return (null, null);

                var vertices = polyline.Vertices
                    .Select(v => new Point2D(v.X, v.Y))
                    .ToList();

                var color = ResolveEntityColor(polyline, dxfFile);
                return (new Polygon(vertices), color);
            }

            case IxMilia.Dxf.Entities.DxfPolyline polyline3d:
            {
                var vertices = polyline3d.Vertices
                    .Select(v => new Point2D(v.Location.X, v.Location.Y))
                    .ToList();

                if (vertices.Count < 3) return (null, null);

                var color = ResolveEntityColor(polyline3d, dxfFile);
                return (new Polygon(vertices), color);
            }

            default:
                return (null, null);
        }
    }

    private static IsolineColor? MapDxfColor(IxMilia.Dxf.DxfColor dxfColor)
    {
        // For ByLayer/ByBlock we'd need layer context; fallback to null
        if (dxfColor.IsByLayer || dxfColor.IsByBlock)
            return null;

        // ACI color index → RGB mapping (full 256-entry palette)
        short aci = dxfColor.RawValue;
        if (aci is < 1 or > 255) return null;
        var (r, g, b) = AciPalette.GetRgb(aci);
        return new IsolineColor(r, g, b);
    }

    /// <summary>
    /// Resolve color for entities with ByLayer color — looks up layer's color.
    /// </summary>
    private static IsolineColor? ResolveEntityColor(
        IxMilia.Dxf.Entities.DxfEntity entity,
        IxMilia.Dxf.DxfFile dxfFile)
    {
        if (!entity.Color.IsByLayer)
            return MapDxfColor(entity.Color);

        // Resolve from layer
        var layer = dxfFile.Layers.FirstOrDefault(l =>
            string.Equals(l.Name, entity.Layer, StringComparison.OrdinalIgnoreCase));

        if (layer is null) return null;
        return MapDxfColor(layer.Color);
    }

}
