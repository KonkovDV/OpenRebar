using OpenRebar.Domain.Models;
using OpenRebar.Domain.Ports;
using OpenRebar.Domain.Rules;

namespace OpenRebar.Application.UseCases;

/// <summary>
/// Samples each zone on a square grid and sums bar area per metre inside each bar's spacing strip.
/// </summary>
public sealed class GridReinforcementVerifier : IReinforcementVerifier
{
  public ReinforcementVerificationResult Verify(
      IReadOnlyList<ReinforcementZone> zones,
      VerificationSettings? settings = null)
  {
    settings ??= new VerificationSettings();
    if (settings.CellSizeMm <= 0)
      throw new ArgumentOutOfRangeException(nameof(settings), "Cell size must be positive.");
    if (settings.UnderCoverageRatio < 0)
      throw new ArgumentOutOfRangeException(nameof(settings), "Under-coverage ratio cannot be negative.");

    double cell = settings.CellSizeMm;
    var samples = new List<CellSample>();
    var meshByLayer = MeshStrips(zones);

    foreach (var zone in zones)
    {
      var bbox = zone.Boundary.GetBoundingBox();
      if (bbox.Width <= 0 || bbox.Height <= 0)
        continue;

      bool ramp = zone.Role != ZoneRole.Background;
      var bars = zone.Rebars
          .Where(bar => bar.Status != BarInstanceStatus.Discarded)
          .Select(bar => BarStrip.From(bar, zone.EffectiveSpec.SpacingMm, ramp))
          .Where(bar => bar.LengthMm > 1e-6)
          .ToList();

      for (double y = bbox.Min.Y + cell / 2.0; y < bbox.Max.Y; y += cell)
      {
        for (double x = bbox.Min.X + cell / 2.0; x < bbox.Max.X; x += cell)
        {
          var center = new Point2D(x, y);
          if (!PolygonDecomposition.IsPointInPolygon(center, zone.Boundary))
            continue;
          if (zone.Holes.Any(hole => PolygonDecomposition.IsPointInPolygon(center, hole)))
            continue;

          double required = RequiredArea(zone, x, y, cell, settings);
          double provided = 0;
          if (!zone.IsBackgroundMesh
              && zone.DesignLayer is LayerKey layer
              && meshByLayer.TryGetValue(layer, out var meshStrips))
          {
            foreach (var strip in meshStrips)
              provided += strip.ProvisionAt(center);
          }

          foreach (var bar in bars)
            provided += bar.ProvisionAt(center);

          samples.Add(new CellSample(
              zone.Id,
              LayerName(zone),
              (int)Math.Floor((x - bbox.Min.X) / cell),
              (int)Math.Floor((y - bbox.Min.Y) / cell),
              x,
              y,
              provided,
              required));
        }
      }
    }

    if (samples.Count == 0)
    {
      return new ReinforcementVerificationResult
      {
        Status = VerificationStatuses.Passed,
        UnderReinforcedAreaM2 = 0,
        CheckedAreaM2 = 0,
        MinMarginMm2PerM = 0,
        MinProvisionRatio = 1,
        CellSizeMm = cell,
        UnderCoverageRatio = settings.UnderCoverageRatio,
        PeakSmoothingWindowMm = settings.PeakSmoothingWindowMm,
        UnderReinforcedCells = 0,
        ExcessSteelKg = 0,
        Layers = [],
        Cells = [],
        DeficitRegions = []
      };
    }

    double minMargin = double.PositiveInfinity;
    double minRatio = double.PositiveInfinity;
    int failed = 0;
    foreach (var sample in samples)
    {
      double margin = sample.Provided - sample.Required;
      double ratio = sample.Required > 1e-9 ? sample.Provided / sample.Required : 1;
      if (margin < minMargin)
        minMargin = margin;
      if (ratio < minRatio)
        minRatio = ratio;
      if (sample.Provided + 1e-6 < sample.Required)
        failed++;
    }

    int smoothedFailed = failed;
    if (settings.PeakSmoothingWindowMm is > 0)
      smoothedFailed = CountSmoothedFailures(samples, settings.PeakSmoothingWindowMm.Value);

    double cellAreaM2 = cell * cell / 1_000_000.0;
    double underArea = failed * cellAreaM2;
    double checkedArea = samples.Count * cellAreaM2;
    double deficitRatio = failed / (double)samples.Count;
    string status = ResolveStatus(failed, smoothedFailed, deficitRatio, settings);

    return new ReinforcementVerificationResult
    {
      Status = status,
      UnderReinforcedAreaM2 = underArea,
      CheckedAreaM2 = checkedArea,
      MinMarginMm2PerM = minMargin,
      MinProvisionRatio = minRatio,
      CellSizeMm = cell,
      UnderCoverageRatio = settings.UnderCoverageRatio,
      PeakSmoothingWindowMm = settings.PeakSmoothingWindowMm,
      UnderReinforcedCells = failed,
      ExcessSteelKg = ExcessMassKg(samples, cell),
      Layers = LayerReports(samples, cell),
      Cells = samples.Select(sample => new VerificationCellReport
      {
        Layer = sample.Layer,
        X = sample.X,
        Y = sample.Y,
        MarginMm2PerM = sample.Provided - sample.Required
      }).ToList(),
      DeficitRegions = MergeDeficits(samples, cell)
    };
  }

