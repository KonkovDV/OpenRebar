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
      lines.Should().Contain("[Диаметры]");
      double piece = ReinforcementLimits.GetLinearMass(12) * 2.450;
      string mass = piece.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
      string total = (piece * 2).ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
      lines.Should().Contain($"1;12;2450;2;{mass};{total};A500C;BottomX;00;{total}");
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
      lines.Should().Contain("1;20;7660;27;18.92;510.85;A500C;BottomX;00;510.85");
      lines.Should().Contain("20;510.85");
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
          .Where(line => line.Length > 0 && char.IsDigit(line[0]) && line.Count(character => character == ';') >= 9)
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
    End = new Point2D(totalLengthMm - 400, y),
    DiameterMm = diameterMm,
    AnchorageLengthStart = 200,
    AnchorageLengthEnd = 200
  };
}
