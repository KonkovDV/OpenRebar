using System.Globalization;
using System.Text.Json;

namespace OpenRebar.Domain.Rules;

/// <summary>
/// Immutable normative profile data loaded from a versioned embedded resource.
/// This keeps engineering tables explicit, versioned, and testable.
/// </summary>
public sealed record NormativeProfileData
{
  public required string ProfileId { get; init; }
  public required string Jurisdiction { get; init; }
  public required string DesignCode { get; init; }
  public required string TablesVersion { get; init; }
  public required string DefaultConcreteClass { get; init; }
  public required string DefaultSteelClass { get; init; }
  public required IReadOnlyCollection<string> PeriodicProfiles { get; init; }
  public required IReadOnlyDictionary<string, string> BarTypeByClass { get; init; }
  public required double Eta1HotRolledPeriodic { get; init; }
  public required double Eta1ColdDeformedPeriodic { get; init; }
  public required double Eta1Smooth { get; init; }
  public required double Eta2UpTo32Mm { get; init; }
  public required double Eta2From36Mm { get; init; }
  public required double AnchorageTensionAlpha { get; init; }
  public required double AnchorageCompressionAlpha { get; init; }
  public required double AsCalOverAsEf { get; init; }
  public required double AnchorageMinimumFactorOfBasic { get; init; }
  public required double AnchorageMinimumDiameters { get; init; }
  public required double AnchorageMinimumMm { get; init; }
  public required double RoundUpMm { get; init; }
  public required double LapUpTo50Alpha { get; init; }
  public required double LapFull100Alpha { get; init; }
  public required double LapCompressionAlpha { get; init; }
  public required double LapMinimumFactorOfAlphaBasic { get; init; }
  public required double LapMinimumDiameters { get; init; }
  public required double LapMinimumMm { get; init; }
  public required double TopBarAnchorageFactor { get; init; }
  public required double MaxSpacingThinSlabThicknessMm { get; init; }
  public required double MaxSpacingThinSlabLimitMm { get; init; }
  public required double MaxSpacingThickSlabFactor { get; init; }
  public required double MaxSpacingThickSlabCapMm { get; init; }
  public required double MinReinforcementRatio { get; init; }
  public required double MandrelSplitDiameterMm { get; init; }
  public required double MandrelSmoothBelowSplit { get; init; }
  public required double MandrelSmoothFromSplit { get; init; }
  public required double MandrelPeriodicBelowSplit { get; init; }
  public required double MandrelPeriodicFromSplit { get; init; }
  public required IReadOnlyList<NormativeTraceabilityRow> Traceability { get; init; }
  public required IReadOnlyDictionary<string, double> BondStressByConcreteClass { get; init; }
  public required IReadOnlyDictionary<string, double> DesignStrengthBySteelClass { get; init; }
  public required IReadOnlyDictionary<string, double> DesignCompressionStrengthBySteelClass { get; init; }
  public required IReadOnlyDictionary<string, double> DesignCompressionStrengthShortTermBySteelClass { get; init; }
  public required string DesignStrengthClauseId { get; init; }
  public required string DesignStrengthSourceQuote { get; init; }
  public required string DesignStrengthSourceUrl { get; init; }
  public required string DesignStrengthAccessedUtc { get; init; }
  public required IReadOnlyDictionary<int, double> LinearMassKgPerMByDiameter { get; init; }
  public required IReadOnlyList<int> StandardDiametersMm { get; init; }
  public required IReadOnlyList<int> StandardSpacingsMm { get; init; }
}

/// <summary>
/// Registry of versioned normative profile data used by the domain rules.
/// </summary>
public static class NormativeProfiles
{
  public const string DefaultProfileId = "ru.sp63.2018";
  public const string DefaultTablesVersion = "ru.sp63.2018.tables.v3";

  private const string DefaultResourceName = "OpenRebar.Domain.Rules.Data.ru.sp63.2018.tables.v3.json";
  private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
  private static readonly Lazy<NormativeProfileData> DefaultProfile = new(LoadDefaultProfile);

