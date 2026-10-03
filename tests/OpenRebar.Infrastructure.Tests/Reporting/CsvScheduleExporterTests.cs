using System.Security.Cryptography;
using System.Text;
using OpenRebar.Application.UseCases;
using OpenRebar.Domain.Models;
using OpenRebar.Domain.Ports;
using OpenRebar.Domain.Rules;
using OpenRebar.Infrastructure.Reporting;
using FluentAssertions;

namespace OpenRebar.Infrastructure.Tests.Reporting;

public class CsvScheduleExporterTests
{
  [Fact]
  public async Task ExportAsync_GroupsBarsIntoOnePosition()
  {
    // The previous expectation was one CSV row per bar because each bar had its own mark.
    // That was a schedule bug: identical bars are one position.
    var exporter = new CsvScheduleExporter();
    var outputPath = Path.Combine(Path.GetTempPath(), $"OpenRebar-schedule-{Guid.NewGuid():N}.csv");

    IReadOnlyList<ReinforcementZone> zones =
    [
        new ReinforcementZone
        {
          Id = "Z-001",
          Boundary = MakeRect(0, 0, 1000, 1000),
          Spec = new ReinforcementSpec { DiameterMm = 12, SpacingMm = 200, SteelClass = "A500C" },
          Direction = RebarDirection.X,
          ZoneType = ZoneType.Simple,
          Rebars =
          [
              MakeRebar(12, 2450, 0),
              MakeRebar(12, 2450, 200)
          ]
        }
    ];
    PositionAssigner.Assign(zones);

    try
    {
      await exporter.ExportAsync(zones, outputPath, ScheduleNumberCulture.Invariant);

      var lines = await File.ReadAllLinesAsync(outputPath, Encoding.UTF8);
      lines.Should().Contain("[BottomX]");
      lines.Should().Contain("status;NotProvided");
      lines.Should().Contain("[Расход стали]");
      double piece = ReinforcementLimits.GetLinearMass(12) * 2.450;
      string mass = piece.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
      string total = (piece * 2).ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
      lines.Should().Contain($"1;;Ø12 A500C l = 2450;2;{mass};форма 00");
      lines.Should().Contain($"A500C;12;{total}");
    }
    finally
    {
      if (File.Exists(outputPath))
        File.Delete(outputPath);
    }
  }

  [Fact]
  public async Task ExportAsync_DefaultCulture_UsesRussianDecimalComma()
  {
    var exporter = new CsvScheduleExporter();
    var outputPath = Path.Combine(Path.GetTempPath(), $"OpenRebar-schedule-ru-{Guid.NewGuid():N}.csv");
    IReadOnlyList<ReinforcementZone> zones = [MakeSingleBarZone()];
    PositionAssigner.Assign(zones);

    try
    {
      await exporter.ExportAsync(zones, outputPath);

      var lines = await File.ReadAllLinesAsync(outputPath, Encoding.UTF8);
      var data = lines.Single(line => line.StartsWith("1;", StringComparison.Ordinal));
      data.Should().Contain(";18,92;");
      data.Should().NotContain("18.92");
    }
    finally
    {
      if (File.Exists(outputPath))
        File.Delete(outputPath);
    }
  }

  [Fact]
  public async Task ExportAsync_InvariantDxfPosition_MatchesAcceptedRow()
  {
    var exporter = new CsvScheduleExporter();
    var outputPath = Path.Combine(Path.GetTempPath(), $"OpenRebar-schedule-dxf-{Guid.NewGuid():N}.csv");
    var bars = Enumerable.Range(0, 27)
        .Select(index => MakeRebar(20, 7660, index * 150.0))
        .ToList();
    IReadOnlyList<ReinforcementZone> zones =
    [
        new ReinforcementZone
        {
          Id = "Z-001",
          Boundary = MakeRect(0, 0, 6000, 4000),
          Spec = new ReinforcementSpec { DiameterMm = 20, SpacingMm = 150, SteelClass = "A500C" },
          Direction = RebarDirection.X,
          Layer = RebarLayer.Bottom,
          ZoneType = ZoneType.Simple,
          Rebars = bars
        }
    ];
    PositionAssigner.Assign(zones);

    try
    {
      await exporter.ExportAsync(zones, outputPath, ScheduleNumberCulture.Invariant);

      var lines = await File.ReadAllLinesAsync(outputPath, Encoding.UTF8);
      lines.Should().Contain("[BottomX]");
      lines.Should().Contain("1;;Ø20 A500C l = 7660;27;18.92;форма 00");
      lines.Should().Contain("A500C;20;510.85");
    }
    finally
    {
      if (File.Exists(outputPath))
        File.Delete(outputPath);
    }
  }

