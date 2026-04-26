using System.Text.Json;
using System.Text.Json.Serialization;
using OpenRebar.Domain.Exceptions;
using OpenRebar.Domain.Models;
using OpenRebar.Domain.Ports;

namespace OpenRebar.Infrastructure.Legend;

/// <summary>
/// Loads legend mappings from a JSON configuration file.
/// </summary>
public sealed class JsonLegendLoader : ILegendLoader
{
  private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
  {
    PropertyNameCaseInsensitive = true
  };

  public async Task<ColorLegend> LoadAsync(string path, CancellationToken ct = default)
  {
    if (!File.Exists(path))
      throw new LegendLoadException(path, "File not found.");

    try
    {
      await using var stream = File.OpenRead(path);
      var config = await JsonSerializer.DeserializeAsync<LegendConfig>(stream, SerializerOptions, ct);

      if (config is null || config.Legends.Count == 0)
        throw new LegendLoadException(path, "Legend file does not contain any legend entries.");

      return new ColorLegend(config.Legends.Select(entry => new LegendEntry(
          CreateColor(entry.Color, path),
          new ReinforcementSpec
          {
            DiameterMm = entry.DiameterMm,
            SpacingMm = entry.SpacingMm,
            SteelClass = entry.SteelClass
          })).ToList());
    }
    catch (LegendLoadException)
    {
      throw;
    }
    catch (JsonException ex)
    {
      throw new LegendLoadException(path, ex.Message);
    }
  }

  private static IsolineColor CreateColor(IReadOnlyList<int> components, string path)
  {
    if (components.Count != 3)
      throw new LegendLoadException(path, "Each legend color must contain exactly 3 RGB components.");

    if (components.Any(component => component is < 0 or > 255))
      throw new LegendLoadException(path, "RGB components must be between 0 and 255.");

    return new IsolineColor((byte)components[0], (byte)components[1], (byte)components[2]);
  }

  public ColorLegend GetDefaultLegend(string steelClass)
  {
    return new ColorLegend(
    [
        new LegendEntry(new IsolineColor(0, 0, 255), new ReinforcementSpec { DiameterMm = 8, SpacingMm = 200, SteelClass = steelClass }),
            new LegendEntry(new IsolineColor(0, 255, 255), new ReinforcementSpec { DiameterMm = 10, SpacingMm = 200, SteelClass = steelClass }),
            new LegendEntry(new IsolineColor(0, 255, 0), new ReinforcementSpec { DiameterMm = 12, SpacingMm = 200, SteelClass = steelClass }),
            new LegendEntry(new IsolineColor(255, 255, 0), new ReinforcementSpec { DiameterMm = 14, SpacingMm = 200, SteelClass = steelClass }),
            new LegendEntry(new IsolineColor(255, 165, 0), new ReinforcementSpec { DiameterMm = 16, SpacingMm = 150, SteelClass = steelClass }),
            new LegendEntry(new IsolineColor(255, 0, 0), new ReinforcementSpec { DiameterMm = 20, SpacingMm = 150, SteelClass = steelClass }),
            new LegendEntry(new IsolineColor(255, 0, 255), new ReinforcementSpec { DiameterMm = 25, SpacingMm = 150, SteelClass = steelClass })
    ]);
  }

  private sealed record LegendConfig
  {
    [JsonPropertyName("legends")]
    public List<LegendEntryDto> Legends { get; init; } = [];
  }

  private sealed record LegendEntryDto
  {
    [JsonPropertyName("color")]
    public required int[] Color { get; init; }

    [JsonPropertyName("diameter_mm")]
    public required int DiameterMm { get; init; }

    [JsonPropertyName("spacing_mm")]
    public required int SpacingMm { get; init; }

    [JsonPropertyName("steel_class")]
    public required string SteelClass { get; init; }
  }
}