  public static NormativeProfileData Sp63_2018 => DefaultProfile.Value;

  public static double GetBondStress(string concreteClass)
  {
    var profile = Sp63_2018;
    string lookup = NormalizeKey(concreteClass, profile.DefaultConcreteClass);
    return profile.BondStressByConcreteClass.TryGetValue(lookup, out double value)
        ? value
        : profile.BondStressByConcreteClass[profile.DefaultConcreteClass];
  }

  public static double GetDesignStrength(string steelClass)
  {
    var profile = Sp63_2018;
    string lookup = NormalizeKey(steelClass, profile.DefaultSteelClass);
    return profile.DesignStrengthBySteelClass.TryGetValue(lookup, out double value)
        ? value
        : profile.DesignStrengthBySteelClass[profile.DefaultSteelClass];
  }

  public static double? GetDesignCompressionStrength(string steelClass, bool shortTerm = false)
  {
    var profile = Sp63_2018;
    string lookup = NormalizeKey(steelClass, profile.DefaultSteelClass);
    if (shortTerm
        && profile.DesignCompressionStrengthShortTermBySteelClass.TryGetValue(lookup, out double shortTermValue))
      return shortTermValue;

    return profile.DesignCompressionStrengthBySteelClass.TryGetValue(lookup, out double value)
        ? value
        : null;
  }

  public static bool IsPeriodicProfile(string steelClass)
  {
    return !string.Equals(GetBarType(steelClass), "smooth", StringComparison.OrdinalIgnoreCase);
  }

  public static string GetBarType(string steelClass)
  {
    var profile = Sp63_2018;
    string lookup = NormalizeKey(steelClass, profile.DefaultSteelClass);
    return profile.BarTypeByClass.TryGetValue(lookup, out string? value)
        ? value
        : "smooth";
  }

  public static double GetEta1(string steelClass)
  {
    var profile = Sp63_2018;
    return GetBarType(steelClass) switch
    {
      "hotRolledPeriodic" => profile.Eta1HotRolledPeriodic,
      "coldDeformedPeriodic" => profile.Eta1ColdDeformedPeriodic,
      _ => profile.Eta1Smooth
    };
  }

  public static double GetEta2(int diameterMm)
  {
    var profile = Sp63_2018;
    return diameterMm <= 32 ? profile.Eta2UpTo32Mm : profile.Eta2From36Mm;
  }

  public static double GetLinearMass(int diameterMm)
  {
    var profile = Sp63_2018;
    return profile.LinearMassKgPerMByDiameter.TryGetValue(diameterMm, out double value)
        ? value
        : Math.PI * Math.Pow(diameterMm / 2.0 / 1000.0, 2) * 7850.0;
  }

