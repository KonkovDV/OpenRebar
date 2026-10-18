using OpenRebar.Domain.Models;

namespace OpenRebar.Domain.Rules;

/// <summary>
/// Where the later piece of a lap is drawn. The design axis stays put.
/// <c>inPlane</c> moves that piece sideways by one diameter. <c>stacked</c> moves it up by one diameter.
/// </summary>
public static class LapPlacement
{
  public const string InPlane = "inPlane";
  public const string Stacked = "stacked";

  public static bool IsKnown(string? representation) =>
      string.Equals(representation, InPlane, StringComparison.Ordinal)
      || string.Equals(representation, Stacked, StringComparison.Ordinal);

  public static RebarSegment ForDrawing(RebarSegment segment, string? representation)
  {
    if (!IsLaterPiece(segment) || IsStacked(representation))
      return segment;

    double length = segment.Start.DistanceTo(segment.End);
    double ux = length > 1e-9 ? (segment.End.X - segment.Start.X) / length : 1;
    double uy = length > 1e-9 ? (segment.End.Y - segment.Start.Y) / length : 0;
    double lateral = segment.DiameterMm;
    return segment with
    {
      Start = new Point2D(segment.Start.X - uy * lateral, segment.Start.Y + ux * lateral),
      End = new Point2D(segment.End.X - uy * lateral, segment.End.Y + ux * lateral)
    };
  }

  public static double VerticalMm(RebarSegment segment, string? representation) =>
      IsLaterPiece(segment) && IsStacked(representation) ? segment.DiameterMm : 0;

  /// <summary>
  /// How far a lap reaches from its design axis, mm.
  /// Side by side the pair is two diameters wide. Stacked, the plan width is one diameter.
  /// </summary>
  public static double ReachFromAxisMm(double diameterMm, string? representation) =>
      IsStacked(representation) ? diameterMm / 2.0 : diameterMm;

  private static bool IsLaterPiece(RebarSegment segment) =>
      segment.LapPieceParity % 2 == 1 && segment.DiameterMm > 0;

  private static bool IsStacked(string? representation) =>
      string.Equals(representation, Stacked, StringComparison.Ordinal);
}