  [Fact]
  public async Task ExportAsync_QuotesDelimitersAndNeutralizesSpreadsheetFormulas()
  {
    var exporter = new CsvScheduleExporter();
    var outputPath = Path.Combine(Path.GetTempPath(), $"OpenRebar-schedule-untrusted-{Guid.NewGuid():N}.csv");
    IReadOnlyList<ReinforcementZone> zones =
    [
        Zone("=HYPERLINK(\"https://example.invalid\");evil", 12, MakeRebar(12, 1000, 0))
    ];
    PositionAssigner.Assign(zones);

    try
    {
      await exporter.ExportAsync(zones, outputPath, ScheduleNumberCulture.Invariant);
      string text = await File.ReadAllTextAsync(outputPath, Encoding.UTF8);

      text.Split('\n').Should().NotContain(line => line.StartsWith("=", StringComparison.Ordinal));
      text.Should().Contain("\"'=HYPERLINK(\"\"https://example.invalid\"\");evil\"");
      text.Should().NotContain("\n=HYPERLINK", "a spreadsheet must not receive an executable formula cell");
    }
    finally
    {
      if (File.Exists(outputPath))
        File.Delete(outputPath);
    }
  }

  [Fact]
  public async Task Schedule_HashFile_IsWrittenWhenRequested()
  {
    var exporter = new CsvScheduleExporter();
    var outputPath = Path.Combine(Path.GetTempPath(), $"OpenRebar-schedule-hash-{Guid.NewGuid():N}.csv");
    var bars = Enumerable.Range(0, 27)
        .Select(index => MakeRebar(20, 7660, index * 150.0))
        .ToList();
    IReadOnlyList<ReinforcementZone> zones =
    [
        new ReinforcementZone
        {
          Id = "Z-001",
          Boundary = MakeRect(0, 0, 6000, 4000),
          Spec = new ReinforcementSpec { DiameterMm = 20, SpacingMm = 150, SteelClass = "A500C" },
          Direction = RebarDirection.X,
          Layer = RebarLayer.Bottom,
          ZoneType = ZoneType.Simple,
          Rebars = bars
        }
    ];
    PositionAssigner.Assign(zones);

    try
    {
      await exporter.ExportAsync(zones, outputPath, ScheduleNumberCulture.Invariant);
      string text = (await File.ReadAllTextAsync(outputPath, Encoding.UTF8)).Replace("\r\n", "\n", StringComparison.Ordinal);
      string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
      hash.Should().HaveLength(64);
      string? directory = Environment.GetEnvironmentVariable("OPENREBAR_TFM_HASH_DIR");
      if (string.IsNullOrWhiteSpace(directory))
        return;

      Directory.CreateDirectory(directory);
      await File.WriteAllTextAsync(Path.Combine(directory, "schedule.sha256"), hash + "\n");
    }
    finally
    {
      if (File.Exists(outputPath))
        File.Delete(outputPath);
    }
  }

  [Fact]
  public async Task ExportAsync_SortsMarksNumerically()
  {
    var exporter = new CsvScheduleExporter();
    var outputPath = Path.Combine(Path.GetTempPath(), $"OpenRebar-schedule-sort-{Guid.NewGuid():N}.csv");
    var bars = Enumerable.Range(0, 10)
        .Select(index => MakeRebar(12, 3000 - index * 100, index * 200.0))
        .ToList();
    IReadOnlyList<ReinforcementZone> zones =
    [
        new ReinforcementZone
        {
          Id = "Z-001",
          Boundary = MakeRect(0, 0, 4000, 4000),
          Spec = new ReinforcementSpec { DiameterMm = 12, SpacingMm = 200, SteelClass = "A500C" },
          Direction = RebarDirection.X,
          ZoneType = ZoneType.Simple,
          Rebars = bars
        }
    ];
    PositionAssigner.Assign(zones);

    try
    {
      await exporter.ExportAsync(zones, outputPath, ScheduleNumberCulture.Invariant);
      var lines = await File.ReadAllLinesAsync(outputPath, Encoding.UTF8);
      var marks = lines
          .Where(line => line.Contains(" l = ", StringComparison.Ordinal))
          .Select(line => line.Split(';')[0])
          .ToArray();
      marks.Should().Equal("1", "2", "3", "4", "5", "6", "7", "8", "9", "10");
    }
    finally
    {
      if (File.Exists(outputPath))
        File.Delete(outputPath);
    }
  }

