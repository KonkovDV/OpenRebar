using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using OpenRebar.Domain.Exceptions;
using OpenRebar.Domain.Models;
using OpenRebar.Domain.Rules;

namespace OpenRebar.Infrastructure.Profiles;

/// <summary>
/// Loads a company profile, applies <c>extends</c>, and rejects values that weaken the norm.
/// </summary>
public static class CompanyProfileLoader
{
  public const string GenericId = "generic";

  private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
  {
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
  };

  private static readonly Dictionary<string, string[]> Allowed = new(StringComparer.Ordinal)
  {
    [""] =
    [
        "id", "version", "extends", "example", "description", "steelClass", "concreteClass",
        "additional", "ends", "laps", "positions", "schedule", "supply", "smoothing",
        "verification", "safety", "legend"
    ],
    ["additional"] = ["diametersMm", "spacingMode", "maxDiameterCount"],
    ["ends"] = ["condition"],
    ["laps"] = ["jointRatioMax", "couplers"],
    ["positions"] = ["includeLayerInKey"],
    ["schedule"] = ["culture"],
    ["supply"] = ["supplierName", "stockLengthsMm", "specialLengths", "offcuts"],
    ["smoothing"] = ["allowed"],
    ["verification"] = ["underCoverageRatio"],
    ["safety"] = ["factor", "anchorageLengthFactor", "lapLengthFactor", "maxSpacingMm"],
    ["legend[]"] = ["color", "diameterMm", "spacingMm"]
  };

  public static LoadedCompanyProfile Load(string path)
  {
    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    return Load(Path.GetFullPath(path), seen);
  }

  public static string? TryResolveBundledGeneric()
  {
    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    while (directory is not null)
    {
      string candidate = Path.Combine(directory.FullName, "profiles", "generic.json");
      if (File.Exists(candidate))
        return candidate;

      directory = directory.Parent;
    }

    return null;
  }

  public static SupplierCatalog ToCatalog(CompanyProfile profile)
  {
    return new SupplierCatalog
    {
      SupplierName = profile.Supply.SupplierName,
      AvailableLengths = profile.Supply.StockLengthsMm
          .Select(length => new StockLength { LengthMm = length, InStock = true })
          .ToList()
    };
  }

  public static ColorLegend ToLegend(CompanyProfile profile, string steelClass)
  {
    return new ColorLegend(profile.Legend.Select(swatch =>
    {
      if (swatch.Color.Count != 3 || swatch.Color.Any(component => component is < 0 or > 255))
        throw new CompanyProfileLoadException("Each legend color must be three RGB components from 0 to 255.");

      return new LegendEntry(
          new IsolineColor((byte)swatch.Color[0], (byte)swatch.Color[1], (byte)swatch.Color[2]),
          new ReinforcementSpec
          {
            DiameterMm = swatch.DiameterMm,
            SpacingMm = swatch.SpacingMm,
            SteelClass = steelClass
          });
    }).ToList());
  }

  public static IReadOnlyList<string> Diff(string leftPath, string rightPath)
  {
    JsonObject left = Flatten(Load(leftPath).Profile);
    JsonObject right = Flatten(Load(rightPath).Profile);
    var paths = left.Select(item => item.Key).Union(right.Select(item => item.Key), StringComparer.Ordinal).OrderBy(path => path, StringComparer.Ordinal);
    var lines = new List<string>();
    foreach (string path in paths)
    {
      string leftValue = left[path]?.ToJsonString() ?? "null";
      string rightValue = right[path]?.ToJsonString() ?? "null";
      if (!string.Equals(leftValue, rightValue, StringComparison.Ordinal))
        lines.Add($"{path}: {leftValue} -> {rightValue}");
    }

    return lines;
  }

