using OpenRebar.Domain.Exceptions;

namespace OpenRebar.Domain.Models;

/// <summary>
/// How a legend interval is turned into one required area.
/// </summary>
public enum AsValueConvention
{
  UpperBound,
  LowerBound,
  Midpoint
}

/// <summary>
/// Whether a zone is the background mesh or additional reinforcement.
/// </summary>
public enum ZoneRole
{
  Background,
  Additional
}

/// <summary>
/// Closed-open required-area band in mm²/m. One cm²/m is 100 mm²/m.
/// </summary>
public sealed record AsInterval(double LowerMm2PerM, double UpperMm2PerM)
{
  public double Resolve(AsValueConvention convention) => convention switch
  {
    AsValueConvention.LowerBound => LowerMm2PerM,
    AsValueConvention.Midpoint => (LowerMm2PerM + UpperMm2PerM) / 2.0,
    _ => UpperMm2PerM
  };
}

public sealed record AsLegendClass(string ClassId, IsolineColor Color, AsInterval As);

/// <summary>
/// Area legend for one layer. Intervals must be monotonic, non-overlapping, and separated in color.
/// </summary>
public sealed class AsLegend
{
  public const string Empty = "LEGEND_EMPTY";
  public const string DuplicateClass = "LEGEND_DUPLICATE_CLASS";
  public const string DuplicateColor = "LEGEND_DUPLICATE_COLOR";
  public const string IntervalInvalid = "LEGEND_INTERVAL_INVALID";
  public const string NotMonotonic = "LEGEND_NOT_MONOTONIC";
  public const string IntervalOverlap = "LEGEND_INTERVAL_OVERLAP";
  public const string IntervalGap = "LEGEND_INTERVAL_GAP";
  public const string ColorTooClose = "LEGEND_COLOR_TOO_CLOSE";

  public const double DefaultMinSeparationDeltaE = 10;

  public LayerKey Layer { get; }
  public IReadOnlyList<AsLegendClass> Classes { get; }
  public AsValueConvention Convention { get; }
  public bool AllowGaps { get; }
  public double MinSeparationDeltaE { get; }

  public AsLegend(
      LayerKey layer,
      IReadOnlyList<AsLegendClass> classes,
      AsValueConvention convention = AsValueConvention.UpperBound,
      bool allowGaps = false,
      double minSeparationDeltaE = DefaultMinSeparationDeltaE)
  {
    if (classes.Count == 0)
      throw new LegendValidationException(Empty, "An area legend needs at least one class.");
    if (!double.IsFinite(minSeparationDeltaE) || minSeparationDeltaE < 0)
      throw new ArgumentOutOfRangeException(nameof(minSeparationDeltaE));

    var seenIds = new HashSet<string>(StringComparer.Ordinal);
    var seenColors = new HashSet<(byte R, byte G, byte B)>();
    for (int i = 0; i < classes.Count; i++)
    {
      var item = classes[i];
      if (string.IsNullOrWhiteSpace(item.ClassId))
        throw new LegendValidationException(DuplicateClass, "A legend class id is required.");
      if (!seenIds.Add(item.ClassId))
        throw new LegendValidationException(DuplicateClass, $"Legend class '{item.ClassId}' is duplicated.");

      var rgb = (item.Color.R, item.Color.G, item.Color.B);
      if (!seenColors.Add(rgb))
        throw new LegendValidationException(DuplicateColor, $"Legend color {item.Color.R},{item.Color.G},{item.Color.B} is duplicated.");

      if (!double.IsFinite(item.As.LowerMm2PerM)
          || !double.IsFinite(item.As.UpperMm2PerM)
          || item.As.LowerMm2PerM < 0
          || item.As.UpperMm2PerM <= item.As.LowerMm2PerM)
        throw new LegendValidationException(IntervalInvalid, $"Legend class '{item.ClassId}' has an empty area interval.");

      if (i > 0 && item.As.LowerMm2PerM + 1e-9 < classes[i - 1].As.LowerMm2PerM)
        throw new LegendValidationException(NotMonotonic, $"Legend class '{item.ClassId}' is out of area order.");

      if (i > 0 && item.As.LowerMm2PerM < classes[i - 1].As.UpperMm2PerM - 1e-9)
        throw new LegendValidationException(IntervalOverlap, $"Legend class '{item.ClassId}' overlaps the previous interval.");

      if (!allowGaps && i > 0 && item.As.LowerMm2PerM > classes[i - 1].As.UpperMm2PerM + 1e-6)
        throw new LegendValidationException(IntervalGap, $"Legend class '{item.ClassId}' leaves a gap after the previous interval.");

      for (int j = 0; j < i; j++)
      {
        if (item.Color.DeltaE(classes[j].Color) < minSeparationDeltaE)
          throw new LegendValidationException(ColorTooClose, $"Legend class '{item.ClassId}' is too close in color to '{classes[j].ClassId}'.");
      }
    }

    Layer = layer;
    Classes = classes;
    Convention = convention;
    AllowGaps = allowGaps;
    MinSeparationDeltaE = minSeparationDeltaE;
  }
}

/// <summary>
/// Background mesh of one layer. Additional bars are chosen against this specification.
/// </summary>
public sealed record BackgroundMesh(LayerKey Layer, ReinforcementSpec Spec, double GridOriginMm);