  private static NormativeProfileData LoadDefaultProfile()
  {
    using var stream = typeof(NormativeProfiles).Assembly.GetManifestResourceStream(DefaultResourceName)
        ?? throw new InvalidOperationException($"Embedded normative resource '{DefaultResourceName}' was not found.");

    var resource = JsonSerializer.Deserialize<NormativeProfileResource>(stream, SerializerOptions)
        ?? throw new InvalidOperationException($"Embedded normative resource '{DefaultResourceName}' could not be deserialized.");

    return new NormativeProfileData
    {
      ProfileId = resource.ProfileId,
      Jurisdiction = resource.Jurisdiction,
      DesignCode = resource.DesignCode,
      TablesVersion = resource.TablesVersion,
      DefaultConcreteClass = resource.DefaultConcreteClass,
      DefaultSteelClass = resource.DefaultSteelClass,
      PeriodicProfiles = resource.BarTypeByClass
          .Where(pair => !string.Equals(pair.Value, "smooth", StringComparison.OrdinalIgnoreCase))
          .Select(pair => pair.Key)
          .ToArray(),
      BarTypeByClass = new Dictionary<string, string>(resource.BarTypeByClass, StringComparer.OrdinalIgnoreCase),
      Eta1HotRolledPeriodic = resource.Eta1.HotRolledPeriodic,
      Eta1ColdDeformedPeriodic = resource.Eta1.ColdDeformedPeriodic,
      Eta1Smooth = resource.Eta1.Smooth,
      Eta2UpTo32Mm = resource.Eta2.UpTo32Mm,
      Eta2From36Mm = resource.Eta2.From36Mm,
      AnchorageTensionAlpha = resource.Anchorage.TensionAlpha,
      AnchorageCompressionAlpha = resource.Anchorage.CompressionAlpha,
      AsCalOverAsEf = resource.Anchorage.AsCalOverAsEf,
      AnchorageMinimumFactorOfBasic = resource.Anchorage.MinimumFactorOfBasic,
      AnchorageMinimumDiameters = resource.Anchorage.MinimumDiameters,
      AnchorageMinimumMm = resource.Anchorage.MinimumMm,
      RoundUpMm = resource.Anchorage.RoundUpMm,
      LapUpTo50Alpha = resource.Lap.UpTo50Alpha,
      LapFull100Alpha = resource.Lap.Full100Alpha,
      LapCompressionAlpha = resource.Lap.CompressionAlpha,
      LapMinimumFactorOfAlphaBasic = resource.Lap.MinimumFactorOfAlphaBasic,
      LapMinimumDiameters = resource.Lap.MinimumDiameters,
      LapMinimumMm = resource.Lap.MinimumMm,
      TopBarAnchorageFactor = resource.TopBarAnchorageFactor.Value,
      MaxSpacingThinSlabThicknessMm = resource.MaxSpacing.ThinSlabThicknessMm,
      MaxSpacingThinSlabLimitMm = resource.MaxSpacing.ThinSlabLimitMm,
      MaxSpacingThickSlabFactor = resource.MaxSpacing.ThickSlabFactorOfThickness,
      MaxSpacingThickSlabCapMm = resource.MaxSpacing.ThickSlabCapMm,
      MinReinforcementRatio = resource.MinReinforcementRatio.Ratio,
      MandrelSplitDiameterMm = resource.Mandrel.SplitDiameterMm,
      MandrelSmoothBelowSplit = resource.Mandrel.SmoothBelowSplit,
      MandrelSmoothFromSplit = resource.Mandrel.SmoothFromSplit,
      MandrelPeriodicBelowSplit = resource.Mandrel.PeriodicBelowSplit,
      MandrelPeriodicFromSplit = resource.Mandrel.PeriodicFromSplit,
      Traceability = resource.Traceability,
      BondStressByConcreteClass = new Dictionary<string, double>(resource.BondStressByConcreteClass, StringComparer.OrdinalIgnoreCase),
      DesignStrengthBySteelClass = new Dictionary<string, double>(resource.DesignStrengthBySteelClass, StringComparer.OrdinalIgnoreCase),
      DesignCompressionStrengthBySteelClass = new Dictionary<string, double>(resource.DesignCompressionStrengthBySteelClass, StringComparer.OrdinalIgnoreCase),
      DesignCompressionStrengthShortTermBySteelClass = new Dictionary<string, double>(resource.DesignCompressionStrengthShortTermBySteelClass, StringComparer.OrdinalIgnoreCase),
      DesignStrengthClauseId = resource.DesignStrengthReview.ClauseId,
      DesignStrengthSourceQuote = resource.DesignStrengthReview.SourceQuote,
      DesignStrengthSourceUrl = resource.DesignStrengthReview.SourceUrl,
      DesignStrengthAccessedUtc = resource.DesignStrengthReview.AccessedUtc,
      LinearMassKgPerMByDiameter = resource.LinearMassKgPerM.ToDictionary(
            pair => int.Parse(pair.Key, CultureInfo.InvariantCulture),
            pair => pair.Value),
      StandardDiametersMm = resource.StandardDiametersMm.ToArray(),
      StandardSpacingsMm = resource.StandardSpacingsMm.ToArray()
    };
  }

