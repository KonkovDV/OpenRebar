using OpenRebar.Domain.Models;
using OpenRebar.Domain.Ports;

namespace OpenRebar.Infrastructure.Geometry;

/// <summary>
/// Free sides are inset by the edge cover. A support grows outward. Openings are then removed.
/// </summary>
public static class WorkingAreaBuilder
{
  public static PlanarRegion Build(SlabGeometry slab, IPlanarGeometry geometry)
  {
    PlanarRegion area;
    if (SlabEdges.HasDeclaredSupport(slab))
      area = Region(SlabEdges.WorkingRectangle(slab));
    else
    {
      area = Region(slab.OuterBoundary);
      if (slab.EdgeCoverMm > 1e-9)
        area = geometry.Buffer(area, -slab.EdgeCoverMm, BufferJoin.Mitre);
    }

    foreach (var opening in slab.Openings)
    {
      var grown = Region(opening);
      if (slab.OpeningClearanceMm > 1e-9)
        grown = geometry.Buffer(grown, slab.OpeningClearanceMm, BufferJoin.Mitre);
      area = geometry.Difference(area, grown);
    }

    return area;
  }

  private static PlanarRegion Region(Polygon polygon) => new([new PlanarPolygon(polygon)]);
}
