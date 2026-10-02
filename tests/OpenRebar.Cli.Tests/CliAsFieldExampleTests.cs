using System.Text.Json;
using FluentAssertions;

namespace OpenRebar.Cli.Tests;

public class CliAsFieldExampleTests
{
  [Fact]
  public async Task Field_FourLayers_VerificationPassed()
  {
    string source = Path.Combine(RepoRoot(), "examples", "fe-field", "uniform-slab.csv");
    string directory = Path.Combine(Path.GetTempPath(), $"openrebar-field-{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    string input = Path.Combine(directory, "uniform-slab.csv");
    File.Copy(source, input);

    try
    {
      int exit = await global::OpenRebar.Cli.Program.Main(
      [
          input,
          "--thickness", "220",
          "--cover", "30",
          "--slab-width", "6000",
          "--slab-height", "4000"
      ]);

      exit.Should().Be(2);
      using var report = JsonDocument.Parse(await File.ReadAllTextAsync(Path.ChangeExtension(input, ".result.json")));
      report.RootElement.GetProperty("verification").GetProperty("status").GetString().Should().Be("Failed");
      var layers = report.RootElement.GetProperty("layers").EnumerateArray().ToList();
      layers.Should().OnlyContain(layer => layer.GetProperty("status").GetString() == "Provided");
      var bottomX = layers.Single(layer => layer.GetProperty("layer").GetString() == "BottomX");
      var position = bottomX.GetProperty("positions").EnumerateArray().Single();
      position.GetProperty("diameterMm").GetInt32().Should().Be(20);
      position.GetProperty("quantity").GetInt32().Should().Be(27);
      bottomX.GetProperty("massKg").GetDouble().Should().BeApproximately(437.82, 0.1);
    }
    finally
    {
      if (Directory.Exists(directory))
        Directory.Delete(directory, recursive: true);
    }
  }

  [Fact]
  public async Task SupportedSlab_OnWalls_Passes()
  {
    string directory = Path.Combine(Path.GetTempPath(), $"openrebar-supported-{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    string input = Path.Combine(directory, "supported-slab.csv");
    string project = Path.Combine(directory, "supported-slab.project.json");
    File.Copy(Path.Combine(RepoRoot(), "examples", "fe-field", "supported-slab.csv"), input);
    File.Copy(Path.Combine(RepoRoot(), "examples", "fe-field", "supported-slab.project.json"), project);

    try
    {
      int exit = await global::OpenRebar.Cli.Program.Main([input, "--project", project]);

      exit.Should().Be(0);
      using var report = JsonDocument.Parse(await File.ReadAllTextAsync(Path.ChangeExtension(input, ".result.json")));
      var verification = report.RootElement.GetProperty("verification");
      verification.GetProperty("status").GetString().Should().Be("Passed");
      verification.GetProperty("edgeDevelopmentAreaM2").GetDouble().Should().Be(0);
      verification.GetProperty("realDeficitAreaM2").GetDouble().Should().Be(0);
      verification.GetProperty("underReinforcedAreaM2").GetDouble().Should().Be(0);
      report.RootElement.GetProperty("warnings").EnumerateArray()
          .Should().NotContain(warning => warning.GetString()!.Contains("EdgeKindDefaulted", StringComparison.Ordinal));
      report.RootElement.GetProperty("clashes").EnumerateArray()
          .Should().NotContain(clash => clash.GetProperty("kind").GetString() == "barOutsideWorkingArea");
    }
    finally
    {
      if (Directory.Exists(directory))
        Directory.Delete(directory, recursive: true);
    }
  }

  private static string RepoRoot()
  {
    var current = new DirectoryInfo(AppContext.BaseDirectory);
    while (current is not null)
    {
      if (File.Exists(Path.Combine(current.FullName, "OpenRebar.sln")))
        return current.FullName;
      current = current.Parent;
    }

    throw new InvalidOperationException("Repository root was not found.");
  }
}