  private static string NormalizeKey(string? value, string fallback)
  {
    return string.IsNullOrWhiteSpace(value)
        ? fallback
        : value.Trim();
  }

  private sealed record NormativeProfileResource
  {
    public required string ProfileId { get; init; }
    public required string Jurisdiction { get; init; }
    public required string DesignCode { get; init; }
    public required string TablesVersion { get; init; }
    public required string DefaultConcreteClass { get; init; }
    public required string DefaultSteelClass { get; init; }
    public required Dictionary<string, double> BondStressByConcreteClass { get; init; }
    public required Dictionary<string, double> DesignStrengthBySteelClass { get; init; }
    public required Dictionary<string, double> DesignCompressionStrengthBySteelClass { get; init; }
    public required Dictionary<string, double> DesignCompressionStrengthShortTermBySteelClass { get; init; }
    public required DesignStrengthReviewResource DesignStrengthReview { get; init; }
    public required Dictionary<string, double> LinearMassKgPerM { get; init; }
    public required Dictionary<string, string> BarTypeByClass { get; init; }
    public required Eta1Resource Eta1 { get; init; }
    public required Eta2Resource Eta2 { get; init; }
    public required AnchorageResource Anchorage { get; init; }
    public required LapResource Lap { get; init; }
    public required TopBarFactorResource TopBarAnchorageFactor { get; init; }
    public required MaxSpacingResource MaxSpacing { get; init; }
    public required MinRatioResource MinReinforcementRatio { get; init; }
    public required MandrelResource Mandrel { get; init; }
    public required List<NormativeTraceabilityRow> Traceability { get; init; }
    public required List<int> StandardDiametersMm { get; init; }
    public required List<int> StandardSpacingsMm { get; init; }
  }

  private sealed record Eta1Resource
  {
    public required double HotRolledPeriodic { get; init; }
    public required double ColdDeformedPeriodic { get; init; }
    public required double Smooth { get; init; }
  }

  private sealed record Eta2Resource
  {
    public required double UpTo32Mm { get; init; }
    public required double From36Mm { get; init; }
  }

  private sealed record AnchorageResource
  {
    public required double TensionAlpha { get; init; }
    public required double CompressionAlpha { get; init; }
    public required double AsCalOverAsEf { get; init; }
    public required double MinimumFactorOfBasic { get; init; }
    public required double MinimumDiameters { get; init; }
    public required double MinimumMm { get; init; }
    public required double RoundUpMm { get; init; }
  }

  private sealed record LapResource
  {
    public required double UpTo50Alpha { get; init; }
    public required double Full100Alpha { get; init; }
    public required double CompressionAlpha { get; init; }
    public required double MinimumFactorOfAlphaBasic { get; init; }
    public required double MinimumDiameters { get; init; }
    public required double MinimumMm { get; init; }
  }

  private sealed record TopBarFactorResource
  {
    public required double Value { get; init; }
  }

  private sealed record MaxSpacingResource
  {
    public required double ThinSlabThicknessMm { get; init; }
    public required double ThinSlabLimitMm { get; init; }
    public required double ThickSlabFactorOfThickness { get; init; }
    public required double ThickSlabCapMm { get; init; }
  }

  private sealed record MinRatioResource
  {
    public required double Ratio { get; init; }
    public required string SectionHeight { get; init; }
  }

  private sealed record DesignStrengthReviewResource
  {
    public required string ClauseId { get; init; }
    public required string SourceQuote { get; init; }
    public required string SourceUrl { get; init; }
    public required string AccessedUtc { get; init; }
  }

  private sealed record MandrelResource
  {
    public required double SplitDiameterMm { get; init; }
    public required double SmoothBelowSplit { get; init; }
    public required double SmoothFromSplit { get; init; }
    public required double PeriodicBelowSplit { get; init; }
    public required double PeriodicFromSplit { get; init; }
  }
}

public sealed record NormativeTraceabilityRow
{
  public required string ClauseId { get; init; }
  public required string Method { get; init; }
  public required string Test { get; init; }
  public required string SourceQuote { get; init; }
}