  private static string LayerName(ReinforcementZone zone) =>
      zone.DesignLayer?.ToString() ?? zone.Id;

  private static Dictionary<LayerKey, List<BarStrip>> MeshStrips(IReadOnlyList<ReinforcementZone> zones)
  {
    var meshByLayer = new Dictionary<LayerKey, List<BarStrip>>();
    foreach (var zone in zones)
    {
      if (!zone.IsBackgroundMesh || zone.DesignLayer is not LayerKey layer)
        continue;

      if (!meshByLayer.TryGetValue(layer, out var strips))
      {
        strips = [];
        meshByLayer[layer] = strips;
      }

      foreach (var bar in zone.Rebars)
      {
        if (bar.Status == BarInstanceStatus.Discarded)
          continue;
        var strip = BarStrip.From(bar, zone.EffectiveSpec.SpacingMm, ramp: false);
        if (strip.LengthMm > 1e-6)
          strips.Add(strip);
      }
    }

    return meshByLayer;
  }

  private static double ExcessMassKg(List<CellSample> samples, double cell)
  {
    const double steelDensityKgPerM3 = 7850;
    double excess = 0;
    foreach (var sample in samples)
    {
      double surplus = sample.Provided - sample.Required;
      if (surplus > 1e-6)
        excess += surplus * cell * cell * steelDensityKgPerM3 / 1e12;
    }

    return excess;
  }

  private static IReadOnlyList<LayerVerificationReport> LayerReports(
      List<CellSample> samples,
      double cell)
  {
    double cellAreaM2 = cell * cell / 1_000_000.0;
    return samples
        .GroupBy(sample => sample.Layer)
        .Select(group =>
        {
          var cells = group.ToList();
          int failed = cells.Count(sample => sample.Provided + 1e-6 < sample.Required);
          double minMargin = cells.Min(sample => sample.Provided - sample.Required);
          return new LayerVerificationReport
          {
            Layer = group.Key,
            Status = failed == 0 ? VerificationStatuses.Passed : VerificationStatuses.Failed,
            UnderReinforcedAreaM2 = failed * cellAreaM2,
            UnderReinforcedCells = failed,
            MinMarginMm2PerM = minMargin,
            ExcessSteelKg = ExcessMassKg(cells, cell)
          };
        })
        .OrderBy(layer => layer.Layer, StringComparer.Ordinal)
        .ToList();
  }

  private static double RequiredArea(
      ReinforcementZone zone,
      double x,
      double y,
      double cell,
      VerificationSettings settings)
  {
    if (settings.Field is not null && zone.DesignLayer is LayerKey layer)
    {
      double fromField = MaxTouchingElement(settings.Field, layer, x, y, cell);
      if (!double.IsNaN(fromField))
        return fromField;
    }

    return zone.AsRequiredMm2PerM > 0
        ? zone.AsRequiredMm2PerM
        : zone.Spec.AreaPerMeterMm2;
  }

  private static double MaxTouchingElement(AsField field, LayerKey layer, double x, double y, double cell)
  {
    double half = cell / 2.0;
    double best = double.NaN;
    foreach (var element in field.Elements)
    {
      if (!element.AsMm2PerM.TryGetValue(layer.ToString(), out double required))
        continue;
      bool touches = element.MaxX > x - half
          && element.MinX < x + half
          && element.MaxY > y - half
          && element.MinY < y + half;
      if (!touches)
        continue;
      if (double.IsNaN(best) || required > best)
        best = required;
    }

    return best;
  }

  private static string ResolveStatus(
      int failed,
      int smoothedFailed,
      double deficitRatio,
      VerificationSettings settings)
  {
    if (failed == 0)
      return VerificationStatuses.Passed;
    if (settings.PeakSmoothingWindowMm is > 0 && smoothedFailed == 0)
      return VerificationStatuses.PassedWithSmoothing;
    if (settings.UnderCoverageRatio > 0 && deficitRatio <= settings.UnderCoverageRatio + 1e-12)
      return VerificationStatuses.PassedWithUnderCoverage;
    return VerificationStatuses.Failed;
  }

  private static int CountSmoothedFailures(List<CellSample> samples, double windowMm)
  {
    double half = windowMm / 2.0;
    int failed = 0;
    foreach (var sample in samples)
    {
      double peak = sample.Provided;
      foreach (var other in samples)
      {
        if (!string.Equals(other.ZoneId, sample.ZoneId, StringComparison.Ordinal))
          continue;
        if (Math.Abs(other.X - sample.X) > half || Math.Abs(other.Y - sample.Y) > half)
          continue;
        if (other.Provided > peak)
          peak = other.Provided;
      }

      if (peak + 1e-6 < sample.Required)
        failed++;
    }

    return failed;
  }

