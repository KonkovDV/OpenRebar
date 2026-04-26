using System.Text;
using OpenRebar.Domain.Models;
using OpenRebar.Infrastructure.Reporting;
using FluentAssertions;

namespace OpenRebar.Infrastructure.Tests.Reporting;

public class CsvScheduleExporterTests
{
  [Fact]
  public async Task ExportAsync_ShouldWriteRussianCsvWithSemicolonDelimiter()
  {
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
                    MakeRebar("1", 12, 2450),
                    MakeRebar("2", 12, 2450)
                ]
            }
    ];

    try
    {
      await exporter.ExportAsync(zones, outputPath);

      var lines = await File.ReadAllLinesAsync(outputPath, Encoding.UTF8);
      lines[0].Should().Be("Марка;Диаметр, мм;Длина, мм;Количество;Масса 1 шт, кг;Масса всего, кг;Класс стали");
      lines.Should().HaveCount(3);
      lines[1].Should().Contain("1;12;2450;1;");
      lines[1].Should().EndWith(";A500C");
      lines[2].Should().Contain("2;12;2450;1;");
      lines[2].Should().EndWith(";A500C");
    }
    finally
    {
      if (File.Exists(outputPath))
        File.Delete(outputPath);
    }
  }

  private static Polygon MakeRect(double x, double y, double width, double height) => new([
      new Point2D(x, y),
        new Point2D(x + width, y),
        new Point2D(x + width, y + height),
        new Point2D(x, y + height)
  ]);

  private static RebarSegment MakeRebar(string mark, int diameterMm, double totalLengthMm) => new()
  {
    Start = new Point2D(0, 0),
    End = new Point2D(totalLengthMm - 400, 0),
    DiameterMm = diameterMm,
    AnchorageLengthStart = 200,
    AnchorageLengthEnd = 200,
    Mark = mark
  };
}
