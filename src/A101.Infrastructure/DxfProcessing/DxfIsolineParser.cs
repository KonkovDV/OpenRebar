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
            var (polygon, color) = ExtractPolygonFromEntity(entity);
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
        IxMilia.Dxf.Entities.DxfEntity entity)
    {
        switch (entity)
        {
            case IxMilia.Dxf.Entities.DxfLwPolyline polyline:
            {
                if (polyline.Vertices.Count < 3) return (null, null);

                var vertices = polyline.Vertices
                    .Select(v => new Point2D(v.X, v.Y))
                    .ToList();

                var color = MapDxfColor(polyline.Color);
                return (new Polygon(vertices), color);
            }

            case IxMilia.Dxf.Entities.DxfPolyline polyline3d:
            {
                var vertices = polyline3d.Vertices
                    .Select(v => new Point2D(v.Location.X, v.Location.Y))
                    .ToList();

                if (vertices.Count < 3) return (null, null);

                var color = MapDxfColor(polyline3d.Color);
                return (new Polygon(vertices), color);
            }

            default:
                return (null, null);
        }
    }

    private static IsolineColor? MapDxfColor(IxMilia.Dxf.DxfColor dxfColor)
    {
        // DXF uses ACI (AutoCAD Color Index) — map to RGB
        // For ByLayer/ByBlock, we'd need context; skip for now
        if (dxfColor.IsByLayer || dxfColor.IsByBlock)
            return null;

        // ACI color index → approximate RGB mapping
        var (r, g, b) = AciToRgb(dxfColor.RawValue);
        return new IsolineColor(r, g, b);
    }

    /// <summary>
    /// Simplified ACI → RGB mapping for common LIRA-SAPR colors.
    /// Full mapping requires the 256-entry ACI palette.
    /// </summary>
    private static (byte R, byte G, byte B) AciToRgb(short aci) => aci switch
    {
        1 => (255, 0, 0),       // Red
        2 => (255, 255, 0),     // Yellow
        3 => (0, 255, 0),       // Green
        4 => (0, 255, 255),     // Cyan
        5 => (0, 0, 255),       // Blue
        6 => (255, 0, 255),     // Magenta
        7 => (255, 255, 255),   // White
        8 => (128, 128, 128),   // Dark gray
        9 => (192, 192, 192),   // Light gray
        10 => (255, 0, 0),      // Red
        30 => (255, 127, 0),    // Orange
        40 => (255, 191, 0),    // Gold
        50 => (255, 255, 0),    // Yellow
        90 => (0, 191, 0),      // Dark green
        150 => (0, 0, 191),     // Dark blue
        200 => (191, 0, 191),   // Dark magenta
        _ => (128, 128, 128),   // Default gray
    };
}
