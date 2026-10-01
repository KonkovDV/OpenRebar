using FluentAssertions;
using OpenRebar.Application.UseCases;
using OpenRebar.Domain.Exceptions;
using OpenRebar.Domain.Models;

namespace OpenRebar.Application.Tests;

public class TabularAsFieldReaderTests
{
  [Fact]
  public void TabularField_ColumnMapping_ReadsNamedLayers()
  {
    const string table = """
      id;minX;minY;maxX;maxY;AsX
      e1;0;0;1000;500;4.5
      """;
    var mapping = new AsFieldMapping
    {
      AdapterId = "tabular-field",
      Delimiter = ";",
      DecimalSeparator = ".",
      Units = "cm2_per_m",
      Columns = new Dictionary<string, string> { ["AsX"] = "BottomX" }
    };

    var field = TabularAsFieldReader.Read(table, mapping);

    field.Elements.Should().ContainSingle();
    field.Elements[0].AsMm2PerM["BottomX"].Should().Be(450);
  }

  [Fact]
  public void TabularField_DecimalComma()
  {
    const string table = """
      id;minX;minY;maxX;maxY;AsX
      e1;0;0;1000;500;5,65
      """;
    var mapping = new AsFieldMapping
    {
      AdapterId = "tabular-field",
      Delimiter = ";",
      DecimalSeparator = ",",
      Units = "cm2_per_m",
      Columns = new Dictionary<string, string> { ["AsX"] = "BottomX" }
    };

    var field = TabularAsFieldReader.Read(table, mapping);

    field.Elements[0].AsMm2PerM["BottomX"].Should().BeApproximately(565, 0.001);
  }

  [Fact]
  public void LiraPreset_MapsAs1ToBottomX()
  {
    const string table = """
      id;minX;minY;maxX;maxY;AS1;AS2;AS3;AS4
      p1;0;0;2000;2000;3,93;1,00;2,50;0,40
      """;

    var field = TabularAsFieldReader.Read(table, AsFieldMapping.LiraPlates);

    field.AdapterId.Should().Be("lira-sapr.plates");
    field.Elements[0].AsMm2PerM["BottomX"].Should().BeApproximately(393, 0.001);
    field.Elements[0].AsMm2PerM["TopX"].Should().BeApproximately(100, 0.001);
    field.Elements[0].AsMm2PerM["BottomY"].Should().BeApproximately(250, 0.001);
    field.Elements[0].AsMm2PerM["TopY"].Should().BeApproximately(40, 0.001);
  }

  [Fact]
  public void Field_UnitsSuspicious_Warns()
  {
    const string table = """
      id;minX;minY;maxX;maxY;AS1;AS2;AS3;AS4
      p1;0;0;1000;1000;5000;5000;5000;5000
      """;

    var field = TabularAsFieldReader.Read(table, AsFieldMapping.LiraPlates);

    field.Warnings.Should().Contain(warning => warning.Contains("units", StringComparison.OrdinalIgnoreCase));
  }

  [Fact]
  public void Field_AxesMismatch_Fails()
  {
    var mapping = AsFieldMapping.LiraPlates with { AxesMatch = false };
    var act = () => TabularAsFieldReader.Read("id;minX\n1;0", mapping);
    act.Should().Throw<AsFieldReadException>().Which.ErrorCode.Should().Be("FIELD_AXES_MISMATCH");
  }

  [Fact]
  public void Field_NegativeValue_Fails()
  {
    const string table = """
      id;minX;minY;maxX;maxY;AsX
      e1;0;0;1000;500;-1
      """;
    var mapping = new AsFieldMapping
    {
      AdapterId = "tabular-field",
      Delimiter = ";",
      DecimalSeparator = ".",
      Units = "mm2_per_m",
      Columns = new Dictionary<string, string> { ["AsX"] = "BottomX" }
    };

    var act = () => TabularAsFieldReader.Read(table, mapping);
    act.Should().Throw<AsFieldReadException>().Which.ErrorCode.Should().Be("FIELD_NEGATIVE");
  }
}
