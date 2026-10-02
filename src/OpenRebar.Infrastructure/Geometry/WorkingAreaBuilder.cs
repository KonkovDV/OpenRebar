using OpenRebar.Domain.Models;
using OpenRebar.Domain.Ports;

namespace OpenRebar.Infrastructure.Geometry;

/// <summary>
/// Free sides are inset by cover + d/2, or by the edge cover when that is larger.
/// A support grows outward. Openings are then removed.
/// </summary>
public static class WorkingAreaBuilder
{
  public static PlanarRegion Build(SlabGeometry slab, IPlanarGeometry geometry, double diameterMm = 0)
  {
    PlanarRegion area;
    if (SlabEdges.HasDeclaredSupport(slab))
      area = Region(SlabEdges.WorkingRectangle(slab, diameterMm));
    else
    {
      area = Region(slab.OuterBoundary);
      double inset = SlabEdges.FreeInsetMm(slab, diameterMm);
      if (inset > 1e-9)
        area = geometry.Buffer(area, -inset, BufferJoin.Mitre);
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
