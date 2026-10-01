namespace OpenRebar.Domain.Models;

/// <summary>
/// One reinforcement layer: its drawing, legend, and optional background mesh.
/// </summary>
public sealed record LayerDesignInput
{
  public const int MaxLayers = 4;

  public required LayerKey Layer { get; init; }
  public required string IsolineFilePath { get; init; }
  public required ColorLegend Legend { get; init; }
  public BackgroundMesh? Background { get; init; }
}