  public static void WriteInit(string outputPath, string? id)
  {
    string? genericPath = TryResolveBundledGeneric();
    if (genericPath is null)
      throw new CompanyProfileLoadException("profiles/generic.json was not found.");

    var document = JsonNode.Parse(File.ReadAllText(genericPath)) as JsonObject
        ?? throw new CompanyProfileLoadException("profiles/generic.json is not a JSON object.");
    if (!string.IsNullOrWhiteSpace(id))
      document["id"] = id;
    document["example"] = false;
    document["description"] = "Created from generic defaults.";
    string? directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
    if (!string.IsNullOrEmpty(directory))
      Directory.CreateDirectory(directory);
    File.WriteAllText(outputPath, document.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
  }

  private static LoadedCompanyProfile Load(string fullPath, HashSet<string> seen)
  {
    if (!seen.Add(fullPath))
      throw new CompanyProfileLoadException($"Profile extends itself: {fullPath}");

    if (!File.Exists(fullPath))
      throw new CompanyProfileLoadException($"Company profile was not found: {fullPath}");

    JsonNode? parsed;
    try
    {
      parsed = JsonNode.Parse(File.ReadAllText(fullPath));
    }
    catch (JsonException ex)
    {
      throw new CompanyProfileLoadException(ex.Message);
    }

    if (parsed is not JsonObject document)
      throw new CompanyProfileLoadException("Company profile must be a JSON object.");

    RejectUnknown(document, "");
    JsonObject merged = document;
    if (document["extends"] is JsonValue extendsValue && extendsValue.TryGetValue<string>(out string? extendsId) && !string.IsNullOrWhiteSpace(extendsId))
    {
      string basePath = ResolveExtends(extendsId, fullPath);
      JsonObject baseDocument = LoadObject(basePath, seen);
      merged = Merge(baseDocument, document);
      merged.Remove("extends");
    }

    CompanyProfile profile;
    try
    {
      profile = merged.Deserialize<CompanyProfile>(SerializerOptions)
          ?? throw new CompanyProfileLoadException("Company profile is empty.");
    }
    catch (CompanyProfileLoadException)
    {
      throw;
    }
    catch (JsonException ex)
    {
      throw new CompanyProfileLoadException(ex.Message);
    }

    RejectNormWeakening(profile);
    return new LoadedCompanyProfile(profile, fullPath, Sha256(fullPath));
  }

  private static JsonObject LoadObject(string fullPath, HashSet<string> seen)
  {
    LoadedCompanyProfile loaded = Load(fullPath, seen);
    return JsonSerializer.SerializeToNode(loaded.Profile, SerializerOptions) as JsonObject
        ?? throw new CompanyProfileLoadException($"Profile is not an object: {fullPath}");
  }

  private static string ResolveExtends(string id, string childPath)
  {
    string? bundled = TryResolveBundledGeneric();
    string profilesDirectory = bundled is null
        ? Path.GetDirectoryName(childPath)!
        : Path.GetDirectoryName(bundled)!;
    string[] candidates =
    [
        Path.Combine(profilesDirectory, id + ".json"),
        Path.Combine(profilesDirectory, "examples", id + ".json"),
        Path.Combine(Path.GetDirectoryName(childPath)!, id + ".json")
    ];
    string? found = candidates.FirstOrDefault(File.Exists);
    if (found is null)
      throw new CompanyProfileLoadException($"Profile '{id}' was not found for extends.");

    return Path.GetFullPath(found);
  }

  private static JsonObject Merge(JsonObject baseDocument, JsonObject child)
  {
    var result = (JsonObject)baseDocument.DeepClone();
    foreach (var property in child)
    {
      if (property.Key == "extends")
        continue;

      if (property.Value is JsonObject childObject && result[property.Key] is JsonObject baseObject)
        result[property.Key] = Merge(baseObject, childObject);
      else
        result[property.Key] = property.Value?.DeepClone();
    }

    return result;
  }

  private static void RejectUnknown(JsonObject document, string scope)
  {
    if (!Allowed.TryGetValue(scope, out string[]? names))
      throw new CompanyProfileLoadException($"Unknown profile object '{scope}'.");

    foreach (var property in document)
    {
      if (!names.Contains(property.Key, StringComparer.Ordinal))
        throw new CompanyProfileLoadException($"Unknown profile field '{Join(scope, property.Key)}'.");

      if (property.Value is JsonObject child)
        RejectUnknown(child, property.Key);
      else if (property.Key == "legend" && property.Value is JsonArray swatches)
      {
        foreach (var swatch in swatches)
        {
          if (swatch is not JsonObject swatchObject)
            throw new CompanyProfileLoadException("Each legend entry must be an object.");
          RejectUnknown(swatchObject, "legend[]");
        }
      }
    }
  }

  private static void RejectNormWeakening(CompanyProfile profile)
  {
    var norm = NormativeProfiles.Sp63_2018;
    if (profile.Safety.Factor < 1)
      throw new CompanyProfileLoadException("safety.factor cannot be below 1. A company profile may only tighten the norm.");
    if (profile.Safety.AnchorageLengthFactor < 1)
      throw new CompanyProfileLoadException("safety.anchorageLengthFactor cannot be below 1. A company profile may only tighten the norm.");
    if (profile.Safety.LapLengthFactor < 1)
      throw new CompanyProfileLoadException("safety.lapLengthFactor cannot be below 1. A company profile may only tighten the norm.");
    if (profile.Safety.MaxSpacingMm is double spacing && spacing > norm.MaxSpacingThickSlabCapMm)
      throw new CompanyProfileLoadException($"safety.maxSpacingMm cannot exceed the normative cap of {norm.MaxSpacingThickSlabCapMm:0} mm.");
    if (profile.Laps.JointRatioMax is < 0 or > 1)
      throw new CompanyProfileLoadException("laps.jointRatioMax must be from 0 to 1.");
    if (profile.Verification.UnderCoverageRatio is < 0 or > 1)
      throw new CompanyProfileLoadException("verification.underCoverageRatio must be from 0 to 1.");
    if (profile.Additional.SpacingMode is not ("interleave" or "explicit" or "s/2"))
      throw new CompanyProfileLoadException("additional.spacingMode must be interleave, explicit, or s/2.");
    if (profile.Schedule.Culture is not ("ru" or "invariant"))
      throw new CompanyProfileLoadException("schedule.culture must be ru or invariant.");
    if (profile.Ends.Condition is not ("NeedsHook" or "Straight" or "Hook" or "LBar" or "UBar"))
      throw new CompanyProfileLoadException("ends.condition is not a known end treatment.");
    if (profile.Supply.StockLengthsMm.Count == 0 || profile.Supply.StockLengthsMm.Any(length => length <= 0))
      throw new CompanyProfileLoadException("supply.stockLengthsMm must list positive lengths.");
    if (profile.Legend.Count == 0)
      throw new CompanyProfileLoadException("legend must contain at least one swatch.");
  }

  private static JsonObject Flatten(CompanyProfile profile)
  {
    var node = JsonSerializer.SerializeToNode(profile, SerializerOptions) as JsonObject
        ?? throw new CompanyProfileLoadException("Profile could not be serialized.");
    var flat = new JsonObject();
    FlattenInto(node, "", flat);
    return flat;
  }

  private static void FlattenInto(JsonObject source, string prefix, JsonObject target)
  {
    foreach (var property in source)
    {
      string path = string.IsNullOrEmpty(prefix) ? property.Key : $"{prefix}.{property.Key}";
      if (property.Value is JsonObject child)
        FlattenInto(child, path, target);
      else
        target[path] = property.Value?.DeepClone();
    }
  }

  private static string Join(string scope, string name) =>
      string.IsNullOrEmpty(scope) ? name : $"{scope}.{name}";

  private static string Sha256(string path)
  {
    using var stream = File.OpenRead(path);
    return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
  }
}

public sealed record LoadedCompanyProfile(CompanyProfile Profile, string SourcePath, string Sha256);
