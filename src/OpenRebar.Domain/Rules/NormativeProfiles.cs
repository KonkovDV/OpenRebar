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
  public required IReadOnlyDictionary<string, double> BondStressByConcreteClass { get; init; }
  public required IReadOnlyDictionary<string, double> DesignStrengthBySteelClass { get; init; }
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
  public const string DefaultTablesVersion = "ru.sp63.2018.tables.v1";

  private const string DefaultResourceName = "OpenRebar.Domain.Rules.Data.ru.sp63.2018.tables.v1.json";
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

  public static bool IsPeriodicProfile(string steelClass)
  {
    var profile = Sp63_2018;
    string lookup = NormalizeKey(steelClass, profile.DefaultSteelClass);
    return profile.PeriodicProfiles.Contains(lookup, StringComparer.OrdinalIgnoreCase);
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
      PeriodicProfiles = resource.PeriodicProfiles.ToArray(),
      BondStressByConcreteClass = new Dictionary<string, double>(resource.BondStressByConcreteClass, StringComparer.OrdinalIgnoreCase),
      DesignStrengthBySteelClass = new Dictionary<string, double>(resource.DesignStrengthBySteelClass, StringComparer.OrdinalIgnoreCase),
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
    public required Dictionary<string, double> LinearMassKgPerM { get; init; }
    public required List<string> PeriodicProfiles { get; init; }
    public required List<int> StandardDiametersMm { get; init; }
    public required List<int> StandardSpacingsMm { get; init; }
  }
}
