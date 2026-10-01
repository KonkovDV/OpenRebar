using System.Text.Json;
using FluentAssertions;

namespace OpenRebar.Application.Tests;

public class CompanyProfilePipelineTests
{
  [Fact]
  public async Task Profile_InitValidateAndDiff_RoundTrip()
  {
    string output = Path.Combine(Path.GetTempPath(), $"openrebar-init-{Guid.NewGuid():N}.json");
    try
    {
      (await global::OpenRebar.Cli.Program.Main(["profile", "init", "--out", output, "--id", "office"])).Should().Be(0);
      (await global::OpenRebar.Cli.Program.Main(["profile", "validate", output])).Should().Be(0);
      string generic = Path.Combine(RepoRoot(), "profiles", "generic.json");
      (await global::OpenRebar.Cli.Program.Main(["profile", "diff", generic, output])).Should().Be(0);
    }
    finally
    {
      if (File.Exists(output))
        File.Delete(output);
    }
  }

  [Fact]
  public async Task Report_ContainsParameterSources()
  {
    string dxf = Path.Combine(RepoRoot(), "examples", "dxf", "simple-slab", "input.dxf");
    string generic = Path.Combine(RepoRoot(), "profiles", "generic.json");
    string example = Path.Combine(RepoRoot(), "profiles", "examples", "residential-monolith.json");

    using var genericReport = await Run(dxf, generic);
    using var exampleReport = await Run(dxf, example);

    genericReport.RootElement.GetProperty("verification").GetProperty("status").GetString().Should().Be("Failed");
    exampleReport.RootElement.GetProperty("verification").GetProperty("status").GetString().Should().Be("Failed");

    double genericWaste = genericReport.RootElement.GetProperty("summary").GetProperty("totalWastePercent").GetDouble();
    double exampleWaste = exampleReport.RootElement.GetProperty("summary").GetProperty("totalWastePercent").GetDouble();
    exampleWaste.Should().BeGreaterThan(genericWaste);

    var stock = exampleReport.RootElement.GetProperty("parameterSources").EnumerateArray()
        .Single(item => item.GetProperty("path").GetString() == "supply.stockLengthsMm");
    stock.GetProperty("value").GetString().Should().Be("11700");
    stock.GetProperty("source").GetString().Should().Be("profile");
    exampleReport.RootElement.GetProperty("profile").GetProperty("id").GetString().Should().Be("residential-monolith");
  }

  private static async Task<JsonDocument> Run(string dxf, string profile)
  {
    string directory = Path.Combine(Path.GetTempPath(), $"openrebar-profile-run-{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    string input = Path.Combine(directory, "input.dxf");
    File.Copy(dxf, input);
    int exit = await global::OpenRebar.Cli.Program.Main(
    [
        input,
        "--thickness", "220",
        "--cover", "30",
        "--slab-width", "6000",
        "--slab-height", "4000",
        "--layer", "BottomX",
        "--profile", profile
    ]);
    exit.Should().Be(2);
    return JsonDocument.Parse(await File.ReadAllTextAsync(Path.ChangeExtension(input, ".result.json")));
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