  [Fact]
  public async Task ExportAsync_SteelConsumption_SplitsClassAndDiameter()
  {
    var exporter = new CsvScheduleExporter();
    var outputPath = Path.Combine(Path.GetTempPath(), $"OpenRebar-schedule-steel-{Guid.NewGuid():N}.csv");
    IReadOnlyList<ReinforcementZone> zones =
    [
        Zone("A500C", 12, MakeRebar(12, 1000, 0)),
        Zone("A400", 12, MakeRebar(12, 1000, 200)),
        Zone("A500C", 16, MakeRebar(16, 1000, 400))
    ];
    zones[2].Direction = RebarDirection.Y;
    PositionAssigner.Assign(zones);

    try
    {
      await exporter.ExportAsync(zones, outputPath, ScheduleNumberCulture.Invariant);
      var lines = await File.ReadAllLinesAsync(outputPath, Encoding.UTF8);
      int sheet = Array.IndexOf(lines, "[Расход стали]");
      var masses = lines.Skip(sheet + 2).Where(line => line.Length > 0).ToArray();
      string twelve = (ReinforcementLimits.GetLinearMass(12) * 1).ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
      string sixteen = (ReinforcementLimits.GetLinearMass(16) * 1).ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
      masses.Should().Equal($"A400;12;{twelve}", $"A500C;12;{twelve}", $"A500C;16;{sixteen}");
    }
    finally
    {
      if (File.Exists(outputPath))
        File.Delete(outputPath);
    }
  }

  [Fact]
  public async Task ExportAsync_SteelTotalUsesExactLength()
  {
    const double totalLengthMm = 175108.1402964464;
    const int count = 27;
    double lengthMm = totalLengthMm / count;
    var exporter = new CsvScheduleExporter();
    var outputPath = Path.Combine(Path.GetTempPath(), $"OpenRebar-schedule-exact-{Guid.NewGuid():N}.csv");
    IReadOnlyList<ReinforcementZone> zones =
    [
        new ReinforcementZone
        {
          Id = "Z-001",
          Boundary = MakeRect(0, 0, 6000, 4000),
          Spec = new ReinforcementSpec { DiameterMm = 20, SpacingMm = 150, SteelClass = "A500C" },
          Direction = RebarDirection.X,
          ZoneType = ZoneType.Simple,
          Rebars = Enumerable.Range(0, count).Select(index => MakeRebar(20, lengthMm, index * 150.0)).ToList()
        }
    ];
    PositionAssigner.Assign(zones);

    try
    {
      await exporter.ExportAsync(zones, outputPath, ScheduleNumberCulture.Invariant);
      var lines = await File.ReadAllLinesAsync(outputPath, Encoding.UTF8);
      double linearMass = ReinforcementLimits.GetLinearMass(20);
      string exact = (linearMass * totalLengthMm / 1000.0).ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
      string rounded = (linearMass * 6485 / 1000.0 * count).ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
      exact.Should().NotBe(rounded);
      lines.Should().Contain($"A500C;20;{exact}");
      lines.Should().Contain(line => line.StartsWith("1;;Ø20 A500C l = 6485;27;", StringComparison.Ordinal));
    }
    finally
    {
      if (File.Exists(outputPath))
        File.Delete(outputPath);
    }
  }

  private static ReinforcementZone Zone(string steelClass, int diameterMm, RebarSegment bar) => new()
  {
    Id = steelClass + diameterMm,
    Boundary = MakeRect(0, 0, 1000, 1000),
    Spec = new ReinforcementSpec { DiameterMm = diameterMm, SpacingMm = 200, SteelClass = steelClass },
    Direction = RebarDirection.X,
    ZoneType = ZoneType.Simple,
    Rebars = [bar]
  };

  private static ReinforcementZone MakeSingleBarZone() => new()
  {
    Id = "Z-001",
    Boundary = MakeRect(0, 0, 1000, 1000),
    Spec = new ReinforcementSpec { DiameterMm = 20, SpacingMm = 200, SteelClass = "A500C" },
    Direction = RebarDirection.X,
    ZoneType = ZoneType.Simple,
    Rebars = [MakeRebar(20, 7660, 0)]
  };

  private static Polygon MakeRect(double x, double y, double width, double height) => new([
      new Point2D(x, y),
      new Point2D(x + width, y),
      new Point2D(x + width, y + height),
      new Point2D(x, y + height)
  ]);

  private static RebarSegment MakeRebar(int diameterMm, double totalLengthMm, double y) => new()
  {
    Start = new Point2D(0, y),
    End = new Point2D(totalLengthMm, y),
    DiameterMm = diameterMm,
    AnchorageLengthStart = 200,
    AnchorageLengthEnd = 200
  };
}
