using OpenRebar.Domain.Models;

namespace OpenRebar.Domain.Ports;

/// <summary>
/// Optimizes rebar cutting to reduce waste, stock consumption, and cost under the configured weights.
/// Solves the rebar cutting-stock problem using the implementation's supported exact and heuristic strategies.
/// </summary>
public interface IRebarOptimizer
{
  /// <summary>
  /// Compute a cutting plan for the requested rebar lengths.
  /// </summary>
  /// <param name="requiredLengths">All required rebar lengths in mm.</param>
  /// <param name="stockLengths">Available stock bar lengths.</param>
  /// <param name="settings">Optimization settings.</param>
  /// <returns>Cutting plan with waste statistics and optimizer provenance.</returns>
  OptimizationResult Optimize(
      IReadOnlyList<double> requiredLengths,
      IReadOnlyList<StockLength> stockLengths,
      OptimizationSettings settings);
}

/// <summary>
/// Settings for the rebar optimization algorithm.
/// </summary>
public sealed record OptimizationSettings
{
  /// <summary>Minimum usable scrap length in mm (shorter pieces are waste).</summary>
  public double MinScrapLengthMm
  {
    get => _minScrapLengthMm;
    init
    {
      if (value < 0)
        throw new ArgumentOutOfRangeException(nameof(MinScrapLengthMm), value, "Minimum scrap length cannot be negative.");
      _minScrapLengthMm = value;
    }
  }
  private readonly double _minScrapLengthMm = 300;

  /// <summary>Saw cut width in mm.</summary>
  public double SawCutWidthMm
  {
    get => _sawCutWidthMm;
    init
    {
      if (value < 0)
        throw new ArgumentOutOfRangeException(nameof(SawCutWidthMm), value, "Saw cut width cannot be negative.");
      _sawCutWidthMm = value;
    }
  }
  private readonly double _sawCutWidthMm = 3;

  /// <summary>Weight for waste minimization (0..1).</summary>
  public double WasteWeight
  {
    get => _wasteWeight;
    init
    {
      ValidateWeight(nameof(WasteWeight), value);
      _wasteWeight = value;
    }
  }
  private readonly double _wasteWeight = 0.5;

  /// <summary>Weight for cost minimization (0..1).</summary>
  public double CostWeight
  {
    get => _costWeight;
    init
    {
      ValidateWeight(nameof(CostWeight), value);
      _costWeight = value;
    }
  }
  private readonly double _costWeight = 0.3;

  /// <summary>Weight for ease of installation (fewer pieces, 0..1).</summary>
  public double InstallationWeight
  {
    get => _installationWeight;
    init
    {
      ValidateWeight(nameof(InstallationWeight), value);
      _installationWeight = value;
    }
  }
  private readonly double _installationWeight = 0.2;

  /// <summary>Maximum computation time.</summary>
  public TimeSpan MaxComputationTime
  {
    get => _maxComputationTime;
    init
    {
      if (value <= TimeSpan.Zero)
        throw new ArgumentOutOfRangeException(nameof(MaxComputationTime), value, "Maximum computation time must be positive.");
      _maxComputationTime = value;
    }
  }
  private readonly TimeSpan _maxComputationTime = TimeSpan.FromSeconds(30);

  private static void ValidateWeight(string propertyName, double value)
  {
    if (value is < 0 or > 1)
      throw new ArgumentOutOfRangeException(propertyName, value, "Weights must be between 0 and 1.");
  }
}
