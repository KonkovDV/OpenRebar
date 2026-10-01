using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using IxMilia.Dxf;
using IxMilia.Dxf.Entities;

namespace OpenRebar.TestCorpus;

internal static partial class DxfCorpusWriter
{
  private static readonly DateTime FrozenTimestamp = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Unspecified);

  public static byte[] Write(ScenarioDefinition scenario)
  {
    var file = new DxfFile();
    file.Header.Version = DxfAcadVersion.R2000;
    FreezeHeader(file.Header);

    foreach (var zone in scenario.Zones)
    {
      var legendClass = LegendPalette.Get(zone.ClassIndex);
      if (legendClass.Aci is not short aci)
      {
        throw new InvalidOperationException(
            $"Legend class {zone.ClassIndex} has no ACI color, so it cannot be written to DXF.");
      }

      DxfEntity entity = zone.Holes.Length == 0
          ? Polyline(zone.Outer, aci)
          : Hatch(zone.Outer, zone.Holes, aci);

      entity.Layer = "BottomX";
      file.Entities.Add(entity);
    }

    using var stream = new MemoryStream();
    file.Save(stream, asText: true);
    return Normalize(stream.ToArray());
  }

  private static DxfLwPolyline Polyline(IReadOnlyList<Mm> ring, short aci)
  {
    var vertices = ring.Select(p => new DxfLwPolylineVertex { X = p.X, Y = p.Y }).ToList();
    return new DxfLwPolyline(vertices)
    {
      IsClosed = true,
      Color = DxfColor.FromIndex((byte)aci)
    };
  }

  private static DxfHatch Hatch(IReadOnlyList<Mm> outer, IReadOnlyList<Mm[]> holes, short aci)
  {
    var hatch = new DxfHatch
    {
      Color = DxfColor.FromIndex((byte)aci),
      FillColor = DxfColor.FromIndex((byte)aci),
      PatternName = "SOLID",
      HatchStyle = DxfHatchStyle.EntireArea
    };

    hatch.BoundaryPaths.Add(Path(outer));
    foreach (var hole in holes)
      hatch.BoundaryPaths.Add(Path(hole));

    return hatch;
  }

  private static DxfHatch.PolylineBoundaryPath Path(IReadOnlyList<Mm> ring)
  {
    var path = new DxfHatch.PolylineBoundaryPath { IsClosed = true };
    foreach (var point in ring)
      path.Vertices.Add(new DxfVertex(new DxfPoint(point.X, point.Y, 0)));

    return path;
  }

  /// <summary>
  /// IxMilia stamps the header with <see cref="DateTime.Now"/> at construction.
  /// A second pass replaces any GUID the writer still emits, and forces LF.
  /// </summary>
  private static void FreezeHeader(DxfHeader header)
  {
    const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public;
    foreach (var property in header.GetType().GetProperties(flags))
    {
      if (property.GetIndexParameters().Length > 0 || property.GetSetMethod() is null)
        continue;

      if (property.PropertyType == typeof(DateTime))
        property.SetValue(header, FrozenTimestamp);
      else if (property.Name.Contains("InsertionUnit", StringComparison.Ordinal)
               && TryMillimetres(property.PropertyType, out object? millimetres))
        property.SetValue(header, millimetres);
    }
  }

  private static bool TryMillimetres(Type type, out object? value)
  {
    value = null;
    if (!type.IsEnum)
      return false;

    string? name = Enum.GetNames(type)
        .FirstOrDefault(candidate => candidate.Contains("Millimeter", StringComparison.OrdinalIgnoreCase));
    if (name is null)
      return false;

    value = Enum.Parse(type, name);
    return true;
  }

  private static byte[] Normalize(byte[] dxf)
  {
    string text = Encoding.ASCII.GetString(dxf);
    text = GuidPattern().Replace(text, "00000000-0000-0000-0000-000000000000");
    text = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal);
    // IxMilia refreshes $TDUPDATE and the drawing timer at Save(), after FreezeHeader.
    text = TimePattern().Replace(text, "${1}0.0");
    return Encoding.ASCII.GetBytes(text);
  }

  [GeneratedRegex(@"(\$(?:TDCREATE|TDUCREATE|TDUPDATE|TDUUPDATE|TDINDWG|TDUSRTIMER)\n 40\n)[^\n]+")]
  private static partial Regex TimePattern();

  [GeneratedRegex("[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}")]
  private static partial Regex GuidPattern();
}
