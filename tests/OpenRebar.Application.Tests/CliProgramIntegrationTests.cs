using System.Text.Json;
using FluentAssertions;
using IxMilia.Dxf;
using IxMilia.Dxf.Entities;

namespace OpenRebar.Application.Tests;

public class CliProgramIntegrationTests
{
    [Fact]
    public async Task Main_WithCustomSlabDimensions_ShouldExportArtifactsAndReturnZero()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"OpenRebar-cli-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);

        var dxfPath = Path.Combine(tempDirectory, "floor-05.dxf");
        CreateSampleDxf(dxfPath);

        try
        {
            var exitCode = await global::OpenRebar.Cli.Program.Main(
            [
                dxfPath,
                "--thickness", "220",
                "--cover", "30",
                "--slab-width", "12000",
                "--slab-height", "9000"
            ]);

            exitCode.Should().Be(0);

            var reportPath = Path.ChangeExtension(dxfPath, ".result.json");
            var schedulePath = Path.ChangeExtension(dxfPath, ".schedule.csv");
            var aeroBimPath = Path.ChangeExtension(dxfPath, ".aerobim.json");
            var ifcPath = Path.ChangeExtension(dxfPath, ".reinforcement.ifc");

            File.Exists(reportPath).Should().BeTrue();
            File.Exists(schedulePath).Should().BeTrue();
            File.Exists(aeroBimPath).Should().BeTrue();
            File.Exists(ifcPath).Should().BeTrue();

            using var report = JsonDocument.Parse(await File.ReadAllTextAsync(reportPath));
            var slab = report.RootElement.GetProperty("slab");
            slab.GetProperty("thicknessMm").GetDouble().Should().Be(220);
            slab.GetProperty("coverMm").GetDouble().Should().Be(30);

            var boundingBox = slab.GetProperty("boundingBox");
            boundingBox.GetProperty("width").GetDouble().Should().Be(12000);
            boundingBox.GetProperty("height").GetDouble().Should().Be(9000);
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
                Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task Main_WhenCoverIsNotLessThanThickness_ShouldReturnOne()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"OpenRebar-cli-invalid-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);

        var dxfPath = Path.Combine(tempDirectory, "floor-06.dxf");
        CreateSampleDxf(dxfPath);

        try
        {
            var exitCode = await global::OpenRebar.Cli.Program.Main(
            [
                dxfPath,
                "--thickness", "200",
                "--cover", "200"
            ]);

            exitCode.Should().Be(1);
            File.Exists(Path.ChangeExtension(dxfPath, ".result.json")).Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
                Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task Main_WithHelpFlag_ShouldReturnZero()
    {
        var exitCode = await global::OpenRebar.Cli.Program.Main(["--help"]);
        exitCode.Should().Be(0);
    }

    [Fact]
    public async Task Main_WithNoArguments_ShouldReturnZero()
    {
        var exitCode = await global::OpenRebar.Cli.Program.Main([]);
        exitCode.Should().Be(0);
    }

    [Fact]
    public async Task Main_WithMissingFile_ShouldReturnOne()
    {
        var exitCode = await global::OpenRebar.Cli.Program.Main(
        [
            Path.Combine(Path.GetTempPath(), "nonexistent-OpenRebar-floor.dxf")
        ]);
        exitCode.Should().Be(1);
    }

    [Fact]
    public async Task Main_WithInvalidThickness_ShouldReturnOne()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"OpenRebar-cli-badnum-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        var dxfPath = Path.Combine(tempDirectory, "floor-07.dxf");
        CreateSampleDxf(dxfPath);

        try
        {
            var exitCode = await global::OpenRebar.Cli.Program.Main(
            [
                dxfPath,
                "--thickness", "abc"
            ]);
            exitCode.Should().Be(1);
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
                Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task Main_WithZeroSlabWidth_ShouldReturnOne()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"OpenRebar-cli-zero-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        var dxfPath = Path.Combine(tempDirectory, "floor-08.dxf");
        CreateSampleDxf(dxfPath);

        try
        {
            var exitCode = await global::OpenRebar.Cli.Program.Main(
            [
                dxfPath,
                "--slab-width", "0"
            ]);
            exitCode.Should().Be(1);
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
                Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task Main_WithNegativeCover_ShouldReturnOne()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"OpenRebar-cli-neg-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        var dxfPath = Path.Combine(tempDirectory, "floor-09.dxf");
        CreateSampleDxf(dxfPath);

        try
        {
            var exitCode = await global::OpenRebar.Cli.Program.Main(
            [
                dxfPath,
                "--cover", "-5"
            ]);
            exitCode.Should().Be(1);
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
                Directory.Delete(tempDirectory, recursive: true);
        }
    }

    private static void CreateSampleDxf(string outputPath)
    {
        var dxfFile = new DxfFile();
        dxfFile.Header.Version = DxfAcadVersion.R2000;
        dxfFile.Entities.Add(new DxfLwPolyline([
            new DxfLwPolylineVertex { X = 0.0, Y = 0.0 },
            new DxfLwPolylineVertex { X = 2500.0, Y = 0.0 },
            new DxfLwPolylineVertex { X = 2500.0, Y = 2500.0 },
            new DxfLwPolylineVertex { X = 0.0, Y = 2500.0 }
        ])
        {
            IsClosed = true,
            Color = DxfColor.FromIndex(1)
        });

        dxfFile.Save(outputPath);
    }
}