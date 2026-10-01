using OpenRebar.Domain.Models;
using OpenRebar.Domain.Ports;

namespace OpenRebar.Infrastructure.Geometry;

/// <summary>
/// W = inset(slab, edge cover) minus each opening grown by the clearance.
/// </summary>
public static class WorkingAreaBuilder
{
  public static PlanarRegion Build(SlabGeometry slab, IPlanarGeometry geometry)
  {
    var area = Region(slab.OuterBoundary);
    if (slab.EdgeCoverMm > 1e-9)
      area = geometry.Buffer(area, -slab.EdgeCoverMm, BufferJoin.Mitre);

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
