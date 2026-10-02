using FluentAssertions;
using OpenRebar.Domain.Models;

namespace OpenRebar.Domain.Tests.Models;

public class SlabEdgeTests
{
  [Fact]
  public void Parse_AcceptsASupport_AndRejectsAnUnknownSide()
  {
    var edge = SlabEdges.Parse("MINX", "supported", 600);

    edge.Segment.Should().Be("minX");
    edge.Kind.Should().Be(SlabEdgeKind.Supported);
    edge.SupportDepthMm.Should().Be(600);

    var act = () => SlabEdges.Parse("left", "Free", null);
    act.Should().Throw<InvalidDataException>();
  }

  [Fact]
  public void FreeEdge_InsetsByCoverPlusHalfDiameter_AndWarnsWhenEdgeCoverIsSmaller()
  {
    var slab = new SlabGeometry
    {
      OuterBoundary = Rectangle(0, 0, 2000, 1000),
      ThicknessMm = 220,
      CoverMm = 30,
      EdgeCoverMm = 0,
      ConcreteClass = "B25"
    };

    var box = SlabEdges.WorkingRectangle(slab, 20).GetBoundingBox();

    box.Min.X.Should().BeApproximately(40, 1e-6);
    box.Max.X.Should().BeApproximately(1960, 1e-6);
    box.Min.Y.Should().BeApproximately(40, 1e-6);
    box.Max.Y.Should().BeApproximately(960, 1e-6);
    SlabEdges.FreeInsetMm(slab, 20).Should().Be(40);
    SlabEdges.Warnings(slab).Should().Contain(SlabEdges.EdgeCoverBelowCover);
  }

  [Fact]
  public void WorkingRectangle_GrowsASupport_AndInsetsAFreeSide()
  {
    var slab = new SlabGeometry
    {
      OuterBoundary = Rectangle(0, 0, 2000, 1000),
      ThicknessMm = 220,
      CoverMm = 30,
      EdgeCoverMm = 40,
      ConcreteClass = "B25",
      Edges = [new SlabEdge { Segment = "minX", Kind = SlabEdgeKind.Supported, SupportDepthMm = 600 }]
    };

    var box = SlabEdges.WorkingRectangle(slab, 0).GetBoundingBox();

    box.Min.X.Should().BeApproximately(-560, 1e-6);
    box.Max.X.Should().BeApproximately(1960, 1e-6);
    box.Min.Y.Should().BeApproximately(40, 1e-6);
    SlabEdges.Kind(slab, "maxY").Should().Be(SlabEdgeKind.Free);
    SlabEdges.Warnings(slab).Should().Contain(warning => warning.StartsWith("EdgeKindDefaulted", StringComparison.Ordinal));
  }

  [Fact]
  public void ShallowSupport_StaysOnTheSlabFace()
  {
    var slab = new SlabGeometry
    {
      OuterBoundary = Rectangle(0, 0, 2000, 1000),
      ThicknessMm = 220,
      CoverMm = 30,
      EdgeCoverMm = 40,
      ConcreteClass = "B25",
      Edges =
      [
          new SlabEdge { Segment = "minX", Kind = SlabEdgeKind.Supported, SupportDepthMm = 20 },
          new SlabEdge { Segment = "maxX", Kind = SlabEdgeKind.Free },
          new SlabEdge { Segment = "minY", Kind = SlabEdgeKind.Free },
          new SlabEdge { Segment = "maxY", Kind = SlabEdgeKind.Free }
      ]
    };

    var box = SlabEdges.WorkingRectangle(slab, 0).GetBoundingBox();

    box.Min.X.Should().BeApproximately(0, 1e-6);
    box.Max.X.Should().BeApproximately(1960, 1e-6);
    SlabEdges.OutwardMm(slab, "minX").Should().Be(0);
  }

  [Fact]
  public void SupportedOutline_DoesNotWarnAboutAFreeEdgeInset()
  {
    var slab = new SlabGeometry
    {
      OuterBoundary = Rectangle(0, 0, 2000, 1000),
      ThicknessMm = 220,
      CoverMm = 30,
      EdgeCoverMm = 0,
      ConcreteClass = "B25",
      Edges = SlabEdges.Segments.Select(segment => new SlabEdge
      {
        Segment = segment,
        Kind = SlabEdgeKind.Supported,
        SupportDepthMm = 400
      }).ToArray()
    };

    SlabEdges.Warnings(slab).Should().NotContain(SlabEdges.EdgeCoverBelowCover);
    SlabEdges.WorkingRectangle(slab, 20).GetBoundingBox().Min.X.Should().BeApproximately(-400, 1e-6);
  }

  [Fact]
  public void Continuous_UsesTheSupportStrip_AndSaysTheNeighbourIsMissing()
  {
    var slab = new SlabGeometry
    {
      OuterBoundary = Rectangle(0, 0, 2000, 1000),
      ThicknessMm = 220,
      CoverMm = 30,
      ConcreteClass = "B25",
      Edges =
      [
          new SlabEdge { Segment = "minX", Kind = SlabEdgeKind.Continuous, SupportDepthMm = 400 },
          new SlabEdge { Segment = "maxX", Kind = SlabEdgeKind.Free },
          new SlabEdge { Segment = "minY", Kind = SlabEdgeKind.Free },
          new SlabEdge { Segment = "maxY", Kind = SlabEdgeKind.Free }
      ]
    };

    SlabEdges.OutwardMm(slab, "minX").Should().Be(400);
    SlabEdges.Warnings(slab).Should().Contain(SlabEdges.ContinuousSpanNotProvided);
    SlabEdges.Warnings(slab).Should().Contain(SlabEdges.EdgeCoverBelowCover);
    SlabEdges.AnyDefaulted(slab).Should().BeFalse();
  }

  private static Polygon Rectangle(double minX, double minY, double maxX, double maxY) => new(
  [
      new Point2D(minX, minY),
      new Point2D(maxX, minY),
      new Point2D(maxX, maxY),
      new Point2D(minX, maxY)
  ]);
}