  private static IReadOnlyList<DeficitRegionReport> MergeDeficits(List<CellSample> samples, double cell)
  {
    var failed = samples
        .Where(sample => sample.Provided + 1e-6 < sample.Required)
        .ToList();
    var pending = new HashSet<(string ZoneId, int Ix, int Iy)>(
        failed.Select(sample => (sample.ZoneId, sample.Ix, sample.Iy)));
    var lookup = failed.ToDictionary(sample => (sample.ZoneId, sample.Ix, sample.Iy));
    var regions = new List<DeficitRegionReport>();

    foreach (var seedKey in pending.ToArray())
    {
      if (!pending.Remove(seedKey))
        continue;

      var queue = new Queue<(string ZoneId, int Ix, int Iy)>();
      queue.Enqueue(seedKey);
      double minX = double.PositiveInfinity;
      double minY = double.PositiveInfinity;
      double maxX = double.NegativeInfinity;
      double maxY = double.NegativeInfinity;

      while (queue.Count > 0)
      {
        var key = queue.Dequeue();
        var sample = lookup[key];
        minX = Math.Min(minX, sample.X - cell / 2.0);
        minY = Math.Min(minY, sample.Y - cell / 2.0);
        maxX = Math.Max(maxX, sample.X + cell / 2.0);
        maxY = Math.Max(maxY, sample.Y + cell / 2.0);

        foreach (var next in Neighbours(key))
        {
          if (pending.Remove(next))
            queue.Enqueue(next);
        }
      }

      regions.Add(new DeficitRegionReport
      {
        ZoneId = seedKey.ZoneId,
        MinX = minX,
        MinY = minY,
        MaxX = maxX,
        MaxY = maxY
      });
    }

    return regions
        .OrderBy(region => region.ZoneId, StringComparer.Ordinal)
        .ThenBy(region => region.MinX)
        .ThenBy(region => region.MinY)
        .ToList();
  }

  private static IEnumerable<(string ZoneId, int Ix, int Iy)> Neighbours((string ZoneId, int Ix, int Iy) key)
  {
    yield return (key.ZoneId, key.Ix - 1, key.Iy);
    yield return (key.ZoneId, key.Ix + 1, key.Iy);
    yield return (key.ZoneId, key.Ix, key.Iy - 1);
    yield return (key.ZoneId, key.Ix, key.Iy + 1);
  }

  private readonly record struct CellSample(
      string ZoneId,
      string Layer,
      int Ix,
      int Iy,
      double X,
      double Y,
      double Provided,
      double Required);

  private readonly record struct BarStrip(
      double OriginX,
      double OriginY,
      double AxisX,
      double AxisY,
      double NormalX,
      double NormalY,
      double LengthMm,
      double HalfSpacingMm,
      double AreaPerMeter,
      double AnchorageStartMm,
      double AnchorageEndMm,
      bool Ramp)
  {
    public static BarStrip From(RebarSegment bar, double spacingMm, bool ramp)
    {
      double dx = bar.End.X - bar.Start.X;
      double dy = bar.End.Y - bar.Start.Y;
      double length = Math.Sqrt(dx * dx + dy * dy);
      double axisX = length > 1e-9 ? dx / length : 1;
      double axisY = length > 1e-9 ? dy / length : 0;
      double area = Math.PI * bar.DiameterMm * bar.DiameterMm / 4.0;
      return new BarStrip(
          bar.Start.X,
          bar.Start.Y,
          axisX,
          axisY,
          -axisY,
          axisX,
          length,
          spacingMm / 2.0,
          area * (1000.0 / spacingMm),
          Math.Max(0, bar.AnchorageLengthStart),
          Math.Max(0, bar.AnchorageLengthEnd),
          ramp);
    }

    public double ProvisionAt(Point2D point)
    {
      double vx = point.X - OriginX;
      double vy = point.Y - OriginY;
      double along = vx * AxisX + vy * AxisY;
      if (along < -1e-6 || along > LengthMm + 1e-6)
        return 0;
      double across = vx * NormalX + vy * NormalY;
      if (Math.Abs(across) > HalfSpacingMm + 1e-6)
        return 0;
      return AreaPerMeter * Development(along);
    }

    private double Development(double along)
    {
      if (!Ramp || (AnchorageStartMm <= 1e-9 && AnchorageEndMm <= 1e-9))
        return 1;

      double fromStart = along;
      double fromEnd = LengthMm - along;
      double start = AnchorageStartMm <= 1e-9 ? 1 : Math.Clamp(fromStart / AnchorageStartMm, 0, 1);
      double end = AnchorageEndMm <= 1e-9 ? 1 : Math.Clamp(fromEnd / AnchorageEndMm, 0, 1);
      return Math.Min(start, end);
    }
  }
}
