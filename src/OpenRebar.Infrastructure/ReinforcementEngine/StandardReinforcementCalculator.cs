using OpenRebar.Domain.Models;
using OpenRebar.Domain.Ports;
using OpenRebar.Domain.Rules;
using OpenRebar.Infrastructure.Geometry;

namespace OpenRebar.Infrastructure.ReinforcementEngine;

/// <summary>
/// Calculates rebar layout within zones.
/// Generates individual rebar segments with correct spacing,
/// anchorage lengths, lap splices, and mark numbering.
/// Supports both X and Y directions and Top/Bottom layers.
/// </summary>
public sealed class StandardReinforcementCalculator : IReinforcementCalculator
{
  private readonly IStructuredLogger _logger;
  private readonly IPlanarGeometry? _planar;

  public StandardReinforcementCalculator(IStructuredLogger logger, IPlanarGeometry? planar = null)
  {
    _logger = logger;
    _planar = planar;
  }

  public IReadOnlyList<ReinforcementZone> CalculateRebars(
      IReadOnlyList<ReinforcementZone> zones,
      SlabGeometry slab)
  {
    int markCounter = 0;

    foreach (var zone in zones)
    {
      var rebars = GenerateRebarsForZone(zone, slab, ref markCounter);
      zone.Rebars = rebars;
    }

    return zones;
  }

  private List<RebarSegment> GenerateRebarsForZone(
      ReinforcementZone zone,
      SlabGeometry slab,
      ref int markCounter)
  {
    zone.Runs = [];
    if (zone.SuppressLayout)
      return [];

    if (slab.EdgeCoverMm <= 1e-9 && slab.OpeningClearanceMm <= 1e-9)
      return GenerateRebarsInPolygon(zone.Boundary, zone.Holes, zone, slab, ref markCounter);

    if (_planar is null)
      throw new InvalidOperationException("Working area clipping needs the geometry port.");

    var clipped = _planar.Intersection(
        new PlanarRegion([new PlanarPolygon(zone.Boundary, zone.Holes)]),
        WorkingAreaBuilder.Build(slab, _planar));

    var rebars = new List<RebarSegment>();
    foreach (var part in clipped.Polygons)
      rebars.AddRange(GenerateRebarsInPolygon(part.Shell, part.Holes, zone, slab, ref markCounter));
    return rebars;
  }

  private List<RebarSegment> GenerateRebarsInPolygon(
      Polygon polygon,
      IReadOnlyList<Polygon> holes,
      ReinforcementZone zone,
      SlabGeometry slab,
      ref int markCounter)
  {
    var rebars = new List<RebarSegment>();
    var spec = zone.EffectiveSpec;
    int spacing = spec.SpacingMm;
    int diameter = spec.DiameterMm;

    double topBarFactor = zone.Layer == RebarLayer.Top
        ? NormativeProfiles.Sp63_2018.TopBarAnchorageFactor
        : 1.0;

    double anchorageLength = AnchorageRules.CalculateAnchorageLength(
        diameter,
        spec.SteelClass,
        slab.ConcreteClass,
        topBarAnchorageFactor: topBarFactor);
    int extendedBeyondZone = 0;
    bool alongX = zone.Direction == RebarDirection.X;
    string requestedEnd = string.IsNullOrWhiteSpace(zone.RequestedEndCondition)
        ? "NeedsHook"
        : zone.RequestedEndCondition;

    var runs = BarRunBuilder.Build(zone, slab, polygon, holes);
    zone.Runs = zone.Runs.Concat(runs).ToList();
    foreach (var run in runs)
    {
      foreach (double line in run.Lines)
      {
        double runLength = run.EndCoord - run.StartCoord;
        if (runLength < 1e-6)
          continue;

        var placed = AnchorageExtender.Extend(
            run.StartCoord,
            run.EndCoord,
            anchorageLength,
            AllowedAlong(slab, holes, line, alongX));
        var startCondition = AnchorageExtender.Condition(placed.AchievedStart, anchorageLength, requestedEnd);
        var endCondition = AnchorageExtender.Condition(placed.AchievedEnd, anchorageLength, requestedEnd);
        var anchorageStatus = AnchorageStatus.WithinZone;
        if (runLength + 1e-6 < anchorageLength)
        {
          extendedBeyondZone++;
          anchorageStatus = AnchorageStatus.ExtendedBeyondZone;
        }

        rebars.Add(new RebarSegment
        {
          Start = alongX
              ? new Point2D(placed.Start, line)
              : new Point2D(line, placed.Start),
          End = alongX
              ? new Point2D(placed.End, line)
              : new Point2D(line, placed.End),
          DiameterMm = diameter,
          AnchorageLengthStart = anchorageLength,
          AnchorageLengthEnd = anchorageLength,
          Mark = $"{++markCounter}",
          AnchorageStatus = anchorageStatus,
          EndConditionStart = startCondition,
          EndConditionEnd = endCondition
        });
      }
    }

    // Spacing limit follows slab thickness. The layer, not the plan axis, chooses the role.
    double maxSpacing = ReinforcementLimits.MaxSpacing(slab.ThicknessMm, ReinforcementLimits.SlabReinforcementRole.Working);
    if (spacing > maxSpacing)
    {
      _logger.Warn(
          "Spacing exceeds normative maximum",
          ("zoneId", zone.Id),
          ("spacingMm", spacing),
          ("maxSpacingMm", Math.Round(maxSpacing, 2)),
          ("slabThicknessMm", slab.ThicknessMm));
    }

    zone.ExtendedBeyondZoneCount = extendedBeyondZone;
    if (extendedBeyondZone > 0)
    {
      _logger.Warn(
          "Rebar clear span is shorter than anchorage; segments were kept",
          ("zoneId", zone.Id),
          ("extendedBeyondZoneCount", extendedBeyondZone),
          ("anchorageLengthMm", Math.Round(anchorageLength, 2)));
    }

    return rebars;
  }

  private List<(double Start, double End)> AllowedAlong(
      SlabGeometry slab,
      IReadOnlyList<Polygon> zoneHoles,
      double station,
      bool alongX)
  {
    IReadOnlyList<Polygon> shells;
    IReadOnlyList<Polygon> holes;
    if (_planar is not null
        && (slab.EdgeCoverMm > 1e-9 || slab.OpeningClearanceMm > 1e-9 || slab.Openings.Count > 0))
    {
      var area = WorkingAreaBuilder.Build(slab, _planar);
      shells = area.Polygons.Select(polygon => polygon.Shell).ToList();
      holes = area.Polygons.SelectMany(polygon => polygon.Holes).Concat(zoneHoles).ToList();
    }
    else
    {
      shells = [slab.OuterBoundary];
      holes = slab.Openings.Concat(zoneHoles).ToList();
    }

    return BarRunBuilder.IntervalsOnLine(shells, holes, station, alongX);
  }
}
