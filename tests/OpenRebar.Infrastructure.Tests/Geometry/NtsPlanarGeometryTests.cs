using FluentAssertions;
using OpenRebar.Domain.Models;
using OpenRebar.Domain.Ports;
using OpenRebar.Infrastructure.Geometry;

namespace OpenRebar.Infrastructure.Tests.Geometry;

public class NtsPlanarGeometryTests
{
  private readonly NtsPlanarGeometry _geometry = new();

  [Fact]
  public void Boolean_Disjoint_UnionAddsAreasAndIntersectionIsEmpty()
  {
    var left = Box(0, 0, 10, 10);
    var right = Box(20, 0, 30, 10);

    var union = _geometry.Union(left, right);
    var intersection = _geometry.Intersection(left, right);

    union.Area.Should().BeApproximately(200, 0.1);
    union.Polygons.Should().HaveCount(2);
    intersection.Area.Should().BeApproximately(0, 0.1);
    intersection.HoleCount.Should().Be(0);
  }

  [Fact]
  public void Boolean_Overlap_SplitsArea()
  {
    var left = Box(0, 0, 10, 10);
    var right = Box(5, 0, 15, 10);

    _geometry.Union(left, right).Area.Should().BeApproximately(150, 0.1);
    _geometry.Intersection(left, right).Area.Should().BeApproximately(50, 0.1);
    _geometry.SymmetricDifference(left, right).Area.Should().BeApproximately(100, 0.1);
  }

  [Fact]
  public void Boolean_SharedEdge_UnionMergesAndIntersectionIsEmpty()
  {
    var left = Box(0, 0, 10, 10);
    var right = Box(10, 0, 20, 10);

    var union = _geometry.Union(left, right);
    var intersection = _geometry.Intersection(left, right);

    union.Area.Should().BeApproximately(200, 0.1);
    union.Polygons.Should().HaveCount(1);
    union.HoleCount.Should().Be(0);
    intersection.Area.Should().BeApproximately(0, 0.1);
  }

  [Fact]
  public void Boolean_NestedDifference_LeavesAHole()
  {
    var outer = Box(0, 0, 30, 30);
    var inner = Box(10, 10, 20, 20);

    var difference = _geometry.Difference(outer, inner);

    difference.Polygons.Should().HaveCount(1);
    difference.HoleCount.Should().Be(1);
    difference.Area.Should().BeApproximately(800, 0.1);
    difference.Contains(new Point2D(15, 15)).Should().BeFalse();
    difference.Contains(new Point2D(1, 1)).Should().BeTrue();
    _geometry.IsValid(difference).Should().BeTrue();
  }

  [Fact]
  public void Boolean_PointTouch_KeepsBothAreas()
  {
    var left = Box(0, 0, 10, 10);
    var right = Box(10, 10, 20, 20);

    var union = _geometry.Union(left, right);
    union.Area.Should().BeApproximately(200, 0.1);
    _geometry.Intersection(left, right).Area.Should().BeApproximately(0, 0.1);
  }

  [Fact]
  public void MakeValid_Bowtie_BecomesValid()
  {
    var bowtie = new PlanarRegion(
    [
        new PlanarPolygon(new Polygon(
        [
            new Point2D(0, 0),
            new Point2D(10, 10),
            new Point2D(0, 10),
            new Point2D(10, 0)
        ]))
    ]);

    _geometry.IsValid(bowtie).Should().BeFalse();
    var valid = _geometry.MakeValid(bowtie);
    _geometry.IsValid(valid).Should().BeTrue();
    valid.Area.Should().BeGreaterThan(0);
  }

  [Fact]
  public void Polygonize_ClosedSquare_ReturnsItsArea()
  {
    var regions = _geometry.Polygonize(
    [
        (new Point2D(0, 0), new Point2D(10, 0)),
        (new Point2D(10, 0), new Point2D(10, 10)),
        (new Point2D(10, 10), new Point2D(0, 10)),
        (new Point2D(0, 10), new Point2D(0, 0))
    ]);

    regions.Should().ContainSingle();
    regions[0].Area.Should().BeApproximately(100, 0.1);
  }

  [Fact]
  public void SnapToGrid_RoundsToTheTenthOfAMillimetre()
  {
    var region = Box(0.04, 0.04, 10.04, 10.04);
    var snapped = _geometry.SnapToGrid(region, NtsPlanarGeometry.MillimetreGrid);
    var box = snapped.Polygons[0].Shell.GetBoundingBox();
    box.Min.X.Should().BeApproximately(0, 0.001);
    box.Min.Y.Should().BeApproximately(0, 0.001);
  }

  [Fact]
  public void Coverage_AdjacentSquares_UnionKeepsTheSum()
  {
    var left = Box(0, 0, 10, 10);
    var right = Box(10, 0, 20, 10);
    var regions = new[] { left, right };

    _geometry.IsValidCoverage(regions).Should().BeTrue();
    _geometry.UnionCoverage(regions).Area.Should().BeApproximately(200, 0.1);
  }

  [Fact]
  public void Index_FindsTheOverlappingRegion()
  {
    var index = _geometry.Index([Box(0, 0, 10, 10), Box(30, 30, 40, 40)]);
    index.Query(Box(5, 5, 15, 15)).Should().Equal(0);
  }

  private static PlanarRegion Box(double minX, double minY, double maxX, double maxY) => new(
  [
      new PlanarPolygon(new Polygon(
      [
          new Point2D(minX, minY),
          new Point2D(maxX, minY),
          new Point2D(maxX, maxY),
          new Point2D(minX, maxY)
      ]))
  ]);
}
