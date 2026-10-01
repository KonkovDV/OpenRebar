using System.Globalization;
using OpenRebar.Domain.Models;

namespace OpenRebar.Application.UseCases;

/// <summary>
/// Records which layer supplied each value that the run actually used.
/// </summary>
public static class ParameterSourceRecorder
{
  public static IReadOnlyList<ParameterSourceReport> Build(
      CompanyProfile profile,
      string steelClass,
      string steelSource,
      string concreteClass,
      string concreteSource,
      string scheduleCulture,
      string scheduleSource,
      string supplierName,
      string stockLengths,
      string catalogSource,
      int legendCount,
      string legendSource)
  {
    return
    [
        Item("steelClass", steelClass, steelSource),
        Item("concreteClass", concreteClass, concreteSource),
        Item("supply.supplierName", supplierName, catalogSource),
        Item("supply.stockLengthsMm", stockLengths, catalogSource),
        Item("ends.condition", profile.Ends.Condition, "profile"),
        Item("laps.jointRatioMax", profile.Laps.JointRatioMax.ToString("0.###", CultureInfo.InvariantCulture), "profile"),
        Item("safety.factor", profile.Safety.Factor.ToString("0.###", CultureInfo.InvariantCulture), "profile"),
        Item("safety.anchorageLengthFactor", profile.Safety.AnchorageLengthFactor.ToString("0.###", CultureInfo.InvariantCulture), "profile"),
        Item("safety.lapLengthFactor", profile.Safety.LapLengthFactor.ToString("0.###", CultureInfo.InvariantCulture), "profile"),
        Item("schedule.culture", scheduleCulture, scheduleSource),
        Item("verification.underCoverageRatio", profile.Verification.UnderCoverageRatio.ToString("0.###", CultureInfo.InvariantCulture), "profile"),
        Item("smoothing.allowed", profile.Smoothing.Allowed ? "true" : "false", "profile"),
        Item("positions.includeLayerInKey", profile.Positions.IncludeLayerInKey ? "true" : "false", "profile"),
        Item("legend.count", legendCount.ToString(CultureInfo.InvariantCulture), legendSource)
    ];
  }

  private static ParameterSourceReport Item(string path, string value, string source) => new()
  {
    Path = path,
    Value = value,
    Source = source
  };
}
